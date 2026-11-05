# Insurance response-readiness demonstration

**Experimental selected-profile implementation; all inputs and effects are synthetic.** This example compares internal response capacity across three imaginary regions. It does not decide insurance coverage, individual claim outcomes, fraud, entitlement, payments or customer contact. No actuarial/empirical validation, production deployment, complete APS-DDA conformance or security certification is claimed. Source development was AI-assisted.

## Run without keys or services

Install the repository's pinned .NET 10 SDK, then run from its root (Bash or PowerShell):

```sh
dotnet run --project examples/ApsDda.InsuranceDemo
dotnet run --project examples/ApsDda.InsuranceDemo -- --plan min-backlog
dotnet run --project examples/ApsDda.InsuranceDemo -- --scenario boundedpolicy
dotnet run --project examples/ApsDda.InsuranceDemo -- --all
dotnet test tests/ApsDda.Demo.Tests -c Release
```

The dataset is embedded in the assembly. P-256 keys are generated in process, never persisted or logged. SQLite state lives in a unique temporary directory and is deleted on normal disposal. Abrupt process termination can leave that temporary database for the local operator to remove. There is no LLM, external API, Vercel application, network read or operational source integration.

For actual console acknowledgement of the chosen exact plan:

```sh
dotnet run --project examples/ApsDda.InsuranceDemo -- --scenario happy --plan balanced --interactive
```

The console shows both alternatives, the exact original ExecutionPlan, and the full normalized payload map. Type the displayed plan digest to issue the locally configured synthetic manager Approval; a mismatch/cancel issues no Approval and commits nothing. `--plan` chooses an alternative **before** plan/authority construction. The default non-interactive quickstart uses the same real approval admission/consumption API but an explicitly configured **synthetic manager identity fixture**, not a live person or production authentication. Console acknowledgement is not a replacement for an authenticated host boundary.

## Four aggregate evidence sources

The [authored dataset](../examples/insurance/synthetic-data.json) contains only canonical integer strings. The [dataset README](../examples/insurance/README.md) explains fake provenance and units. Each of claims, forecast, capacity and finance has a separately generated signing key, pinned issuer and DataProduct. Source values are retained under a real JCS content-addressed URI and referenced by actual signed EvidenceBundles. All four evidence refs are included in DecisionSpec and each plan step, and revalidated at source dispatch/commit. Exact regions, units, period, product/semantic refs, retained values, age/quality/cache state and trusted signatures matter—not schema shape alone.

| Input | North | Central | South |
|---|---:|---:|---:|
| Aggregate open items | 85 | 65 | 45 |
| Expected added items | 35 | 30 | 25 |
| Upper stress added items | 55 | 50 | 40 |
| Base internal teams | 2 | 2 | 1 |
| Available additional teams before reserve exclusion | 5 | 5 | 4 |
| Protected regional emergency reserve | 1 | 1 | 1 |
| Minimum additional teams | 1 | 1 | 1 |
| Minimum expected served items | 60 | 60 | 40 |
| Synthetic budget credits per additional team | 120 | 100 | 80 |

One team processes 20 aggregate items during the invented day. The shared pool has 7 teams, of which 1 is reserved; the model permits at most 6 additional teams. Synthetic budget is 700 credits. Regional and pool emergency reserves are separate teaching constraints, not real insurer resources.

The aggregate capacity product is separate from the source-owned regional reservation-state product used in operation postconditions. Notification draft state likewise has its own product. An analytical input is never relabelled as an action result.

## Real finite integer enumeration

Let `x[r]` denote the additional internal teams for region `r`. For every candidate, the model computes:

```text
expectedDemand[r] = open[r] + expectedAdded[r]
upperDemand[r]    = open[r] + upperAdded[r]
processing[r]     = (baseTeams[r] + x[r]) * itemsPerTeam
expectedServed[r] = min(expectedDemand[r], processing[r])
expectedBacklog[r] = max(0, expectedDemand[r] - processing[r])
upperBacklog[r]    = max(0, upperDemand[r] - processing[r])
cost = sum(x[r] * costPerTeam[r])
```

Actual loops visit all 100 integer combinations: north and central `0..4`, south `0..3`. A candidate is feasible only if every `x[r] >= minimumAdditionalTeams[r]`, every expected served count meets the numerical service floor, every regional reserve is excluded, the sum is no more than the deployable pool, and cost is no more than budget. Nineteen candidates survive for the included data. Input/candidate sizes are bounded; no feasible candidate means a real refusal/manual planning requirement, not a fabricated recommendation.

Two reviewed alternatives are selected from that feasible set:

