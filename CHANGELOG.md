# Change history

This log separates the **proposed standard**, the **public technical edition**, and **reference implementation versions**. It is not a ratification or certification record. Author: Kunal Yadav; initial maintainer: @kunal-y-abi. [Approved licensing](LICENSING.md) distinguishes Apache-2.0 new code/schema/synthetic fixtures, CC BY 4.0 authored prose/diagrams and retained third-party exceptions. Historical technical content and third-party notices are retained; preparation headers are generalised for this distribution.

## First preview prepared — `v0.1.0-preview.1` / public technical edition

### Added on 3 November 2026

- Experimental .NET/C# `ApsDda.Core` library using the full unchanged original APS-DDA schema, strict JSON parsing, pinned upstream RFC 8785 canonicalization source, exact content/plan projections and references, immutable artifacts, documented experimental P-256 detached signatures and tenant trust pins.
- Experimental `ApsDda.Runtime` library with file-backed SQLite artifact/evidence/authority-use/workflow/source-operation persistence, authenticated principal handles, exact approval and bounded internal policy authority, source-local version/invariant enforcement, original-key reconciliation and verified local receipts/postconditions.
- Selected source commands for synthetic internal capacity reservation and preparation of an internal notification draft. The draft command sends nothing; neither is live insurance adjudication, payment or customer entitlement processing.
- Real Core/Runtime automated suites, local NuGet packages and an outside-checkout installed-package consumer. Recorded final suites passed 130 Core and 85 Runtime tests with no failures/skips; recorded locked restores/Release builds had no warnings/errors. Coverage and limitations are in [implementation status](docs/implementation-status.md).
- Public technical paper centered on cross-system insurance analytical readiness, explicitly separating the broader analytical/domain extension from core contracts and actual source effects. The original procurement-centered paper is retained unchanged under [docs/original](docs/original/technical-white-paper.md).
- Architecture guide, three editable Mermaid diagrams, selected-requirement status matrix and contribution/security/conduct guidance. The verified initial maintainer is @kunal-y-abi; GitHub private vulnerability reporting is enabled and ordinary conduct concerns use a separately verified private maintainer contact.

### Publication metadata approved on 3 November 2026

- Applied Apache-2.0 to new code, tests/tools/configuration, machine-readable schema and synthetic JSON fixtures; CC BY 4.0 to authored prose (including retained originals and proposed specification) and original diagrams. Added full license texts, scope mapping and nuanced package license/notices preserving mixed upstream terms.
- Added Kunal Yadav authorship/copyright, @kunal-y-abi maintainer/CODEOWNERS, repository metadata and SOURCE.md AI-assistance disclosure. First preview is prepared for a marked GitHub prerelease with PDFs and nupkgs; no NuGet.org publication.
- Enabled repository issues and verified GitHub private vulnerability reporting. Contribution terms require rights under the applicable scope, without a separate CLA or IP assignment.

### Preserved and clarified

- [APS-DDA v0.1](spec/APS-DDA-v0.1.md) retains the original proposed standard’s normative requirements. Preparation headers are generalised for this distribution; this is not a normative amendment. The [original schema/fixtures](spec/original) remain historical baselines with placeholder example digests.
- The standard is language-neutral; .NET/C# is one selected experimental reference. Full conformance, certification, expert sign-off, production deployment and empirical analytical-platform replacement are not claimed.
- Policy evaluation is evidence, not mutation authority. Authority consumption and source effects use separate local transactions. Local state guards do not protect independent external hard invariants. Unknown/partial outcomes remain truthful; no automatic compensation or global rollback is implied.
- MCP/A2A named revisions are historical documentation anchors and useful substrates, not latest-version claims or implemented adapters. Governed models, history, materialized products and copies remain legitimate.
- New source development was AI-assisted. Existing upstream source/fixture provenance and notices are retained; AI-assisted drafting/inspection/tests are not human expert review.

### Integrated local verification on 3 November 2026

- Added all HTTP/demo/example/test modules to the solution, a 20-case cross-module acceptance suite, portable C# Kestrel/package verifier, developer README, secret/state ignore rules, empty sample environment and read-only Ubuntu/Windows .NET CI definition.
- Integrated locked restore and Release build passed with zero warnings/errors on Linux SDK 10.0.403. The full suite passed 368 xUnit cases: Core 130, Runtime 85, HTTP 89, Demo 44, Acceptance 20; zero failed/skipped. Revision-specific cross-platform CI results are recorded separately in GitHub Actions.
- All 13 supported console scenarios measured real SQLite effects/refusals. Actual loopback Kestrel and the typed-client synthetic approval replay passed, with available 18/reserved 2/version 2, unauthenticated 401 and no reset endpoint; the host was stopped afterward.
- Packed all three reusable libraries, made the copied original schema package path explicit, inspected covered upstream source/notices and verified a newly created outside-checkout NuGet-only consumer with an isolated cache, embedded schema hash, actual signature/authority/effect/postcondition, duplicate idempotency and durable reopen. Packages are prepared as GitHub prerelease assets, not a NuGet.org publication.
- Acceptance tests directly exercise source guards after dispatch and corrupt an actual retained postcondition value; they do not simulate verification by returning a canned success result. No production feature expansion or HTTP fault/admin endpoints were added.

### Cross-platform portability corrections

- The initial GitHub Ubuntu job passed all checks. Windows exposed Git checkout LF/CRLF conversion in pinned schema/corpus bytes and pooled test-owned SQLite handles that prevented cleanup.
- Added byte-preserving repository attributes and disabled pooling on the four direct test-owned connections. Kept the original expected hashes, all 10,000 numerical vectors and all source/authority/effect assertions unchanged. Runtime library source and normative text were not amended by these corrections.
- The initial failed run remains visible in [verification history](docs/implementation-status.md); subsequent CI results apply to their exact revisions.

## 0.1 proposed working draft — 16 October 2026

The retained original bundle contains the proposed participation standard, Draft 2020-12 artifact schema, fictional procurement structural examples and technical paper. The original technical vetting was a bounded internal AI-assisted review, not external peer review or certification. Its recorded tests were structural, with placeholder digests—not a cryptographic runtime or a live-source pilot.

No earlier release history or standards approval is asserted here; the retained original authored prose is now included in the approved CC BY 4.0 scope through LICENSING.md. Future normative changes require a versioned proposal and explicit decision record under [CONTRIBUTING.md](CONTRIBUTING.md).
