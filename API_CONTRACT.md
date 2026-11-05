# APS-DDA C# API contract — core v0.1.0-preview.1

This file is the concrete handoff contract for `ApsDda.Core` on `net10.0`. The implementation is experimental and covers selected requirements, not complete conformance or certification. New source development was AI-assisted. Author: Kunal Yadav. [LICENSING.md](LICENSING.md) maps Apache-2.0 code/schema/synthetic fixtures, CC BY 4.0 authored prose/diagrams and preserved third-party exceptions. The mixed-license Core binary is described in [package-license.md](package-license.md); vendored source retains its own notices.

## Build and resources

The solution is `ApsDda.sln`. The pinned SDK is 10.0.403 in `global.json`; package references and transitive resolutions have lockfiles. Core uses JsonSchema.Net **7.4.0**, not the incompatible newer-major documentation API. Build with `.NET 10` using `dotnet restore --locked-mode`, `dotnet build`, and `dotnet test`. The original schema resource is installed in the assembly as `ApsDda.Core.aps-dda.schema.json`; no current directory, checkout-relative file or network is needed. Original schema/standard/placeholder fixtures live in `spec/original`. Those fixtures must not be consumed as verified runtime artifacts.

Namespace for all APIs below is `ApsDda.Core`. This library accepts the full unchanged original schema. It does not define a smaller artifact format with the same `APS-DDA/0.1` label. Bodies remain `JsonObject`s because domain meanings, source checks and persistence belong to the next layer.

## Strict JSON and canonicalization

```csharp
JsonNode StrictJson.Parse(string json);
JsonNode StrictJson.Parse(ReadOnlySpan<byte> utf8);
JsonObject StrictJson.ParseObject(string json);
JsonObject StrictJson.ParseObject(ReadOnlySpan<byte> utf8);
void StrictJson.ValidateUnicode(string text);
byte[] Jcs.Canonicalize(string json);
byte[] Jcs.Canonicalize(ReadOnlySpan<byte> utf8);
byte[] Jcs.Canonicalize(JsonNode node);
string Jcs.Sha256Digest(JsonNode node);
string Jcs.Base64Url(ReadOnlySpan<byte> bytes);
byte[] Jcs.DecodeBase64Url(string text);
```

Parsing refuses duplicate decoded names at every object depth, malformed UTF-8, lone escaped/raw surrogates, comments, trailing commas, extra root values, non-finite numbers, and numbers whose magnitude exceeds `9007199254740991`. Decimal and exponent representations do not evade the safe-number limit. Nonzero values that underflow binary64 are refused. Inputs are limited to 1 MiB, depth 64 and numeric tokens of 256 bytes. Exact money, quantity and large IDs should be profile-defined canonical **strings**, not decimal/IEEE JSON numbers. Ordinary fractional numbers within the allowed magnitude follow binary64/JCS rounding, not exact decimal arithmetic. Domain normalization must precede construction, and authorization identifiers must not be normalized into equivalence afterward.

`Jcs` uses the **unmodified** reviewed upstream C# canonicalizer and ECMAScript number serializer from cyberphone/json-canonicalization commit `32a48195b9b20fe67b1795e39e4509f14d860b45`. Its original license/provenance and covered source are included in packages under `third-party/jcs`. This is real RFC 8785 serialization, including UTF-16 ordinal sorting, ECMAScript numeric spelling and array order, not merely sorting properties before System.Text.Json serialization. JCS does not normalize Unicode or URIs. The SDK intentionally accepts a narrower numeric input profile than general RFC 8785: the upstream RFC `1e30` example and large binary64 samples are tested against the reference code itself, while SDK entry points reject those unsafe magnitudes. Object/array roots only are supported by this binding.

`Jcs.Sha256Digest` returns `sha256:` plus 64 lowercase hex digits over JCS bytes. It may be used for payloads or model parameters **only when that external resource's own profile explicitly adopts these byte rules**. It does not silently establish a canonicalization contract for arbitrary policy or schema resources. Base64url is unpadded; the decoder rejects alternative encodings with unused-bit ambiguity.

## Shape validation and immutable artifacts

```csharp
public enum ArtifactType {
  CapabilityManifest, DataProduct, ProcessCapability, Task, Negotiation,
  EvidenceBundle, DecisionSpec, ExecutionPlan, PolicyDecision, Authority,
  Approval, CommandInvocation, OperationReceipt, ApplicationSpec, BuilderPrompt
}
public sealed record ArtifactHeader(
  string Id, string ArtifactVersion, string SpecVersion, string Issuer,
  DateTimeOffset IssuedAt, string ContentDigest);

Artifact Artifact.Parse(string json);
Artifact Artifact.Parse(ReadOnlySpan<byte> utf8);
Artifact Artifact.FromJson(JsonObject document);
Artifact Artifact.Create(ArtifactType type, string issuer, JsonObject body,
  DateTimeOffset issuedAt, string? id = null, string artifactVersion = "1.0.0");
```

`Parse`/`FromJson` validate strict input and **shape only**, not declared digest/signature/trust. `Create` constructs the original header, computes a real plan digest if applicable, then the content digest, validates the complete original shape, and returns a defensively copied snapshot. IDs default to a newly generated UUID; callers may supply an exact UUID string for deterministic fixtures. `issuedAt` is rendered as UTC `O` at construction; parsed artifact bytes retain their original timestamp/identifier spellings. Caller-provided body fields are copied; a caller cannot mutate an issued artifact via a body or document returned by the SDK.

