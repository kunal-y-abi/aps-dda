# Contributing

APS-DDA is a proposed, language-neutral participation profile with an experimental .NET/C# selected-subset reference. Contributions must distinguish **normative proposal**, **experimental binding**, **analytical/domain extension**, and **tested implementation**. Neither an accepted change nor passing tests establishes ratification, certification, lawful processing, production readiness or factual truth.

Contributions intentionally submitted for inclusion are supplied under the applicable scope in [LICENSING.md](LICENSING.md): Apache-2.0 for new code, tests, tools/configuration, machine-readable schema and synthetic JSON fixtures; CC BY 4.0 for authored prose and original diagrams. You must have the rights needed to submit the material under that scope. No separate CLA, copyright transfer or IP assignment is required. Preserve third-party source headers, notices and provenance; their existing terms are not overridden. CC BY 4.0 prose does not grant patent rights, and no additional patent promise is requested.

## Start with the right report

Use [repository issues](https://github.com/kunal-y-abi/aps-dda/issues) for a reproducible non-sensitive bug, documentation correction or versioned proposal. Use the verified private vulnerability-reporting process in [SECURITY.md](SECURITY.md) for a suspected vulnerability, not a public exploit report. Follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) in discussions and reviews; ordinary conduct reports use a verified private maintainer contact, not security advisories. [MAINTAINERS.md](MAINTAINERS.md) identifies the initial maintainer. No guaranteed response schedule is declared.

Explain the question or failure, affected version/module, expected versus actual behavior, a minimal synthetic reproduction and relevant safe test output. Never include credentials, signing keys, real customer data, protected evidence, production endpoints or sensitive logs. Schema-shaped historical fixtures with placeholder digests are not verified runtime reproductions.

## Normative changes require an explicit proposal

The [v0.1 standard](spec/APS-DDA-v0.1.md) and [original bundle](spec/original) are preserved draft baselines. Do not silently amend them while retaining the same version label. A proposal should contain:

1. A stable proposal identifier and title, proposed profile/spec version and status (`draft`, `under discussion`, `accepted for a named revision`, `rejected` or `withdrawn`).
2. Problem, bounded use cases and non-goals; accountable domain/source ownership requirements.
3. Exact affected sections/artifacts and proposed normative wording, with schema/byte rules where relevant.
4. Compatibility, migration and refusal behavior; pinned bindings, semantic namespaces and extension rules.
5. Threat/failure analysis, authority and source-invariant implications, and deterministic/adversarial fixtures.
6. Alternatives, primary-source evidence for new external claims, open questions and a clearly identified decision record.

Acceptance requires an accountable maintainer decision recorded with rationale, reviewed text, target revision and evidence. The initial maintainer is [@kunal-y-abi](MAINTAINERS.md); this project workflow does not establish a standards committee. No contributor may claim certification or endorsement from a proposal's status.

A fuller analytical/insurance extension must define structured units/grain/time/identity, model/scenario inputs/results, objective and constraint semantics, uncertainty, chosen action commitments and reproducibility. It is not already defined by the core DecisionSpec strings and parameter digest. Use reviewed versioned extensions or a new profile; do not create a smaller incompatible schema using the original `APS-DDA/0.1` label.

## Implementation changes

Read [API_CONTRACT.md](API_CONTRACT.md), [architecture](docs/architecture.md), [status](docs/implementation-status.md) and the module-specific binding documentation first. Keep original artifacts intact, raw payloads in explicitly documented bindings, signatures detached, references exact and trust provisioning independent of requests/discovery. `PolicyDecision allow` is evidence, never a mutation grant. Sources must enforce exact plans, current permission/state, authority and their own invariants; unknown or partial effects must stay truthful.

Tests should cover the change and applicable negative cases: changed target/payload/evidence/policy/version after approval; wrong tenant/issuer/audience; expired/revoked authority; duplicates/conflicts; lost reply and original-key lookup; invalid receipts; partial failure/manual ownership; unsupported external hard invariants; and untrusted text that tries to broaden scope. A test-local fault hook must not become a public reset, SQL, arbitrary code or administration endpoint.

On Linux or Windows, run from the repository root using the SDK selected by `global.json`:

```sh
dotnet restore ApsDda.sln --locked-mode
dotnet build ApsDda.sln -c Release --no-restore
dotnet test ApsDda.sln -c Release --no-build --no-restore
dotnet pack src/ApsDda.Core/ApsDda.Core.csproj -c Release --no-build --no-restore -o artifacts/packages
dotnet pack src/ApsDda.Runtime/ApsDda.Runtime.csproj -c Release --no-build --no-restore -o artifacts/packages
```

Record actual commands/results and limitations, not just intended tests. Dependency changes require reviewed metadata/provenance, updated lockfiles and renewed audit/compatibility evidence; never suppress a vulnerability warning merely to obtain a clean build. Package changes should also be consumed by a clean project outside the checkout to exercise installed resources and public APIs. Update [CHANGELOG.md](CHANGELOG.md) and the status matrix when coverage changes. No particular platform's successful run proves untested OS support.

## AI assistance and review

New source development in this project was AI-assisted. Contributions using generated or AI-assisted material should disclose that fact and identify material provenance when relevant. Generated code, prompts, analysis and tests still require ordinary review, safe reproduction, dependency assessment and accountable acceptance. Do not describe AI drafting, automated code inspection or an AI-assisted review as human expert sign-off. Do not invent authorship, test counts, licenses, citations or results. For publication text, reuse unchanged checked citations or supply a primary source and the precise claim it supports.

## Review checklist

A review should confirm scope/version boundaries, faithful draft preservation, exact byte/authority/source checks, safe errors/logs, portable paths, no secrets or real customer data, provenance/notices, reproducible tests and honest gaps. Reviewers should distinguish a known pre-commit no-effect refusal from an ambiguous failure, and a source receipt from a prediction. Approval of a contribution is not approval of a live business action.
