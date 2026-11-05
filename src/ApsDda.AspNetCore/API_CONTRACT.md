# ASP.NET Core concrete handoff — v0.1.0-preview.1

Target: `net10.0`; namespaces `ApsDda.AspNetCore` (library) and `ApsDda.Api` (sample). Experimental selected behavior, not complete APS-DDA conformance, production authentication or certification. New implementation source was AI-assisted. Author: Kunal Yadav; see [approved license scopes and exceptions](../../LICENSING.md).

This supplements the [shared Core/Runtime contract](../../API_CONTRACT.md) without changing it. The [HTTP quickstart](../../docs/http-quickstart.md) is the developer-facing wire contract and commands.

## Configuration and actual factory

```csharp
new ApsDdaHttpBinding(WorkflowRuntime runtime, string tenantId, string purpose,
    SignedArtifact manifest, IEnumerable<HttpResourceRegistration> resources,
    IEnumerable<KeyValuePair<string,string>> tokenToIdentity,
    IBoundedPlanPreparer? planPreparer = null,
    bool allowDevelopmentLoopbackHttp = false,
    int maximumRequestBytes = 262_144, int maximumPlanSteps = 32);

IServiceCollection services.AddApsDdaHttp(ApsDdaHttpBinding binding);
WebApplication app.UseApsDdaHttp();
RouteGroupBuilder app.MapApsDda();
WebApplication ApsDdaApplication.Create(string[] args, ApsDdaHttpBinding binding,
    Action<WebApplicationBuilder>? configureBuilder = null);

public sealed record HttpResourceRegistration(string Alias,
    ArtifactRef CapabilityRef, string Target);
public interface IBoundedPlanPreparer {
    StorePlanRequest Prepare(PreparePlanRequest request,
        AuthenticatedPrincipal principal);
}
```

`ApsDdaApplication.Create` returns an unstarted actual `WebApplication`; the caller owns its lifetime. The callback runs before `Build`, allowing `builder.WebHost.UseTestServer()` or host-controlled Kestrel configuration. Extensions support an existing ASP.NET Core host. Use middleware before mapping endpoints; do not insert forwarded-header rewriting or another authentication policy that bypasses the selected boundary. No forwarded-header middleware, CORS, public listener or production TLS certificate provisioning is supplied by this library.

Trust, source adapters, policy, directory, principal registrations and aliases are trusted host configuration. Binding tenant must equal runtime trusted-directory tenant. Every configured read alias points to an exact source capability/target. The catalog is bounded to 100 aliases. Principals are resolved only after a configured bearer token authenticates; no request identity, role or context establishes trust. Token values must be 43–256 base64url characters; generate from at least 32 random bytes, because syntax cannot prove entropy. The boundary retains SHA-256 token hashes, compares with `FixedTimeEquals`, and resolves the current server registration for every request. There is no default credential, administrator role or token creation HTTP endpoint.

The configured `Reader`, `Executor` and independent `Approver` permissions are intersected with tenant, purpose, resource, capability, source audience, classification/residency and current registration. Only matching executors may store/authorize/execute/reconcile/lookup workflows. Approvers may inspect exact plans/payloads and approve within their configured scope, not act as the submitting executor. Runtime independently rejects same-subject approval and rechecks authority/source permission at dispatch/commit.

HTTPS is required unless both the ASP.NET environment is `Development` and `allowDevelopmentLoopbackHttp` is true and the request host and actual remote IP are loopback. A null remote IP is accepted for TestServer's in-process development transport. Production ignores that exception. Direct Kestrel configuration is the sample's transport boundary; proxy/TLS-termination/mTLS/OAuth integration is not implemented.

## Wire records

`HttpBinding.Version = "aps-dda.experimental/http/1"`; `HttpBinding.Prefix = "/aps-dda/v1"`. `HttpBinding.CreateJsonOptions()` supplies camel-case JSON, case-sensitive properties, required constructor fields, rejected unknown members and string enums. Original `Artifact`/`SignedArtifact` converters are from Core.