```csharp
ArtifactType artifact.Type;
ArtifactHeader artifact.Header;
JsonObject artifact.Body;                 // defensive copy
ArtifactRef artifact.Reference;           // no location hint
string? artifact.PlanDigest;              // null except ExecutionPlan
JsonObject artifact.ToJson();             // defensive copy
string artifact.ToJsonString();
byte[] artifact.ToCanonicalBytes();
void artifact.RequireIntegrity();
void artifact.RequireType(ArtifactType expected);

JsonObject Artifact.ArtifactCore(JsonObject completeArtifact);
JsonObject Artifact.PlanCore(JsonObject completePlan);
string Artifact.ComputeContentDigest(JsonObject completeArtifact);
string Artifact.ComputePlanDigest(JsonObject completePlan);
```

`artifact_core` removes **only** `/header/contentDigest`. Content digest is `sha256:lowercase_hex(SHA256(JCS(artifact_core)))`. `plan_core` requires the `ExecutionPlan` discriminator and removes **only** `/header/contentDigest` and `/body/planDigest` from the complete artifact. Plan digest is unprefixed, unpadded base64url of the 32 SHA-256 bytes, length 43. Every other field, including header, full execution context, extensions and ordered steps, is covered. Insert `planDigest` first, then compute content digest. Projection/hash helpers are pure calculations, not standalone shape/trust validators; use `Artifact` entry points and `RequireIntegrity` before accepting data. Signatures, mutable status, transport metadata and attempts remain detached.

```csharp
public sealed class ArtifactSchema {
  public static ArtifactSchema Default { get; }
  public static string ReadOriginalSchema();
  public SchemaValidationResult Validate(JsonNode document);
  public SchemaValidationResult Validate(string json);
  public void RequireValid(JsonNode document);
  public void RequireDefinition(JsonNode document, string definition);
}
public sealed record SchemaValidationResult(bool IsValid);
```

Only the embedded original bundle and its fixed `$defs` are used. Draft 2020-12 and format assertions are enabled. Local registry registration resolves every bundled URI/embedded resource, and external `Fetch` throws. Callers cannot supply an arbitrary schema URI to this validator. Unknown `$defs` names throw `ArgumentException`. Schema-invalid input returns `IsValid=false`; unsafe parsing input throws even from `Validate`, not a misleading schema result. Evaluation is serialized per validator instance. Schema validation does not imply semantic or runtime correctness.

## Exact references and field helpers

```csharp
public sealed record ArtifactRef(string ArtifactType, string Id,
  string ArtifactVersion, string ContentDigest, string? Uri = null) {
  public JsonObject ToJson();
  public static ArtifactRef FromJson(JsonNode node);
  public void RequireMatches(Artifact artifact, ArtifactType? expectedType = null);
}
```

The record serializes exact original camel-case `artifactType`, `id`, `artifactVersion`, `contentDigest`, plus optional `uri`. Helpers validate the original `ArtifactRef` definition and compare all four required pins with ordinal equality, then require actual artifact integrity. UUID strings, versions, and identifier URIs are not normalized for matching. `Uri` is only a location hint, neither identity nor trust, and is never fetched. `latest` and incomplete/untyped references are unsupported.

`JsonFields` supplies extension methods on `JsonObject`: `String(key)`, `Object(key)`, `Array(key)`, `Time(key)`, `Ref(key)`, `Refs(key)`. Missing/wrong types fail with safe `INVALID_ARTIFACT`. `Time` returns `DateTimeOffset`; use on already schema-validated timestamps. `Refs` returns `IReadOnlyList<ArtifactRef>`.

## Detached signature binding

```csharp
public sealed record DetachedSignature(string Algorithm, string KeyId,
  string Issuer, ArtifactRef ArtifactRef, string Value);
public sealed record SignedArtifact(Artifact Artifact, DetachedSignature Signature) {
  public JsonObject ToJson();
  public string ToJsonString();
  public static SignedArtifact Parse(string json);
  public static SignedArtifact Parse(ReadOnlySpan<byte> utf8);
}
SignedArtifact DetachedSigning.Sign(Artifact artifact, string keyId, ECDsa privateKey);
bool DetachedSigning.VerifyCryptographic(SignedArtifact signed,
  ReadOnlySpan<byte> publicKeySpki);
byte[] DetachedSigning.ProtectedInput(DetachedSignature signature);
```

The experimental detached wire envelope is `{"artifact": <original artifact>, "signature": {"algorithm": ..., "keyId": ..., "issuer": ..., "artifactRef": ..., "value": ...}}`. It is not a new APS-DDA artifact, JWS, JOSE `ES256` token or Ed25519. Strict converters for `Artifact` and `SignedArtifact` support System.Text.Json round trips; explicit raw parsing is preferred at effect-bearing HTTP boundaries so the **entire** request envelope is checked for duplicates before normal deserialization. Parsing a signed envelope does not verify it.

Algorithm constant is `APS-DDA-ECDSA-P256-SHA256-P1363-v1`. The signature input is UTF-8/JCS of exactly this protected object, without `value`:

```json
{
  "domain": "APS-DDA/0.1 detached artifact signature v1",
  "algorithm": "APS-DDA-ECDSA-P256-SHA256-P1363-v1",
  "keyId": "<absolute pinned key URI>",
  "issuer": "<exact artifact issuer>",
  "artifactRef": {
    "artifactType": "<type>", "id": "<id>",
    "artifactVersion": "<version>", "contentDigest": "<actual content digest>"
  }
}
```

Native `.NET ECDsa` on named NIST P-256 signs SHA-256 of these bytes. `value` is unpadded base64url of a **64-byte IEEE P1363 r||s** signature, not DER. Public keys are X.509 SubjectPublicKeyInfo DER, pinned independently, with no trailing data. Signature references contain no optional URI. Signing first checks actual artifact/plan integrity; verification checks algorithm, issuer, exact reference/integrity, named curve, key bytes, encoding and cryptographic validity. Signatures are nondeterministic; no byte-identical signature promise or low-S normalization is made. Never use signature bytes as the idempotency identity. Signing keys must be generated/loaded at runtime and excluded from source, fixtures, logs and artifacts. The SDK does not manage private-key storage.

