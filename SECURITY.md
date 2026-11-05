# Security policy

## Status and support scope

This project is an experimental reference implementation of selected requirements of proposed APS-DDA v0.1. It is not a production identity/authority service, externally security-reviewed standard, certification, or guarantee of safe decisions. The current preview line is `0.1.0-preview.1`; no supported production version, security-update service, response SLA or certification program is declared.

New source development was AI-assisted. Passing automated tests or AI-assisted code inspection is not a human expert assessment. Read [implementation status](docs/implementation-status.md), [architecture](docs/architecture.md), [runtime binding](docs/reference-binding.md) and [API_CONTRACT.md](API_CONTRACT.md) for actual behavior and deferred controls.

## Reporting a suspected vulnerability

**GitHub private vulnerability reporting is enabled.** Use [Report a vulnerability](https://github.com/kunal-y-abi/aps-dda/security/advisories/new) to send a private report to the repository maintainer, [@kunal-y-abi](https://github.com/kunal-y-abi). Confirm the repository is `kunal-y-abi/aps-dda` before sending sensitive details. This channel is for suspected software vulnerabilities, not ordinary conduct complaints. No email address, staffed security team, response timeline or bug-bounty program is implied.

Do not put exploitable details, production targets, credentials, private keys, customer data or protected evidence in a public issue. If GitHub private reporting is unavailable, request a private contact from the verified repository owner using a non-sensitive message that does not disclose the exploit. Confirm the recipient and channel before sharing sensitive information. A public report may describe a general affected area and ask for the reporting route, but must not include reproduction details that would expose others. An immediate real-world incident should be handled through the affected organization's own incident-response process, not by assuming this repository can respond.

In the private report, provide the affected revision/package, component and environment; a synthetic minimal reproduction; observed versus expected behavior; impact and preconditions; and safe test output. Explain whether the issue permits a new effect, exposes data, corrupts plan/authority binding, or leaves outcome ambiguity. Keep sensitive material out of routine logs. Do not test against third-party systems or real policyholders without their authorization.

Maintainers should acknowledge, reproduce, assess affected versions, agree a coordinated disclosure plan with the reporter where appropriate, and record the eventual fix/advisory and residual limitations. These are intended handling steps, not promised deadlines or a staffed response claim.

## Security-relevant boundaries

Authentication, source permission, policy evaluation and business authority are separate. A request identity string, self-published manifest, policy `allow`, UI boolean or model response is not a grant. Administrator-configured issuer/key/role/profile/contract pins are not derived from clients or discovery. Approval binds an authenticated independent approver and exact plan/policy/steps/time window to one durable workflow before effects.

The source enforces its own current permission, authority, expected state and local invariants. A digest protects declared bytes, not truth or entitlement. A local version guards only the state it covers. Required external hard invariants without an enforcing source/coordination mechanism must be refused. All source results require actual source signatures, exact invocation/operation links and verified postconditions before completion.

Ambiguous commits/replies remain unknown. Reconcile the original invocation and key; do not reset a workflow or generate a new key to hide uncertainty. Partial success requires declared source recovery or a named manual owner, not a claim of global rollback. Cancellation cannot undo an existing effect.

Store retention, seeding, current-policy, revocation and trust/principal registration APIs are in-process administrator operations, not public endpoints. Hosts must protect reads as well as writes. No fault/reset, arbitrary SQL/client code, request-loaded plugin or production default-admin endpoint is acceptable. Use synthetic data in examples and test-local fault injection only.

## Deferred production controls

Production deployments need HTTPS and an appropriate authenticated identity provider, protected key custody/rotation, trusted provisioning, tenant/source authorization, encrypted storage where required, retention/deletion and audit-access policy, status/revocation propagation, safe source adapters, operational migration/recovery, monitoring and load testing. The local Core/Runtime does not implement all of these. Principal/trust directories must be independently reprovisioned after restart; local durable revocation/policy rows are not a distributed revocation guarantee.

SQLite data and protected values are not automatically encrypted or deleted. Sample native P-256 keys are generated/loaded at runtime, not a key-management solution. Localhost development exceptions must not be promoted to production. Real customer decisions, payments, notices, claims adjudication and fraud/entitlement determinations are outside the synthetic source commands.

## Dependency and disclosure discipline

Keep package versions/lockfiles and vendored upstream notices/provenance intact. Re-run dependency audit and compatibility tests for each release; a previous clean check does not establish permanent safety. Do not claim that every vendored source has a single project license. Do not commit signing keys, bearer tokens, sensitive logs, signed private asset URLs or real customer data. If a secret is exposed, remove access and rotate it through its owner; deletion from a current file alone does not revoke it.

Security fixes must add regression tests and explain which threat/failure boundary was addressed and which remains. No legal-compliance, universal security or certified-conformance claim should be inferred from a fix, audit result or test count.
