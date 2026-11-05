# ApsDda.Demo API contract — 0.1.0-preview.1

Namespace: `ApsDda.Demo`; target: `net10.0`; project: `src/ApsDda.Demo/ApsDda.Demo.csproj`. This **non-packable example library** references the real Core/Runtime projects and embeds `examples/insurance/synthetic-data.json`. It adds no external dependency beyond their locked graph. The [shared SDK contract](../../API_CONTRACT.md) remains authoritative for original-schema artifacts, signatures, trust, source enforcement and authority. Source development was AI-assisted. Author: Kunal Yadav; see [approved licensing](../../LICENSING.md). No production or full-conformance claim is made.

## Deterministic model

```csharp
new AllocationModel(JsonObject dataset);
JsonObject AllocationModel.LoadDataset(); // embedded synthetic aggregate resource
bool AllocationModel.ValidateSource(string source, JsonObject value);
StructuredDecisionSpec model.Compare(string selected = "balanced");
JsonObject model.NumericalProjection(string source); // omits retrievedText only
IReadOnlyList<RegionInput> model.Regions;
long model.ItemsPerTeam, model.Budget, model.DeployablePool;
JsonObject model.Dataset;

public sealed record RegionInput(string Region, long Open, long ExpectedAdded,
  long UpperAdded, long BaseTeams, long AvailableTeams,
  long EmergencyReserveTeams, long MinimumAdditionalTeams,
  long MinimumServedItems, long CostPerTeam);
public sealed record AllocationPlan(string Name,
  IReadOnlyList<long> AdditionalTeams, IReadOnlyList<long> ExpectedBacklog,
  IReadOnlyList<long> UpperBacklog, long Cost) { JsonObject ToJson(); }
public sealed record StructuredDecisionSpec(string ModelVersion,
  string SelectedAlternative, int EnumeratedCandidates, int FeasibleCandidates,
  IReadOnlyList<AllocationPlan> Alternatives, JsonObject Parameters) {
  JsonObject ToJson();
}
```

`StructuredDecisionSpec` is a typed **experimental domain commitment**, not a replacement for the original APS-DDA DecisionSpec body. Its full JSON is committed under the reviewed URI `https://profiles.example.invalid/insurance/allocation-v1`, version `1.0.0`, `critical:true`. Session trust pins the independently constructed expected object; unknown/modified extension values fail closed. The original DecisionSpec uses `method.type="optimizer"`, `method.version="finite-integer-enumeration/1.0.0"`, real evidence refs and parameter digest. The original decision's alternatives are strings containing the numerical comparison JSON; the richer typed form lives in the explicit extension.

All three named regions are required in exact order. Canonical quantity strings, exact units and one synthetic period are checked; unknown fields are refused except bounded `claims.retrievedText`. Arithmetic/candidate sizes are bounded, and an infeasible model raises `POLICY_EVALUATION_FAILED`, never an eligible fabricated result. Public `JsonObject` model/spec properties are developer data, not immutable accepted wire artifacts; do not mutate them after configuring a session. SDK Artifact snapshots remain immutable.

`NumericalPolicy(StructuredDecisionSpec spec, bool human)` implements `IRuntimePolicy`, pins model result and parameter commitments, and evaluates to `allow-with-human-approval` or `allow` only for the expected eligible DecisionSpec. It is local trusted example code, not a general insurer policy/actuarial engine. Eligibility alone or a PolicyDecision is not execution authority.

## Trusted in-process session and effects

```csharp
new InsuranceSession(string selected = "balanced", bool boundedPolicy = false,
  bool loseReply = false, bool semanticMismatch = false,
  bool staleEvidence = false, JsonObject? dataset = null);
WorkflowSnapshot session.Submit();
void session.Authorize(IEnumerable<string>? approvedSteps = null);
void session.Reopen();
JsonObject session.Effects();
void session.Dispose();
```

The session exposes `Model`, `Analysis`, `Clock` (`DemoClock.Now`), `Store`, `Trust`, `Principals`, `Runtime`, configured `Caller`/`Approver`/`Granter`, signing helpers, four signed `Evidence`, actual `Decision`/`PolicyDecision`/`Plan`, `Payloads`, `AuthorityArtifact`, `Compiled` and `Compiler`. `CapacityCapability`/`DraftCapability` are exact pinned original artifacts. `PublicTrustPins` is a defensive copy of **public** SPKI pins; no private key is exported. `InvokeCalls`/`LookupCalls` are measured calls, not synthetic success counts.

