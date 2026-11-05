using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;

namespace ApsDda.Demo;

public sealed record CompiledApplication(SignedArtifact ApplicationSpec, SignedArtifact BuilderPrompt);

/// <summary>Offline deterministic metadata compiler; this emits no app, code, credential or execution authority.</summary>
public sealed class ApplicationCompiler(TrustedDirectory trust, ArtifactSigner signer, TimeProvider clock,
    SignedArtifact capacityAction, SignedArtifact draftAction)
{
    public const string CatalogVersion = "1.0.0";
    public const string CompilerVersion = "1.0.0";
    public const string TargetBuilder = "offline-static-review-template/1";
    public static readonly IReadOnlyList<string> Catalog = Array.AsReadOnly(new[] { "allocation-comparison", "evidence-provenance", "exact-plan-review", "receipt-status" });
    private static readonly JsonObject InputSchema = new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject { ["reviewComment"] = new JsonObject { ["type"] = "string", ["maxLength"] = 256 } }
    };
    public static JsonObject DefaultOptions() => new() { ["components"] = ArtifactFactory.Strings(Catalog), ["targetBuilder"] = TargetBuilder };
    public CompiledApplication Compile(SignedArtifact decision, SignedArtifact plan, IReadOnlyList<SignedArtifact> evidence,
        IReadOnlyDictionary<string, JsonObject> payloads, JsonObject? options = null)
    {
        ValidateInputs(decision, plan, evidence, payloads);
        options = options is null ? DefaultOptions() : StrictJson.ParseObject(options.ToJsonString());
        // No arbitrary configuration, retrieved text, secret bag, URLs or executable snippets cross this boundary.
        Require(options.Count == 2 && options.ContainsKey("components") && options.String("targetBuilder") == TargetBuilder, "UNSUPPORTED_PROFILE");
        var components = options.Array("components").Select(n => n!.GetValue<string>()).ToArray();
        Require(components.Length > 0 && components.Distinct(StringComparer.Ordinal).Count() == components.Length && components.All(Catalog.Contains), "UNSUPPORTED_PROFILE");
        var application = signer.Issue(ArtifactType.ApplicationSpec, new JsonObject
        {
            ["decisionRef"] = decision.Artifact.Reference.ToJson(), ["planRef"] = plan.Artifact.Reference.ToJson(),
            ["evidenceRefs"] = ArtifactFactory.Refs(decision.Artifact.Body.Refs("evidenceRefs")), ["componentCatalogVersion"] = CatalogVersion,
            ["components"] = ArtifactFactory.Strings(components), ["classification"] = "internal", ["accessibility"] = new JsonArray("text-only readable review", "no colour-only status"),
            ["userInputSchema"] = ArtifactFactory.SchemaReference("https://schemas.example.invalid/insurance/review-input-v1", InputSchema),
            ["actionMappings"] = new JsonArray(plan.Artifact.Body.Array("steps").Select(n => (JsonNode)new JsonObject { ["event"] = "request-runtime-step:" + n!.AsObject().String("stepId"), ["planStepId"] = n.AsObject().String("stepId") }).ToArray())
        }, clock.GetUtcNow());
        ValidateApplication(application.Artifact, decision.Artifact, plan.Artifact);
        var prompt = signer.Issue(ArtifactType.BuilderPrompt, new JsonObject
        {
            ["applicationSpecRef"] = application.Artifact.Reference.ToJson(), ["targetBuilder"] = TargetBuilder, ["compilerVersion"] = CompilerVersion,
            ["permittedComponentCatalog"] = CatalogVersion, ["requestedOutputs"] = new JsonArray("static-review-description.txt"), ["noSecretDeclaration"] = true,
            ["promptText"] = "Template offline-static-review-template/1; compiler 1.0.0. Render only catalog 1.0.0 components: " + string.Join(", ", components) +
                ". Application=" + application.Artifact.Header.ContentDigest + "; Decision=" + decision.Artifact.Header.ContentDigest + "; ExactPlan=" + plan.Artifact.Header.ContentDigest +
                "; PlanDigest=" + plan.Artifact.PlanDigest + ". Show bound evidence and exact steps. An action event is a request to the authenticated runtime, never permission. No secrets, arbitrary code, network calls or customer decisions. This derived artifact has no authority."
        }, clock.GetUtcNow());
        return new(application, prompt);
    }
    public void ValidateApplication(Artifact application, Artifact decision, Artifact plan)
    {
        application.RequireType(ArtifactType.ApplicationSpec); application.RequireIntegrity(); decision.RequireIntegrity(); plan.RequireIntegrity();
        var b = application.Body; b.Ref("decisionRef").RequireMatches(decision, ArtifactType.DecisionSpec); b.Ref("planRef").RequireMatches(plan, ArtifactType.ExecutionPlan);
        plan.Body.Ref("decisionRef").RequireMatches(decision, ArtifactType.DecisionSpec);
        Require(b.String("componentCatalogVersion") == CatalogVersion && b.Array("components").Count > 0 && b.Array("components").All(n => Catalog.Contains(n!.GetValue<string>())), "UNSUPPORTED_PROFILE");
        Require(Jcs.Sha256Digest(b.Array("evidenceRefs")) == Jcs.Sha256Digest(ArtifactFactory.Refs(decision.Body.Refs("evidenceRefs"))), "INVALID_ARTIFACT");
        Require(Jcs.Sha256Digest(b.Object("userInputSchema")) == Jcs.Sha256Digest(ArtifactFactory.SchemaReference("https://schemas.example.invalid/insurance/review-input-v1", InputSchema)), "UNSUPPORTED_PROFILE");
        var ids = plan.Body.Array("steps").Select(n => n!.AsObject().String("stepId")).ToArray();
        Require(b.Array("actionMappings").Count == ids.Length && b.Array("actionMappings").Select(n => n!.AsObject().String("planStepId")).SequenceEqual(ids), "INVALID_ARTIFACT");
        Require(b.Array("actionMappings").All(n => n!.AsObject().String("event") == "request-runtime-step:" + n.AsObject().String("planStepId")), "UNSUPPORTED_PROFILE");
    }
    private void ValidateInputs(SignedArtifact decision, SignedArtifact plan, IReadOnlyList<SignedArtifact> evidence, IReadOnlyDictionary<string, JsonObject> payloads)
    {
        trust.Validate(decision, new(ArtifactType.DecisionSpec, "APS-Orchestrator", signer.Issuer));
        trust.Validate(plan, new(ArtifactType.ExecutionPlan, "APS-Orchestrator", signer.Issuer));
        plan.Artifact.Body.Ref("decisionRef").RequireMatches(decision.Artifact, ArtifactType.DecisionSpec);
        ArtifactBindings.RequireSameContext(decision.Artifact.Body.Object("context"), plan.Artifact.Body.Object("context")); ArtifactBindings.RequirePlanDag(plan.Artifact);
        var refs = decision.Artifact.Body.Refs("evidenceRefs"); Require(refs.Count == evidence.Count && refs.Count > 0, "INVALID_ARTIFACT");
        foreach (var r in refs) { var actual = evidence.SingleOrDefault(e => e.Artifact.Reference == r); Require(actual is not null, "INVALID_ARTIFACT"); r.RequireMatches(actual!.Artifact, ArtifactType.EvidenceBundle); trust.Validate(actual, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher")); }
        var steps = plan.Artifact.Body.Array("steps"); Require(payloads.Count == steps.Count, "INVALID_ARTIFACT");
        foreach (var n in steps)
        {
            var s = n!.AsObject(); Require(payloads.TryGetValue(s.String("stepId"), out var p) && BindingDigests.Payload(p!) == s.String("payloadDigest"), "INVALID_ARTIFACT");
            Require(p!.Count == 2, "UNSUPPORTED_PROFILE");
            var action = p.String("transition") switch { "reserve-internal-capacity" => capacityAction, "prepare-notification-draft" => draftAction, _ => throw new ApsDdaException("UNSUPPORTED_PROFILE", "Unreviewed action mapping.") };
            var capability = trust.Validate(action, new(ArtifactType.ProcessCapability, "APS-Action-Executor", action.Artifact.Header.Issuer, RequirePinnedArtifact: true));
            s.Ref("capabilityRef").RequireMatches(capability, ArtifactType.ProcessCapability);
            Require(capability.Body.Object("commandSemantic").String("localBinding") == p.String("transition"), "UNSUPPORTED_PROFILE");
            _ = CanonicalQuantity.Parse(p.String("quantity"));
            Require(s.String("invocationFingerprint") == BindingDigests.Invocation(plan.Artifact.Body.Object("context"), capability.Reference, s.String("target"), p, s.String("expectedState"), s.String("idempotencyDomain"), capability.Body.Object("endpoint").String("audience")), "INVALID_ARTIFACT");
        }
        var analysis = decision.Artifact.Body.Object("extensions").Object(AllocationModel.ExtensionUri);
        var chosen = analysis.Array("alternatives").Single(n => n!.AsObject().String("name") == analysis.String("selectedAlternative"))!.AsObject();
        if (steps.Count == 3)
        {
            for (var i = 0; i < 3; i++) Require(payloads[steps[i]!.AsObject().String("stepId")].String("quantity") == chosen.Array("additionalTeams")[i]!.GetValue<string>() && steps[i]!.AsObject().String("target") == InsuranceSession.CapacityTarget(AllocationModel.RegionNames[i]), "INVALID_ARTIFACT");
        }
        else Require(steps.Count == 1 && steps[0]!.AsObject().String("target") == InsuranceSession.DraftTarget && payloads[steps[0]!.AsObject().String("stepId")].String("transition") == "prepare-notification-draft" && payloads[steps[0]!.AsObject().String("stepId")].String("quantity") == "1", "UNSUPPORTED_PROFILE");
    }
    private static void Require(bool condition, string code) { if (!condition) throw new ApsDdaException(code, "Compiler refused an unreviewed or incorrectly bound input."); }
}
