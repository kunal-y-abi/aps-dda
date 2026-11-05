# Canonicalization test data provenance

The six input/output JSON pairs and `README.upstream.md` are copied unchanged from cyberphone/json-canonicalization commit `32a48195b9b20fe67b1795e39e4509f14d860b45`, `testdata/`. The original repository license is retained in `LICENSE.upstream`; the full Apache license is available at `../../../src/ApsDda.Core/ThirdParty/Jcs/LICENSE.Apache-2.0.txt`.

`numgen-10000.js` is the same commit's `testdata/numgen.js` with only the line count changed from 100 million to 10,000 and output filename/comments changed from `es6testfile100m.txt` to `es6testfile10000.txt`. Its original WebPKI/Apache-2.0 header is retained. It generates test data only; it is not SDK/runtime logic and is not required by `dotnet test`.

The generated V8/Node.js expected number corpus `es6testfile10000.txt` has SHA-256 `b9f7a8e75ef22a835685a52ccba7f7d6bdc99e34b010992cbc5864cd12be6892`, matching the pinned upstream README's 10,000-line published hash. Tests verify that hash, line count and all 10,000 C# reference formatter outputs. This does not claim the 100-million-vector corpus was run.
