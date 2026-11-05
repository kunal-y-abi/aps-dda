using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ApsDda.Core;

namespace ApsDda.Runtime;

public sealed class ArtifactSigner(string issuer, string keyId, ECDsa privateKey)
{
    private readonly object _gate = new();
    public string Issuer { get; } = issuer;
    public string KeyId { get; } = keyId;
    public SignedArtifact Issue(ArtifactType type, JsonObject body, DateTimeOffset now, string? id = null)
    { lock (_gate) return DetachedSigning.Sign(Artifact.Create(type, Issuer, body, now, id), KeyId, privateKey); }
}

public sealed record PolicyEvaluation(string Outcome, IReadOnlyList<string> Obligations);
/// <summary>Trusted deterministic policy implementation; the external rule bytes use the explicitly declared JCS profile.</summary>
public interface IRuntimePolicy
{
    JsonObject PolicyReference { get; }
    PolicyEvaluation Evaluate(Artifact decision);
}

public sealed class EligibilityPolicy : IRuntimePolicy
{
    private readonly JsonObject _reference;
    public EligibilityPolicy(string uri, string version, bool requireHumanApproval)
    {
        RequireHumanApproval = requireHumanApproval;
        _reference = new JsonObject { ["uri"] = uri, ["version"] = version,
            ["digest"] = Jcs.Sha256Digest(new JsonObject { ["profile"] = "aps-dda.experimental/eligibility-policy/1", ["version"] = version, ["requireHumanApproval"] = requireHumanApproval }) };
        ArtifactSchema.Default.RequireDefinition(_reference, "PolicyRef");
    }
    public bool RequireHumanApproval { get; }
    public JsonObject PolicyReference => (JsonObject)_reference.DeepClone();
    public PolicyEvaluation Evaluate(Artifact decision) => new(decision.Body.String("actionEligibility") == "eligible" ?
        RequireHumanApproval ? "allow-with-human-approval" : "allow" : "deny", []);
}