`VerifyCryptographic` returns `false` for normal invalid cryptographic/integrity input. It explicitly does **not** decide whether that issuer/key is trusted or permitted to issue the artifact, nor does it grant execution authority.

## Tenant-scoped trusted directory

```csharp
new TrustedKey(string keyId, byte[] publicKeySpki,
  DateTimeOffset? notBefore = null, DateTimeOffset? expiresAt = null);
byte[] trustedKey.ExportPublicKey(); // defensive copy

new TrustedParticipant(string issuer, IEnumerable<TrustedKey> keys,
  IEnumerable<ArtifactType> artifactTypes, IEnumerable<string> roles,
  IEnumerable<string> profiles, IEnumerable<string> artifactVersions,
  IEnumerable<string>? bindings = null, IEnumerable<string>? audiences = null,
  IEnumerable<string>? endpoints = null,
  IEnumerable<string>? semanticNamespaces = null);

new TrustedDirectory(string tenantId, TimeProvider? clock = null);
void directory.Register(TrustedParticipant participant);
void directory.PinArtifact(Artifact artifact);
void directory.RevokeIssuer(string issuer);
void directory.RevokeKey(string issuer, string keyId);
void directory.RevokeArtifact(ArtifactRef reference);
void directory.SupportExtension(ExtensionSupport support);
Artifact directory.Validate(SignedArtifact signed, TrustRequirement requirement);
Artifact directory.Resolve(ArtifactRef reference,
  IEnumerable<SignedArtifact> retainedArtifacts, TrustRequirement requirement);

public sealed record TrustRequirement(ArtifactType ExpectedType,
  string RequiredRole, string? ExpectedIssuer = null,
  string Profile = Artifact.SpecVersion, string? Binding = null,
  string? Audience = null, bool RequirePinnedArtifact = false);
public sealed record ExtensionSupport(string Uri, string Version,
  Func<JsonNode, bool> Validate);
```

All keys, artifact types, roles, profiles, artifact versions, endpoint/audience and semantic-namespace allowlists must be supplied by **trusted administrator configuration**, not copied from a client request, self-published manifest or discovery response. Participant collections are immutable frozen copies. Issuers cannot be silently re-registered. `PinArtifact` is an administrator action that pins an actual digest; it does not automatically authenticate the artifact's source. Dynamic evidence/results can validate without an artifact pin if their explicitly approved issuing key/type/role is sufficient for the chosen profile. Set `RequirePinnedArtifact=true` for manifests, data/process contracts and any artifact whose exact version must be in a trusted directory. Do not derive this choice from the incoming request.

Every validation checks type, integrity, expected source issuer if supplied, known/non-revoked participant/key/artifact, configured role/type/profile/artifact-version/binding/audience, key time window, no future artifact issuance, actual signature and tenant context if present. Manifest claims must be subsets of configured roles/profiles/bindings/endpoints/audiences. Process capabilities must use pinned endpoint/audience/key and approved command semantic namespace; DataProducts must use approved semantic namespaces. Source-issued receipt/evidence validation must pass the **actual configured source issuer**, not just any participant supporting the role. Resolution only scans retained provided artifacts and validates exact pins; it never fetches reference URLs.

The original schema's extension values are unconstrained. This reference binding imposes a stricter fail-closed acceptance rule: each extension must have a pinned HTTPS URI and an object containing an exact supported `version` string and a `critical` Boolean; its registered deterministic validator must succeed. Unknown or unversioned extensions, including unknown non-critical ones, are refused as `UNKNOWN_CRITICAL_EXTENSION`. This acceptance policy is explicitly experimental; it does not amend the unchanged normative schema. A supported validator must check every effect-bearing extension field, not simply accept any object. Callback exceptions fail closed. There is no arbitrary code or schema fetched from artifacts.

Trust validation returns an immutable artifact, not a durable, permanently trusted certificate. Invoke it again at each relevant dispatch/commit boundary. Registry state and revocation are in process and local; durable trust provisioning, key rotation APIs, remote status assertions, revocation propagation and production identity providers are **not** implemented in core. This class is not a substitute for authenticated principal/source permissions, expiry/consumption of business authority or source authorization.

## Plan, approval, invocation and receipt binding checks

```csharp
void ArtifactBindings.RequireSameContext(JsonObject expected, JsonObject actual);
void ArtifactBindings.RequirePlanDag(Artifact plan);
JsonObject ArtifactBindings.GetStep(Artifact plan, string stepId);
void ArtifactBindings.RequirePlanReference(Artifact boundArtifact, Artifact plan);
void ArtifactBindings.RequireApprovalBinding(Artifact approval, Artifact plan,
  Artifact policyDecision);
void ArtifactBindings.RequireInvocationBinding(Artifact invocation, Artifact plan,
  JsonObject payload, Artifact capability, string sourceAudience);
void ArtifactBindings.RequireReceiptBinding(Artifact receipt, Artifact invocation);
void ArtifactBindings.RequireRefType(ArtifactRef reference, ArtifactType expected);

string BindingDigests.Payload(JsonObject normalizedPayload);
string BindingDigests.Invocation(JsonObject context, ArtifactRef capability,
  string target, JsonObject normalizedPayload, string expectedState,
  string idempotencyDomain, string sourceAudience);
```

