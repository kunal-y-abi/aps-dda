# Removing the Separate Analytical and Decision Stack for Governed Operational Workflows

**Technical White Paper — Working Draft**  
**Preparation:** AI-assisted working draft  
**Date:** 16 October 2026  
**Status:** Prepared for review. This is a working draft, not ratified, peer reviewed, implemented, certified, or empirically validated.

## Abstract

The central proposition of this paper is that, for a bounded class of operational decision workflows, an organization should not have to build a new analytical replica, bespoke decision application, and disconnected action bridge before it can turn a user request into a safe operational result. Instead, source systems should publish usable data products and named business-process capabilities. Independently developed participants can compose those products and capabilities into a governed loop: **intent → evidence → decision → authority → action → verified outcome**. The decision belongs inside that loop, rather than in a dashboard or chat response that a person must manually translate into a separate action.

This is not a claim that live federation eliminates data engineering, history stores, replication, materialized products, identity work, or workload isolation. Nor is it a claim that Model Context Protocol (MCP) or Agent2Agent (A2A) cannot support the design. MCP revision **2026-07-28** and A2A **v1.0** supply useful tool, context, task, artifact, and delegation mechanics. The narrower conclusion is that generic connectivity or a collection of individually capable source agents is insufficient *by itself* to establish the semantic commitments, exact authority, source-side process checks, durable recovery, and source-issued outcome evidence required by this operational claim. Those missing commitments can be added through a profile carried over HTTPS and existing protocols.

This paper proposes that profile as **APS-DDA v0.1**, the Agent Participation Standard for Data, Decisions and Actions. APS-DDA is a proposed behavioral and semantic participation standard over HTTPS, not a new networking layer and not a required agent implementation. An LLM agent, rules engine, deterministic adapter, workflow runner, or vendor-native service can participate if it meets the observable obligations. The proposal includes typed Decision and Application Specifications, with optional compilation into builder-specific prompts for v0/Vercel-like application builders. Those are enabling artifacts; they are not the policy, entitlement, authority, or system of record.

## The operational replacement objective

The target outcome comes first: **remove the requirement to construct a separate data platform and disconnected decision/action application for supported operational workflows**. A user should be able to ask for an operational outcome in business terms. The system should locate only permitted, source-owned products; obtain bounded evidence with its meaning and observation conditions; calculate or validate a decision; seek an exact approval or apply a bounded policy delegation; invoke named source capabilities; and show receipts and postconditions from systems of record.

Today, a cross-application request often becomes a project to extract data into a warehouse or lakehouse, model it, publish a dashboard, have a person interpret the dashboard, and then re-enter or bridge a decision into workflow software. Each stage may be useful, and many organizations have strong reasons to retain it. The problem is not that analytical systems are inherently wrong. The problem is that a new operational decision frequently has to reconstruct operational reality in a different stack before anyone can act. Evidence, the accountable judgment, approval, and actual state change become separated across teams and systems.

A replacement claim has meaning only if it is testable. For a selected workflow, the proposed loop qualifies as a replacement only when it avoids a **new bespoke** central ingestion/model/dashboard/disconnected-action chain, while still meeting the agreed semantics, load, latency, security, recovery, and audit requirements. Reusing an existing governed product is compatible with the proposition. So is a deliberate materialized product where history, throughput, reconciliation, residency, reproducibility, or isolation demand it. Calling a conventional warehouse with an agent front end a replacement is not.

The intended boundary is operational decision work with finite, named evidence and action scope: a buyer deciding whether to cancel an unreceived purchase-order balance; a service manager deciding whether to approve a bounded refund; or a planner deciding whether to reserve stock under published rules. It is not a claim about unconstrained exploration, enterprise-wide historical analysis, model training, universal optimization, or every cross-system transaction. The narrower scope makes the proposition falsifiable rather than rhetorical.

## Why an additional participation profile is proposed

The difficult question is not whether applications can exchange HTTP messages. They already can. The question is what must be true before independently built participants may characterize a cross-source decision and side effect as grounded, authorized, and verified.

