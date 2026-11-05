#!/usr/bin/env bash
# AI-assisted source development. Finite local checks; no Python, Git writes, publish or Docker.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
command -v dotnet >/dev/null || { printf '%s\n' 'Install the SDK selected by global.json and put dotnet on PATH.' >&2; exit 1; }
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore ApsDda.sln --locked-mode
dotnet build ApsDda.sln -c Release --no-restore -warnaserror
dotnet test ApsDda.sln -c Release --no-build --no-restore --logger trx --results-directory artifacts/test-results
dotnet run --project examples/ApsDda.InsuranceDemo -c Release --no-build --no-restore -- --all --export artifacts/demo-runs
for library in Core Runtime AspNetCore; do
  dotnet pack "src/ApsDda.$library/ApsDda.$library.csproj" -c Release --no-build --no-restore -o artifacts/packages
done
dotnet run --project tools/ApsDda.Verify -c Release --no-build --no-restore -- "$root" "$root/artifacts/packages"
printf '%s\n' 'PASS all local checks; reusable libraries are under artifacts/packages (not published).'
