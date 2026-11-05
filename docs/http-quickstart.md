# HTTP quickstart — experimental .NET 10 binding

`ApsDda.AspNetCore` is an actual ASP.NET Core Minimal API boundary and typed HttpClient over `ApsDda.Runtime`. `examples/ApsDda.Api` is a developer-run **synthetic internal capacity** host, not a hosted website, production identity integration or insurer deployment. Source development was AI-assisted. It implements selected APS-DDA v0.1 behavior, not complete conformance or certification. Author: Kunal Yadav; see [approved license scopes and exceptions](../LICENSING.md).

The unchanged original artifacts remain schema-shaped and signed; the versioned HTTP envelopes are separately experimental. See the [concrete HTTP API contract](../src/ApsDda.AspNetCore/API_CONTRACT.md), [Core/Runtime API](../API_CONTRACT.md), and [runtime binding limits](reference-binding.md).

## Prerequisites and verification

Install the SDK selected by `global.json` (10.0.403) and work from the repository root. The projects target `net10.0`. Linux/macOS Bash and Windows PowerShell commands below do not require GitHub authentication, a Docker daemon, an LLM or any public deployment. Restore lockfiles are checked in for the library, sample and tests.

```sh
dotnet restore tests/ApsDda.AspNetCore.Tests/ApsDda.AspNetCore.Tests.csproj --locked-mode
dotnet build tests/ApsDda.AspNetCore.Tests/ApsDda.AspNetCore.Tests.csproj -c Release --no-restore
dotnet test tests/ApsDda.AspNetCore.Tests/ApsDda.AspNetCore.Tests.csproj -c Release --no-build --no-restore
dotnet pack src/ApsDda.AspNetCore/ApsDda.AspNetCore.csproj -c Release --no-build --no-restore -o artifacts/packages
```

These project-specific commands work independently of solution aggregation. The integration suite uses an actual `WebApplication` factory and ASP.NET Core TestServer, not handler stubs. It exercises authentication/authorization, strict JSON/media/size boundaries, exact payload review/approval, real source SQLite mutation, original-receipt lookup, lost reply/reconciliation without resend and refusal of unsafe public operations. HTTPS policy tests use TestServer's request scheme; they do **not** claim actual TLS certificate/handshake testing.

## Local synthetic protocol replay: Bash

Run the following in **one Bash shell** after building. Generate three independent random bearer tokens; there is no administrator/default token. The tokens are process environment values, not written to artifacts or passed in arguments. Do not use shell tracing (`set -x`) or print them. The server binds loopback only, and HTTP additionally requires the explicit Development exception.

```bash
set -euo pipefail
new_token() { openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n'; }
export APS_DDA_EXECUTOR_TOKEN="$(new_token)"
export APS_DDA_APPROVER_TOKEN="$(new_token)"
export APS_DDA_READER_TOKEN="$(new_token)"
export DOTNET_ENVIRONMENT=Development
export APS_DDA_ALLOW_LOOPBACK_HTTP=true
export ASPNETCORE_URLS=http://localhost:5127
work_dir="$(mktemp -d)"
export APS_DDA_DATABASE="$work_dir/runtime.sqlite"
api_dll=examples/ApsDda.Api/bin/Release/net10.0/ApsDda.Api.dll

dotnet "$api_dll" > "$work_dir/host.log" 2>&1 &
server_pid=$!
cleanup() {
  kill "$server_pid" 2>/dev/null || true
  wait "$server_pid" 2>/dev/null || true
  unset APS_DDA_EXECUTOR_TOKEN APS_DDA_APPROVER_TOKEN APS_DDA_READER_TOKEN
}
trap cleanup EXIT
ready=false
for attempt in $(seq 1 60); do
  status="$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5127/aps-dda/v1/manifest || true)"
  if [ "$status" = 401 ]; then ready=true; break; fi
  kill -0 "$server_pid" 2>/dev/null || { cat "$work_dir/host.log"; exit 1; }
  sleep 0.5
done
[ "$ready" = true ] || { cat "$work_dir/host.log"; exit 1; }

dotnet "$api_dll" --exercise --approve-reviewed-plan
```

Expected replay output includes the exact retained payload `{"quantity":"2","transition":"reserve-internal-capacity"}`, then `state=COMPLETED originalOperationFound=True`. The live source changes available capacity from `20` to `18` and reserved from `0` to `2`. The printed workflow UUID is generated at runtime; there are no placeholder runtime hashes. Cleanup stops the server and clears the token variables. Temporary SQLite and safe host log remain under `work_dir` for inspection; remove that directory yourself when finished. Do not commit it.