MCP revision **2026-07-28** is a valuable substrate. Its official release announcement states that the revision was released on 28 July 2026; the specification uses stateless, self-describing requests and supports resources, prompts, and tools in a host/client/server model. [1] [2] Tools can declare JSON Schema input and output schemas, and when an output schema is declared the server’s structured content must conform. [3] This is a useful type boundary. MCP also permits mutating tools; its tools guidance expects servers to validate inputs, implement access controls and rate limits, while clients should confirm sensitive operations and audit use. It would be inaccurate to claim that MCP cannot write. [3]

The same revision has an optional HTTP authorization profile using OAuth-oriented protected-resource metadata, resource indicators, and audience validation. [4] It has optional client interaction through Multi Round-Trip Requests, and its former experimental Tasks work is an opt-in `io.modelcontextprotocol/tasks` extension. [1] [5] These mechanisms are useful for a host calling a source service. They do not, on their own, require that two tools share a definition of “unreceived balance,” declare whether a product is authoritative, bind a manager’s approval to an ordered command graph, or prove an external effect.

A2A **v1.0**, announced on 12 March 2026 as the first stable, production-ready release, is also useful rather than a strawman. [6] Its task model, artifacts, task updates, and agent descriptions provide a credible cross-agent delegation envelope. The specification’s task states include `SUBMITTED`, `WORKING`, `INPUT_REQUIRED`, `AUTH_REQUIRED`, `COMPLETED`, `FAILED`, `CANCELED`, and `REJECTED`. [7] The cited detailed route is mutable `/latest/` documentation, so an implementation must pin its selected A2A version and binding rather than use that route as a release lock. An A2A Agent Card can advertise skills, interfaces, and security requirements. The discovery material allows well-known cards and curated registries, but does not prescribe a standard curated-registry API. [8]

Those features are not a portable source contract. A skill description does not prove that its publisher is authoritative for a metric or eligible to change an order. A generic artifact does not say which evidence fields, state preconditions, idempotency behavior, approval scope, or postcondition constitute a business receipt. A2A’s `AUTH_REQUIRED` state usefully signals an authorization dependency, but the v1 specification expressly leaves the scope, representation, validity, and revocation of resulting authorization to implementation. [7] Thus an authorization interaction must not be confused with a plan-bound business approval.

The conclusion is deliberately limited. **An unprofiled MCP-only integration or a collection of standalone source agents is insufficient alone for this paper’s end-to-end claim. It is not inherently impossible, unusable, or incapable of governed action.** A composed MCP implementation can expose APS-DDA typed tools and resources, use deterministic services behind tools, issue structured receipts, and add a trusted resolver and policy service. An A2A implementation can carry APS-DDA artifacts in task messages and artifacts, use its task mechanics for durable delegation, and negotiate an extension. A vendor-native agent can be wrapped by an adapter. Once those commitments are mandatory, externally visible, and tested, the result is substantively an APS-DDA-like participation profile over MCP, A2A, or native HTTPS.

This distinction avoids two errors. It avoids dismissing mature connectivity mechanisms that reduce adoption cost. It also avoids treating wire compatibility, JSON shape, or persuasive agent text as evidence that a cross-domain decision is meaningful and a consequential action is controlled.

## The architecture: source products and source capabilities

The proposed architecture has a simple division of responsibility. Sources retain ownership of facts and state changes. A participation control plane makes their products and capabilities discoverable, composable, and governable. The user-facing decision workspace exposes the evidence, uncertainty, plan, approval scope, receipts, and recovery status. It does not become a shadow system of record.

A **Data Product Contract** is a source-owned, versioned commitment. It identifies its owner and authoritative scope; schema and semantic identifiers; grain and entity keys; temporal interpretation; lineage; classification; permitted purpose; entitlement conditions; quality and freshness state; correction, deletion, and history behavior; query shapes; budgets; residency; retention; compatibility; and deprecation. It must say enough to prevent a consumer from treating a current operational response as a complete, globally consistent historical dataset.