| Alternative | Additional teams N/C/S | Expected backlog N/C/S | Upper backlog N/C/S | Cost |
|---|---|---|---|---:|
| `min-backlog` | 2 / 2 / 2 | 40 / 15 / 10 | 60 / 35 / 25 | 600 |
| `balanced` | 3 / 2 / 1 | 20 / 15 / 30 | 40 / 35 / 45 | 640 |

`min-backlog` minimizes expected total backlog, then cost, then the lexicographic allocation. `balanced` minimizes the largest upper-scenario regional backlog, then upper total backlog, then cost and lexicographic allocation. Both are deterministic optimizations, not LLM judgements. Equal expected totals do not imply equal regional exposure; the balanced allocation reduces the worst upper-scenario backlog at a higher modeled cost. Tests change budget, staffing floors and numerical service floors and assert real changes in the feasible set/allocation.

Assumptions include one day, uniform productivity and no regional transfer delay. Forecasts are deterministic expected/upper teaching inputs, not probabilities, calibrated confidence intervals or insured-loss predictions. Model version, parameters/digests, objective ordering, source commitments, chosen alternative, numerical comparisons, assumptions and uncertainty are recorded in the original DecisionSpec and the explicit versioned `StructuredDecisionSpec` domain extension. The extension does not silently amend the unchanged original standard/schema.

**Boundary:** budget, shared-pool and service constraints are model feasibility checks over observational evidence. The local action adapter protects only its own actual regional available stock and expected version. There is no live multi-source budget/pool/service invariant or global snapshot guarantee. If an operation truly requires an external hard invariant, a source-owner enforcing mechanism is necessary; the SDK refuses unsupported such contracts. This example never spends finance credits.

## Exact plan → authority → source effects

The chosen human path creates three ordered internal capacity reservations, one per region, with exact target, payload digest, expected source version, original idempotency key/domain/fingerprint, evidence refs and manual owner. Normalized payloads are retained through the SDK's explicitly experimental binding, not undeclared original artifact fields. A signed original Approval binds the exact plan ref/digest, policy, allowed steps and four-minute window. An independent configured Approver is admitted, and authority is consumed once for that durable workflow before dispatch. Every source rechecks current authority, evidence, policy and caller permissions.

Each regional operation decrements actual SQLite `available` and increments `reserved`; the regional emergency stock was excluded before this state was seeded. Operation identity, actual source state, signed receipt and postcondition evidence commit together in one source-local transaction. Completion verifies actual receipt/invocation/target/version/post-state bindings. Approval consumption is a **separate** transaction. There is no all-or-nothing regional rollback.

The bounded policy path is deliberately different: the same numerical comparison informs one source-owned **internal notification draft**. Its original PolicyGrant has exactly one low-risk capability, one resource, one audience, quantity 1, one operation per rolling minute and four-minute authority. It increments only `draftCount`; no notification is sent, no teams are reserved and no finance/customer decision occurs. `PolicyDecision.outcome=allow` alone is evidence, not a grant.

## Implemented scenarios and actual measured outcomes

All scenarios mutate inputs/state/authority or transport in process and then run the same SDK paths. None return hardcoded workflow results. Reports include before/after resource state, actual operation and authority-use counts, full runtime history and original invocation/receipt refs. CLI exit 0 means the *scenario demonstration ran*, not that a business workflow succeeded.

| Scenario | Actual induced condition | Observed outcome | Committed operations |
|---|---|---|---:|
| `happy` | Exact chosen human plan and admitted manager Approval | `COMPLETED` | 3 |
| `boundedpolicy` | Admitted source-scoped quantity/rate-bounded internal draft grant | `COMPLETED`; only draft changes | 1 |
| `stale` | North source version advances after approval | `REJECTED`, `STALE_PRECONDITION` | 0 |
| `expired` | Clock advances exactly to Approval expiry while plan/evidence remain current | `REJECTED`, `AUTHORITY_EXPIRED` | 0 |
| `revoked` | Actual durable Approval revocation before dispatch | `REJECTED`, `UNTRUSTED_PARTICIPANT` | 0 |
| `tampered` | Effect-bearing plan target changes without new digest/signature | Not admitted, `INVALID_ARTIFACT` | 0 |
| `tampered-payload` | Retained proposed quantity differs from plan digest/fingerprint | Not admitted, `INVALID_ARTIFACT` | 0 |
| `semanticmismatch` | Signed retained finance value has the wrong unit | Not admitted, `INVALID_ARTIFACT` | 0 |
| `stale-evidence` | Forecast observation exceeds the configured five-minute maximum age | Not admitted, `EVIDENCE_STALE` | 0 |
| `lostresponse-reconcile` | Real first commit, then dropped reply; new runtime/store opens same SQLite | Intermediate `UNKNOWN_OUTCOME`; original-key lookup verifies receipt; final `COMPLETED`, no resend | 3 |
| `partial-manual` | North commits, then Central source version changes | `MANUAL_RESOLUTION_REQUIRED`, `STALE_PRECONDITION`; North effect remains | 1 |
| `partial-approval` | Manager approves only North; future Central step has no authority | `MANUAL_RESOLUTION_REQUIRED`, `APPROVAL_REQUIRED`; North effect remains | 1 |
| `maliciousdataignored` | Retained claims prose says to ignore rules/run code/send payments | `COMPLETED` with the unchanged selected numerical plan; prose absent from template | 3 |