Context comparison covers the complete original ExecutionContext with JCS equality: tenant/subject/workload/executor/purpose/correlation, policy refs, optional delegation/classification/residency. `context.correlationId` is the durable workflow identity, not an invented same-version `workflowId` field. The ordered DAG check refuses duplicate steps, duplicate dependencies, self/forward/missing dependencies and wrong typed decision/policy/capability/evidence/compensation refs. `GetStep` returns a defensive step copy.

`RequirePlanReference` checks the exact plan artifact reference, real plan digest and complete context. Approval checks additionally bind both plan/approval policy refs to the actual PolicyDecision and verify the allowed step IDs exist and are unique. Invocation checks bind the exact plan/context, capability reference/digest, source audience from the referenced capability, step ID, target, payload digest, expected state, idempotency key/domain/fingerprint and typed authority refs. Receipt checks bind the actual invocation and require typed postcondition/predecessor refs. Actual trust, source state/postconditions, time, policy outcomes and authority consumption remain separate runtime checks. These helpers never treat PolicyDecision `allow` as an authority grant.

The original plan/command schema contains `payloadDigest`, not raw payload or a separate source-audience field. Supply normalized payload through a **separate explicitly experimental runtime/HTTP binding**; bind its JCS digest to the original step and invocation. Resolve source audience and command semantics from the exact pinned ProcessCapability. Do not add undeclared fields to the original closed artifact bodies. Use reviewed URI/versioned body extensions for richer analytical/domain commitments.

`BindingDigests.InvocationProfile` is `aps-dda.experimental/invocation-fingerprint/1`. Its documented external fingerprint core is exactly:

```json
{
  "profile": "aps-dda.experimental/invocation-fingerprint/1",
  "context": "<complete original ExecutionContext object>",
  "capabilityRef": "<exact original ArtifactRef object, without optional uri>",
  "sourceAudience": "<exact source audience>",
  "target": "<exact resolved target>",
  "payload": "<complete normalized JsonObject payload>",
  "expectedState": "<source precondition>",
  "idempotencyDomain": "<source domain>"
}
```

The angle-bracket descriptions above stand for actual JSON objects/values, not strings in the computed core. Output is `sha256:lowercase_hex(SHA256(JCS(core)))`. Complete context intentionally binds correlation, delegation and policy identity. Attempt number, transport time/credentials/trace, authority consumption status, receipt and signatures are excluded because they are outside this core. Do not mint a new correlation/key after a lost reply. This fingerprint convention is an explicit experimental source contract, not a claim the v0.1 standard mandates this exact invocation projection. Payload and any external schema/policy digest rules must be documented by their publisher.

## Minimal use with runtime-generated keys

```csharp
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ApsDda.Core;

// body must be a full original-schema body generated by your domain/runtime;
// never load historical fixtures as trusted runtime artifacts.
Artifact Issue(JsonObject body, string issuer, ECDsa runtimeKey, string keyId) {
    var artifact = Artifact.Create(ArtifactType.DecisionSpec, issuer, body,
        DateTimeOffset.UtcNow);
    var signed = DetachedSigning.Sign(artifact, keyId, runtimeKey);
    // Persist artifact and detached signature separately or as SignedArtifact.ToJson().
    return signed.Artifact;
}

using var runtimeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var issuer = "https://decision.example.invalid";
var keyId = "https://decision.example.invalid/keys/test-p256-1";
var directory = new TrustedDirectory("synthetic-tenant");
directory.Register(new TrustedParticipant(issuer,
    [new TrustedKey(keyId, runtimeKey.ExportSubjectPublicKeyInfo())],
    [ArtifactType.DecisionSpec], ["APS-Orchestrator"],
    [Artifact.SpecVersion], ["1.0.0"], ["https-json"]));
// Validate signed artifacts with caller-configured issuer/type/role/binding, then
// verify retained evidence, policy, exact plan and source authority in runtime.
```

Exceptions use `ApsDdaException.Code` and a safe message; payload values and secrets are not deliberately included. Normal refusal codes include `INVALID_ARTIFACT`, `UNTRUSTED_PARTICIPANT`, `UNSUPPORTED_PROFILE`, `UNSUPPORTED_VERSION`, `UNKNOWN_CRITICAL_EXTENSION` and `AUTH_DENIED`. Administrative misuse can throw argument/cryptographic/invalid-operation exceptions. Do not serialize stack traces/inner exceptions into public HTTP responses. Correlation/retry metadata belongs to the runtime's original-schema Error or a documented experimental transport error envelope.

## Not implemented by core

The core has no persistent stores, source effects, approval consumption, authenticated subject entitlement, policy engine, evidence freshness/domain equivalence, global snapshot/transaction, operation polling/reconciliation, compensation, application compiler, HTTP host/client or production identity/key service. Full behavioral and semantic conformance is not claimed. Runtime authors must persist exact original artifacts/signatures/references, intersect authenticated principal with source permissions, validate real retained evidence and policy, enforce local transactions/state guards/idempotency, and verify actual source receipts/postconditions before workflow completion. Hashes bind declared bytes, not truth or business safety.

## Core verification recorded 3 November 2026

The core-only solution passed locked restore and a Release build with **zero warnings/errors** on SDK 10.0.403. The real xUnit suite passed **130 tests, zero failures/skips**. One of those tests compares all **10,000** deterministic V8 number vectors after verifying their pinned upstream corpus hash; that is not 10,000 independently counted xUnit test cases. Coverage also includes the six upstream canonicalization corpora, RFC 8785 Appendix B vectors, strict parsing/Unicode/unsafe-number cases, all 15 original shape fixtures with placeholder-integrity rejection, actual-digest artifact chains, independent golden plan/content digests, exact field/reference/DAG binding, detached signature tampering/wrong curve/key, issuer/type/role/profile/version/binding/audience pins, tenant isolation and local revocation.