This aligns with, but is not replaced by, existing data-contract and semantic-layer work. Data mesh describes domain interfaces as containing operational capabilities as well as analytical endpoints, and describes a data product as code, data/metadata, and infrastructure. [9] Open Data Contract Standard (ODCS) **v3.2.0** is a platform-agnostic contract structure with sections such as schema, context, quality, roles, SLA, infrastructure, and authoritative definitions. [10] dbt’s Semantic Layer documents centralized metric definitions, automatic joins, and options to materialize or cache metrics. [11] None of those cited mechanisms alone supplies portable, source-enforced authority for a consequential command. They demonstrate useful antecedents rather than an empty field.

A **Process Capability Contract** is separate from a data product. It names a business command owned by the source: for example, `CancelPurchaseOrder`, not `PATCH status=cancelled`. It describes allowed transitions and invariants, input and result schemas, resource scope, source-side authorization and separation-of-duties checks, current-state precondition, idempotency domain and duplicate behavior, risk tier, approval/evidence requirements, timeout and error status model, receipt, and compensation or explicit non-compensability. Google AIP-136 gives a related design rationale for custom methods when an action does not fit standard resource methods. [12]

This separation allows a source to publish a stable external promise while using any internal design. It may expose a live lookup, a curated snapshot, a domain read model, an event stream, or a materialized historical product. It may implement a command through a rules engine, a vendor workflow, or a transactional API. APS-DDA does not declare a universal ontology or require an LLM. It requires that the external participant say what it means, what it can do, and under what limits.

A tenant-controlled resolver or registry is therefore a production requirement for action workloads. Self-description is not trust. The registry pins approved publisher identity, allowed HTTPS domains and token audiences, role claims, status, contract digest, profile version, semantic domain permissions, and revocation information. Capability selection fails closed if a required contract, semantic binding, or supported profile is absent. This is an enterprise deployment control, not a claim that APS-DDA creates an external global registry.

## The governed decision loop

The loop begins with a typed task and an Execution Context. The context identifies tenant, authenticated subject, workload or client identity, executor, delegation chain, correlation identifier, purpose, classification/residency context, and pinned policy references. Each source independently authenticates the workload and authorizes the delegated subject for its resource, purpose, capability, and current business rules. Effective permission is the intersection of subject/delegation, source entitlement, product contract, data classification, purpose or consent, residency, and current policy. An orchestrator’s broad credential is not an acceptable substitute.

Audience restriction is a useful lower-layer control. RFC 8707 defines OAuth resource indicators, and RFC 9700 recommends least privilege, audience restriction, and refusal of a token not intended for the resource server. [13] [14] MCP’s 2026-07-28 HTTP authorization profile similarly requires the target resource and intended-audience validation where that profile is used. [4] APS-DDA adds the business distinction that authentication, source authorization, policy authorization, and human approval are separate records and checks.

Retrieval is bounded. The orchestrator asks for a declared product, projection, predicate, aggregation, row/scan/time/cost budget, freshness requirement, region, and priority. A publisher either accepts within contract, returns a typed counteroffer, or refuses. Negotiation may narrow scope, select a declared alternate product, or explain a quota. It may not silently relax classification, entitlement, source invariants, required evidence, segregation of duties, or hard risk limits.

Every evidence item records the product contract and semantic version, query or compiled-plan digest, observation time, source version or ETag where supplied, watermark or snapshot identifier, quality state, and cache/materialization status. Cross-source evidence is called **observational** by default. It may be called a coordinated snapshot only when a documented, tested source/integrated-product contract covers the named sources. Per-source versions are valuable but do not manufacture a global atomic read.

A **Decision Spec** is then a first-class typed record. It states the accountable owner, question, alternatives, objective, hard constraints, eligible population, evidence references and cutoff, source/product/semantic versions, quality condition, deterministic formula or optimizer version, assumptions, uncertainty, recommendation, and action eligibility. An agent may draft a question, propose alternatives, or explain a result. For consequential arithmetic, eligibility, allocation, or optimization, a deterministic method should calculate and validate the record. Schema-constrained model output is a syntax aid, not proof that the decision is correct or complete. [15]

