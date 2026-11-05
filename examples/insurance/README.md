# Synthetic insurance response-readiness data

[`synthetic-data.json`](synthetic-data.json) is deliberately authored fake aggregate data for the offline [insurance demo](../../docs/insurance-demo.md). It contains no personal, customer, policy, medical, contact or real claims information. Names denote three imaginary regions, not geographic observations.

The four source-owned inputs cover one invented day: aggregate open items, deterministic added-item forecasts, internal team-day capacity, and synthetic budget credits. Counts, quantities and costs are canonical nonnegative integer **strings**, not floating-point money. Expected and upper forecasts are two stress inputs, not calibrated probabilities or confidence bounds. Budget credits are not a currency and cannot authorize spending.

The embedded dataset is loaded from the `ApsDda.Demo` assembly, so execution does not depend on the working directory or access to this file. Changing the file and rebuilding changes the numerical input; there is no remote read service. Each run generates independently signed source DataProducts/EvidenceBundles and retains content-addressed values in a temporary SQLite store. This is source-contract simulation, not evidence that an insurer publishes these products.

`claims.retrievedText` is explicitly untrusted descriptive prose. It is retained as evidence but is omitted from the numerical projection, policy parameters and builder template. It cannot provide component selection, authority, code or instructions. The malicious-data scenario changes only this prose and tests that the exact allocation remains the same.

Run from the repository root:

```sh
dotnet run --project examples/ApsDda.InsuranceDemo
dotnet run --project examples/ApsDda.InsuranceDemo -- --all
```

No keys, API credentials, LLM, Vercel application or live operational system are required. New source development was AI-assisted. The authored synthetic JSON data is Apache-2.0; this README is CC BY 4.0. Author: Kunal Yadav; see [LICENSING.md](../../LICENSING.md).
