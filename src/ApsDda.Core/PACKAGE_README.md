# ApsDda.Core — experimental reference SDK

.NET 10/C# implementation of selected APS-DDA v0.1 parsing, original schema validation, RFC 8785 canonicalization, integrity, detached P-256 signatures and explicit in-process trust checks. This is not complete conformance, certification, external security review or proof of data truth.

Author: Kunal Yadav; maintainer: @kunal-y-abi. Source and accompanying development/testing were prepared with AI assistance, not human peer review. New code and schema are Apache-2.0; authored documentation is CC BY 4.0. This combined binary is **not solely Apache-2.0**: it includes unchanged MPL-2.0-covered canonicalizer files and other Apache-2.0, V8 BSD-style and Lucent notices. The eight upstream sources and full notices are available under `third-party/jcs` in this package. Read `package-license.md` and `LICENSING.md` for scope, full notices and MIT dependency terms. Source: https://github.com/kunal-y-abi/aps-dda .

The authoritative original schema is embedded; it does not require a relative checkout path or network access. Read the repository's `API_CONTRACT.md` for signature byte binding, strict numeric profile, reference matching and trust requirements. No keys, credentials, source adapters or execution authority are supplied by this package.