The Decision Spec may produce an **Execution Plan**: an ordered directed acyclic graph of exact command steps. Each step binds source audience and capability version, target, normalized effect-bearing payload or payload digest, expected state, idempotency domain/key and invocation fingerprint, evidence references, policy/approval obligation, time bound, risk, dependency ordering, and either a named compensator or explicit non-compensability with a recovery owner. The canonical security-relevant plan is serialized with the JSON Canonicalization Scheme (JCS) and hashed as `base64url(SHA-256(UTF-8(JCS(plan_core))))`. RFC 8785 defines JCS for deterministic JSON serialization, but it is an informational canonicalization specification, not a policy or semantic standard. [16]

Hashing has narrow value. It binds the defined representation; it does not prove that evidence is true, current, authorized, or sufficient. The plan core must include every field that changes who, what, where, when, why, or how much is affected, and exclude bearer tokens, signatures over the plan, attempt counters, transport tracing, and receipts created afterward. Profiles must reject duplicate JSON member names and ambiguous numeric representations. Exact money, quantities, timestamps, and large identifiers should use canonical strings. Object order is handled by JCS; ordered command arrays remain semantic.

The companion standard fixes the v0.1 projection rather than allowing each implementer to select fields: the plan core is the complete ExecutionPlan artifact minus `/header/contentDigest` and `/body/planDigest`. After inserting the computed plan digest, the artifact content digest covers the complete artifact minus only `/header/contentDigest`. Detached signatures and mutable runtime records remain outside these projections. This prevents self-reference and different approval/execution interpretations of the same plan. The supplied fixtures use placeholder digests and do not constitute a tested canonicalization implementation.

## Authority, source control, and verified outcomes

A user acknowledgement in an MCP interaction or an A2A `AUTH_REQUIRED` response is not itself authority. A **Policy Decision** records the evaluation result and obligations; it is evidence, not permission to mutate. A **Human Approval** binds an authenticated approver, policy decision, exact plan digest, permitted steps/scopes, effect summary, issue time, expiry, and single-use state. A **Policy Grant** can replace a human approval only where a named risk profile permits it. It must bind actor/workload, source audience, capability, resource boundary, purpose, amount/quantity/rate/time limit, required evidence, policy version, revocation reference, and escalation path.

For a multi-step plan, approval authorizes one durable workflow instance. Immediately before each side effect, the execution plane derives a narrow step authorization for the workflow, step, source audience, capability version, resolved target, payload/precondition fingerprint, authority references, expiry, and nonce or serial. The source validates this scope and its own current authorization immediately before effect. A retry reconciles the same authorized invocation and idempotency key; it is not a license to mint a different operation.

The approval service atomically consumes the approval before its authorized effect. A material change to target, payload, capability version, evidence, policy, delegation, expiry, or expected state requires new authority. OWASP guidance makes the same core point: agent authorization belongs outside the agent context; a simple `user_confirmed` flag is insufficient, and approval must match the actor and exact tool call, expire, and be atomically consumed. [17]

For a multi-step workflow, that consumption means a durable, one-time binding of the approval to one workflow, plan digest and permitted step set. Recovery reconciles the same binding; it does not authorize a second workflow. This local authority-store transaction does not atomically commit a source action. A crash between approval consumption and dispatch must recover the existing workflow, and each later step still checks expiry, revocation and source authorization.

Source systems remain final enforcers. A command must carry an expected state and the source must evaluate it immediately before the effect. HTTP `If-Match` is a standard conditional mechanism: a false condition prevents the method from being applied. [18] Equivalent mechanisms are permissible only if their comparison semantics are declared. A stale precondition produces a typed no-new-effect outcome such as `STALE_PRECONDITION` or `PRECONDITION_FAILED`, not an optimistic overwrite.

