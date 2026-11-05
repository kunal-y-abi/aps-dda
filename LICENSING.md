# Licensing and attribution

Copyright © 2026 Kunal Yadav for the project-authored material described below. Author: **Kunal Yadav**. Initial maintainer: [@kunal-y-abi](https://github.com/kunal-y-abi). Project: [APS-DDA](https://github.com/kunal-y-abi/aps-dda).

## Project-authored material

**Apache License 2.0** applies to new project implementation code, tests, tools, scripts and build/configuration files; the machine-readable APS-DDA schema (including its unchanged copies under `spec/original` and `src/ApsDda.Core/Schema`); and project-authored synthetic JSON fixtures/data, including `spec/original/fixtures` and `examples/insurance/synthetic-data.json`. The full license is in [LICENSE](LICENSE). This grant excludes the third-party materials identified below.

**Creative Commons Attribution 4.0 International (CC BY 4.0)** applies to project-authored prose: the whitepapers, proposed standard/specification text, README files, API contracts, guides and project policies. This includes the retained original prose under `docs/original` and `spec/original`, and the faithful public normative copy `spec/APS-DDA-v0.1.md`. It also applies to original project diagrams, their editable Mermaid sources, rendered images and corresponding PDF editions. The full legal code is in [LICENSES/CC-BY-4.0.txt](LICENSES/CC-BY-4.0.txt).

For prose and diagrams, retain attribution to Kunal Yadav, the copyright and license notices, a link to the project or material where reasonably practicable, and indicate modifications as required by CC BY 4.0. Historical technical content and normative requirements are retained; preparation headers have been generalised for this distribution. This external scope notice supplies the approved license. The machine-readable schema, fixtures and upstream files remain byte-for-byte unchanged. Embedded executable code examples remain Apache-2.0; third-party quotations and materials retain their own applicable rights and notices.

## Third-party exceptions and packages

The complete vendored canonicalizer directory `src/ApsDda.Core/ThirdParty/Jcs` and reference-vector directory `tests/ApsDda.Core.Tests/JcsVectors` retain their existing source, data, copyright, provenance and license notices. The project license does not replace them. The pinned canonicalizer includes Apache-2.0, MPL-2.0, V8 BSD-style and David M. Gay/Lucent terms. In particular, `NumberFastDToABuilder.cs` and `NumberDToA.cs` are MPL-2.0-covered source; the latter also contains the Lucent notice. Other helpers retain their V8 notices. See the preserved [canonicalizer provenance](src/ApsDda.Core/ThirdParty/Jcs/PROVENANCE.md) and [vector provenance](tests/ApsDda.Core.Tests/JcsVectors/PROVENANCE.md). Any preserved pre-approval licensing sentence in those provenance records is superseded only for project-authored material by this scope notice, not by altering upstream terms.

`ApsDda.Core` binaries contain the MPL-covered files, so the combined package must not be described as solely Apache-2.0. All three reusable packages carry [package-license.md](package-license.md), the full root Apache license and scope notice, and the CC BY 4.0 legal code for their included documentation. Core additionally distributes the eight unchanged canonicalizer source files and their original full licenses/notices under `third-party/jcs/`; this is the corresponding covered source available to binary recipients. Its pinned public upstream source is also identified in the package license. Managed/native dependencies are independently licensed; MIT and SQLitePCLRaw/SQLite notices are retained in the package license, not replaced by this project's license.

## Patent and endorsement boundary

Apache-2.0 includes only the contributor patent grant defined in its Section 3. CC BY 4.0 does not license patent or trademark rights. No broader patent clearance, third-party patent promise, standards ratification, software certification or endorsement is made. This is a license-scope summary, not legal advice.