public static class RuntimeDigests
{
    public static string PolicyDecision(Artifact decision, JsonObject policyReference, PolicyEvaluation evaluation) => Jcs.Sha256Digest(new JsonObject
    {
        ["profile"] = "aps-dda.experimental/policy-evaluation/1", ["decisionRef"] = decision.Reference.ToJson(),
        ["policyRef"] = policyReference.DeepClone(), ["outcome"] = evaluation.Outcome,
        ["obligations"] = new JsonArray(evaluation.Obligations.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
    });
}

public sealed class EvidenceRule
{
    private readonly JsonArray _semantics;
    public EvidenceRule(ArtifactRef productRef, string issuer, JsonArray semantics, TimeSpan maxAge,
        Func<JsonObject, bool> validateValue, string qualityState = "good")
    { ProductRef = productRef; Issuer = issuer; _semantics = (JsonArray)semantics.DeepClone(); MaxAge = maxAge; ValidateValue = validateValue; QualityState = qualityState; }
    public ArtifactRef ProductRef { get; }
    public string Issuer { get; }
    public JsonArray Semantics => (JsonArray)_semantics.DeepClone();
    public TimeSpan MaxAge { get; }
    public string QualityState { get; }
    public Func<JsonObject, bool> ValidateValue { get; }
}

/// <summary>Source-owner configuration. Exact resources, contracts and risk limits, no caller-selected guards.</summary>
public sealed class SourceRegistration
{
    public SourceRegistration(SignedArtifact capability, SignedArtifact product, ArtifactSigner signer,
        IEnumerable<string> resources, IEnumerable<string> purposes, IEnumerable<string> subjectIds,
        IEnumerable<string> workloadIds, IEnumerable<string> roles, IEnumerable<EvidenceRule> evidenceRules,
        long maximumQuantity = 100, bool policyGrantAllowed = false, long maximumPolicyQuantity = 10,
        int maximumPolicyOperationsPerMinute = 1, bool requiresExternalHardInvariant = false,
        string classification = "internal", string residency = "local")
    {
        Capability = capability; Product = product; Signer = signer;
        Resources = resources.ToFrozenSet(StringComparer.Ordinal); Purposes = purposes.ToFrozenSet(StringComparer.Ordinal);
        SubjectIds = subjectIds.ToFrozenSet(StringComparer.Ordinal); WorkloadIds = workloadIds.ToFrozenSet(StringComparer.Ordinal);
        Roles = roles.ToFrozenSet(StringComparer.Ordinal); EvidenceRules = Array.AsReadOnly(evidenceRules.ToArray());
        MaximumQuantity = maximumQuantity; PolicyGrantAllowed = policyGrantAllowed; MaximumPolicyQuantity = maximumPolicyQuantity;
        MaximumPolicyOperationsPerMinute = maximumPolicyOperationsPerMinute; RequiresExternalHardInvariant = requiresExternalHardInvariant;
        Classification = classification; Residency = residency;
        Errors.Require(maximumQuantity > 0 && maximumPolicyQuantity > 0 && maximumPolicyOperationsPerMinute > 0);
    }
    public SignedArtifact Capability { get; }
    public SignedArtifact Product { get; }
    public ArtifactSigner Signer { get; }
    public FrozenSet<string> Resources { get; }
    public FrozenSet<string> Purposes { get; }
    public FrozenSet<string> SubjectIds { get; }
    public FrozenSet<string> WorkloadIds { get; }
    public FrozenSet<string> Roles { get; }
    public IReadOnlyList<EvidenceRule> EvidenceRules { get; }
    public long MaximumQuantity { get; }
    public bool PolicyGrantAllowed { get; }
    public long MaximumPolicyQuantity { get; }
    public int MaximumPolicyOperationsPerMinute { get; }
    public bool RequiresExternalHardInvariant { get; }
    public string Classification { get; }
    public string Residency { get; }
    public string Audience => Capability.Artifact.Body.Object("endpoint").String("audience");
}

public sealed record ResourceSnapshot(string Target, string TenantId, string Version, JsonObject State);
public sealed record SourceDispatch(ArtifactRef InvocationRef, AuthenticatedPrincipal Caller);
public sealed record SourceResult(SignedArtifact Receipt, SignedArtifact PostconditionEvidence);
public sealed record OperationIdentity(string TenantId, string SourceIssuer, string IdempotencyDomain, string IdempotencyKey, string Fingerprint);

/// <summary>Source-authorized boundary. An implementation MUST enforce authorization itself, commit operation and effect locally, and return retained signed receipts.</summary>
public interface ISourceAdapter
{
    SourceRegistration Registration { get; }
    long MeasureQuantity(JsonObject normalizedPayload);
    SourceResult Invoke(SourceDispatch dispatch);
    SourceResult? Lookup(OperationIdentity identity, AuthenticatedPrincipal caller);
    void VerifyPostcondition(SourceResult result, Artifact invocation);
}

/// <summary>Reviewed deterministic source-local command. Apply is pure; SQL and client code are not accepted.</summary>
public interface ILocalSourceCommand
{
    string Transition { get; }
    long Quantity(JsonObject payload);
    JsonObject Apply(JsonObject current, JsonObject payload);
    bool Verify(JsonObject postcondition, JsonObject payload);
}

public sealed class ReserveCapacityCommand : ILocalSourceCommand
{
    public string Transition => "reserve-internal-capacity";
    public long Quantity(JsonObject payload)
    {
        Errors.Require(payload.Count == 2 && payload.String("transition") == Transition);
        return CanonicalQuantity.Parse(payload.String("quantity"));
    }
    public JsonObject Apply(JsonObject current, JsonObject payload)
    {
        var quantity = Quantity(payload); var available = CanonicalQuantity.Parse(current.String("available"), allowZero: true);
        var reserved = CanonicalQuantity.Parse(current.String("reserved"), allowZero: true);
        Errors.Require(quantity <= available, "PRECONDITION_FAILED", "Source-owned available capacity guard refused the reservation.");
        return new JsonObject { ["available"] = CanonicalQuantity.Format(available - quantity), ["reserved"] = CanonicalQuantity.Format(checked(reserved + quantity)),
            ["lastQuantity"] = CanonicalQuantity.Format(quantity), ["lastTransition"] = Transition };
    }
    public bool Verify(JsonObject postcondition, JsonObject payload) => postcondition.String("lastTransition") == Transition && postcondition.String("lastQuantity") == payload.String("quantity");
}

public sealed class PrepareNotificationDraftCommand : ILocalSourceCommand
{
    public string Transition => "prepare-notification-draft";
    public long Quantity(JsonObject payload)
    { Errors.Require(payload.Count == 2 && payload.String("transition") == Transition && payload.String("quantity") == "1"); return 1; }
    public JsonObject Apply(JsonObject current, JsonObject payload)
    { _ = Quantity(payload); return new JsonObject { ["draftCount"] = CanonicalQuantity.Format(checked(CanonicalQuantity.Parse(current.String("draftCount"), true) + 1)), ["lastQuantity"] = "1", ["lastTransition"] = Transition }; }
    public bool Verify(JsonObject postcondition, JsonObject payload) => postcondition.String("lastTransition") == Transition && postcondition.String("lastQuantity") == "1";
}

public static class CanonicalQuantity
{
    public static long Parse(string text, bool allowZero = false)
    {
        Errors.Require(text.Length > 0 && text.All(c => c is >= '0' and <= '9') && (text.Length == 1 || text[0] != '0') &&
            long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _));
        var n = long.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Errors.Require(n > 0 || allowZero && n == 0); return n;
    }
    public static string Format(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum WorkflowState { PLANNED, AUTHORIZED, EXECUTING, VERIFYING, COMPLETED, REJECTED, FAILED, CANCELED, UNKNOWN_OUTCOME, MANUAL_RESOLUTION_REQUIRED }
public sealed record WorkflowSnapshot(string WorkflowId, ArtifactRef PlanRef, WorkflowState State, ArtifactRef? AuthorityRef, string? SafeError,
    IReadOnlyList<StepSnapshot> Steps);
public sealed record StepSnapshot(string StepId, string State, ArtifactRef? InvocationRef, ArtifactRef? ReceiptRef, string? ManualRecoveryOwner);