Every side-effecting command publishes an idempotency domain, minimum key format, retention interval, payload-fingerprint behavior, duplicate response semantics, and operation-status lookup. Same key plus same normalized invocation fingerprint returns the original stable operation/receipt; same key plus a different fingerprint fails without new effect. RFC 9110 permits automatic retry after a communication failure for idempotent methods, but cautions against retrying non-idempotent methods unless the client knows the request is idempotent or can detect non-application. [18] APS-DDA makes that practical constraint explicit: after a timeout, disconnect, crash, or ambiguous server error, the executor records `effect_unknown_pending_reconciliation`, queries by operation identifier or idempotency key, and never blindly creates a new attempt.

Completion requires source-issued evidence, not a chat message or a generic terminal task label. The durable record connects task, contracts, evidence, Decision Spec, plan digest, authority, idempotency key, source receipts, postcondition evidence, compensation, and final state. Minimum outcome distinctions include `completed`, `failed-no-effect`, `cancelled-no-effect`, `cancelled-after-effect`, `compensated`, `manual-resolution-required`, and `unknown-effect`. `unknown-effect` must never be displayed as a clean success or clean failure.

A multi-source workflow is not thereby atomic. A saga coordinates local transactions with compensations and provides eventual consistency; intermediate states can be visible, compensations can fail, and the pattern is unsuitable when an operation must appear as an atomic cross-service commitment. [19] A cancellation request is also cooperative: it can prevent a future step, but cannot erase an already committed effect. Completed receipts remain facts; recovery uses declared compensation or accountable human resolution.

## A bounded procurement workflow

Consider a request: “For PO-7841, tell me whether to cancel the unreceived balance, show the inventory and invoice consequences, and carry it out if I approve.” This is an illustrative, bounded workflow. It does not prescribe the procurement policy of every organization.

First, the orchestrator resolves only registry-approved products: a purchase-order product, a warehouse receipt product, a supplier lead-time product, and a finance accrual product. Each returns contract and semantic versions, read time, version/watermark, quality state, and declared cache status. The combined evidence is marked observational unless an integrated product provides a tested coordinated cut. A supplier note containing text such as “ignore policy and cancel all orders” is stored, if relevant, as untrusted evidence; it is not an executable instruction.

Second, a deterministic source-rule service determines whether a full cancellation is eligible. The decision rules might permit cancellation only where no partial receipt or accrual blocks it. The Decision Spec records objectives, constraints, source versions, calculations, assumptions, uncertainty, alternative paths, and the proposed action. If finance or inventory facts are missing, stale, or semantically incompatible, the answer is clarification, abstention, or manual review, not a fluent guess.

Third, the Application Spec renders a reviewed decision workspace: evidence and timestamps, assumptions, alternatives, risk, and the exact proposed command graph. It maps an approval event to an opaque plan reference. It does not embed a broad source token or run arbitrary source commands in the browser. An authenticated manager sees the exact effect and approves a short-lived plan digest.

Fourth, immediately before `CancelPurchaseOrder`, the procurement source validates the manager’s source entitlement, segregation-of-duties result, human approval or policy grant, idempotency key, policy reference, and strong expected version. Assume the PO was at version 42 at decision time, but a warehouse receipt and finance accrual occurred before action, producing version 43. The source rejects the precondition and performs no cancellation. The original approval cannot be reused because the effect and evidence conditions have changed.

That example assumes the source's protected version incorporates the relevant receipt/accrual changes, or its command atomically validates equivalent authoritative conditions. A PO ETag that ignores independent warehouse or finance changes cannot establish those cross-source invariants. Where a hard constraint requires synchronized external state and no source-owned guard or coordinated mechanism provides it, the workflow must remain unsupported or escalate rather than claim that re-reading alone closes the race.

Fifth, the workflow rebuilds the evidence and decision. It may offer a source-supported partial cancellation, return, credit, or manual exception. That is a new plan requiring new authority. If a later submission may have succeeded but the response is lost, the executor queries the original supplier operation using the same idempotency key before sending anything again. Completion is shown only when required source receipts and postconditions are present. Partial success produces explicit compensation or `manual-resolution-required`, not a false claim of rollback.