```csharp
StorePlanRequest(string Binding, SignedArtifact Plan,
    Dictionary<string,JsonObject> Payloads);
PreparePlanRequest(string Binding, string ResourceAlias, string Quantity,
    string ExpectedVersion);
ApprovalRequest(string Binding, string ReviewDigest,
    DateTimeOffset ExpiresAt, string[] AllowedStepIds);
AuthorizationRequest(string Binding, ArtifactRef AuthorityRef);
ActionRequest(string Binding);
WorkflowReview(string Binding, WorkflowSnapshot Workflow, SignedArtifact Plan,
    Dictionary<string,JsonObject> Payloads, string ReviewDigest);
ResourceRead(string Binding, ResourceSnapshot Resource, SignedArtifact Product,
    DateTimeOffset ObservedAt);
OperationLookup(string Binding, bool Found, SourceResult? Result);
HttpError(string Binding, string Code);
```

All these are public sealed records. Envelopes are experimental, not additional APS-DDA artifact types or normative schema modifications. Every POST strictly parses the **entire raw UTF-8 envelope** with Core `StrictJson` before deserializing. The default request bound is 256 KiB, configurable to at most Core's 1 MiB. Only uncompressed `application/json`, optionally UTF-8 charset, is accepted. Decoded duplicate names at any depth, malformed Unicode, unsafe JSON numeric magnitudes, missing required fields, unknown fields and non-finite JSON numbers fail closed.

The normalized payload map is separate from the original closed `ExecutionPlan` body. It must bind each exact plan step's original payload digest/fingerprint. StorePlan accepts only a signed plan from the independently pinned runtime orchestrator and matching server principal context; evidence/decision/policy dependencies must already be provisioned or generated through a reviewed server factory. There is no generic artifact-ingestion, arbitrary policy/signing or trust-registration route.

Approval `reviewDigest` is `sha256:hex(SHA256(JCS({"profile":"aps-dda.experimental/http-review/1","planRef":<exact original ref>,"payloads":<complete retained payload map>})))`. Its equality binds a review response to the exact retained plan and payloads; it proves neither comprehension nor human presence. A different plan/correlation yields a different digest even with the same quantity. Approval returns a signed original `Approval`; it is admitted by the authenticated independent approver. Authorize consumes its exact reference through runtime before any effect. PolicyDecision evidence is not authority.

## Routes

All routes below use the authenticated configured-token policy, including metadata and reads.

| Method/path under prefix | Permission | Input → output |
| --- | --- | --- |
| GET `/manifest` | Reader/Executor/Approver with full configured catalog scope | original `SignedArtifact` CapabilityManifest |
| GET `/resources/{alias}` | Reader/Executor/Approver, exact catalog/source scope | `ResourceRead`, one actual SQLite resource snapshot and original signed DataProduct |
| POST `/plans/prepare` | Executor | `PreparePlanRequest` → `StorePlanRequest`, only when a reviewed `IBoundedPlanPreparer` is configured |
| POST `/plans` | matching Executor | `StorePlanRequest` → `WorkflowSnapshot`, persists exact signed plan/payload map |
| GET `/workflows/{workflowId}` | matching Executor or scoped Approver | `WorkflowReview`, including complete retained payloads |
| POST `/workflows/{workflowId}/approve` | scoped independent Approver | `ApprovalRequest` → signed original Approval |
| POST `/workflows/{workflowId}/authorize` | matching Executor | `AuthorizationRequest` → `WorkflowSnapshot` |
| POST `/workflows/{workflowId}/execute` | matching Executor | `ActionRequest` → `WorkflowSnapshot`, at most ONE next source step |
| POST `/workflows/{workflowId}/reconcile` | matching Executor | `ActionRequest` → `WorkflowSnapshot`, lookup original unknown invocations only |
| GET `/workflows/{workflowId}/steps/{stepId}/operation` | matching Executor | `OperationLookup`, derives original source/key/fingerprint from retained invocation; never caller-provided source IDs |

Operation lookup verifies a found receipt/postcondition through the configured source adapter. `found:false` does not prove no effect and never authorizes resend. Execute after an unknown intent routes through runtime reconciliation, not a new invocation/key. Workflow refusals and unknown outcomes may have HTTP 200 with truthful snapshot state; inspect state and `safeError`, not only transport status.