A pre-submission refusal has no durable workflow: `NOT_ADMITTED` is report metadata, not an invented Runtime state. After partial failure, remaining work is assigned to `synthetic-capacity-owner`; prior effects are not undone. Lost-response reconciliation records three invokes and one lookup for the three-step plan—the first invocation is not resent. Unknown/not-found behavior, general cancellation and additional security checks are documented/tested in the separate [Runtime binding](reference-binding.md); only the listed demo scenarios are exposed by this console. Unsupported scenario names return `UNSUPPORTED_PROFILE`; they are not simulated as successes.

## Deterministic application compilation

The compiler accepts verified DecisionSpec, exact ExecutionPlan, all retained evidence, normalized payloads and reviewed source actions. It verifies selected allocation quantities/targets, refs/context/DAG, payload digests/fingerprints and action contracts. The closed `1.0.0` catalog is: `allocation-comparison`, `evidence-provenance`, `exact-plan-review`, `receipt-status`.

It emits an original ApplicationSpec pinning exact decision/plan/evidence refs, input-schema digest, catalog and per-step events. A BuilderPrompt is then derived using compiler `1.0.0` and target `offline-static-review-template/1`. Its text contains only allowlisted names and artifact identifiers/digests—not retrieved prose, credentials, free-form code or an arbitrary user question. The compiler emits no app, LLM request or executable program.

Artifact IDs/signatures are generated at run time: equivalent application bodies and the template structure are deterministic, while the new ApplicationSpec digest inside the derived prompt changes with its new artifact identity. `noSecretDeclaration:true` is backed by closed input handling, not a security claim about all possible external data. Secret/config/code fields, unsupported components/targets/extensions, missing refs/payloads, wrong plan, altered allocations and wrong source actions are actual tested refusals. Neither ApplicationSpec nor BuilderPrompt is execution authority; runtime admission rejects both.

## Inspect actual artifacts

Use `--export` with a directory outside the checkout, for example `/tmp/aps-dda-demo-export` on Linux or `"$env:TEMP/aps-dda-demo-export"` on PowerShell. The export includes original schema-shaped signed evidence/contracts/decision/policy/plan/authority/invocations/receipts/postconditions, content-addressed retained values, exact payloads, public SPKI pins, report and derived template. All successful-path digests and native signatures are verified by tests. Tampered-scenario exports intentionally contain a plan that fails verification.

No private key/database is exported. Public keys are only reproduction pins for this synthetic fixture; an untrusted export cannot appoint itself a trusted directory or grant authority. Generated keys/state/exports must remain outside source history. The console has no public seed/reset/fault/admin endpoints.

## Verification and limitations

Real tests cover numerical enumeration and data sensitivity; all listed effect scenarios; real original-schema digest/signature chains; exact chosen allocation/Approval binding; bounded policy quantity; policy-allow-not-authority; reopen/original-key reconciliation; partial/manual state preservation; malicious retrieved text; compiler wrong-plan/unsupported-component/action/extension/missing-ref/secret refusals; and exported source receipts/public signatures/value digests. Verification is performed with the SDK's real xUnit framework and file-backed SQLite, not a handwritten result counter. See the [demo API contract](../src/ApsDda.Demo/API_CONTRACT.md) and [console quickstart](../examples/ApsDda.InsuranceDemo/README.md).

The example does not implement production authentication/OAuth/mTLS, durable key/trust/principal provisioning across process restarts, encrypted SQLite, distributed revocation, live read APIs, independently operated external databases, global snapshots/transactions, general compensation, model calibration, insurer deployment, customer automation, payments or financial transfers. Logical sources share one SQLite file and remain separately signed/configured; this is not a distributed-system validation. Reopen tests recreate store/runtime objects in the same process while keeping ephemeral keys alive. Required external hard invariants need a real enforcing source mechanism, not a re-read or an assumption. The original proposed standard remains unchanged.