This example matters because it makes uncertainty visible. The useful outcome is not merely a fast cancellation. It is a decision and execution path that refuses to hide stale state, cross-source inconsistency, or ambiguity behind a conversational interface.

## Data locality, history, and consistency

Source ownership is not a zero-copy promise. It aims to minimize unnecessary non-authoritative copies while meeting declared operational requirements. Data can move in bounded results, query-engine exchange and spill, encrypted working sets, application payloads, logs, caches, replication, and disaster recovery. A materialized product can be the safest and most economical answer when it is explicit: owner, source contract and semantic version, transformation version, watermark/as-of time, classification, purpose, region, TTL, invalidation/revocation, and access policy are declared. It must not be an undisclosed shadow warehouse.

Federation must be selected by evidence, not slogan. Trino documents that predicate, projection, aggregation, limit, top-N, and join pushdown depend on connector and source; a pushed join requires tables in the same catalog and pushable predicates, and `EXPLAIN` is needed to verify execution. [20] The planner should use declared query shapes and budgets, record its actual plan when available, and select a published materialized product when a recurring join cannot meet source load or latency requirements.

Temporal meaning is equally important. Delta Sharing exposes per-table version/timestamp interfaces, with history/time travel dependent on whether history sharing is enabled. [21] That supports per-product temporal evidence; it does not establish a heterogeneous global snapshot. Cross-region and cross-cloud sharing can also require replication, as Snowflake’s documentation explains. [22] A decision that truly requires one temporal cut must use a tested coordinated product or abstain. It must not manufacture atomicity through a common orchestration timestamp.

Identity resolution remains a governed responsibility. Data mesh identifies unified identification across domains as a global concern. [9] A product contract must name authoritative identifiers and allowed mappings, including match confidence, consent/purpose, merge/split, survivorship, and deletion propagation. The orchestrator must not equate names, email addresses, or locally scoped identifiers because they happen to resemble each other.

Long-horizon analytics, training data, recurring high-volume joins, reconciled history, legal retention, and workload isolation often justify durable engineered products. These cases are not failures of the proposal. They are places where a source-owned or centrally governed data product should be explicit. The failure would be representing such a product as if it did not exist, or promising its elimination without meeting the work’s actual needs.

## Threat and failure model

The architecture deliberately separates a reasoning/presentation plane from a trusted control/execution plane. The first plane can contain LLMs, agents, retrieval adapters, generated prompts, and user interfaces. It treats user input, documents, source text, tool descriptions, and inter-agent messages as untrusted instructions. The second plane owns delegated identity, policy, semantic/query compilation, plan hashing, approvals, source command dispatch, durable workflow state, receipt verification, and audit references. It admits only registered products and named capabilities.

This boundary addresses prompt injection and confused-deputy risks without claiming to eliminate them. OWASP warns that indirect prompt injection can arrive through retrieved documents and API content, and that authorization must be enforced outside the agent context. [17] NIST similarly describes agent hijacking as indirect prompt injection exploiting inadequate separation of trusted instructions and untrusted data. [23] Even a signed source document can identify its issuer and protect bytes without becoming a policy instruction or proof that an action is safe.

Required behavioral tests should inject altered amounts, targets, capability versions, purposes, evidence versions, preconditions, and step ordering after approval. They should demonstrate that the authority check fails. They should alter source state between read and dispatch and demonstrate a no-effect stale-write rejection. They should commit at the source while dropping the response, crash the executor, and demonstrate reconciliation without duplicate effect. They should reorder, duplicate, and lose notifications, cancel at different points, revoke a grant, and fail each compensation. A terminal task state is not enough evidence for any of these cases.

The design does not claim universal security. A contract can be mis-specified, a source can lie, a policy can be wrong, an approver can make a poor decision, and a non-compensable external effect can remain irreversible. The value of the profile is to make the obligations inspectable, testable, and fail-closed where a required proof is unavailable.

## Competitive and novelty boundaries

