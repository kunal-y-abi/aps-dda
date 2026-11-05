using System.Text.Json.Nodes;
using ApsDda.Core;
using static ApsDda.Runtime.SqliteRuntimeStore;

namespace ApsDda.Runtime;

/// <summary>A source-enforced, known pre-commit refusal. Unknown transport errors MUST NOT use this exception.</summary>
public sealed class SourceRefusedException(string code) : Exception("Source refused before committing any new effect.")
{ public string Code { get; } = code; }

public sealed class SqliteSourceAdapter(WorkflowRuntime runtime, SourceRegistration registration, ILocalSourceCommand command) : ISourceAdapter
{
    public SourceRegistration Registration { get; } = registration;
    public ILocalSourceCommand Command { get; } = command;
    public long MeasureQuantity(JsonObject normalizedPayload) => Command.Quantity(normalizedPayload);
    public SourceResult Invoke(SourceDispatch dispatch)
    {
        // Every failure inside the SQLite transaction rolls back both source state and source operation.
        try
        {
            return runtime.Store.Transaction((c, t) =>
            {
                var original = runtime.Validate(c, t, dispatch.InvocationRef, new(ArtifactType.CommandInvocation, "APS-Orchestrator", runtime.OrchestratorSigner.Issuer));
                RequireLookupCaller(original, dispatch.Caller);
                var identity = WorkflowRuntime.Identity(original, Registration);
                var existing = SqliteRuntimeStore.Lookup(c, t, identity);
                if (existing is not null) return existing; // Original operation/receipt, not a new receipt or source effect.
                var checkedRequest = runtime.ValidateDispatch(c, t, dispatch, Registration, Command);
                var invocation = checkedRequest.Invocation; var payload = checkedRequest.Payload;
                var target = invocation.Body.String("target"); var before = GetResource(c, t, Registration.Signer.Issuer, target);
                Errors.Require(before.TenantId == identity.TenantId, "AUTH_DENIED");
                Errors.Require(before.Version == invocation.Body.String("expectedState"), "STALE_PRECONDITION", "Source version changed; no new effect was committed.");
                var nextVersion = CanonicalQuantity.Format(checked(CanonicalQuantity.Parse(before.Version) + 1));
                var after = Command.Apply((JsonObject)before.State.DeepClone(), (JsonObject)payload.DeepClone()); _ = Jcs.Canonicalize(after);
                var operationId = Guid.NewGuid().ToString("D"); var now = runtime.Clock.GetUtcNow();
                var postValue = new JsonObject { ["target"] = target, ["version"] = nextVersion, ["state"] = after.DeepClone(), ["beforeState"] = before.State.DeepClone(),
                    ["invocationRef"] = invocation.Reference.ToJson(), ["operationId"] = operationId };
                var valueRef = RetainEvidenceValue(c, t, postValue);
                var query = new JsonObject { ["profile"] = "aps-dda.experimental/source-postcondition/1", ["target"] = target, ["operationId"] = operationId, ["invocationRef"] = invocation.Reference.ToJson() };
                var evidence = ArtifactFactory.Evidence(Registration.Signer, invocation.Body.Object("context"), Registration.Product.Artifact,
                    Registration.Product.Artifact.Body.Array("semanticRefs"), Jcs.Sha256Digest(query), nextVersion, valueRef, now, now);
                var receipt = Registration.Signer.Issue(ArtifactType.OperationReceipt, new JsonObject
                {
                    ["invocationRef"] = invocation.Reference.ToJson(), ["operationId"] = operationId, ["receiptSequence"] = 1,
                    ["observedAt"] = now.ToString("O"), ["outcome"] = "completed", ["effectStatus"] = Command.Transition,
                    ["resourceVersion"] = nextVersion, ["postconditionEvidenceRefs"] = new JsonArray(evidence.Artifact.Reference.ToJson()),
                    ["reconciliationRoute"] = Registration.Capability.Artifact.Body.Object("idempotency").String("operationLookup")
                }, now);
                Retain(c, t, evidence); Retain(c, t, receipt);
                var result = new SourceResult(receipt, evidence); var resultJson = new JsonObject { ["receipt"] = receipt.ToJson(), ["evidence"] = evidence.ToJson() }.ToJsonString();
                var updated = Execute(c, t, "UPDATE resources SET version=$next,state=$state WHERE source_issuer=$source AND target=$target AND tenant_id=$tenant AND version=$expected", ("$next", long.Parse(nextVersion)), ("$state", after.ToJsonString()), ("$source", identity.SourceIssuer), ("$target", target), ("$tenant", identity.TenantId), ("$expected", long.Parse(before.Version)));
                Errors.Require(updated == 1, "STALE_PRECONDITION");
                Execute(c, t, "INSERT INTO source_operations VALUES($tenant,$source,$domain,$key,$fingerprint,$inv,$operation,$result,$time,$authority,$target)",
                    ("$tenant", identity.TenantId), ("$source", identity.SourceIssuer), ("$domain", identity.IdempotencyDomain), ("$key", identity.IdempotencyKey),
                    ("$fingerprint", identity.Fingerprint), ("$inv", RefJson(invocation.Reference)), ("$operation", operationId), ("$result", resultJson),
                    ("$time", now.ToUnixTimeMilliseconds()), ("$authority", RefKey(checkedRequest.Authority.Reference)), ("$target", target));
                // Revalidate current time, local durable revocation and policy immediately before commit. No external hard-invariant promise.
                _ = runtime.ValidateDispatch(c, t, dispatch, Registration, Command, checkRate: false);
                return result;
            });
        }
        catch (ApsDdaException e) { throw new SourceRefusedException(e.Code); }
        // SQLite I/O/commit exceptions are ambiguous to caller, not known-no-effect assertions.
    }
    public SourceResult? Lookup(OperationIdentity identity, AuthenticatedPrincipal caller)
    {
        Errors.Require(identity.SourceIssuer == Registration.Signer.Issuer && identity.TenantId == caller.TenantId, "AUTH_DENIED");
        using var c = runtime.Store.Open();
        var result = SqliteRuntimeStore.Lookup(c, null, identity);
        if (result is not null)
        {
            var invocation = GetArtifact(c, null, result.Receipt.Artifact.Body.Ref("invocationRef")).Artifact;
            RequireLookupCaller(invocation, caller);
        }
        else runtime.Principals.RequireCurrent(caller, "Executor");
        return result;
    }
    private void RequireLookupCaller(Artifact invocation, AuthenticatedPrincipal caller)
    {
        invocation.Body.Ref("capabilityRef").RequireMatches(Registration.Capability.Artifact, ArtifactType.ProcessCapability);
        runtime.Principals.RequireContext(caller, invocation.Body.Object("context"), "Executor");
        Errors.Require(caller.Audiences.Contains(Registration.Audience) && caller.Resources.Contains(invocation.Body.String("target")) &&
            caller.CapabilityDigests.Contains(Registration.Capability.Artifact.Header.ContentDigest) && Registration.Resources.Contains(invocation.Body.String("target")) &&
            Registration.SubjectIds.Contains(caller.SubjectId) && Registration.WorkloadIds.Contains(caller.WorkloadId) && Registration.Roles.Overlaps(caller.Roles) &&
            Registration.Purposes.Contains(invocation.Body.Object("context").String("purpose")), "AUTH_DENIED");
    }
    public void VerifyPostcondition(SourceResult result, Artifact invocation)
    {
        using var c = runtime.Store.Open();
        var receipt = runtime.Trust.Validate(result.Receipt, new(ArtifactType.OperationReceipt, "APS-Action-Executor", Registration.Signer.Issuer));
        var evidence = runtime.Trust.Validate(result.PostconditionEvidence, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", Registration.Signer.Issuer));
        RequireNotRevoked(c, null, result.Receipt); RequireNotRevoked(c, null, result.PostconditionEvidence);
        ArtifactBindings.RequireReceiptBinding(receipt, invocation);
        var original = SqliteRuntimeStore.Lookup(c, null, WorkflowRuntime.Identity(invocation, Registration));
        Errors.Require(original is not null && RefKey(original.Receipt.Artifact.Reference) == RefKey(receipt.Reference) && RefKey(original.PostconditionEvidence.Artifact.Reference) == RefKey(evidence.Reference));
        var rb = receipt.Body; var eb = evidence.Body;
        Errors.Require(rb.String("outcome") == "completed" && rb.String("effectStatus") == Command.Transition && rb["receiptSequence"]!.GetValue<int>() == 1 && rb["predecessorReceiptRef"] is null && rb.String("reconciliationRoute") == Registration.Capability.Artifact.Body.Object("idempotency").String("operationLookup"));
        Errors.Require(rb.Refs("postconditionEvidenceRefs").Count == 1); rb.Refs("postconditionEvidenceRefs")[0].RequireMatches(evidence);
        ArtifactBindings.RequireSameContext(invocation.Body.Object("context"), eb.Object("context"));
        Errors.Require(eb.String("observationMode") == "observational" && eb.Array("items").Count == 1);
        var item = eb.Array("items")[0]!.AsObject(); item.Ref("productRef").RequireMatches(Registration.Product.Artifact, ArtifactType.DataProduct);
        Errors.Require(Jcs.Canonicalize(item.Array("semanticRefs")).AsSpan().SequenceEqual(Jcs.Canonicalize(Registration.Product.Artifact.Body.Array("semanticRefs"))) && item.String("qualityState") == "good" && item.String("cacheStatus") == "live");
        var value = GetEvidenceValue(c, null, item.String("valueRef"));
        value.Ref("invocationRef").RequireMatches(invocation); Errors.Require(value.String("operationId") == rb.String("operationId") && value.String("target") == invocation.Body.String("target") && value.String("version") == rb.String("resourceVersion") && value.String("version") == item.String("sourceVersion") &&
            value.String("version") == CanonicalQuantity.Format(checked(CanonicalQuantity.Parse(invocation.Body.String("expectedState")) + 1)));
        var query = new JsonObject { ["profile"] = "aps-dda.experimental/source-postcondition/1", ["target"] = value.String("target"), ["operationId"] = value.String("operationId"), ["invocationRef"] = invocation.Reference.ToJson() };
        Errors.Require(item.String("queryDigest") == Jcs.Sha256Digest(query) && item.Time("observedAt") == rb.Time("observedAt") && item.Time("observedAt") <= runtime.Clock.GetUtcNow());
        var context = invocation.Body.Object("context"); var payload = GetPayload(c, null, context.String("correlationId"), invocation.Body.String("stepId"));
        ArtifactBindings.RequireInvocationBinding(invocation, GetArtifact(c, null, invocation.Body.Ref("planRef")).Artifact, payload, Registration.Capability.Artifact, Registration.Audience);
        var expected = Command.Apply(value.Object("beforeState"), payload);
        Errors.Require(Jcs.Canonicalize(expected).AsSpan().SequenceEqual(Jcs.Canonicalize(value.Object("state"))) && Command.Verify(value.Object("state"), payload), "INVALID_ARTIFACT", "Source postcondition does not verify the actual original invocation.");
    }
}