The replay is an explicit **synthetic protocol exercise using two separately authenticated configured identities**, not evidence of a real human reviewing a plan. The `--approve-reviewed-plan` flag acknowledges that limitation. A production application needs a real review workflow that displays the complete retained payloads and submits approval using the independently authenticated reviewer.

To run only the foreground host after setting the same required variables:

```sh
dotnet run --project examples/ApsDda.Api/ApsDda.Api.csproj -c Release --no-build
```

Use a fresh `APS_DDA_DATABASE` path when starting a new process. Never reuse a prior process's synthetic database: the sample deliberately refuses existing files rather than reset data or replace signing/trust keys. Without an explicit database variable it creates a unique temporary path per process.

## Local synthetic replay: Windows PowerShell

Run from the repository root after the build commands. `New-Token` works without depending on PowerShell's newer static random-byte APIs. The child host inherits the environment, so token values do not need to be copied into a second terminal.

```powershell
$ErrorActionPreference = 'Stop'
function New-Token {
  $bytes = New-Object byte[] 32
  $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
  try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
  [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
}
$env:APS_DDA_EXECUTOR_TOKEN = New-Token
$env:APS_DDA_APPROVER_TOKEN = New-Token
$env:APS_DDA_READER_TOKEN = New-Token
$env:DOTNET_ENVIRONMENT = 'Development'
$env:APS_DDA_ALLOW_LOOPBACK_HTTP = 'true'
$env:ASPNETCORE_URLS = 'http://localhost:5127'
$workDir = Join-Path ([IO.Path]::GetTempPath()) ('aps-dda-http-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir | Out-Null
$env:APS_DDA_DATABASE = Join-Path $workDir 'runtime.sqlite'
$apiDll = 'examples/ApsDda.Api/bin/Release/net10.0/ApsDda.Api.dll'
$server = Start-Process dotnet -ArgumentList $apiDll -PassThru `
  -RedirectStandardOutput (Join-Path $workDir 'host.log') `
  -RedirectStandardError (Join-Path $workDir 'host-error.log')
try {
  $ready = $false
  for ($i=0; $i -lt 60; $i++) {
    try { Invoke-WebRequest 'http://localhost:5127/aps-dda/v1/manifest' -UseBasicParsing | Out-Null }
    catch { if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 401) { $ready=$true; break } }
    if ($server.HasExited) { throw 'Host exited; inspect the temporary host logs.' }
    Start-Sleep -Milliseconds 500
  }
  if (-not $ready) { throw 'Host did not become ready.' }
  dotnet $apiDll --exercise --approve-reviewed-plan
  if ($LASTEXITCODE -ne 0) { throw 'Synthetic replay failed.' }
}
finally {
  Stop-Process -Id $server.Id -ErrorAction SilentlyContinue
  Remove-Item Env:APS_DDA_EXECUTOR_TOKEN, Env:APS_DDA_APPROVER_TOKEN, Env:APS_DDA_READER_TOKEN -ErrorAction SilentlyContinue
}
```

The PowerShell recipe is documented for portability; current execution evidence is Linux, not a Windows CI run.

## HTTPS and production boundary

Production always requires HTTPS. It does not become HTTP-capable because `APS_DDA_ALLOW_LOOPBACK_HTTP=true` was set. For local HTTPS development, use a local certificate accepted by your platform, for example the SDK development-certificate flow:

```sh
dotnet dev-certs https --trust
```

Then keep the three token variables explicit, use a fresh database, set `DOTNET_ENVIRONMENT=Development`, remove `APS_DDA_ALLOW_LOOPBACK_HTTP`, and set `ASPNETCORE_URLS=https://localhost:7127`. Certificate trust support differs by OS; do not disable TLS verification as a production workaround. The default is HTTPS loopback unless the explicit Development HTTP exception is enabled. The sample rejects wildcard/non-loopback listener URLs even when HTTPS is chosen. It does not provide production certificate/key deployment, public hosting, forwarded-header/proxy trust, OAuth/OIDC, mTLS, key rotation or encrypted SQLite.

## Supported wire API

Prefix: `/aps-dda/v1`. Every endpoint requires `Authorization: Bearer <configured-token>`, including the manifest and resource reads. A credential maps to a **server-provisioned identity**, not client-selected claims. The server never trusts `X-Identity`, `X-Role`, request-supplied tenant/context, approver IDs, manifests as registry input or a PolicyDecision as an authority grant.