The human path reserves the selected alternative's exact quantities in three source-owned regional resources, sequentially. Emergency reserves are excluded from each source's available stock. The policy path is a different exact plan with one internal draft step, maximum quantity/rate 1, four-minute authority, low risk and one exact source/resource/capability/audience. It does not reserve capacity or send a notification. `Authorize` calls the SDK's authenticated admission and durable exact-plan authority consumption; default principals are explicit synthetic fixture registrations. Interactive console review adds a digest acknowledgement but no production identity provider.

`ChangeRegionalVersion(string region)` and `TamperPlanWithoutResigning()` are fixed **in-process fault/provisioning helpers only**. They expose no HTTP endpoint, arbitrary SQL, user-configured plugin or client code. Lost reply invokes the real adapter, commits the real effect, then drops the result; reopen/reconciliation queries the original operation before continuing. SQLite authority consumption and each source effect are separate transactions. Source-local state guard and operation/receipt persistence do not establish a global budget, pool, service or snapshot invariant across live external systems.

## Closed application compiler

```csharp
new ApplicationCompiler(TrustedDirectory trust, ArtifactSigner signer,
  TimeProvider clock, SignedArtifact capacityAction, SignedArtifact draftAction);
JsonObject ApplicationCompiler.DefaultOptions();
CompiledApplication compiler.Compile(SignedArtifact decision, SignedArtifact plan,
  IReadOnlyList<SignedArtifact> evidence,
  IReadOnlyDictionary<string,JsonObject> payloads, JsonObject? options = null);
void compiler.ValidateApplication(Artifact application, Artifact decision,
  Artifact plan);
public sealed record CompiledApplication(SignedArtifact ApplicationSpec,
  SignedArtifact BuilderPrompt);
```

`Compile` verifies signed trusted inputs, original refs/context/DAG, complete retained evidence, payload digests and invocation fingerprints, pinned reviewed source actions, selected numerical quantities/targets, and the closed catalog. Options contain **exactly** `components` and `targetBuilder`; no secret bag, arbitrary code, executable component, URL or retrieved text is allowed. Source actions are supplied by trusted code, not incoming discovery.

Catalog `1.0.0`: `allocation-comparison`, `evidence-provenance`, `exact-plan-review`, `receipt-status`. Compiler `1.0.0`, target `offline-static-review-template/1`. Original ApplicationSpec pins exact decision/evidence/plan refs, input-schema digest, catalog, components and per-step event mappings. `ValidateApplication` is an integrity/reference/domain helper on Artifact objects; callers accepting an external signed ApplicationSpec must additionally invoke Core trust validation. The BuilderPrompt pins ApplicationSpec and has `noSecretDeclaration:true`; its template is derived from identifiers/digests and allowlisted components only, never source prose or a free-form question.

Artifact IDs and ECDSA signatures are freshly generated, so complete signed outputs are not byte-identical between runs. Equivalent ApplicationSpec **bodies** and the versioned template structure are deterministic; the prompt's ApplicationSpec digest necessarily changes with that artifact's new identity. The compiler generates no LLM call, web application, executable code or authority. Both derived types are rejected by runtime authority admission.

## Scenario reports/export

```csharp
InsuranceSession ScenarioRunner.Create(string scenario, string selected = "balanced");
DemoReport ScenarioRunner.Run(InsuranceSession session, string scenario);
void ScenarioRunner.Export(InsuranceSession session, DemoReport report,
  string directory);
JsonObject report.ToJson();
```

Supported names and actual fault effects are documented in [insurance-demo.md](../../docs/insurance-demo.md). Reports return measured source state, runtime `WorkflowSnapshot`/history, safe error codes and actual artifact refs/digests; admission refusals have no WorkflowSnapshot and are marked `NOT_ADMITTED`, **not** invented runtime states. Unsupported names raise `UNSUPPORTED_PROFILE`.

Export profile `aps-dda.experimental/synthetic-demo-export/1` is an external binding, not a new original artifact type. The chain includes actual original artifacts and detached signatures; `bindings.json` contains content-addressed retained values, normalized payload map and public pins. Value URIs follow Runtime's `urn:aps-dda:value:` plus JCS `sha256:` digest. Tampered test exports intentionally fail verification. Exported public pins are fixture reproduction evidence, not an externally authenticated directory.

## Not implemented

No real data, cover/claim entitlement/adjudication/fraud/payment automation, live insurer/source mutation, real notifications, financial transaction, production identity provider, remote trust/revocation, persistent private-key service, global transaction/snapshot, empirical model validation, probability distribution, LLM or Vercel app. The current example uses multiple logical sources in one SQLite file; independently operated databases/APIs are not tested. An actual external hard invariant requires a source-owner mechanism; the SDK refuses such unsupported contracts. Abrupt termination can leave a temporary database, but no private key is saved. Reopen tests recreate runtime/store objects in one process; restart with durable key/principal/trust reprovisioning is not implemented here.