A local Release NuGet package was produced and inspected for the original schema and all eight unchanged vendored sources/licenses/notices. A newly created **outside-checkout** net10.0 console consumer restored that package and passed embedded-schema hash, actual artifact digest, runtime-generated native P-256 detached signature and pinned directory validation. No private key was written to disk. The package also includes this API contract. The first preview is prepared for GitHub Releases, not NuGet.org publication.

These are actual core test results, not production integration tests, external security review, full behavioral/semantic conformance or certification. Integrated Runtime/HTTP/demo/Acceptance verification is recorded in [implementation status](docs/implementation-status.md).

## Runtime v0.1.0-preview.1 — concrete handoff

Namespace is `ApsDda.Runtime`; target remains `net10.0`. The owned project is `src/ApsDda.Runtime/ApsDda.Runtime.csproj`, with `tests/ApsDda.Runtime.Tests` and [binding guarantees/limits](docs/reference-binding.md). Package references pin Microsoft.Data.Sqlite **10.0.0** and the patched native SQLitePCLRaw.lib.e_sqlite3 **3.53.3** (native version asserted by an actual SQL query). Restore lockfiles include all transitives. The approved license scopes and dependency exceptions are in [LICENSING.md](LICENSING.md) and [package-license.md](package-license.md).

### Trusted principals and source configuration

```csharp
public sealed record PrincipalRegistration(string Identity, string TenantId,
  string SubjectId, string WorkloadId, string ExecutorId,
  IEnumerable<string> Roles, IEnumerable<string> Purposes,
  IEnumerable<string> Audiences, IEnumerable<string> Resources,
  IEnumerable<string> CapabilityDigests,
  string Classification = "internal", string Residency = "local");

new PrincipalDirectory();
AuthenticatedPrincipal principals.Register(PrincipalRegistration registration);
AuthenticatedPrincipal principals.ResolveAuthenticated(string identity);
void principals.Revoke(string identity);
void principals.RequireCurrent(AuthenticatedPrincipal principal, string role);
```

`AuthenticatedPrincipal` has **no public constructor** and exposes immutable `Identity`, `TenantId`, `SubjectId`, `WorkloadId`, `ExecutorId`, `Classification`, `Residency`, and frozen `Roles`, `Purposes`, `Audiences`, `Resources`, `CapabilityDigests`. Register is an administrator action; ResolveAuthenticated is only for the already-authenticated host boundary, not a user-chosen request identity. Callers cannot supply this object through JSON. Exact context/role/source permissions are checked again on dispatch. Fixed selected roles are `Executor`, independent `Approver`, and `PolicyGranter`. Delegation is refused.

```csharp
new ArtifactSigner(string issuer, string keyId, ECDsa privateKey);
string signer.Issuer;
string signer.KeyId;
SignedArtifact signer.Issue(ArtifactType type, JsonObject body,
  DateTimeOffset now, string? id = null);

public sealed record PolicyEvaluation(string Outcome,
  IReadOnlyList<string> Obligations);
public interface IRuntimePolicy {
  JsonObject PolicyReference { get; }
  PolicyEvaluation Evaluate(Artifact decision);
}
new EligibilityPolicy(string uri, string version, bool requireHumanApproval);
string RuntimeDigests.PolicyDecision(Artifact decision,
  JsonObject policyReference, PolicyEvaluation evaluation);

new EvidenceRule(ArtifactRef productRef, string issuer, JsonArray semantics,
  TimeSpan maxAge, Func<JsonObject,bool> validateValue,
  string qualityState = "good");

new SourceRegistration(SignedArtifact capability, SignedArtifact product,
  ArtifactSigner signer, IEnumerable<string> resources,
  IEnumerable<string> purposes, IEnumerable<string> subjectIds,
  IEnumerable<string> workloadIds, IEnumerable<string> roles,
  IEnumerable<EvidenceRule> evidenceRules,
  long maximumQuantity = 100, bool policyGrantAllowed = false,
  long maximumPolicyQuantity = 10,
  int maximumPolicyOperationsPerMinute = 1,
  bool requiresExternalHardInvariant = false,
  string classification = "internal", string residency = "local");
```

SourceRegistration exposes immutable corresponding PascalCase properties and `Audience` derived from its pinned capability. EvidenceRule exposes ProductRef, Issuer, defensive Semantics, MaxAge, ValidateValue, QualityState. Trust must independently pin the source capability/product and actual issuer, key, role, semantic namespace, audience and endpoint. Configure named hard-external-invariant requirements to refuse them unless implementing a different source mechanism. The built-in adapter refuses nonempty capability `invariantRefs` because it has no implementation for arbitrary invariant terms.

### File-backed stores and coordinator