| Method/path | Caller role | Purpose |
| --- | --- | --- |
| GET `/manifest` | Reader / Executor / Approver | Signed original manifest, full configured catalog scope required |
| GET `/resources/north` | Reader / Executor / Approver | One authorized actual resource snapshot with signed original DataProduct |
| POST `/plans/prepare` | Executor | Server-owned bounded synthetic plan preparation |
| POST `/plans` | matching Executor | Retain exact original signed plan and separate normalized payload map |
| GET `/workflows/{id}` | matching Executor / scoped Approver | Exact signed plan, complete retained payloads and review digest |
| POST `/workflows/{id}/approve` | independent Approver | Issue/admit exact original signed Approval |
| POST `/workflows/{id}/authorize` | matching Executor | Bind/consume the admitted authority for this durable workflow |
| POST `/workflows/{id}/execute` | matching Executor | Execute at most one next step, source guard/idempotency enforced |
| POST `/workflows/{id}/reconcile` | matching Executor | Recognize original source operation; no automatic resend |
| GET `/workflows/{id}/steps/{step}/operation` | matching Executor | Lookup original persisted invocation/key and verified receipt/postcondition |

`plans/prepare` is a separately experimental optional server factory, not a standard artifact type. In this sample it accepts exactly:

```json
{"binding":"aps-dda.experimental/http/1","resourceAlias":"north","quantity":"2","expectedVersion":"1"}
```

The **server**, not the body, chooses tenant, subject, workload, executor, purpose, policy, correlation UUID, approver permissions, source adapter and trust. A canonical positive quantity up to `50` is permitted; the source currently has `20` available and refuses insufficient capacity at commit. Versions and exact quantities are strings, not floating-point JSON numbers. Evidence is an actual trusted retained observation generated by the configured synthetic source; preparing a plan does not reserve capacity or grant authority.

Preparation returns a `StorePlanRequest` envelope:

```text
{ "binding": "aps-dda.experimental/http/1",
  "plan": <original schema-shaped SignedArtifact ExecutionPlan>,
  "payloads": { "reserve": { "quantity": "2", "transition": "reserve-internal-capacity" } } }
```

POST that exact envelope to `/plans`. The plan body contains the original `payloadDigest`/`invocationFingerprint`, **not** undeclared raw payload fields. Only artifacts signed by the separately configured orchestrator and exactly matching the authenticated executor context are accepted; required decision/policy/evidence dependencies must already be trusted and retained. There is no generic public artifact-ingestion, policy/grant creation, arbitrary signing, source-write, SQL/code, reset, fault, revocation or administrator endpoint.

GET the workflow as the independent reviewer and inspect all returned `payloads` and the exact plan. Approval input is:

```text
{ "binding": "aps-dda.experimental/http/1",
  "reviewDigest": <digest from this exact workflow review>,
  "expiresAt": <UTC timestamp within the plan window>,
  "allowedStepIds": ["reserve"] }
```

The review digest binds `planRef` and the entire retained payload map under the explicit experimental JCS review profile. It prevents approving a stale/different plan or digest-only effect summary; it does not prove human comprehension or presence. The reviewer identity comes from authentication, not an `approverId` field. Runtime enforces independent subject and admits the actual Approval before an executor can authorize with its exact original `ArtifactRef`.

Authorize with `{"binding":"aps-dda.experimental/http/1","authorityRef":<exact Approval reference>}`. Execute/reconcile with `{"binding":"aps-dda.experimental/http/1"}`. Never mint a new workflow, invocation or key after a timeout/lost reply. Operation lookup derives the source issuer, idempotency domain/key/fingerprint from the retained invocation; callers cannot choose an arbitrary source or tenant. A not-found operation is **not** a no-effect guarantee or permission to resend.

Inspect workflow state even after HTTP 200: source refusal may produce `REJECTED`, ambiguous outcome `UNKNOWN_OUTCOME`, and earlier effects plus a later refusal `MANUAL_RESOLUTION_REQUIRED`. Completion requires verified source receipt/postcondition, not a canned success message. HTTP errors expose only `{"binding":"aps-dda.experimental/http/1","code":"<safe code>"}`. Unauthenticated token is 401; scope/role refusal 403; unsupported media 415; body bound 413; conflict 409; validation/refusal generally 400. No stacks, exception messages or credentials are serialized.

## Embed the actual factory and typed client

The host owner provisions the runtime, stable signing keys, principal/trust/source registrations and exact manifest first, then supplies explicit token mappings to `ApsDdaHttpBinding`. No registration is derived from a request.

```csharp
var binding = new ApsDdaHttpBinding(runtime, tenantId, configuredPurpose,
    pinnedSignedManifest, configuredReadAliases, configuredTokenIdentityMap);
await using var app = ApsDdaApplication.Create(args, binding);
await app.RunAsync();
```

