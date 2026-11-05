using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;
using Xunit;

namespace ApsDda.Runtime.Tests;

public sealed class MultiSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentCapacityAndDraftSourcesDoNotClaimGlobalAtomicity(bool staleSecondSource)
    {
        using var h = new Harness(); using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        const string issuer = "https://notification.example.invalid/issuer";
        const string audience = "https://notification.example.invalid";
        const string resource = audience + "/drafts/north";
        const string ns = "https://semantics.example.invalid/notification";
        var keyId = issuer + "/keys/runtime"; var signer = new ArtifactSigner(issuer, keyId, key);
        h.Trust.Register(new TrustedParticipant(issuer, [new TrustedKey(keyId, key.ExportSubjectPublicKeyInfo())],
            [ArtifactType.ProcessCapability, ArtifactType.DataProduct, ArtifactType.EvidenceBundle, ArtifactType.OperationReceipt],
            ["APS-Read-Publisher", "APS-Action-Executor"], [Artifact.SpecVersion], ["1.0.0"], audiences: [audience], endpoints: [audience + "/commands"], semanticNamespaces: [ns]));
        var semantics = new JsonArray(ArtifactFactory.Semantic(ns, ns + "/draft-count", "metric", "internal-draft-count"));
        var schema = ArtifactFactory.SchemaReference("https://schema.example.invalid/draft", new JsonObject { ["type"] = "object" });
        var product = ArtifactFactory.DataProduct(signer, "Internal draft state", semantics, schema, [Harness.Purpose], "local", h.Clock.Now);
        var capability = ArtifactFactory.ProcessCapability(signer, "PrepareInternalDraft", ArtifactFactory.Semantic(ns, ns + "/prepare", "command", "prepare-notification-draft"),
            audience + "/commands", audience, schema, schema, "internal-notification-draft-v1", resource, "notification-operations-owner", h.Clock.Now);
        h.Trust.PinArtifact(product.Artifact); h.Trust.PinArtifact(capability.Artifact);
        var registration = new SourceRegistration(capability, product, signer, [resource], [Harness.Purpose], [h.Caller.SubjectId], [h.Caller.WorkloadId], ["Executor"], h.SourceConfig.EvidenceRules);
        h.Runtime.RegisterSource(new SqliteSourceAdapter(h.Runtime, registration, new PrepareNotificationDraftCommand()));
        h.Store.SeedResource(issuer, resource, Harness.Tenant, new JsonObject { ["draftCount"] = "0" });
        h.Principals.Revoke(h.Caller.Identity);
        var caller = h.Principals.Register(new(h.Caller.Identity, Harness.Tenant, h.Caller.SubjectId, h.Caller.WorkloadId, h.Caller.ExecutorId,
            ["Executor"], [Harness.Purpose], [Harness.Audience, audience], [Harness.Resource, resource], [h.Capability.Artifact.Header.ContentDigest, capability.Artifact.Header.ContentDigest]));
        var payload = new JsonObject { ["quantity"] = "1", ["transition"] = "prepare-notification-draft" };
        var context = h.Plan.Artifact.Body.Object("context");
        var step = ArtifactFactory.PlanStep("draft", ["step-1"], capability.Artifact, resource, payload, staleSecondSource ? "2" : "1", Guid.NewGuid().ToString("N"), [h.Evidence.Artifact.Reference], context, h.Clock.Now.AddMinutes(10), "notification-operations-owner");
        h.ReplacePlan(p => { p.Array("steps").Add(step.DeepClone()); return p; }); h.Payloads["draft"] = payload;
        h.Runtime.Submit(h.Plan, h.Payloads, caller);
        var approval = h.Runtime.Approve(h.WorkflowId, h.Approver, h.Clock.Now.AddMinutes(4)); h.Runtime.Authorize(h.WorkflowId, approval.Artifact.Reference, caller);
        var w = h.Runtime.ExecuteAll(h.WorkflowId, caller);
        Assert.Equal(staleSecondSource ? WorkflowState.MANUAL_RESOLUTION_REQUIRED : WorkflowState.COMPLETED, w.State);
        Assert.Equal(staleSecondSource ? 1 : 2, h.Store.OperationCount);
        Assert.Equal("2", h.Store.GetResource(h.Source.Issuer, Harness.Resource).State.String("reserved"));
        Assert.Equal(staleSecondSource ? "0" : "1", h.Store.GetResource(issuer, resource).State.String("draftCount"));
        Assert.Equal("notification-operations-owner", w.Steps[1].ManualRecoveryOwner);
        Assert.Equal("COMPLETED", w.Steps[0].State);
    }
}