```csharp
new SqliteRuntimeStore(string databasePath); // refuses :memory:
string store.DatabasePath;
void store.Retain(SignedArtifact artifact); // integrity retention, NOT trust/grant
SignedArtifact store.GetArtifact(ArtifactRef reference);
string store.RetainEvidenceValue(JsonObject value);
JsonObject store.GetEvidenceValue(string reference);
void store.SetCurrentPolicy(JsonObject policyReference); // explicit admin provisioning
void store.RevokeArtifact(ArtifactRef reference, DateTimeOffset observedAt);
void store.RevokeIssuer(string issuer, DateTimeOffset observedAt);
void store.RevokeKey(string issuer, string keyId, DateTimeOffset observedAt);
void store.SeedResource(string sourceIssuer, string target, string tenantId,
  JsonObject state, long version = 1); // in-process fixture/provisioning only
ResourceSnapshot store.GetResource(string sourceIssuer, string target);
JsonObject store.GetPayload(string workflowId, string stepId);
WorkflowSnapshot store.GetWorkflow(string workflowId);
long store.OperationCount;
long store.AuthorityUseCount;

new WorkflowRuntime(SqliteRuntimeStore store, TrustedDirectory trust,
  PrincipalDirectory principals, ArtifactSigner orchestratorSigner,
  ArtifactSigner authoritySigner, IRuntimePolicy policy,
  TimeProvider? clock = null, TimeSpan? maximumPolicyAge = null);
void runtime.RegisterEvidenceRule(EvidenceRule rule);
void runtime.RegisterSource(ISourceAdapter adapter);
ISourceAdapter runtime.GetSource(ArtifactRef capability);
void runtime.Retain(SignedArtifact artifact, TrustRequirement requirement);
WorkflowSnapshot runtime.Submit(SignedArtifact plan,
  IReadOnlyDictionary<string,JsonObject> payloads,
  AuthenticatedPrincipal caller);
SignedArtifact runtime.Approve(string workflowId,
  AuthenticatedPrincipal approver, DateTimeOffset expiresAt,
  IEnumerable<string>? allowedSteps = null);
void runtime.AdmitAuthority(SignedArtifact authority,
  AuthenticatedPrincipal authenticatedIssuer);
WorkflowSnapshot runtime.Authorize(string workflowId,
  ArtifactRef authorityRef, AuthenticatedPrincipal caller);
WorkflowSnapshot runtime.ExecuteNext(string workflowId,
  AuthenticatedPrincipal caller);
WorkflowSnapshot runtime.ExecuteAll(string workflowId,
  AuthenticatedPrincipal caller);
WorkflowSnapshot runtime.Reconcile(string workflowId,
  AuthenticatedPrincipal caller);
WorkflowSnapshot runtime.Cancel(string workflowId,
  AuthenticatedPrincipal caller);
```

The runtime also exposes Store, Trust, Principals, OrchestratorSigner, AuthoritySigner, Policy, Clock, MaximumPolicyAge. TrustedDirectory uses its own clock: configure **the same clock** for directory and runtime. Runtime startup never automatically overwrites the durable current policy. Configure it explicitly before new submissions. It currently requires a single exact policy reference in context. Evidence/decision/policy must be retained before Submit. Plans/DecisionSpecs/invocations are from the configured orchestrator issuer; PolicyDecisions and business authority are from the configured authority issuer. Roles in Core trust requirements are respectively `APS-Orchestrator`, `APS-Policy-Service`, `APS-Authority-Service`, `APS-Read-Publisher`, `APS-Action-Executor`.

Payload map keys must exactly name the plan's unique steps, with no missing or extra payload. Original schema-shaped steps contain only payload digests and Core's exact invocation fingerprints; the normalized payload map is a separate **experimental** binding and is durably retained. Correlation UUID is the workflow identity. Plan DAG supports up to 256 ordered steps. Only explicit `non-compensable` steps with the exact capability's manual owner are implemented; named compensation/source-local groups are refused rather than simulated.

`Approve` issues a signed original Approval **and authenticates/admit its approver**. `ArtifactFactory.Approval` alone does not establish admission. `Authorize` verifies original bindings and admitted identity and atomically records one durable authority-use row before any source effect. For the policy path use `ArtifactFactory.PolicyGrant`, `AdmitAuthority(grant, authenticated PolicyGranter)`, then `Authorize`. An `allow` PolicyDecision cannot be used as authority. Policy grant is one exact source/resource/capability/audience, low-risk, source-allowed, quantity/rate/time-bounded; money/delegation grants are refused. Human Approvals can allow a subset of steps; attempting an unapproved future step is refused and may require manual resolution after earlier effects.

**Host boundary:** store APIs do not authenticate reads. HTTP authors must protect all resource/workflow/payload/artifact access and all provisioning/admin operations. Do not expose seed/reset/fault/current-policy/revocation endpoints. Show the retained exact payload to an authenticated reviewer before approving, not merely the digest-based effect summary. Errors expose only ApsDdaException.Code or safe workflow error code, not stack traces/inner exceptions. ExecuteAll/ExecuteNext return truthful WorkflowSnapshot refusals/unknown states for dispatch errors; pre-admission authorization/shape failures usually throw ApsDdaException.

### Source-owned adapter API

```csharp
public sealed record ResourceSnapshot(string Target, string TenantId,
  string Version, JsonObject State);
public sealed record SourceDispatch(ArtifactRef InvocationRef,
  AuthenticatedPrincipal Caller);
public sealed record SourceResult(SignedArtifact Receipt,
  SignedArtifact PostconditionEvidence);
public sealed record OperationIdentity(string TenantId, string SourceIssuer,
  string IdempotencyDomain, string IdempotencyKey, string Fingerprint);
public interface ISourceAdapter {
  SourceRegistration Registration { get; }
  long MeasureQuantity(JsonObject normalizedPayload);
  SourceResult Invoke(SourceDispatch dispatch);
  SourceResult? Lookup(OperationIdentity identity, AuthenticatedPrincipal caller);
  void VerifyPostcondition(SourceResult result, Artifact invocation);
}
public interface ILocalSourceCommand {
  string Transition { get; }
  long Quantity(JsonObject payload);
  JsonObject Apply(JsonObject current, JsonObject payload);
  bool Verify(JsonObject postcondition, JsonObject payload);
}
new SqliteSourceAdapter(WorkflowRuntime runtime,
  SourceRegistration registration, ILocalSourceCommand command);
ILocalSourceCommand adapter.Command;
new ReserveCapacityCommand();
new PrepareNotificationDraftCommand();
long CanonicalQuantity.Parse(string text, bool allowZero = false);
string CanonicalQuantity.Format(long value);
new SourceRefusedException(string code);
string sourceRefusal.Code;
```