For an existing host use `builder.Services.AddApsDdaHttp(binding)`, then `app.UseApsDdaHttp()` and `app.MapApsDda()`. The included optional factory is trusted application code, not a request-selected plugin.

The typed client uses the same records and real `IHttpClientFactory`:

```csharp
services.AddApsDdaClient(new Uri("https://localhost:7127"), executorToken);
var client = provider.GetRequiredService<ApsDdaClient>();
var read = await client.ReadResourceAsync("north", cancellationToken);
var prepared = await client.PreparePlanAsync(new PreparePlanRequest(
    HttpBinding.Version, "north", "2", read.Resource.Version), cancellationToken);
var workflow = await client.StorePlanAsync(prepared, cancellationToken);
```

Configure a separate client with the reviewer credential for review/approval. Client configuration is not carried in artifacts. The DI extension disables redirects and uses a 30-second client timeout; manually supplied HttpClient instances should disable redirects too. Requests are bounded to 256 KiB and responses streamed/strictly checked to 1 MiB. Parsing a signed response does not establish issuer/key trust; independently pin and validate returned artifacts where required.

## Tested guarantees versus limitations

Strict parsing occurs at every critical **whole-envelope** POST boundary before ordinary deserialization. It rejects duplicate decoded names anywhere, bad UTF-8/surrogates, non-finite/unsafe JSON numbers, unknown/missing fields and payload/plan-signature/context mismatch. Requests are bounded to 256 KiB by default, plans to 32 steps by default (the sample factory creates one), configured read aliases to 100, each read to one exact resource and each execute call to one source dispatch. These are selected finite reference bounds, not comprehensive production rate/budget/SLO enforcement.

Only a source-local SQLite transaction commits the source change and original operation/receipt together. Approval-use consumption is a separate local transaction; there is no global multi-source atomicity. An expected-version read/prepare is observational, not an external invariant lock. ResourceRead is an experimental ordinary snapshot with signed DataProduct metadata, not a signed EvidenceBundle or implementation of the original product's generic read engine and complete SLO/budget contract. The `.example.invalid` manifest/capability endpoints are **synthetic logical contract pins for the in-process source**, not actual hosted source URL interoperability.

The sample creates signing keys only in memory and refuses an existing database when restarted. It deliberately does not persist or silently replace source keys/trust. SQLite retains real workflow/invocation/operation rows, but a recoverable long-lived host must separately provision the same secure signing keys, exact contract artifacts, principal directory and trusted directory. The HTTP tests also reopen SQLite to verify original operation/workflow persistence; that is not a claim that this sample's ephemeral key configuration supports restart recovery.

Production identity provisioning/storage, OAuth/OIDC/mTLS, TLS/certificate operations, encrypted database, distributed revocation, key rotation, request/issuance rate limiting, background workers/outbox, remote source adapters, compensation and human-review UX are not implemented. No customer claim, payment, entitlement or real notification is performed. Do not expose this reference token host on the public Internet or call it production-secure without building and reviewing those controls.

## Verification recorded 3 November 2026

The HTTP project graph passed locked restore and Release build on SDK 10.0.403 with **zero warnings/errors**. The actual xUnit TestServer suite passed **89 tests, zero failures/skips**. Cases cover all ten supported routes' unauthenticated refusal, wrong tenant/purpose/role/source scope, current registration revocation, exact independent approval review, no policy-evidence-as-authority, complete-envelope duplicate handling at each critical POST, malformed UTF-8/Unicode/numbers, content/media/byte limits, signed-plan/payload/context tampering, expected-state refusal, real source effect/receipt, unknown lost reply, original-key lookup/reconciliation, historical operation recognition after authority/plan expiry, and not-found staying unknown without resend. No reset/fault/admin/arbitrary-source-write routes exist.

A separate **actual localhost Kestrel** smoke run executed the included typed-client synthetic protocol replay, verified resource state `available=18`, `reserved=2`, `version=2` through the read-only HTTP identity, asserted unauthenticated 401 and no reset endpoint, and terminated the server. Credentials were generated into environment values only and the temporary database was discarded by the verifier. This was HTTP under the explicit Development exception, not a production TLS/identity-provider test.

The local `ApsDda.AspNetCore.0.1.0-preview.1.nupkg` contains DLL/XML docs, API contract, package README and quickstart. A new outside-checkout consumer restored it and its Runtime/Core dependencies via NuGet, resolved the actual HttpClientFactory-registered typed client and passed wire-record, strict duplicate and required-field checks. A transitive dependency vulnerability query reported no vulnerable packages **given the configured sources at the check time**, not a security certification. The first preview is prepared for GitHub Releases, not NuGet.org; no hosted website is claimed.
