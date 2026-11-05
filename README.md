# APS-DDA — Data, Decisions and Actions

**Author:** Kunal Yadav · **Maintainer:** [@kunal-y-abi](https://github.com/kunal-y-abi) · **First preview prepared:** `v0.1.0-preview.1`

An experimental .NET/C# reference for selected requirements of the **proposed Agent Participation Standard for Data, Decisions and Actions (APS-DDA v0.1)**. Source owners publish products and capabilities; analytical methods produce evidence-bound decisions; separate authenticated authority binds an exact plan; sources enforce their own permissions/state; completion requires verified original receipts and postconditions.

This is **not complete APS-DDA conformance, certification, a production identity system or a live insurance deployment**. Hashes and signatures bind declared bytes and issuers, not data truth, model validity or business benefit. The standard is language-neutral; this implementation targets `net10.0`.

## Start here

- [Public technical white paper](docs/whitepaper.md), [architecture](docs/architecture.md) and [implementation status / gaps](docs/implementation-status.md).
- Publication PDFs: [technical white paper](docs/pdf/APS-DDA-Technical-White-Paper.pdf) and [proposed v0.1 standard](docs/pdf/APS-DDA-v0.1-Proposed-Standard.pdf).
- [Proposed standard](spec/APS-DDA-v0.1.md) and [original schema / historical fixtures](spec/original/README.md). Historical fixture digests are placeholders, not valid runtime authority.
- [Core/Runtime API](API_CONTRACT.md), [HTTP contract](src/ApsDda.AspNetCore/API_CONTRACT.md) and [demo/compiler contract](src/ApsDda.Demo/API_CONTRACT.md).
- [Insurance analytical walkthrough](docs/insurance-demo.md) and [HTTP host/client quickstart](docs/http-quickstart.md).

## Build and test — Windows and Linux

Install the .NET SDK selected by `global.json` (`10.0.403`, patch roll-forward allowed), put `dotnet` on PATH and run from the repository root. No Python, Docker, LLM credentials, GitHub authentication or deployment is needed. First restore requires the configured NuGet sources; lockfiles pin resolutions rather than removing dependency downloads.

These commands work in Bash and Windows PowerShell:

```sh
dotnet restore ApsDda.sln --locked-mode
dotnet build ApsDda.sln -c Release --no-restore -warnaserror
dotnet test ApsDda.sln -c Release --no-build --no-restore --logger trx --results-directory artifacts/test-results
dotnet run --project examples/ApsDda.InsuranceDemo -c Release --no-build --no-restore -- --all --export artifacts/demo-runs
dotnet pack src/ApsDda.Core/ApsDda.Core.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet pack src/ApsDda.Runtime/ApsDda.Runtime.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet pack src/ApsDda.AspNetCore/ApsDda.AspNetCore.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet run --project tools/ApsDda.Verify -c Release --no-build --no-restore -- . artifacts/packages
```

Bash users can run the same sequence with `bash scripts/check.sh` from any working directory. In PowerShell, check `$LASTEXITCODE` after commands or use the supplied CI steps; a nonzero native exit code is failure. The CI definition checks Ubuntu and Windows with read-only repository permissions and no secrets. **Recorded local execution is Linux; cross-platform results are recorded in [GitHub Actions](https://github.com/kunal-y-abi/aps-dda/actions).** Check the run for the revision you intend to use.

`ApsDda.Verify` is a finite C# developer verifier, not a hosted service. It inspects local packages; launches actual loopback Kestrel with independently generated executor, reviewer and reader tokens; runs the typed-client synthetic approval replay; checks source `available=18`, `reserved=2`, `version=2`, unauthenticated 401 and absence of an admin/reset route; then stops the host. It creates a fresh console outside the checkout with an isolated NuGet cache and package-source mapping that resolves `ApsDda.*` only from your local package folder. The installed consumer verifies the embedded original schema/hash, real digests/native signatures, exact authority consumption, SQLite effect, original receipt, duplicate no-extra-effect and durable reopen. Temporary credentials/cache/data are removed; packages and test/demo outputs remain under ignored `artifacts/`.

## Run the insurance example

```sh
dotnet run --project examples/ApsDda.InsuranceDemo -c Release --no-build -- --scenario happy --plan balanced --export artifacts/happy
dotnet run --project examples/ApsDda.InsuranceDemo -c Release --no-build -- --scenario happy --plan min-backlog
dotnet run --project examples/ApsDda.InsuranceDemo -c Release --no-build -- --scenario lostresponse-reconcile
```

Four logical signed publishers supply fictional claims, forecast, capacity and finance data. A deterministic integer enumerator compares two three-region allocations under declared service floors, modeled budget and reserve constraints. The human path reserves regional internal teams; `boundedpolicy` prepares one internal draft only. Neither path makes customer decisions, spends money or sends a notification. The default manager is an explicitly configured **synthetic identity fixture**, not a real human review or production login. `--interactive` shows alternatives, the exact plan and all payloads and requires the exact plan digest; that interactive branch is not included in recorded execution evidence.

All supported scenarios are `happy`, `boundedpolicy`, `stale`, `expired`, `revoked`, `tampered`, `tampered-payload`, `lostresponse-reconcile`, `partial-manual`, `partial-approval`, `semanticmismatch`, `maliciousdataignored`, and `stale-evidence`. Fault scenarios are demonstrations of actual refusal/unknown/manual states, not successful business actions. Tests measure persisted effects; the console exit code alone is not proof of the intended fault outcome.

Exports include actual signed artifacts, public fixture keys, content-addressed retained values, normalized payloads, measured before/after state and a deterministic builder template. Public fixture keys are not externally authenticated trust roots. A tampered scenario intentionally exports an invalid chain. ApplicationSpec/BuilderPrompt are derived artifacts, never action authority and never arbitrary executable code.

## Use the libraries in your own application

| Project | What you can reuse |
| --- | --- |
| `ApsDda.Core` | Strict JSON, embedded unchanged schema, immutable artifacts, pinned upstream JCS, exact digests/references, native P-256 detached signatures and explicit tenant trust |
| `ApsDda.Runtime` | File-backed SQLite records, principal handles, reviewed evidence rules/policy, exact approval/bounded grants, source-owned adapter interfaces and verified recovery |
| `ApsDda.AspNetCore` | Authenticated bounded Minimal API routes and the typed `ApsDdaClient` using the same records |
| `ApsDda.Demo` / examples | Non-packable synthetic domain model, console/compiler and ephemeral example host; adapt reviewed configuration, not fixture credentials |

Local pack outputs are `artifacts/packages/ApsDda.{Core,Runtime,AspNetCore}.0.1.0-preview.1.nupkg`. The first preview is prepared for [GitHub Releases](https://github.com/kunal-y-abi/aps-dda/releases), with PDFs and these packages; it is not a NuGet.org release. Create a new `net10.0` project with explicit PackageReferences and local NuGet source mapping, or use the [package-only smoke template](tools/package-smoke/PackageSmoke.csproj) through the verifier. The Core schema is an installed assembly resource; consumers need no checkout-relative schema or network lookup. XML docs, contracts and covered upstream canonicalizer sources/notices are in the packages.

Provision stable signing keys, an independently trusted tenant directory, exact product/capability pins, authenticated principal registrations, reviewed evidence value semantics and source-owned commands before constructing `WorkflowRuntime`. `ResolveAuthenticated` is for a host identity **already authenticated**, not a client-selected identifier. Never derive trust/roles/policy from discovered manifests or request fields. Use [the concrete API](API_CONTRACT.md) and [reference binding guarantees](docs/reference-binding.md) for constructor order and exact payload/fingerprint contracts; the original closed artifact bodies contain payload digests, while runtime/HTTP envelopes carry the retained raw payload separately.

For an existing ASP.NET host, provision `ApsDdaHttpBinding`, then use `AddApsDdaHttp`, `UseApsDdaHttp` and `MapApsDda`; configure a separate reviewer client. For the supplied localhost host, follow the [Bash and PowerShell token-generation/replay recipes](docs/http-quickstart.md). There is no default administrator credential, and `.env.example` is documentation only—.NET does not load it automatically. Do not print tokens, enable shell tracing or put private keys/environment overrides in source control. The example requires a **fresh database per process** because signing keys/trust are ephemeral; it refuses existing files instead of resetting data. Long-lived restart recovery requires your stable secure identity/key provisioning.

## What the checks establish

On 3 November 2026, the integrated locked restore and Release build passed with zero warnings/errors on Linux SDK `10.0.403`. **368 xUnit cases passed, zero failed/skipped:** Core 130, Runtime 85, HTTP 89, Demo 44, cross-module Acceptance 20. One Core case validates 10,000 V8 number vectors; these are not separately counted tests. All 13 console scenarios, actual loopback Kestrel and a fresh installed-package consumer passed. [Status](docs/implementation-status.md) records selections and limitations.

The tests include actual source authority enforcement after dispatch, durable revocation, stale expected versions, payload tampering, duplicate operation conflict/no-extra-effect, corrupted retained postcondition refusing completion, original-invocation reconciliation, partial effects/manual ownership and derived builder artifacts refusing authority. Source effects, operation and signed postcondition commit in **one source-local transaction**; authority consumption is separate. No cross-source atomicity, external hard-invariant guard, automatic resend after unknown/not-found, general read negotiation/budget service or automatic compensation is implemented. Production OAuth/OIDC/mTLS, key custody/rotation, distributed revocation, encrypted SQLite/retention, external source interoperability, background workers, live model validation and independent security review remain adoption work.

## Provenance, AI assistance and publication status

New source code and accompanying documentation, development and testing were prepared with **AI assistance**; see [SOURCE.md](SOURCE.md). This disclosure is not human expert sign-off or a security audit. [Canonicalizer provenance and notices](src/ApsDda.Core/ThirdParty/Jcs/PROVENANCE.md) identify the pinned unchanged upstream source and its Apache-2.0, MPL-2.0, V8/BSD and Lucent notices. [Vector provenance](tests/ApsDda.Core.Tests/JcsVectors/PROVENANCE.md) distinguishes third-party reference data. Their existing terms remain applicable; no ownership of upstream code is claimed.

Copyright © 2026 Kunal Yadav. New code, machine-readable schema and synthetic JSON fixtures are **Apache-2.0**; authored prose (including the retained original whitepaper and proposed specification) and original diagrams are **CC BY 4.0**. [LICENSING.md](LICENSING.md) gives the exact scope and preserved third-party exceptions; [package-license.md](package-license.md) includes full notices for the mixed-license Core binary and dependencies. The [repository](https://github.com/kunal-y-abi/aps-dda) has [issues](https://github.com/kunal-y-abi/aps-dda/issues) and [private vulnerability reporting](https://github.com/kunal-y-abi/aps-dda/security/advisories/new). See [maintainer](MAINTAINERS.md), [contributing](CONTRIBUTING.md), [security](SECURITY.md), [conduct](CODE_OF_CONDUCT.md) and [change history](CHANGELOG.md).