Transport status: unauthenticated/invalid/revoked token 401; role/tenant/purpose/context/source-scope refusal 403; unknown alias/step or malformed workflow identifier 404; request byte/step bound 413; unsupported media 415; authority-consumption/conflict 409; other safe validation/refusal 400. A syntactically valid but unretained workflow currently inherits runtime `INVALID_ARTIFACT`/400. Unexpected errors produce a generic 500. Errors serialize only version and allowlisted code, never exception messages, inner exceptions or stacks. Response caching is disabled for supported-boundary traffic.

## Typed client

```csharp
new ApsDdaClient(HttpClient http, string token,
    bool allowDevelopmentLoopbackHttp = false);
IHttpClientBuilder services.AddApsDdaClient(Uri baseAddress, string token,
    bool allowDevelopmentLoopbackHttp = false);
```

Typed methods are `GetManifestAsync`, `ReadResourceAsync`, `PreparePlanAsync`, `StorePlanAsync`, `GetWorkflowAsync`, `ApproveAsync`, `AuthorizeAsync`, `ExecuteNextAsync`, `ReconcileAsync` and `LookupOperationAsync`; each accepts a final optional `CancellationToken`. Their input/output types follow the route table. The DI extension uses actual `IHttpClientFactory`, a 30-second client timeout and disabled redirects. When manually constructing `HttpClient`, disable redirects yourself. Base addresses must be HTTPS without URI user credentials/query/fragment, or explicitly permitted loopback HTTP. Tokens are added only as Authorization headers, never artifact fields. Typed requests have a 256-KiB bound; responses are streamed with a 1-MiB bound and strict duplicate JSON validation. `ApsDdaHttpException` exposes `StatusCode` and safe `Code`, not credential/payload diagnostics. Signed response parsing/integrity is not issuer trust pinning; clients must independently configure desired issuer/key trust before relying on artifacts.

## Sample and limits

`SyntheticApiScenario` provisions process-generated P-256 keys, exact pinned original artifacts/contracts and separate worker/reviewer/read-only identities against real file-backed SQLite. Its fixed `north` reservation factory permits canonical positive quantity up to 50 and requires the actual expected version. Source availability is checked atomically by the source at commit, not guaranteed by read/prepare. The source starts with 20 available/0 reserved units and sends no notifications or customer decisions. Source contract URIs under `.example.invalid` are logical synthetic pins for the in-process adapter, **not hosted external source endpoints**. The API is an explicit reference gateway binding rather than implementation of every original ProcessCapability URL.

The sample requires a new database per process and refuses an existing path: in-memory signing keys/trust are not recoverable across sample host restarts. Runtime's durable database/operation functionality is independently implemented and tested; production hosts must provision stable secure signing keys, exact source contracts, principals and trust before reopening. No automatic reset occurs. There is no worker scheduling/outbox, remote revocation propagation, key rotation, encryption-at-rest, OAuth/mTLS, proxy trust, HTTP rate limiter, durable identity provisioning, general source query/budget engine, global transactions, external-source interoperability, compensation API or production human-review UX. ResourceRead is an experimental one-row observational snapshot, not a signed EvidenceBundle or guarantee of the DataProduct's complete generic read-contract/SLO.

## Verification and integration handoff — 3 November 2026

The project-specific locked restore and Release build passed on SDK 10.0.403 with zero warnings/errors; **89 real xUnit HTTP pipeline tests passed, zero failures/skips**. A finite actual localhost Kestrel replay committed internal capacity from available 20/reserved 0 to available 18/reserved 2/version 2, verified it over HTTP with the read-only identity, found the original source operation, checked no reset route and stopped the host. This is not TLS handshake, Windows execution or production identity-provider validation. A local package and outside-checkout package consumer passed. A transitive vulnerability query found none given its configured sources at that time.

The HTTP library, sample host and HTTP tests are integrated into `ApsDda.sln`. [Implementation status](../../docs/implementation-status.md) records the complete 368-case local Linux run, all 13 console scenarios, actual loopback Kestrel and the fresh local-package consumer. The first preview is prepared for GitHub Releases, not NuGet.org; no hosted sample or production deployment is claimed.
