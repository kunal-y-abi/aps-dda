using System.Text.Json.Nodes;
using ApsDda.Core;
using Microsoft.Data.Sqlite;
using static ApsDda.Runtime.SqliteRuntimeStore;

namespace ApsDda.Runtime;

/// <summary>Finite selected-profile coordinator. Each source independently revalidates the same retained bindings before local commit.</summary>
public sealed class WorkflowRuntime
{
    private readonly Dictionary<string, ISourceAdapter> _sources = new(StringComparer.Ordinal);
    private readonly List<EvidenceRule> _evidenceRules = [];
    public WorkflowRuntime(SqliteRuntimeStore store, TrustedDirectory trust, PrincipalDirectory principals, ArtifactSigner orchestratorSigner,
        ArtifactSigner authoritySigner, IRuntimePolicy policy, TimeProvider? clock = null, TimeSpan? maximumPolicyAge = null)
    {
        Store = store; Trust = trust; Principals = principals; OrchestratorSigner = orchestratorSigner; AuthoritySigner = authoritySigner;
        Policy = policy; Clock = clock ?? TimeProvider.System; MaximumPolicyAge = maximumPolicyAge ?? TimeSpan.FromMinutes(5);
        // Policy provisioning is explicit via Store.SetCurrentPolicy; a restart must never reset a newer policy.
    }
    public SqliteRuntimeStore Store { get; }
    public TrustedDirectory Trust { get; }
    public PrincipalDirectory Principals { get; }
    public ArtifactSigner OrchestratorSigner { get; }
    public ArtifactSigner AuthoritySigner { get; }
    public IRuntimePolicy Policy { get; }
    public TimeProvider Clock { get; }
    public TimeSpan MaximumPolicyAge { get; }
    public void RegisterEvidenceRule(EvidenceRule rule) => _evidenceRules.Add(rule);
    public void RegisterSource(ISourceAdapter adapter)
    {
        var s = adapter.Registration;
        Trust.Validate(s.Capability, new(ArtifactType.ProcessCapability, "APS-Action-Executor", s.Signer.Issuer, Audience: s.Audience, RequirePinnedArtifact: true));
        Trust.Validate(s.Product, new(ArtifactType.DataProduct, "APS-Read-Publisher", s.Signer.Issuer, RequirePinnedArtifact: true));
        Store.Retain(s.Capability); Store.Retain(s.Product);
        _sources.Add(s.Capability.Artifact.Header.ContentDigest, adapter);
        foreach (var e in s.EvidenceRules) if (!_evidenceRules.Contains(e)) _evidenceRules.Add(e);
    }
    public ISourceAdapter GetSource(ArtifactRef capability)
    {
        Errors.Require(_sources.TryGetValue(capability.ContentDigest, out var adapter), "UNSUPPORTED_PROFILE", "Source capability adapter is not configured.");
        capability.RequireMatches(adapter!.Registration.Capability.Artifact, ArtifactType.ProcessCapability); return adapter;
    }
    /// <summary>Retention is not authority. Exact issuer/type/role trust is revalidated at use.</summary>
    public void Retain(SignedArtifact artifact, TrustRequirement requirement)
    { Trust.Validate(artifact, requirement); Store.Retain(artifact); }
    internal Artifact Validate(SqliteConnection c, SqliteTransaction? t, ArtifactRef reference, TrustRequirement requirement)
    {
        var signed = GetArtifact(c, t, reference); RequireNotRevoked(c, t, signed);
        return Trust.Validate(signed, requirement);
    }
    public WorkflowSnapshot Submit(SignedArtifact plan, IReadOnlyDictionary<string, JsonObject> payloads, AuthenticatedPrincipal caller)
    {
        var safePayloads = payloads.ToDictionary(x => x.Key, x => StrictJson.ParseObject(x.Value.ToJsonString()), StringComparer.Ordinal);
        Trust.Validate(plan, new(ArtifactType.ExecutionPlan, "APS-Orchestrator", OrchestratorSigner.Issuer));
        return Store.Transaction((c, t) =>
        {
            SqliteRuntimeStore.Retain(c, t, plan); var id = plan.Artifact.Body.Object("context").String("correlationId");
            ValidatePlan(c, t, plan.Artifact, safePayloads, caller, checkTime: true);
            var old = Scalar(c, t, "SELECT plan_ref FROM workflows WHERE workflow_id=$id", ("$id", id)) as string;
            if (old is not null)
            { ParseRef(old).RequireMatches(plan.Artifact); return GetWorkflow(c, t, id); }
            Execute(c, t, "INSERT INTO workflows VALUES($id,$plan,'PLANNED',NULL,NULL)", ("$id", id), ("$plan", RefJson(plan.Artifact.Reference)));
            var ordinal = 0;
            foreach (var n in plan.Artifact.Body.Array("steps"))
            { var step = n!.AsObject(); Execute(c, t, "INSERT INTO workflow_steps VALUES($id,$step,$ordinal,$payload,'PENDING',NULL,NULL)", ("$id", id), ("$step", step.String("stepId")), ("$ordinal", ordinal++), ("$payload", safePayloads[step.String("stepId")].ToJsonString())); }
            return GetWorkflow(c, t, id);
        });
    }
    public SignedArtifact Approve(string workflowId, AuthenticatedPrincipal approver, DateTimeOffset expiresAt, IEnumerable<string>? allowedSteps = null)
    {
        var workflow = Store.GetWorkflow(workflowId); var plan = Store.GetArtifact(workflow.PlanRef).Artifact;
        var steps = (allowedSteps ?? workflow.Steps.Select(x => x.StepId)).ToArray();
        Principals.RequireContext(approver, plan.Body.Object("context"), "Approver", approver: true);
        Errors.Require(approver.SubjectId != plan.Body.Object("context").String("subjectId"), "AUTH_DENIED", "Separation of duties requires another authenticated approver.");
        var approval = ArtifactFactory.Approval(AuthoritySigner, plan, approver, steps, expiresAt, Clock.GetUtcNow());
        AdmitAuthority(approval, approver); return approval;
    }
    public void AdmitAuthority(SignedArtifact authority, AuthenticatedPrincipal authenticatedIssuer)
    {
        var type = authority.Artifact.Type;
        Errors.Require(type is ArtifactType.Approval or ArtifactType.Authority, "APPROVAL_REQUIRED", "Policy evaluation is not an authority grant.");
        Trust.Validate(authority, new(type, "APS-Authority-Service", AuthoritySigner.Issuer));
        Store.Transaction((c, t) =>
        {
            SqliteRuntimeStore.Retain(c, t, authority); var a = authority.Artifact;
            var plan = Validate(c, t, a.Body.Ref("planRef"), new(ArtifactType.ExecutionPlan, "APS-Orchestrator", OrchestratorSigner.Issuer));
            var policy = ValidatePolicy(c, t, plan);
            ValidateAuthorityShape(c, t, a, plan, policy);
            Principals.RequireContext(authenticatedIssuer, plan.Body.Object("context"), type == ArtifactType.Approval ? "Approver" : "PolicyGranter", approver: true);
            if (type == ArtifactType.Approval)
                Errors.Require(a.Body.String("approverId") == authenticatedIssuer.SubjectId && authenticatedIssuer.SubjectId != plan.Body.Object("context").String("subjectId"), "AUTH_DENIED", "Approval must bind the authenticated independent approver.");
            Execute(c, t, "INSERT OR IGNORE INTO authorities VALUES($ref,$identity,$subject,$time)", ("$ref", RefKey(a.Reference)), ("$identity", authenticatedIssuer.Identity), ("$subject", authenticatedIssuer.SubjectId), ("$time", Clock.GetUtcNow().ToString("O")));
            return true;
        });
    }
    public WorkflowSnapshot Authorize(string workflowId, ArtifactRef authorityRef, AuthenticatedPrincipal caller)
    {
        return Store.Transaction((c, t) =>
        {
            var w = GetWorkflow(c, t, workflowId); var plan = Validate(c, t, w.PlanRef, new(ArtifactType.ExecutionPlan, "APS-Orchestrator", OrchestratorSigner.Issuer));
            Principals.RequireContext(caller, plan.Body.Object("context"), "Executor");
            var policy = ValidatePolicy(c, t, plan); var a = ValidateAuthority(c, t, authorityRef, plan, policy);
            var ids = AllowedSteps(a, plan); var stepsJson = ArtifactFactory.Strings(ids).ToJsonString();
            var authorityId = a.Type + "\n" + a.Header.Id;
            using (var cmd = Command(c, t, "SELECT refkey,workflow_id,plan_digest,steps FROM authority_uses WHERE authority_id=$id", ("$id", authorityId)))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                { Errors.Require(r.GetString(0) == RefKey(a.Reference) && r.GetString(1) == workflowId && r.GetString(2) == plan.PlanDigest && r.GetString(3) == stepsJson, "AUTHORITY_CONSUMED", "Authority is already bound to another durable workflow or scope.");
                    Errors.Require(w.AuthorityRef == authorityRef, "AUTHORITY_CONSUMED"); return w; }
            }
            Errors.Require(w.State == WorkflowState.PLANNED && w.AuthorityRef is null, "AUTHORITY_CONSUMED");
            Execute(c, t, "INSERT INTO authority_uses VALUES($id,$ref,$workflow,$digest,$steps,$time)", ("$id", authorityId), ("$ref", RefKey(a.Reference)), ("$workflow", workflowId), ("$digest", plan.PlanDigest), ("$steps", stepsJson), ("$time", Clock.GetUtcNow().ToString("O")));
            Execute(c, t, "UPDATE workflows SET authority_ref=$ref,state='AUTHORIZED' WHERE workflow_id=$id", ("$ref", RefJson(authorityRef)), ("$id", workflowId));
            return GetWorkflow(c, t, workflowId);
        }); // Deliberately distinct local transaction from source invocation/effect.
    }
    public WorkflowSnapshot ExecuteNext(string workflowId, AuthenticatedPrincipal caller)
    {
        var w = Store.GetWorkflow(workflowId); RequireWorkflowCaller(w, caller);
        if (w.Steps.Any(x => x.State == "UNKNOWN")) return Reconcile(workflowId, caller); // Always lookup before any possible resend.
        if (IsTerminal(w.State)) return w;
        Errors.Require(w.AuthorityRef is not null, "APPROVAL_REQUIRED", "An authenticated approval or bounded policy grant is required.");
        SourceDispatch? dispatch = null; ISourceAdapter? source = null;
        try
        {
            Store.Transaction((c, t) =>
            {
                var current = GetWorkflow(c, t, workflowId);
                Errors.Require(current.State is WorkflowState.AUTHORIZED or WorkflowState.EXECUTING, "CONFLICT");
                var stepState = current.Steps.FirstOrDefault(x => x.State != "COMPLETED");
                if (stepState is null) { SetWorkflowState(c, t, workflowId, WorkflowState.COMPLETED); return true; }
                Errors.Require(stepState.State == "PENDING", "CONFLICT", "An existing invocation must reconcile first.");
                var plan = Validate(c, t, current.PlanRef, new(ArtifactType.ExecutionPlan, "APS-Orchestrator", OrchestratorSigner.Issuer));
                var step = ArtifactBindings.GetStep(plan, stepState.StepId); source = GetSource(step.Ref("capabilityRef"));
                var authority = Validate(c, t, current.AuthorityRef!, new(Enum.Parse<ArtifactType>(current.AuthorityRef!.ArtifactType), "APS-Authority-Service", AuthoritySigner.Issuer));
                var invocation = ArtifactFactory.Invocation(OrchestratorSigner, plan, stepState.StepId, authority, source.Registration.Audience + "/receipts", Clock.GetUtcNow());
                SqliteRuntimeStore.Retain(c, t, invocation);
                // Persist the original invocation and UNKNOWN intent before transport; a process crash here is ambiguous, never a new invocation.
                Execute(c, t, "UPDATE workflow_steps SET state='UNKNOWN',invocation_ref=$inv WHERE workflow_id=$id AND step_id=$step", ("$inv", RefJson(invocation.Artifact.Reference)), ("$id", workflowId), ("$step", stepState.StepId));
                SetWorkflowState(c, t, workflowId, WorkflowState.EXECUTING);
                dispatch = new(invocation.Artifact.Reference, caller); return true;
            });
            var result = source!.Invoke(dispatch!);
            CompleteStep(workflowId, source, Store.GetArtifact(dispatch!.InvocationRef).Artifact, result);
        }
        catch (SourceRefusedException e) { MarkRefused(workflowId, dispatch?.InvocationRef, e.Code); }
        catch (ApsDdaException e)
        {
            // Only a source-enforced pre-commit refusal proves no new effect; a bad reply/verification failure remains unknown.
            if (dispatch is not null) MarkUnknown(workflowId, e.Code);
            else if (Store.GetWorkflow(workflowId).Steps.Any(x => x.State == "UNKNOWN")) MarkUnknown(workflowId, e.Code);
            else MarkRefused(workflowId, dispatch?.InvocationRef, e.Code);
        }
        catch (Exception) { MarkUnknown(workflowId, "OPERATION_UNKNOWN"); }
        return Store.GetWorkflow(workflowId);
    }
    public WorkflowSnapshot ExecuteAll(string workflowId, AuthenticatedPrincipal caller)
    {
        for (var i = 0; i < 256; i++)
        { var before = Store.GetWorkflow(workflowId); var after = ExecuteNext(workflowId, caller); if (IsTerminal(after.State) || after.State == WorkflowState.UNKNOWN_OUTCOME || after == before) return after; }
        throw new ApsDdaException("UNSUPPORTED_PROFILE", "Reference runner step bound exceeded.");
    }
    public WorkflowSnapshot Reconcile(string workflowId, AuthenticatedPrincipal caller)
    {
        var w = Store.GetWorkflow(workflowId); RequireWorkflowCaller(w, caller);
        foreach (var step in w.Steps.Where(x => x.State == "UNKNOWN"))
        {
            var invocation = Store.GetArtifact(step.InvocationRef!).Artifact; var source = GetSource(invocation.Body.Ref("capabilityRef"));
            try
            {
                var result = source.Lookup(Identity(invocation, source.Registration), caller);
                if (result is null) { MarkUnknown(workflowId, "OPERATION_UNKNOWN"); return Store.GetWorkflow(workflowId); }
                CompleteStep(workflowId, source, invocation, result);
            }
            catch (Exception) { MarkUnknown(workflowId, "OPERATION_UNKNOWN"); return Store.GetWorkflow(workflowId); }
        }
        return Store.GetWorkflow(workflowId);
    }
    /// <summary>No automatic resend even after not-found; this selected profile leaves absent/ambiguous original operations for accountable resolution.</summary>
    public WorkflowSnapshot Cancel(string workflowId, AuthenticatedPrincipal caller)
    {
        var w = Store.GetWorkflow(workflowId); RequireWorkflowCaller(w, caller);
        return Store.Transaction((c, t) =>
        {
            var current = GetWorkflow(c, t, workflowId);
            if (current.State == WorkflowState.COMPLETED) return current;
            var state = current.Steps.Any(x => x.State == "UNKNOWN") ? WorkflowState.UNKNOWN_OUTCOME : current.Steps.Any(x => x.State == "COMPLETED") ? WorkflowState.MANUAL_RESOLUTION_REQUIRED : WorkflowState.CANCELED;
            Execute(c, t, "INSERT OR IGNORE INTO metadata VALUES($key,'1')", ("$key", "cancel:" + workflowId));
            Execute(c, t, "UPDATE workflow_steps SET state='CANCELED' WHERE workflow_id=$id AND state='PENDING'", ("$id", workflowId));
            SetWorkflowState(c, t, workflowId, state, state == WorkflowState.MANUAL_RESOLUTION_REQUIRED ? "CANCEL_NOT_POSSIBLE" : null);
            return GetWorkflow(c, t, workflowId);
        });
    }
    private void CompleteStep(string workflowId, ISourceAdapter source, Artifact invocation, SourceResult result)
    {
        // A source transport response cannot establish completion. Verification can itself fail -> UNKNOWN.
        Trust.Validate(result.Receipt, new(ArtifactType.OperationReceipt, "APS-Action-Executor", source.Registration.Signer.Issuer));
        Trust.Validate(result.PostconditionEvidence, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", source.Registration.Signer.Issuer));
        ArtifactBindings.RequireReceiptBinding(result.Receipt.Artifact, invocation);
        Errors.Require(result.Receipt.Artifact.Body.String("outcome") == "completed" && result.Receipt.Artifact.Body.Refs("postconditionEvidenceRefs").Count == 1);
        result.Receipt.Artifact.Body.Refs("postconditionEvidenceRefs")[0].RequireMatches(result.PostconditionEvidence.Artifact, ArtifactType.EvidenceBundle);
        source.VerifyPostcondition(result, invocation);
        Store.Transaction((c, t) =>
        {
            RequireNotRevoked(c, t, result.Receipt); RequireNotRevoked(c, t, result.PostconditionEvidence);
            SqliteRuntimeStore.Retain(c, t, result.Receipt); SqliteRuntimeStore.Retain(c, t, result.PostconditionEvidence);
            Execute(c, t, "UPDATE workflow_steps SET state='COMPLETED',receipt_ref=$receipt WHERE workflow_id=$id AND step_id=$step AND invocation_ref=$inv", ("$receipt", RefJson(result.Receipt.Artifact.Reference)), ("$id", workflowId), ("$step", invocation.Body.String("stepId")), ("$inv", RefJson(invocation.Reference)));
            var w = GetWorkflow(c, t, workflowId);
            var canceled = Scalar(c, t, "SELECT value FROM metadata WHERE key=$key", ("$key", "cancel:" + workflowId)) is not null;
            SetWorkflowState(c, t, workflowId, canceled || w.Steps.Any(x => x.State is "CANCELED" or "REFUSED") ? WorkflowState.MANUAL_RESOLUTION_REQUIRED : w.Steps.All(x => x.State == "COMPLETED") ? WorkflowState.COMPLETED : WorkflowState.AUTHORIZED);
            return true;
        });
    }
    private void MarkUnknown(string id, string error) => Store.Transaction((c, t) => { SetWorkflowState(c, t, id, WorkflowState.UNKNOWN_OUTCOME, error); return true; });
    private void MarkRefused(string id, ArtifactRef? invocation, string code) => Store.Transaction((c, t) =>
    {
        if (invocation is not null) Execute(c, t, "UPDATE workflow_steps SET state='REFUSED' WHERE workflow_id=$id AND invocation_ref=$inv", ("$id", id), ("$inv", RefJson(invocation)));
        var w = GetWorkflow(c, t, id);
        SetWorkflowState(c, t, id, w.Steps.Any(x => x.State == "COMPLETED") ? WorkflowState.MANUAL_RESOLUTION_REQUIRED : WorkflowState.REJECTED, code); return true;
    });
    private void RequireWorkflowCaller(WorkflowSnapshot w, AuthenticatedPrincipal caller)
        => Principals.RequireContext(caller, Store.GetArtifact(w.PlanRef).Artifact.Body.Object("context"), "Executor");
    private static bool IsTerminal(WorkflowState state) => state is WorkflowState.COMPLETED or WorkflowState.REJECTED or WorkflowState.FAILED or WorkflowState.CANCELED or WorkflowState.MANUAL_RESOLUTION_REQUIRED;
    internal static OperationIdentity Identity(Artifact invocation, SourceRegistration source)
    { var b = invocation.Body; return new(b.Object("context").String("tenantId"), source.Signer.Issuer, b.String("idempotencyDomain"), b.String("idempotencyKey"), b.String("invocationFingerprint")); }

    private void ValidatePlan(SqliteConnection c, SqliteTransaction t, Artifact plan, IReadOnlyDictionary<string, JsonObject> payloads, AuthenticatedPrincipal caller, bool checkTime)
    {
        RequireNotRevoked(c, t, GetArtifact(c, t, plan.Reference)); ArtifactBindings.RequirePlanDag(plan);
        var body = plan.Body; Principals.RequireContext(caller, body.Object("context"), "Executor");
        Errors.Require(body.Array("steps").Count <= 256 && payloads.Count == body.Array("steps").Count);
        _ = ValidatePolicy(c, t, plan);
        var decisionRefs = GetArtifact(c, t, body.Ref("decisionRef")).Artifact.Body.Refs("evidenceRefs");
        var planEvidence = body.Array("steps").SelectMany(x => x!.AsObject().Refs("evidenceRefs")).Select(RefKey).ToHashSet(StringComparer.Ordinal);
        Errors.Require(decisionRefs.Count == planEvidence.Count && planEvidence.SetEquals(decisionRefs.Select(RefKey)), "INVALID_ARTIFACT", "All decision evidence must be represented by the exact plan evidence set.");
        if (checkTime) RequirePlanTime(plan);
        foreach (var n in body.Array("steps"))
        {
            var step = n!.AsObject(); Errors.Require(payloads.TryGetValue(step.String("stepId"), out var payload));
            var source = GetSource(step.Ref("capabilityRef")).Registration;
            var capability = Validate(c, t, step.Ref("capabilityRef"), new(ArtifactType.ProcessCapability, "APS-Action-Executor", source.Signer.Issuer, Audience: source.Audience, RequirePinnedArtifact: true));
            RequireStepBinding(step, capability, payload!, body.Object("context"), source);
            ValidateEvidence(c, t, step.Refs("evidenceRefs"), plan, source);
        }
    }
    internal Artifact ValidatePolicy(SqliteConnection c, SqliteTransaction? t, Artifact plan)
    {
        var context = plan.Body.Object("context"); RequireCurrentPolicy(c, t, Policy.PolicyReference);
        Errors.Require(Jcs.Canonicalize(context.Array("policyRefs")).AsSpan().SequenceEqual(Jcs.Canonicalize(new JsonArray(Policy.PolicyReference))), "POLICY_EVALUATION_FAILED");
        var decision = Validate(c, t, plan.Body.Ref("decisionRef"), new(ArtifactType.DecisionSpec, "APS-Orchestrator", OrchestratorSigner.Issuer));
        ArtifactBindings.RequireSameContext(context, decision.Body.Object("context"));
        Errors.Require(decision.Body.String("actionEligibility") == "eligible", "POLICY_EVALUATION_FAILED", "Decision is not eligible for source action.");
        var policy = Validate(c, t, plan.Body.Ref("policyRef"), new(ArtifactType.PolicyDecision, "APS-Policy-Service", AuthoritySigner.Issuer));
        ArtifactBindings.RequireSameContext(context, policy.Body.Object("context"));
        var expected = Policy.Evaluate(decision); var p = policy.Body;
        Errors.Require(expected.Outcome is "allow" or "allow-with-human-approval" && p.String("outcome") == expected.Outcome && p.String("policyVersion") == Policy.PolicyReference.String("version") &&
            p.String("decisionDigest") == RuntimeDigests.PolicyDecision(decision, Policy.PolicyReference, expected) &&
            Jcs.Canonicalize(p["obligations"] ?? new JsonArray()).AsSpan().SequenceEqual(Jcs.Canonicalize(ArtifactFactory.Strings(expected.Obligations))), "POLICY_EVALUATION_FAILED");
        var age = Clock.GetUtcNow() - p.Time("evaluatedAt"); Errors.Require(age >= TimeSpan.Zero && age <= MaximumPolicyAge, "POLICY_EVALUATION_FAILED", "Policy evidence is outside its accepted time window.");
        return policy;
    }
    private void RequirePlanTime(Artifact plan)
    {
        var now = Clock.GetUtcNow(); var window = plan.Body.Object("executionWindow");
        Errors.Require(window.Time("notBefore") <= now && now < window.Time("expiresAt"), "AUTHORITY_EXPIRED", "Plan execution window is not current.");
    }
    internal void RequireStepBinding(JsonObject step, Artifact capability, JsonObject payload, JsonObject context, SourceRegistration source)
    {
        var cb = capability.Body; var idem = cb.Object("idempotency");
        Errors.Require(step.String("payloadDigest") == BindingDigests.Payload(payload) && step.String("invocationFingerprint") == BindingDigests.Invocation(context, capability.Reference, step.String("target"), payload, step.String("expectedState"), step.String("idempotencyDomain"), source.Audience));
        Errors.Require(step.String("idempotencyDomain") == idem.String("domain") && step.String("idempotencyKey").Length >= idem["minimumKeyLength"]!.GetValue<int>() && step.String("risk") == cb.String("riskTier"));
        Errors.Require(step.String("compensation") == "non-compensable" && cb.Object("compensation").String("mode") == "non-compensable" && step.String("manualRecoveryOwner") == cb.Object("compensation").String("manualRecoveryOwner"), "UNSUPPORTED_PROFILE", "Only declared accountable manual recovery is implemented.");
        Errors.Require(cb.Object("precondition").String("mechanism") == "exact-local-version", "UNSUPPORTED_PROFILE");
        _ = CanonicalQuantity.Parse(step.String("expectedState"));
    }
    internal void ValidateEvidence(SqliteConnection c, SqliteTransaction? t, IReadOnlyList<ArtifactRef> refs, Artifact plan, SourceRegistration source)
    {
        var decision = GetArtifact(c, t, plan.Body.Ref("decisionRef")).Artifact;
        var decisionRefs = decision.Body.Refs("evidenceRefs");
        Errors.Require(refs.Count > 0 && refs.Distinct().Count() == refs.Count);
        var seenProducts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in refs)
        {
            Errors.Require(decisionRefs.Any(x => RefKey(x) == RefKey(reference)));
            var signed = GetArtifact(c, t, reference); var evidence = signed.Artifact;
            evidence.RequireType(ArtifactType.EvidenceBundle); ArtifactBindings.RequireSameContext(plan.Body.Object("context"), evidence.Body.Object("context"));
            Errors.Require(evidence.Body.String("observationMode") == "observational", "UNSUPPORTED_PROFILE", "No coordinated snapshot mechanism is implemented.");
            foreach (var n in evidence.Body.Array("items"))
            {
                var item = n!.AsObject(); var productRef = item.Ref("productRef");
                var rule = _evidenceRules.SingleOrDefault(x => RefKey(x.ProductRef) == RefKey(productRef));
                Errors.Require(rule is not null, "UNSUPPORTED_PROFILE", "No reviewed evidence semantics are configured.");
                Validate(c, t, reference, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", rule!.Issuer));
                var product = Validate(c, t, productRef, new(ArtifactType.DataProduct, "APS-Read-Publisher", rule.Issuer, RequirePinnedArtifact: true));
                var access = product.Body.Object("access"); var context = plan.Body.Object("context");
                Errors.Require(access.Array("permittedPurposes").Any(x => x!.GetValue<string>() == context.String("purpose")) && access.String("classification") == source.Classification && access.Array("residency").Any(x => x!.GetValue<string>() == source.Residency), "AUTH_DENIED");
                Errors.Require(Jcs.Canonicalize(item.Array("semanticRefs")).AsSpan().SequenceEqual(Jcs.Canonicalize(rule.Semantics)) &&
                    Jcs.Canonicalize(product.Body.Array("semanticRefs")).AsSpan().SequenceEqual(Jcs.Canonicalize(rule.Semantics)), "INVALID_ARTIFACT", "Evidence product semantics do not match the reviewed exact binding.");
                var age = Clock.GetUtcNow() - item.Time("observedAt");
                Errors.Require(age >= TimeSpan.Zero && age <= rule.MaxAge && item.String("qualityState") == rule.QualityState && item.String("cacheStatus") == "live", "EVIDENCE_STALE", "Evidence freshness, quality or cache contract refused action.");
                var value = GetEvidenceValue(c, t, item.String("valueRef"));
                bool valid; try { valid = rule.ValidateValue((JsonObject)value.DeepClone()); } catch (Exception) { valid = false; }
                Errors.Require(valid, "INVALID_ARTIFACT", "Retained evidence does not satisfy reviewed domain meaning.");
                seenProducts.Add(RefKey(productRef));
            }
        }
        Errors.Require(source.EvidenceRules.All(x => seenProducts.Contains(RefKey(x.ProductRef))), "INVALID_ARTIFACT", "Required source evidence is missing.");
    }
    private static string[] AllowedSteps(Artifact a, Artifact plan) => a.Type == ArtifactType.Approval ? a.Body.Array("allowedStepIds").Select(x => x!.GetValue<string>()).ToArray() : plan.Body.Array("steps").Select(x => x!.AsObject().String("stepId")).ToArray();
    internal Artifact ValidateAuthority(SqliteConnection c, SqliteTransaction? t, ArtifactRef reference, Artifact plan, Artifact policy)
    {
        Errors.Require(reference.ArtifactType is "Approval" or "Authority", "APPROVAL_REQUIRED");
        var a = Validate(c, t, reference, new(Enum.Parse<ArtifactType>(reference.ArtifactType), "APS-Authority-Service", AuthoritySigner.Issuer));
        ValidateAuthorityShape(c, t, a, plan, policy);
        using var cmd = Command(c, t, "SELECT authenticated_identity,authenticated_subject FROM authorities WHERE refkey=$ref", ("$ref", RefKey(reference)));
        using var r = cmd.ExecuteReader(); Errors.Require(r.Read(), "APPROVAL_REQUIRED", "Authority has no trusted authenticated admission.");
        var identity = r.GetString(0); var subject = r.GetString(1);
        var issuer = Principals.ResolveAuthenticated(identity);
        Principals.RequireContext(issuer, plan.Body.Object("context"), a.Type == ArtifactType.Approval ? "Approver" : "PolicyGranter", approver: true);
        Errors.Require(subject == issuer.SubjectId && (a.Type != ArtifactType.Approval || a.Body.String("approverId") == subject), "AUTH_DENIED");
        return a;
    }
    private void ValidateAuthorityShape(SqliteConnection c, SqliteTransaction? t, Artifact a, Artifact plan, Artifact policy)
    {
        ArtifactBindings.RequirePlanReference(a, plan); RequirePlanTime(plan);
        var now = Clock.GetUtcNow(); var body = a.Body;
        Errors.Require(body.Time("issuedAt") == a.Header.IssuedAt && body.Time("issuedAt") <= now && now < body.Time("expiresAt") && body.Time("expiresAt") <= plan.Body.Object("executionWindow").Time("expiresAt"), "AUTHORITY_EXPIRED");
        if (a.Type == ArtifactType.Approval)
        {
            ArtifactBindings.RequireApprovalBinding(a, plan, policy);
            Errors.Require(body.String("effectSummary") == ArtifactFactory.EffectSummary(plan, AllowedSteps(a, plan)));
        }
        else
        {
            Errors.Require(body.String("authorityKind") == "policy-grant" && policy.Body.String("outcome") == "allow", "APPROVAL_REQUIRED", "Policy grant cannot replace required human approval.");
            Errors.Require(body.String("revocationRef") == AuthoritySigner.Issuer + "/status", "AUTH_DENIED", "Grant status source is not the configured local authority service.");
            var scope = body.Object("scope"); Errors.Require(scope["maximumAmount"] is null, "UNSUPPORTED_PROFILE", "Money authority is unsupported in this internal-quantity binding.");
            var max = CanonicalQuantity.Parse(scope.String("maximumQuantity")); var rate = CanonicalQuantity.Parse(scope.String("maximumRate")); long total = 0;
            var requiredEvidence = plan.Body.Array("steps").SelectMany(x => x!.AsObject().Refs("evidenceRefs")).Select(RefKey).ToHashSet(StringComparer.Ordinal);
            Errors.Require(scope.Refs("evidenceRefs").Count == requiredEvidence.Count && requiredEvidence.SetEquals(scope.Refs("evidenceRefs").Select(RefKey)));
            foreach (var n in plan.Body.Array("steps"))
            {
                var step = n!.AsObject(); var adapter = GetSource(step.Ref("capabilityRef")); var source = adapter.Registration; var cap = source.Capability.Artifact;
                Errors.Require(source.PolicyGrantAllowed && cap.Body.Object("authority")["policyGrantAllowed"]!.GetValue<bool>() && !cap.Body.Object("authority")["humanApprovalRequired"]!.GetValue<bool>() && cap.Body.String("riskTier") == "low", "APPROVAL_REQUIRED");
                Errors.Require(scope.String("resourceBoundary") == step.String("target") && scope.String("purpose") == plan.Body.Object("context").String("purpose") && scope.String("riskTier") == "low" && max <= source.MaximumPolicyQuantity && rate <= source.MaximumPolicyOperationsPerMinute, "AUTH_DENIED");
                Errors.Require(scope.Refs("capabilityRefs").Count == 1 && RefKey(scope.Refs("capabilityRefs")[0]) == RefKey(cap.Reference) && scope.Array("sourceAudiences").Count == 1 && scope.Array("sourceAudiences")[0]!.GetValue<string>() == source.Audience && Jcs.Canonicalize(scope.Array("allowedTransitions")).AsSpan().SequenceEqual(Jcs.Canonicalize(cap.Body.Array("allowedTransitions"))));
                Errors.Require(step.Refs("evidenceRefs").All(x => scope.Refs("evidenceRefs").Any(y => RefKey(x) == RefKey(y))));
                var payload = GetPayload(c, t, plan.Body.Object("context").String("correlationId"), step.String("stepId"));
                var quantity = adapter.MeasureQuantity((JsonObject)payload.DeepClone()); Errors.Require(quantity > 0);
                total = checked(total + quantity);
            }
            Errors.Require(total <= max, "QUOTA_EXCEEDED", "Exact plan exceeds the source-bound total policy quantity.");
        }
    }
    internal (Artifact Invocation, Artifact Plan, Artifact Authority, JsonObject Payload) ValidateDispatch(SqliteConnection c, SqliteTransaction t,
        SourceDispatch dispatch, SourceRegistration source, ILocalSourceCommand command, bool checkRate = true)
    {
        var inv = Validate(c, t, dispatch.InvocationRef, new(ArtifactType.CommandInvocation, "APS-Orchestrator", OrchestratorSigner.Issuer));
        var context = inv.Body.Object("context"); Principals.RequireContext(dispatch.Caller, context, "Executor");
        var plan = Validate(c, t, inv.Body.Ref("planRef"), new(ArtifactType.ExecutionPlan, "APS-Orchestrator", OrchestratorSigner.Issuer));
        var workflow = GetWorkflow(c, t, context.String("correlationId")); workflow.PlanRef.RequireMatches(plan);
        Errors.Require(Scalar(c, t, "SELECT value FROM metadata WHERE key=$key", ("$key", "cancel:" + workflow.WorkflowId)) is null, "CANCEL_NOT_POSSIBLE", "Cancellation intent prevents a new effect; existing operations must reconcile.");
        Errors.Require(workflow.State is WorkflowState.EXECUTING or WorkflowState.UNKNOWN_OUTCOME, "AUTH_DENIED", "Workflow is not dispatchable.");
        var step = ArtifactBindings.GetStep(plan, inv.Body.String("stepId")); var payload = GetPayload(c, t, workflow.WorkflowId, step.String("stepId"));
        var capability = Validate(c, t, inv.Body.Ref("capabilityRef"), new(ArtifactType.ProcessCapability, "APS-Action-Executor", source.Signer.Issuer, Audience: source.Audience, RequirePinnedArtifact: true));
        inv.Body.Ref("capabilityRef").RequireMatches(source.Capability.Artifact); ArtifactBindings.RequireInvocationBinding(inv, plan, payload, capability, source.Audience);
        RequireStepBinding(step, capability, payload, context, source); RequirePlanTime(plan);
        Errors.Require(Clock.GetUtcNow() < step.Time("expiry") && step.Time("expiry") <= plan.Body.Object("executionWindow").Time("expiresAt"), "AUTHORITY_EXPIRED");
        Errors.Require(source.Resources.Contains(step.String("target")) && dispatch.Caller.Resources.Contains(step.String("target")) && source.Purposes.Contains(context.String("purpose")) && source.SubjectIds.Contains(dispatch.Caller.SubjectId) && source.WorkloadIds.Contains(dispatch.Caller.WorkloadId) &&
            source.Roles.Overlaps(dispatch.Caller.Roles) && dispatch.Caller.CapabilityDigests.Contains(capability.Header.ContentDigest) && dispatch.Caller.Audiences.Contains(source.Audience) &&
            source.Classification == context.String("classification") && source.Residency == context.String("residency"), "AUTH_DENIED");
        Errors.Require(!source.RequiresExternalHardInvariant && (capability.Body["invariantRefs"] is not JsonArray invariants || invariants.Count == 0), "UNSUPPORTED_PROFILE", "Required external hard invariant has no enforcing mechanism.");
        Errors.Require(capability.Body.String("riskTier") != "prohibited" && capability.Body.Array("allowedTransitions").Any(x => x!.GetValue<string>() == command.Transition) && capability.Body.Object("commandSemantic").String("localBinding") == command.Transition, "AUTH_DENIED");
        var quantity = command.Quantity(payload); Errors.Require(quantity <= source.MaximumQuantity, "QUOTA_EXCEEDED");
        var policy = ValidatePolicy(c, t, plan); Errors.Require(inv.Body.Refs("authorityRefs").Count == 1 && workflow.AuthorityRef is not null && RefKey(inv.Body.Refs("authorityRefs")[0]) == RefKey(workflow.AuthorityRef), "APPROVAL_REQUIRED");
        var authority = ValidateAuthority(c, t, workflow.AuthorityRef!, plan, policy);
        Errors.Require(AllowedSteps(authority, plan).Contains(step.String("stepId"), StringComparer.Ordinal), "APPROVAL_REQUIRED");
        var binding = Scalar(c, t, "SELECT workflow_id FROM authority_uses WHERE authority_id=$id AND refkey=$ref AND plan_digest=$digest", ("$id", authority.Type + "\n" + authority.Header.Id), ("$ref", RefKey(authority.Reference)), ("$digest", plan.PlanDigest)) as string;
        Errors.Require(binding == workflow.WorkflowId, "AUTHORITY_CONSUMED");
        Errors.Require(workflow.Steps.Single(x => x.StepId == step.String("stepId")).InvocationRef is { } retained && RefKey(retained) == RefKey(inv.Reference), "AUTH_DENIED");
        Errors.Require(step.Array("dependsOn").All(x => workflow.Steps.Single(s => s.StepId == x!.GetValue<string>()).State == "COMPLETED"), "PRECONDITION_FAILED");
        ValidateEvidence(c, t, GetArtifact(c, t, plan.Body.Ref("decisionRef")).Artifact.Body.Refs("evidenceRefs"), plan, source);
        if (authority.Type == ArtifactType.Authority && checkRate)
        {
            var limit = CanonicalQuantity.Parse(authority.Body.Object("scope").String("maximumRate"));
            var count = Convert.ToInt64(Scalar(c, t, "SELECT count(*) FROM source_operations WHERE tenant_id=$tenant AND source_issuer=$source AND committed_at>$cutoff", ("$tenant", context.String("tenantId")), ("$source", source.Signer.Issuer), ("$cutoff", Clock.GetUtcNow().AddMinutes(-1).ToUnixTimeMilliseconds())));
            Errors.Require(count < limit && count < source.MaximumPolicyOperationsPerMinute, "QUOTA_EXCEEDED", "Source-bound rolling one-minute rate limit refused action.");
        }
        return (inv, plan, authority, payload);
    }
}