The proposal should not claim to invent the data-to-decision-to-action loop. Palantir documents Foundry’s Ontology as an operational layer over datasets, virtual tables, and models, with semantic elements and actions, functions, and dynamic security; its action types describe transactions, validation, side effects, and writeback. [24] [25] Palantir AIP documents agents and workflows on top of that model. [26] A customer already using that environment may reasonably prefer its native architecture for a local loop.

Salesforce also documents Agentforce custom action paths and Data 360’s ingestion, mapping, transformation, identity-resolution, and activation paths through agents, flows, webhooks, and APIs. Its documented zero-copy connectors are read-only. [27] [28] These features disprove any suggestion that vendor platforms lack governed actions or integration. They also illustrate why it would be wrong to promise the end of all data movement and transformation.

The proposed distinction is neutral, portable participation across independently operated products and agents: versioned data/process contracts, semantic bindings, trusted capability selection, bounded authority, source receipts, and recovery semantics. That distinction is a design proposal, not evidence of uniqueness, necessity, superior economics, or a durable moat. A vendor-native platform can be a participant. APS-DDA should be adopted only where cross-platform interoperability and source-owned control outweigh its publishing, registry, testing, and operational costs.

Builder-specific application generation belongs on the same boundary. v0 documents a code and GitHub delivery path with branches, previews, CI, pull requests, and branch protection. [29] These are useful delivery practices, not a business-authorization model. An Application Spec can constrain reviewed components, typed evidence bindings, classification, accessibility, and plan-action references. A prompt for a v0-like builder is a versioned compilation artifact from that spec. Generated code still requires normal review, testing, release ownership, dependency assessment, and security controls. It never becomes the policy, entitlement, decision record, or source receipt.

## Adoption and falsifiable evaluation

A practical minimum viable profile should start with one tenant, trusted publishers, bounded keyed products, named source commands, a finite command graph, immutable evidence/plan/receipt links, polling and reconciliation, and one domain profile. It should exclude universal ontology, arbitrary federated SQL, distributed atomic commit, free semantic bargaining, automated certification, and arbitrary generated-code execution. These exclusions are safety and scope boundaries, not claims that excluded outcomes are impossible.

The first pilot should compare the same two to four sources, identities, policy rules, and preregistered cases in four arms: a warehouse/transformed model/manual action path; isolated source agents with human reconciliation; direct MCP typed tools without mandatory cross-domain profile obligations; and APS-DDA using any suitable binding. Each arm retains manual fallback. The case set should intentionally include identity ambiguity, incompatible grain/currency/calendar, version drift, non-aligned reads, permission denial, quotas, late correction/delete, stale write, timeout-after-possible-success, duplicate delivery, revocation, and partial failure.

Measures should include initial and change engineering effort; rows/bytes and source impact; cache/replica footprint; adjudicated decision correctness; appropriate detection of semantic conflict or need for clarification; evidence reproducibility; approval/policy violations; stale/duplicate write prevention; receipt coverage; recovery time; completion; human reconciliation effort; and source-owner support burden. Pre-register criteria and adjudication. Do not infer performance or safety from the architecture alone.

The thesis is falsified for a workflow if it still requires a new central team to ingest and harmonize every source, build a bespoke dashboard or decision app, and manually bridge every action; if published contracts cannot meet source load, history, semantics, latency, or recovery needs; or if the incremental source-owner and control-plane burden exceeds the avoided work. Conversely, passing conformance fixtures establishes only declared profile behavior. It does not establish factual truth, universal security, or business success.

## Conclusion

For suitable operational decisions, the intended replacement is not an agent wrapper around a conventional analytical and action stack. It is a governed operating loop that begins with source-published products and capabilities and ends with source-issued evidence of what happened. The separate analytical/decision stack is avoided where the sources can publish adequate contracts and where measured workflow requirements permit it.

MCP 2026-07-28 and A2A v1.0 are valuable parts of the route, not obstacles to it. The proposal does not require a new transport or a uniform agent internals. It requires a shared answer to harder questions: what a product and command mean; who owns them; what evidence supports a decision; what exact effect was approved or policy-authorized; how the source protects state; and how a distributed workflow proves, reconciles, or escalates its outcome. APS-DDA v0.1 is a working draft intended to make those questions testable. Its value must be shown by bounded implementations and falsifiable comparisons, not asserted by protocol labels.

