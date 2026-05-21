# Security Policy

## Supported Versions

Security fixes are applied to the latest release of `orkyo-foundation`. We do not backport fixes to older major versions unless a critical severity warrants it.

| Version | Supported |
|---------|-----------|
| Latest  | ✅        |
| Older   | ❌        |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Report vulnerabilities privately via [GitHub's private vulnerability reporting](../../security/advisories/new) for this repository.

Include as much of the following as possible:

- Type of vulnerability (e.g. injection, broken access control, insecure deserialization).
- Location: file path, line number, endpoint, or relevant component.
- Steps to reproduce or a minimal proof of concept.
- Potential impact: what an attacker could achieve.

## Response Timeline

- **Acknowledgement:** within 2 business days.
- **Initial assessment:** within 5 business days.
- **Fix or mitigation:** timeline communicated after initial assessment, depending on severity.

## Scope

This policy covers the `orkyo-foundation` shared library. Vulnerabilities in the consuming products (`orkyo-saas`, `orkyo-community`) should be reported to those repositories instead.

Security-relevant components in this repo include:

- Authentication / Keycloak integration (`backend/src/Integrations/Keycloak/`)
- Authorisation and quota enforcement (`backend/src/Security/`)
- Migration framework (SQL execution, checksum validation)
- The Keycloak custom theme and image (`keycloak/`)