`ReserveCapacityCommand` payload is **exactly** `{"quantity":"<positive canonical Int64>","transition":"reserve-internal-capacity"}`; initial source state has `available` and `reserved` canonical nonnegative strings. It decrements available and increments reserved with a local capacity guard. Post-state records lastQuantity/lastTransition. `PrepareNotificationDraftCommand` payload is exactly `{"quantity":"1","transition":"prepare-notification-draft"}`; initial state has `draftCount`. It increments an internal count only and sends nothing. Source versions are canonical positive Int64 strings, initially `"1"` by default and incremented by one on a committed operation. These are synthetic internal business bindings, not universal domain semantics.

Custom adapters are trusted source-owner code, not request-configured plugins. They must preserve exact reference/fingerprint/expected-state/authority checks inside their own source enforcement boundary. `SourceRefusedException` means **known pre-commit no-new-effect**; never throw it for ambiguous transport/commit/receipt failures. Any other adapter exception is unknown. The built-in adapter stores its successful operation/receipt/postconditions and effect in one local transaction. It queries duplicate identity before considering another effect; same fingerprint returns the original operation, changed fingerprint conflicts. Reconciliation uses authenticated source lookup and verifies actual source signatures and retained source-local postconditions. It does not require the old authority to remain unexpired just to recognize a historical effect.

The coordinator persists `UNKNOWN` invocation intent before source transport. After ambiguity `ExecuteNext` routes to `Reconcile`, which queries the original key/invocation. This selected conservative binding never auto-resends, even after not-found. Unknown/not-found stays unknown with the plan's manual owner. No restart or lost reply creates a new key/correlation/invocation. Cancellation blocks future commits; it cannot undo existing effects. A later failure/cancel after effects is manual resolution, never fake global rollback.

```csharp
public enum WorkflowState {
  PLANNED, AUTHORIZED, EXECUTING, VERIFYING, COMPLETED, REJECTED,
  FAILED, CANCELED, UNKNOWN_OUTCOME, MANUAL_RESOLUTION_REQUIRED
}
public sealed record WorkflowSnapshot(string WorkflowId, ArtifactRef PlanRef,
  WorkflowState State, ArtifactRef? AuthorityRef, string? SafeError,
  IReadOnlyList<StepSnapshot> Steps);
public sealed record StepSnapshot(string StepId, string State,
  ArtifactRef? InvocationRef, ArtifactRef? ReceiptRef,
  string? ManualRecoveryOwner);
```

Step strings currently emitted are `PENDING`, `UNKNOWN`, `COMPLETED`, `REFUSED`, `CANCELED`. `VERIFYING`/`FAILED` are vocabulary members but are not currently emitted by this synchronous selected runner. Completion requires all verified step receipts. The conservative failure boundary uses `REJECTED` for a known no-new-effect refusal before any prior effect; unknown transport/verification states never become a no-effect terminal result.

### Original-compatible domain artifact factories

All methods below are static members of `ArtifactFactory`; artifacts carry real original-schema content/plan digests and detached signatures. Helpers return defensive snapshots; they do not establish trust, source data truth, approval admission or consumption.

```csharp
JsonObject Context(AuthenticatedPrincipal caller, string purpose,
  Guid workflowId, JsonObject policyReference);
JsonObject Semantic(string ns, string term, string kind,
  string localBinding, string version = "1.0.0");
JsonObject SchemaReference(string uri, JsonObject schema,
  string version = "1.0.0");
SignedArtifact DataProduct(ArtifactSigner signer, string name,
  JsonArray semanticRefs, JsonObject schemaReference,
  IEnumerable<string> purposes, string residency, DateTimeOffset now,
  string classification = "internal", string grain = "one source-owned resource");
SignedArtifact ProcessCapability(ArtifactSigner signer, string name,
  JsonObject commandSemantic, string endpoint, string audience,
  JsonObject inputSchema, JsonObject resultSchema, string domain,
  string resourceScope, string manualOwner, DateTimeOffset now,
  bool policyGrantAllowed = false, string riskTier = "moderate");
SignedArtifact Evidence(ArtifactSigner signer, JsonObject context,
  Artifact product, JsonArray semantics, string queryDigest,
  string sourceVersion, string valueRef, DateTimeOffset observedAt,
  DateTimeOffset now, string qualityState = "good", string cacheStatus = "live");
SignedArtifact Decision(ArtifactSigner signer, JsonObject context,
  string owner, string question, string objective,
  IEnumerable<string> hardConstraints, IEnumerable<string> alternatives,
  IEnumerable<ArtifactRef> evidence, string methodType, string methodVersion,
  JsonObject parameters, IEnumerable<string> assumptions,
  IEnumerable<string> uncertainty, string recommendation,
  string eligibility, DateTimeOffset now, JsonObject? extensions = null);
SignedArtifact PolicyDecision(ArtifactSigner signer, Artifact decision,
  IRuntimePolicy policy, DateTimeOffset now);
JsonObject PlanStep(string stepId, IEnumerable<string> dependencies,
  Artifact capability, string target, JsonObject payload,
  string expectedVersion, string idempotencyKey,
  IEnumerable<ArtifactRef> evidence, JsonObject context,
  DateTimeOffset expiry, string manualOwner);
SignedArtifact Plan(ArtifactSigner signer, Artifact decision,
  Artifact policyDecision, IEnumerable<JsonObject> steps,
  DateTimeOffset notBefore, DateTimeOffset expiry, DateTimeOffset now,
  JsonObject? extensions = null);
SignedArtifact Approval(ArtifactSigner signer, Artifact plan,
  AuthenticatedPrincipal approver, IEnumerable<string> allowedStepIds,
  DateTimeOffset expiry, DateTimeOffset now);
string EffectSummary(Artifact plan, IEnumerable<string> steps);
SignedArtifact PolicyGrant(ArtifactSigner signer, Artifact plan,
  Artifact capability, string resource, IEnumerable<ArtifactRef> evidence,
  string purpose, long quantity, int rate, DateTimeOffset expiry,
  DateTimeOffset now, string revocationRef);
SignedArtifact Invocation(ArtifactSigner signer, Artifact plan,
  string stepId, Artifact authority, string receiptRoute, DateTimeOffset now);
JsonArray Strings(IEnumerable<string> values);
JsonArray Refs(IEnumerable<ArtifactRef> values);
```