## References

[1]: https://blog.modelcontextprotocol.io/posts/2026-07-28/ "The 2026-07-28 Specification"

[2]: https://modelcontextprotocol.io/specification/2026-07-28 "Model Context Protocol Specification, revision 2026-07-28"

[3]: https://modelcontextprotocol.io/specification/2026-07-28/server/tools "MCP Server Tools, revision 2026-07-28"

[4]: https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization "MCP Authorization, revision 2026-07-28"

[5]: https://modelcontextprotocol.io/extensions/tasks/overview "MCP Tasks Extension Overview"

[6]: https://a2a-protocol.org/latest/blog/2026/03/12/a2a-protocol-ships-v10-production-ready-standard-for-agent-to-agent-communication/ "A2A Protocol Ships v1.0: Production-Ready Standard for Agent-to-Agent Communication"

[7]: https://a2a-protocol.org/latest/specification/ "Agent2Agent (A2A) Protocol Specification"

[8]: https://a2a-protocol.org/latest/topics/agent-discovery/ "Agent Discovery in A2A"

[9]: https://martinfowler.com/articles/data-mesh-principles.html "Data Mesh Principles and Logical Architecture"

[10]: https://bitol-io.github.io/open-data-contract-standard/v3.2.0/ "Open Data Contract Standard v3.2.0"

[11]: https://docs.getdbt.com/docs/use-dbt-semantic-layer/dbt-sl "dbt Semantic Layer"

[12]: https://google.aip.dev/136 "Google AIP-136: Custom Methods"

[13]: https://www.rfc-editor.org/rfc/rfc8707.html "RFC 8707: Resource Indicators for OAuth 2.0"

[14]: https://www.rfc-editor.org/rfc/rfc9700.html "RFC 9700: Best Current Practice for OAuth 2.0 Security"

[15]: https://ai-sdk.dev/docs/ai-sdk-core/generating-structured-data "AI SDK: Generating Structured Data"

[16]: https://www.rfc-editor.org/rfc/rfc8785.html "RFC 8785: JSON Canonicalization Scheme"

[17]: https://cheatsheetseries.owasp.org/cheatsheets/AI_Agent_Security_Cheat_Sheet.html "OWASP AI Agent Security Cheat Sheet"

[18]: https://www.rfc-editor.org/rfc/rfc9110.html "RFC 9110: HTTP Semantics"

[19]: https://docs.temporal.io/design-patterns/saga-pattern "Temporal Saga Pattern"

[20]: https://trino.io/docs/current/optimizer/pushdown.html "Trino Pushdown"

[21]: https://raw.githubusercontent.com/delta-io/delta-sharing/main/PROTOCOL.md "Delta Sharing Protocol"

[22]: https://docs.snowflake.com/en/user-guide/secure-data-sharing-across-regions-platforms "Snowflake Cross-Region and Cross-Cloud Data Sharing"

[23]: https://www.nist.gov/news-events/news/2025/01/technical-blog-strengthening-ai-agent-hijacking-evaluations "NIST Technical Blog: Strengthening AI Agent Hijacking Evaluations"

[24]: https://palantir.com/docs/foundry/ontology/overview/ "Overview: Ontology — Palantir Foundry"

[25]: https://palantir.com/docs/foundry/action-types/overview/ "Action Types: Overview — Palantir Foundry"

[26]: https://palantir.com/docs/foundry/aip/overview/ "Overview: AIP — Palantir Foundry"

[27]: https://developer.salesforce.com/docs/ai/agentforce/guide/get-started-actions.html "Build and Enhance Agentforce Actions"

[28]: https://developer.salesforce.com/docs/data/data-cloud-dev/guide/dc-architecture.html "Data 360 Architecture"

[29]: https://v0.app/docs/github "v0 GitHub Documentation"
