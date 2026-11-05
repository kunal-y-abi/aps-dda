# Offline insurance response-readiness console

Requires the repository's pinned .NET 10 SDK. From the repository root:

```sh
dotnet run --project examples/ApsDda.InsuranceDemo
dotnet run --project examples/ApsDda.InsuranceDemo -- --plan min-backlog
dotnet run --project examples/ApsDda.InsuranceDemo -- --scenario boundedpolicy
dotnet run --project examples/ApsDda.InsuranceDemo -- --all
dotnet run --project examples/ApsDda.InsuranceDemo -- --scenario happy --plan balanced --interactive
dotnet test tests/ApsDda.Demo.Tests -c Release
```

These commands also work in PowerShell; a single `--` separates `dotnet run` options from application options. No environment variables, external services or keys are needed. The data is embedded, and keys are generated in process and never written to disk. SQLite state is in a unique operating-system temporary directory and deleted on normal disposal; abrupt process termination can leave temporary state for the local operator to remove.

The default `happy` scenario uses a **synthetic, administrator-configured manager identity fixture**, not a live human identity provider. It issues/admit/consumes an original-schema exact-plan Approval through the real runtime. `--interactive` additionally requires a person at the console to inspect alternatives, exact plan and normalized payloads and type the exact plan digest; a mismatch issues no Approval and performs no source operation. This local interaction is not production authentication.

`boundedpolicy` instead admits a real source-scoped PolicyGrant for **one internal notification draft**, quantity 1, one operation per rolling minute, four-minute authority window. It reserves no regional capacity, spends nothing and sends nothing. A PolicyDecision with outcome `allow` alone is never authority.

The JSON report records actual workflow/step states, references/digests, source state before/after, operation counts and original invocation/receipt references. Fault scenarios succeed as *test demonstrations* even when the workflow refuses or needs manual resolution. Check reported workflow/error fields; CLI exit 0 does not mean a business action succeeded. Unsupported names/options return a safe error and nonzero exit.

Optionally export inspected artifacts outside the checkout:

```sh
dotnet run --project examples/ApsDda.InsuranceDemo -- --scenario happy --export /tmp/aps-dda-demo-export
```

PowerShell example: `--export "$env:TEMP/aps-dda-demo-export"`. Exports contain `artifact-chain.json`, `bindings.json` (synthetic retained values, exact payloads and **public** keys), `report.json`, and `builder-template.txt`. No private key or SQLite database is exported. Tampered-scenario exports intentionally contain an invalid signed plan for refusal inspection. Public keys reproduce only the local fixture; never trust an arbitrary export as an external authority/trust root. Keep generated files out of source control.

See [the complete model, scenarios and limitations](../../docs/insurance-demo.md), [demo API contract](../../src/ApsDda.Demo/API_CONTRACT.md), and [original SDK binding](../../API_CONTRACT.md). New source development was AI-assisted; selected-profile experimental implementation, not conformance or security certification.