Factory context includes explicit internal classification/local residency unless the principal's configured values differ. Product factory declares exact-key reads, source-current time semantics and manual retention; it is a construction helper, **not** a implemented general read publisher. Capability factory declares the exact-local-version/indefinite-retention/original-receipt profile and internal non-compensability. Decision/plan extensions must be explicitly reviewed and registered in Core TrustedDirectory; the runtime does not guess which extensions are safe. Create original analytical/domain artifacts with these full-schema factories or ArtifactSigner.Issue, not historical placeholder fixtures. [Harness.cs](tests/ApsDda.Runtime.Tests/Harness.cs) gives the concrete trusted key/contract/source/identity generation order and signed artifact chain; [MultiSourceTests.cs](tests/ApsDda.Runtime.Tests/MultiSourceTests.cs) shows a second independently signed internal source.

Runtime does not implement production OAuth/mTLS/HTTPS, encrypted SQLite, private-key storage/rotation, principal/trust provisioning persistence, remote revocation distribution, source contract negotiation/read budgets, arbitrary semantics/identity mappings, global transactions/snapshots, actual compensation, worker/outbox scheduling, source API interoperability or full standard conformance. Local durable artifact/key/issuer revocations and current policy are persisted, but Core's independently configured directory and host principal directory must be provisioned again on process startup without trusting request claims. No live insurer mutation or customer contact occurs.

### Runtime verification recorded 3 November 2026

Locked restore and Release solution build passed on SDK 10.0.403 with **zero warnings/errors**. The real runtime xUnit suite passed **85 tests, zero failures/skips**; the unchanged Core suite passed **130 tests, zero failures/skips** in separate final runs. Runtime tests use real file-backed SQLite and newly opened store/runtime objects, runtime-generated P-256 keys, original-schema actual-digest artifacts, authenticated authority admission, and actual local source state mutations. They cover human approval, policy-allow-not-authority, exact bounded policy grants/quantity/rate, caller/context/source permissions, durable approval-use races/reopen, policy/current-version/revocation/expiry/freshness, semantic and payload/signature tampering, expected-version/source-owned guard refusal, operation-insertion rollback, original-key idempotency/conflict, lost replies/unknown lookup without resend, invalid receipts/postconditions, cancellation intent and partial/manual outcomes. Two independently signed logical sources are exercised both successfully and with a second-source stale refusal preserving the first effect.

All decision evidence must be represented by the exact union of plan-step evidence references. The source revalidates **the complete decision evidence set** at each dispatch/commit boundary, not only the current step's evidence subset. The source's explicitly configured required EvidenceRules must also be present. This is still a reviewed-domain binding, not universal semantic equivalence or a source-truth guarantee.

A local `ApsDda.Runtime.0.1.0-preview.1.nupkg` was generated and inspected for DLL/XML documentation, exact API contract, package README and reference-binding document. A newly created **outside-checkout** net10.0 console restored that package and the Core package through NuGet (not project references), constructed original-schema signed artifacts, authenticated and consumed an exact human Approval, committed a real SQLite capacity reservation, verified source receipt/postcondition completion, and reopened SQLite to assert the original invocation and one authority/operation row remained. No private key was written to disk. A current NuGet vulnerability check including transitive runtime dependencies reported **no vulnerable packages given its configured sources**; this is not a security certification or permanent absence-of-vulnerability guarantee.

Production identity/key services, remote status propagation, external-source interoperability and complete APS-DDA conformance remain unimplemented.

## Integrated HTTP, analytical demo and verification contracts

The complete solution also contains the ASP.NET Core library/API example, synthetic analytical demo/console, five test projects and finite verification tool. See the [HTTP public API supplement](src/ApsDda.AspNetCore/API_CONTRACT.md), [analytical demo/compiler supplement](src/ApsDda.Demo/API_CONTRACT.md), [HTTP developer guide](docs/http-quickstart.md), [insurance walkthrough](docs/insurance-demo.md) and [integrated evidence/status](docs/implementation-status.md). Supplements specify separately experimental envelopes and domain bindings; they do not amend the original artifact schema.

`tools/ApsDda.Verify` is a developer-only C# executable. After Release build and local pack, invoke `dotnet run --project tools/ApsDda.Verify -c Release --no-build --no-restore -- . artifacts/packages`. It inspects all three packages, starts a finite loopback Kestrel process with independent runtime-generated credentials, exercises the typed client, measures actual source state, stops the host, and creates a fresh package-only console outside the checkout with an isolated NuGet cache. The consumer checks the installed embedded original schema, cryptographic chain, authority consumption, actual effect, verified original receipt, duplicate idempotency and durable reopen. Temporary state is removed. It publishes nothing and requires no Python or Docker.

The package-only consumer template is not part of the solution: its APS-DDA package references intentionally resolve only after the local libraries are packed. All solution projects have restore lockfiles; the fresh consumer generates a lockfile in its temporary directory, then repeats locked restore. See the root README for cross-platform commands and measured test counts rather than inferring coverage from the existence of these modules.
