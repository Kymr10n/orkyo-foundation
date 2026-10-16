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

## Dependencies

A vulnerability in a dependency is handled by where it can run.

- **Reachable in production.** The package is in a runtime dependency set: `npm audit --omit=dev` reports it, `dotnet list package --vulnerable --include-transitive` reports it in a non-test project, or the Trivy scan reports it in a published image. A fix is merged within 7 days. If no upstream fix exists, a `.trivyignore` entry with a review date not more than 14 days ahead records the exception.
- **Everything else.** Development-only packages, build-stage images and test projects are updated in the weekly Dependabot cycle. Minor and patch updates merge on their own when CI is green. Majors wait for a review.
- **Upstream-blocked majors** are listed as ignores in `.github/dependabot.yml`, with the blocker named. The monthly freshness report issue lists them again so they are re-checked.

## Scope

This policy covers the `orkyo-foundation` shared library. Vulnerabilities in the consuming products (`orkyo-saas`, `orkyo-community`) should be reported to those repositories instead.

Security-relevant components in this repo include:

- Authentication / Keycloak integration (`backend/src/Integrations/Keycloak/`)
- Authorisation and quota enforcement (`backend/src/Security/`)
- Migration framework (SQL execution, checksum validation)
- The Keycloak custom theme and image (`keycloak/`)
