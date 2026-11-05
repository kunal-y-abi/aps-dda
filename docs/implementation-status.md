# Implementation status

**Evidence snapshot:** 3 November 2026. **Package line:** `0.1.0-preview.1`. **Standard:** proposed APS-DDA v0.1, not ratified. New source development was AI-assisted.

This is an **experimental .NET/C# reference implementation of selected requirements**. It does not claim complete syntactic, behavioral or semantic conformance, an `APS-Read`/`APS-Action` certification, production readiness, independent security review, a live insurer deployment, or empirical replacement of an analytical platform. The [standard](../spec/APS-DDA-v0.1.md) remains language-neutral; the [API contract](../API_CONTRACT.md) documents the experimental binding. Author: Kunal Yadav; initial maintainer: [@kunal-y-abi](https://github.com/kunal-y-abi). The approved [license scopes](../LICENSING.md) distinguish Apache-2.0 code/schema/synthetic fixtures, CC BY 4.0 authored prose/diagrams and retained third-party exceptions.

## Recorded evidence and reproducible commands

The final integrated solution locked restore and Release build succeeded on Linux SDK `10.0.403` with zero warnings/errors. **368 xUnit cases passed, zero failures/skips: Core 130, Runtime 85, ASP.NET Core 89, Demo 44 and cross-module Acceptance 20.** The demo count includes the final minimum-served-items sensitivity test. Counts are selected cases, not a complete standard conformance suite. These counts record local Linux verification; revision-specific Ubuntu/Windows execution is tracked in [GitHub Actions](https://github.com/kunal-y-abi/aps-dda/actions).

Run from the repository root on Linux or Windows with the SDK selected by `global.json`:

```sh
dotnet restore ApsDda.sln --locked-mode
dotnet build ApsDda.sln -c Release --no-restore -warnaserror
dotnet test ApsDda.sln -c Release --no-build --no-restore
dotnet run --project examples/ApsDda.InsuranceDemo -c Release --no-build --no-restore -- --all --export artifacts/demo-runs
dotnet pack src/ApsDda.Core/ApsDda.Core.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet pack src/ApsDda.Runtime/ApsDda.Runtime.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet pack src/ApsDda.AspNetCore/ApsDda.AspNetCore.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet run --project tools/ApsDda.Verify -c Release --no-build --no-restore -- . artifacts/packages
```

All three reusable packages were generated locally and inspected; the Core copied schema path was made explicit as `schema/aps-dda.schema.json` (the assembly resource was already independent of the checkout). A fresh outside-checkout console with an isolated NuGet cache and local-only APS-DDA source mapping consumed packages rather than project references. It verified the embedded original schema/hash, real digests/native signatures, authenticated and consumed exact approval, committed an actual local capacity effect, verified signed postconditions, rejected extra effects on a duplicate and reopened the original durable invocation with one operation/authority-use row. The fresh consumer generated then repeated locked restore. This is local installed-package evidence, not public publication or external-source interoperability. The public C# verifier replaces any dependency on private Python smoke scripts.

A recorded NuGet vulnerability check of the runtime's transitive dependencies reported no vulnerable packages given its configured sources. This point-in-time result is neither a permanent guarantee nor certification. Lockfiles and the exact upstream source provenance are the reproducibility inputs; release owners must rerun dependency checks for later releases.

## Coverage matrix

“Implemented/tested” below means the selected binding and its named tests, not every obligation in the associated standard section.

| Area | Selected implementation and evidence | Remaining boundary |
| --- | --- | --- |
| Original artifact shape (§4) | Full unchanged original Draft 2020-12 schema embedded offline; fifteen historical fixture shapes tested separately | Shape does not validate truth, authority, semantics or effects; historical fixtures have placeholder digests |
| Parsing/canonicalization (§4, §8) | Duplicate decoded names, malformed UTF-8/Unicode, non-finite/unsafe numbers and limits refused; pinned upstream JCS serialization, golden digests and corpora tested | Object/array roots and stricter safe-magnitude input profile; ordinary fractions use binary64 rounding; not exact decimal arithmetic |
| Integrity/references (§4, §8) | Immutable snapshots; exact type/ID/version/digest refs; complete defined plan/content projections; extensions and ordered steps covered | External resources need their own documented byte rules; hashes do not prove facts |
| Signature/trust (§5) | Experimental native P-256/P1363 detached envelope; independent issuer/key/role/profile/version/audience/endpoint/semantic pins; local revocation | Not JWS/JOSE or a complete production PKI; no automatic network discovery/trust, key storage/rotation or durable directory provisioning |
| Extensions (§14) | Registered exact version and deterministic validator; unknown/unversioned extensions fail closed | Stricter experimental refusal of unknown non-critical extensions; no general extension negotiation |
| Caller context (§6) | Administrator-provisioned immutable handles; compare tenant/subject/workload/executor/purpose/classification/residency; source permission intersection | `ResolveAuthenticated` is not login; host must authenticate first; delegation refused; no production OAuth/mTLS identity provider |
| Evidence/decision (§7) | Retained signed evidence and protected values; exact product/issuer/semantics, quality, age and reviewed value validator; DecisionSpec eligibility checked | Domain-reviewed validators, not universal meaning; observational/live evidence only; no general read publisher, query negotiation/budget enforcement or coordinated snapshot |
| Complete evidence set | Decision evidence equals exact union of plan-step references; source revalidates complete decision evidence set at dispatch and commit | No global atomic read or cross-source invariant guarantee |
| Policy (§7, §9) | Pinned deterministic eligibility policy/version/outcome/obligations/evaluation digest/age; current policy persisted and not reset on restart | Small eligibility evaluator, not a general policy engine or insurer analytical/entitlement model; `allow` is never a grant |
| Exact human authority (§9) | Authenticated independent approver admission; exact plan/policy/steps/effect summary/expiry; one durable use binding; race/reopen tests | Host must render complete retained payload and implement appropriate approval authentication; no production step-up provider |
| Bounded policy authority (§9) | Authenticated PolicyGranter; source-enabled low-risk exact capability/resource/audience; canonical quantity, total quantity, source rolling rate and expiry checks | Money, delegation and wildcard grants refused; no remote status distribution |
| Source effect (§10) | Expected local version plus capacity guard, operation record, effect, signed receipt/postcondition in one local transaction; insertion-failure rollback test | Synthetic SQLite sources only; external API wrapper cannot guarantee remote operation/effect atomicity; arbitrary/external hard invariants refused |
| Idempotency/recovery (§10) | Original key/fingerprint returns original operation; changed fingerprint conflicts; durable original invocation before transport; lost reply and reopen lookup tests | Never auto-resends, even on not-found; unknown outcome requires original lookup/manual owner; no worker/outbox scheduler |
| Verified completion (§10–11) | Actual source issuer/signatures; exact invocation, operation, version, retained before/after state and transition verification | Source-local transition correctness, not future forecasts or business benefit; no external interoperability proof |
| Multi-source/partial work (§11) | Two independently signed logical sources: success and second-source stale refusal preserving first effect; manual ownership and cancellation intent | Local tests may share a database but never a cross-command transaction; no distributed atomicity, source-local atomic groups or automated compensation |
| Untrusted content (§12) | Deterministic controls have no text-to-code/source-command path; schema/extensions and reviewed commands are bounded | Not a comprehensive LLM prompt-injection/security assessment |
| Analytics | Synthetic four-publisher, three-region numerical model; two allocations, feasibility/service-floor/budget sensitivity and real evidence/model commitments tested; separate paper example verified | Observational model constraints are not enforced external invariants; no actuarial/calibrated probability/business validation; richer domain extension is experimental |
| Renderer/compiler (§13) | Tested deterministic ApplicationSpec/BuilderPrompt compiler pins decision/exact plan/component catalog/actions; unsupported components, missing refs, wrong plans and secrets refused | No actual UI, Vercel deployment or arbitrary builder execution; derived artifacts cannot grant authority |
| HTTP/MCP/A2A (§14) | Authenticated bounded ASP.NET Core routes and typed client tested via TestServer and actual loopback Kestrel; separate experimental envelopes preserve original artifacts | No implemented MCP/A2A adapter, production identity/TLS provisioning or external-source federation; ordinary ResourceRead is not a signed generic EvidenceBundle read engine |

## Test evidence by module

[Core tests](../tests/ApsDda.Core.Tests) cover actual digest chains, exact projections/reference/DAG binding, cryptographic tampering, role/profile/audience/tenant pins, local revocation and parser/number/Unicode cases. One xUnit case compares **10,000** pinned V8 expected-number vectors. Those are not 10,000 independently counted tests. The six upstream corpora and RFC vectors are reference data, not customer evidence. See [source provenance](../src/ApsDda.Core/ThirdParty/Jcs/PROVENANCE.md).

[Runtime tests](../tests/ApsDda.Runtime.Tests) use real file-backed SQLite, runtime-generated keys, actual-digest original-schema artifacts, trusted principal handles, authority admission and real local state changes. They cover authority expiry/revocation/consumption, policy-allow-not-authority, source scope, stale state, required complete evidence, semantic/payload/receipt tampering, original-key reconciliation, cancellation and partial manual recovery. Reopening objects against a retained file demonstrates those persisted records; it is not a production disaster-recovery or multiprocess performance test.

The publication pass verified the paper's fictional numerical example independently: ten feasible allocations, with `(1,4,1)` producing minimum weighted backlog `320` under its stated tie-breaking and constraints. That calculation is editorial example verification, not an actuarial validation or an assertion that it is the separate console's dataset.

[HTTP tests](../tests/ApsDda.AspNetCore.Tests) passed 89 real TestServer cases across all supported routes, authentication/tenant/purpose/source scope, independent exact-payload review, strict entire-envelope duplicate/Unicode/media/byte limits, policy-evidence-not-authority, tamper/stale refusal, source effect, unknown/lost reply and original lookup/reconciliation without resend. The separate actual loopback Kestrel replay used runtime-generated independent tokens, verified available `18`, reserved `2`, version `2` using a read-only identity, asserted unauthenticated 401 and absence of admin/reset, then stopped the server. It used the explicit Development HTTP exception, not a TLS handshake or production identity provider.

[Demo/compiler tests](../tests/ApsDda.Demo.Tests) passed 44 cases. The independent console dataset enumerates 100 candidates, 19 feasible, and selects `[2,2,2]` for minimum expected total backlog or `[3,2,1]` for minimax upper-scenario backlog; these are not the paper's different fictional dataset. All 13 supported console scenarios ran from the final Release tree. Happy and malicious-data paths committed three regional reservations; bounded policy committed one internal draft; stale/expired/revoked committed zero; plan/payload/semantic/stale-evidence faults failed admission with zero authority uses; lost reply retained/reconciled the original invocation across object reopen with three invokes and one lookup; both partial cases preserved one effect with manual owner. The interactive exact-digest console review branch was not executed, and the default manager is a synthetic authenticated fixture, not real human approval.

[Cross-module acceptance](../tests/ApsDda.Acceptance.Tests) passed 20 cases: measured every console scenario's persisted source effects, denied direct source calls without admitted/consumed authority, refused actual retained-payload changes/durable revocation/stale versions after dispatch, blocked completion after corrupting the actual retained postcondition value, then restored the original value and verified original-invocation reconciliation/duplicate no-extra-effect. A builder artifact round-tripped through the typed HTTP authorization record still could not be consumed as runtime authority. In-process fault controls remain test fixtures, not HTTP administrator endpoints.

## Honest gaps before a production adoption

Production identity-provider integration, HTTPS transport configuration, durable principal/trust provisioning, signing-key custody and rotation, encrypted storage, retention/deletion/access auditing, remote revocation propagation, appropriate source-owner authorization, source API interoperability, migration operations, worker scheduling, load/performance testing and independently reviewed threat models are not established. Local durable policy/revocation records are not distributed revocation guarantees.

No global snapshot, cross-source atomic commit, general semantic equivalence/identity mapping, arbitrary SQL/code execution, automated compensation or complete analytical/domain extension is supplied. Named unsupported scopes must be refused, not simulated. No real insurer data, payment, claims adjudication, customer notification or entitlement change occurs in the selected source commands.

No empirical business, ROI, ecosystem adoption, lawful-processing or universal security claim follows from these test results. The retained original draft's statement that it had no implementation was historically correct for that bundle; this status describes later selected reference code without silently changing that original.

## Cross-platform verification history

The [initial GitHub run](https://github.com/kunal-y-abi/aps-dda/actions/runs/41662404059) completed every Ubuntu check, but Windows reported two pinned-byte hash failures and three test-cleanup failures. The observed hash values matched LF-to-CRLF checkout conversion exactly. Repository attributes now preserve checkout bytes; the four test-owned SQLite connections now disable pooling so disposal releases Windows handles. No expected hashes, numerical vectors, authority checks or effect assertions were relaxed. Subsequent run results are the verification authority for the corresponding revision; the initial failure remains visible in history.

## First preview preparation

`v0.1.0-preview.1` is prepared for a GitHub prerelease with PDFs and locally built packages, not a NuGet.org publication. Authorship and license scopes were approved on 3 November 2026. The [repository](https://github.com/kunal-y-abi/aps-dda) has issues and verified enabled private vulnerability reporting; [MAINTAINERS.md](../MAINTAINERS.md) identifies the initial maintainer. Integrated local results and revision-specific CI history are recorded separately; no independent review is implied. Release preparation includes checking the final tree for sensitive material and inspecting package contents. Review the [contribution process](../CONTRIBUTING.md), [security policy](../SECURITY.md), [conduct reporting](../CODE_OF_CONDUCT.md) and [change history](../CHANGELOG.md).
