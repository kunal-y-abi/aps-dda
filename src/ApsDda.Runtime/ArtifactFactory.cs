using System.Text.Json.Nodes;
using ApsDda.Core;

namespace ApsDda.Runtime;

/// <summary>Original-schema construction helpers. These create integrity-protected artifacts, not trust or authority by themselves.</summary>
public static class ArtifactFactory
{
    public static JsonObject Context(AuthenticatedPrincipal caller, string purpose, Guid workflowId, JsonObject policyReference) => new()
    {
        ["tenantId"] = caller.TenantId, ["subjectId"] = caller.SubjectId, ["workloadId"] = caller.WorkloadId, ["executorId"] = caller.ExecutorId,
        ["purpose"] = purpose, ["correlationId"] = workflowId.ToString("D"), ["policyRefs"] = new JsonArray(policyReference.DeepClone()),
        ["classification"] = caller.Classification, ["residency"] = caller.Residency
    };
    public static JsonObject Semantic(string ns, string term, string kind, string localBinding, string version = "1.0.0") => new()
    { ["namespace"] = ns, ["term"] = term, ["version"] = version, ["kind"] = kind, ["localBinding"] = localBinding };
    /// <summary>External JSON resources explicitly adopt the experimental JCS SHA-256 convention.</summary>
    public static JsonObject SchemaReference(string uri, JsonObject schema, string version = "1.0.0") => new()
    { ["uri"] = uri, ["version"] = version, ["digest"] = Jcs.Sha256Digest(schema) };
    public static SignedArtifact DataProduct(ArtifactSigner signer, string name, JsonArray semanticRefs, JsonObject schemaReference,
        IEnumerable<string> purposes, string residency, DateTimeOffset now, string classification = "internal", string grain = "one source-owned resource")
    {
        return signer.Issue(ArtifactType.DataProduct, new JsonObject
        {
            ["name"] = name, ["owner"] = signer.Issuer, ["authoritativeStatus"] = "authoritative", ["schema"] = schemaReference.DeepClone(),
            ["semanticRefs"] = semanticRefs.DeepClone(), ["grain"] = grain,
            ["timeSemantics"] = new JsonObject { ["validTime"] = "observation time", ["transactionTime"] = "local commit time", ["readConsistency"] = "source-current", ["correctionBehavior"] = "new immutable evidence", ["historyBehavior"] = "retained operation postconditions" },
            ["quality"] = new JsonObject { ["freshnessSlo"] = "PT5M", ["qualityStateValues"] = new JsonArray("good") },
            ["access"] = new JsonObject { ["classification"] = classification, ["permittedPurposes"] = Strings(purposes), ["retention"] = "administrator-managed; no automatic deletion", ["residency"] = new JsonArray(residency) },
            ["readContract"] = new JsonObject { ["permittedQueryShapes"] = new JsonArray("exact-key"), ["maxRows"] = 1, ["maxScan"] = "1 resource", ["maxDuration"] = "PT5S", ["paging"] = "none" },
            ["evidenceProfile"] = new JsonObject { ["includeObservedAt"] = true, ["includeSourceVersion"] = true, ["includeQualityState"] = true }
        }, now);
    }
    public static SignedArtifact ProcessCapability(ArtifactSigner signer, string name, JsonObject commandSemantic, string endpoint, string audience,
        JsonObject inputSchema, JsonObject resultSchema, string domain, string resourceScope, string manualOwner, DateTimeOffset now,
        bool policyGrantAllowed = false, string riskTier = "moderate") => signer.Issue(ArtifactType.ProcessCapability, new JsonObject
    {
        ["name"] = name, ["owner"] = signer.Issuer, ["commandSemantic"] = commandSemantic.DeepClone(),
        ["endpoint"] = new JsonObject { ["endpoint"] = endpoint, ["audience"] = audience, ["authentication"] = "other", ["trustKeyRef"] = signer.KeyId },
        ["inputSchema"] = inputSchema.DeepClone(), ["resultSchema"] = resultSchema.DeepClone(), ["resourceScope"] = resourceScope,
        ["allowedTransitions"] = new JsonArray(commandSemantic.String("localBinding")),
        ["precondition"] = new JsonObject { ["required"] = true, ["mechanism"] = "exact-local-version" },
        ["idempotency"] = new JsonObject { ["domain"] = domain, ["minimumKeyLength"] = 16, ["retention"] = "indefinite-until-administrator-archive", ["duplicateBehavior"] = "original-signed-receipt; fingerprint-profile=" + BindingDigests.InvocationProfile, ["operationLookup"] = audience + "/operations" },
        ["authority"] = new JsonObject { ["humanApprovalRequired"] = !policyGrantAllowed, ["policyGrantAllowed"] = policyGrantAllowed, ["sourceAuthorization"] = true },
        ["riskTier"] = riskTier, ["receiptProfile"] = new JsonObject { ["sourceIssued"] = true, ["postconditionRequired"] = true },
        ["cancellation"] = "stop undispatched steps; no undo", ["compensation"] = new JsonObject { ["mode"] = "non-compensable", ["manualRecoveryOwner"] = manualOwner }
    }, now);
    public static SignedArtifact Evidence(ArtifactSigner signer, JsonObject context, Artifact product, JsonArray semantics,
        string queryDigest, string sourceVersion, string valueRef, DateTimeOffset observedAt, DateTimeOffset now,
        string qualityState = "good", string cacheStatus = "live") => signer.Issue(ArtifactType.EvidenceBundle, new JsonObject
    {
        ["context"] = context.DeepClone(), ["observationMode"] = "observational", ["items"] = new JsonArray(new JsonObject
        { ["productRef"] = product.Reference.ToJson(), ["semanticRefs"] = semantics.DeepClone(), ["queryDigest"] = queryDigest,
            ["observedAt"] = observedAt.ToString("O"), ["sourceVersion"] = sourceVersion, ["qualityState"] = qualityState,
            ["cacheStatus"] = cacheStatus, ["valueRef"] = valueRef })
    }, now);
    public static SignedArtifact Decision(ArtifactSigner signer, JsonObject context, string owner, string question, string objective,
        IEnumerable<string> hardConstraints, IEnumerable<string> alternatives, IEnumerable<ArtifactRef> evidence,
        string methodType, string methodVersion, JsonObject parameters, IEnumerable<string> assumptions, IEnumerable<string> uncertainty,
        string recommendation, string eligibility, DateTimeOffset now, JsonObject? extensions = null)
    {
        var body = new JsonObject
        {
            ["context"] = context.DeepClone(), ["accountableOwner"] = owner, ["question"] = question, ["objective"] = objective,
            ["hardConstraints"] = Strings(hardConstraints), ["alternatives"] = Strings(alternatives), ["evidenceRefs"] = Refs(evidence),
            ["method"] = new JsonObject { ["type"] = methodType, ["version"] = methodVersion, ["parameterDigest"] = Jcs.Sha256Digest(parameters) },
            ["assumptions"] = Strings(assumptions), ["uncertainty"] = Strings(uncertainty), ["recommendation"] = recommendation, ["actionEligibility"] = eligibility
        };
        if (extensions is not null) body["extensions"] = extensions.DeepClone();
        return signer.Issue(ArtifactType.DecisionSpec, body, now);
    }
    public static SignedArtifact PolicyDecision(ArtifactSigner signer, Artifact decision, IRuntimePolicy policy, DateTimeOffset now)
    {
        var evaluation = policy.Evaluate(decision);
        return signer.Issue(ArtifactType.PolicyDecision, new JsonObject { ["context"] = decision.Body.Object("context").DeepClone(),
            ["policyVersion"] = policy.PolicyReference.String("version"), ["evaluatedAt"] = now.ToString("O"), ["outcome"] = evaluation.Outcome,
            ["decisionDigest"] = RuntimeDigests.PolicyDecision(decision, policy.PolicyReference, evaluation), ["obligations"] = Strings(evaluation.Obligations) }, now);
    }
    public static JsonObject PlanStep(string stepId, IEnumerable<string> dependencies, Artifact capability, string target, JsonObject payload,
        string expectedVersion, string idempotencyKey, IEnumerable<ArtifactRef> evidence, JsonObject context, DateTimeOffset expiry, string manualOwner)
    {
        var b = capability.Body; var domain = b.Object("idempotency").String("domain");
        return new JsonObject { ["stepId"] = stepId, ["dependsOn"] = Strings(dependencies), ["capabilityRef"] = capability.Reference.ToJson(),
            ["target"] = target, ["payloadDigest"] = BindingDigests.Payload(payload), ["expectedState"] = expectedVersion,
            ["idempotencyKey"] = idempotencyKey, ["idempotencyDomain"] = domain,
            ["invocationFingerprint"] = BindingDigests.Invocation(context, capability.Reference, target, payload, expectedVersion, domain, b.Object("endpoint").String("audience")),
            ["evidenceRefs"] = Refs(evidence), ["risk"] = b.String("riskTier"), ["expiry"] = expiry.ToString("O"), ["compensation"] = "non-compensable", ["manualRecoveryOwner"] = manualOwner };
    }
    public static SignedArtifact Plan(ArtifactSigner signer, Artifact decision, Artifact policyDecision, IEnumerable<JsonObject> steps,
        DateTimeOffset notBefore, DateTimeOffset expiry, DateTimeOffset now, JsonObject? extensions = null)
    {
        var body = new JsonObject { ["context"] = decision.Body.Object("context").DeepClone(), ["decisionRef"] = decision.Reference.ToJson(), ["policyRef"] = policyDecision.Reference.ToJson(),
            ["executionWindow"] = new JsonObject { ["notBefore"] = notBefore.ToString("O"), ["expiresAt"] = expiry.ToString("O") },
            ["steps"] = new JsonArray(steps.Select(x => (JsonNode?)x.DeepClone()).ToArray()) };
        if (extensions is not null) body["extensions"] = extensions.DeepClone();
        return signer.Issue(ArtifactType.ExecutionPlan, body, now);
    }
    public static SignedArtifact Approval(ArtifactSigner signer, Artifact plan, AuthenticatedPrincipal approver,
        IEnumerable<string> allowedStepIds, DateTimeOffset expiry, DateTimeOffset now) => signer.Issue(ArtifactType.Approval, new JsonObject
    {
        ["context"] = plan.Body.Object("context").DeepClone(), ["planRef"] = plan.Reference.ToJson(), ["planDigest"] = plan.PlanDigest,
        ["policyRef"] = plan.Body.Ref("policyRef").ToJson(), ["approverId"] = approver.SubjectId,
        ["authenticationEvidenceRef"] = "urn:aps-dda:authentication:server-admission", ["issuedAt"] = now.ToString("O"), ["expiresAt"] = expiry.ToString("O"),
        ["singleUse"] = true, ["allowedStepIds"] = Strings(allowedStepIds), ["effectSummary"] = EffectSummary(plan, allowedStepIds)
    }, now);
    public static string EffectSummary(Artifact plan, IEnumerable<string> steps) => string.Join("; ", steps.Select(id =>
    { var s = ArtifactBindings.GetStep(plan, id); return id + " target=" + s.String("target") + " payload=" + s.String("payloadDigest") + " expected=" + s.String("expectedState") + " capability=" + s.Ref("capabilityRef").ContentDigest; }));
    public static SignedArtifact PolicyGrant(ArtifactSigner signer, Artifact plan, Artifact capability, string resource,
        IEnumerable<ArtifactRef> evidence, string purpose, long quantity, int rate, DateTimeOffset expiry, DateTimeOffset now,
        string revocationRef) => signer.Issue(ArtifactType.Authority, new JsonObject
    {
        ["context"] = plan.Body.Object("context").DeepClone(), ["authorityKind"] = "policy-grant", ["planRef"] = plan.Reference.ToJson(), ["planDigest"] = plan.PlanDigest,
        ["scope"] = new JsonObject { ["capabilityRefs"] = Refs([capability.Reference]), ["resourceBoundary"] = resource, ["purpose"] = purpose,
            ["maximumQuantity"] = CanonicalQuantity.Format(quantity), ["maximumRate"] = CanonicalQuantity.Format(rate),
            ["sourceAudiences"] = new JsonArray(capability.Body.Object("endpoint").String("audience")), ["allowedTransitions"] = capability.Body.Array("allowedTransitions").DeepClone(),
            ["riskTier"] = capability.Body.String("riskTier"), ["evidenceRefs"] = Refs(evidence) },
        ["issuedAt"] = now.ToString("O"), ["expiresAt"] = expiry.ToString("O"), ["revocationRef"] = revocationRef
    }, now);
    public static SignedArtifact Invocation(ArtifactSigner signer, Artifact plan, string stepId, Artifact authority, string receiptRoute, DateTimeOffset now)
    {
        var step = ArtifactBindings.GetStep(plan, stepId);
        var body = new JsonObject { ["context"] = plan.Body.Object("context").DeepClone(), ["planRef"] = plan.Reference.ToJson(), ["planDigest"] = plan.PlanDigest,
            ["stepId"] = stepId, ["capabilityRef"] = step.Ref("capabilityRef").ToJson(), ["authorityRefs"] = Refs([authority.Reference]), ["receiptRoute"] = receiptRoute };
        foreach (var field in new[] { "target", "payloadDigest", "expectedState", "idempotencyKey", "idempotencyDomain", "invocationFingerprint" }) body[field] = step[field]!.DeepClone();
        return signer.Issue(ArtifactType.CommandInvocation, body, now);
    }
    public static JsonArray Strings(IEnumerable<string> values) => new(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    public static JsonArray Refs(IEnumerable<ArtifactRef> values) => new(values.Select(x => (JsonNode?)x.ToJson()).ToArray());
}
