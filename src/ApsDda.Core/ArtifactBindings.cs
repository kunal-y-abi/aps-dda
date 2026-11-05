#nullable enable
using System.Text.Json.Nodes;

namespace ApsDda.Core;

/// <summary>Pure binding checks; these do not authenticate principals, grant permission or prove external effects.</summary>
public static class ArtifactBindings
{
    public static void RequireSameContext(JsonObject expected, JsonObject actual)
    {
        ArtifactSchema.Default.RequireDefinition(expected, "ExecutionContext");
        ArtifactSchema.Default.RequireDefinition(actual, "ExecutionContext");
        if (!Jcs.Canonicalize(expected).AsSpan().SequenceEqual(Jcs.Canonicalize(actual)))
            throw new ApsDdaException("AUTH_DENIED", "Execution contexts differ, including workflow, policy or delegation binding.");
    }
    public static void RequirePlanDag(Artifact plan)
    {
        plan.RequireType(ArtifactType.ExecutionPlan); plan.RequireIntegrity();
        var body = plan.Body;
        RequireRefType(body.Ref("decisionRef"), ArtifactType.DecisionSpec);
        RequireRefType(body.Ref("policyRef"), ArtifactType.PolicyDecision);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in body.Array("steps"))
        {
            var step = n!.AsObject();
            var id = step.String("stepId");
            if (seen.Contains(id)) throw StrictJson.Invalid("Duplicate plan step identifier.");
            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in step.Array("dependsOn"))
            {
                var dep = d!.GetValue<string>();
                if (!dependencies.Add(dep) || !seen.Contains(dep)) throw StrictJson.Invalid("Plan DAG must be ordered and have unique prior dependencies.");
            }
            seen.Add(id);
            RequireRefType(step.Ref("capabilityRef"), ArtifactType.ProcessCapability);
            foreach (var reference in step.Refs("evidenceRefs")) RequireRefType(reference, ArtifactType.EvidenceBundle);
            if (step["compensationCapabilityRef"] is JsonObject compensation) RequireRefType(ArtifactRef.FromJson(compensation), ArtifactType.ProcessCapability);
        }
    }
    public static JsonObject GetStep(Artifact plan, string stepId)
    {
        RequirePlanDag(plan);
        return plan.Body.Array("steps").Select(n => n!.AsObject()).SingleOrDefault(s => s.String("stepId") == stepId)
            ?? throw StrictJson.Invalid("Plan step not found.");
    }
    public static void RequirePlanReference(Artifact boundArtifact, Artifact plan)
    {
        plan.RequireType(ArtifactType.ExecutionPlan); plan.RequireIntegrity(); boundArtifact.RequireIntegrity();
        var body = boundArtifact.Body;
        body.Ref("planRef").RequireMatches(plan, ArtifactType.ExecutionPlan);
        if (body.String("planDigest") != plan.PlanDigest) throw StrictJson.Invalid("Bound plan digest differs.");
        if (body["context"] is JsonObject context) RequireSameContext(plan.Body.Object("context"), context);
    }
    public static void RequireApprovalBinding(Artifact approval, Artifact plan, Artifact policyDecision)
    {
        approval.RequireType(ArtifactType.Approval); policyDecision.RequireType(ArtifactType.PolicyDecision);
        RequirePlanDag(plan); RequirePlanReference(approval, plan);
        approval.Body.Ref("policyRef").RequireMatches(policyDecision, ArtifactType.PolicyDecision);
        plan.Body.Ref("policyRef").RequireMatches(policyDecision, ArtifactType.PolicyDecision);
        RequireSameContext(plan.Body.Object("context"), policyDecision.Body.Object("context"));
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in approval.Body.Array("allowedStepIds"))
        {
            var value = id!.GetValue<string>();
            if (!allowed.Add(value)) throw StrictJson.Invalid("Duplicate approved step identifier.");
            _ = GetStep(plan, value);
        }
        // Approver authentication/role, policy outcome, time and single-use consumption are runtime checks.
    }
    public static void RequireInvocationBinding(Artifact invocation, Artifact plan, JsonObject payload,
        Artifact capability, string sourceAudience)
    {
        invocation.RequireType(ArtifactType.CommandInvocation); capability.RequireType(ArtifactType.ProcessCapability);
        capability.RequireIntegrity(); RequirePlanReference(invocation, plan);
        var body = invocation.Body;
        var step = GetStep(plan, body.String("stepId"));
        body.Ref("capabilityRef").RequireMatches(capability, ArtifactType.ProcessCapability);
        step.Ref("capabilityRef").RequireMatches(capability, ArtifactType.ProcessCapability);
        if (capability.Body.Object("endpoint").String("audience") != sourceAudience) throw new ApsDdaException("AUTH_DENIED", "Capability source audience differs.");
        string[] equalFields = ["target", "payloadDigest", "expectedState", "idempotencyKey", "idempotencyDomain", "invocationFingerprint"];
        foreach (var field in equalFields)
            if (body.String(field) != step.String(field)) throw StrictJson.Invalid("Invocation effect/precondition/idempotency binding differs from plan.");
        if (body.String("payloadDigest") != BindingDigests.Payload(payload)) throw StrictJson.Invalid("Effect payload commitment differs.");
        var fingerprint = BindingDigests.Invocation(body.Object("context"), capability.Reference, body.String("target"), payload,
            body.String("expectedState"), body.String("idempotencyDomain"), sourceAudience);
        if (fingerprint != body.String("invocationFingerprint")) throw StrictJson.Invalid("Invocation fingerprint differs from exact source binding.");
        foreach (var authority in body.Refs("authorityRefs"))
            if (authority.ArtifactType is not (nameof(ArtifactType.Approval) or nameof(ArtifactType.Authority))) throw StrictJson.Invalid("Invocation reference is not execution authority.");
        // Time, caller permission, retained evidence and current source state must still be checked at commit.
    }
    public static void RequireReceiptBinding(Artifact receipt, Artifact invocation)
    {
        receipt.RequireType(ArtifactType.OperationReceipt); invocation.RequireType(ArtifactType.CommandInvocation);
        receipt.RequireIntegrity(); receipt.Body.Ref("invocationRef").RequireMatches(invocation, ArtifactType.CommandInvocation);
        foreach (var r in receipt.Body["postconditionEvidenceRefs"] is JsonArray array ? array.Select(n => ArtifactRef.FromJson(n!)) : [])
            RequireRefType(r, ArtifactType.EvidenceBundle);
        if (receipt.Body["predecessorReceiptRef"] is JsonObject predecessor) RequireRefType(ArtifactRef.FromJson(predecessor), ArtifactType.OperationReceipt);
    }
    public static void RequireRefType(ArtifactRef reference, ArtifactType expected)
    {
        _ = reference.ToJson();
        if (reference.ArtifactType != expected.ToString()) throw StrictJson.Invalid("Reference declares the wrong artifact type.");
    }
}

/// <summary>Explicit experimental external-resource byte binding; not an amendment to the original APS-DDA schema.</summary>
public static class BindingDigests
{
    public const string InvocationProfile = "aps-dda.experimental/invocation-fingerprint/1";
    public static string Payload(JsonObject normalizedPayload) => Jcs.Sha256Digest(normalizedPayload);
    public static string Invocation(JsonObject context, ArtifactRef capability, string target, JsonObject normalizedPayload,
        string expectedState, string idempotencyDomain, string sourceAudience)
    {
        ArtifactSchema.Default.RequireDefinition(context, "ExecutionContext");
        ArtifactBindings.RequireRefType(capability, ArtifactType.ProcessCapability);
        var pinned = capability with { Uri = null };
        return Jcs.Sha256Digest(new JsonObject
        {
            ["profile"] = InvocationProfile, ["context"] = context.DeepClone(), ["capabilityRef"] = pinned.ToJson(),
            ["sourceAudience"] = sourceAudience, ["target"] = target, ["payload"] = normalizedPayload.DeepClone(),
            ["expectedState"] = expectedState, ["idempotencyDomain"] = idempotencyDomain
        });
    }
}
