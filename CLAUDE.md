# Claude guide — orkyo-foundation

## What this repo is

The shared domain layer consumed by **both** product repos (`orkyo-saas`, `orkyo-community`). Contains NO runtime wiring — no `Program.cs`, no DI registration, no middleware. Published to GitHub Packages as `Orkyo.Foundation` (NuGet) and `@kymr10n/foundation` (npm).

Also owns the Keycloak image (`ghcr.io/kymr10n/keycloak:26.6-orkyo-<version>`) including the Orkyo theme.

## Placement rule

> **New or touched Orkyo code belongs in orkyo-foundation unless the behavior has NO analogue in single-tenant Community.**

Multi-tenancy or a `tenantId` parameter alone is NOT a reason to keep code in SaaS. The `OrgContext` abstraction lets the same code work in both editions.

## Conventions to follow

- **No runtime wiring** in this repo. Products do the wiring.
- **Public API surface** must stay backward-compatible within a major version. Breaking changes require a major bump + coordinated downstream PRs in saas + community.
- **Tests live where the code lives.** Service tests for foundation code stay here; integration tests against product wiring stay in the product repo.
- **`dotnet format`** must pass before push (enforced by `.githooks/pre-push`, to be replaced by `pre-commit`).

## Before merging changes that touch the public API

When you alter the signature of a service registered in `FoundationWebApplicationFactory`, or rename/remove a `Map*Endpoints()` function, run:

```
./scripts/test-downstream.sh
```

This runs the Foundation, Community, and SaaS test suites in sequence. Foundation tests alone won't see downstream DI graph or route-registration regressions — Community/SaaS `Program.cs` is where those bugs land, and their test suites use `WebApplicationFactory<Program>` which boots the real wiring.

## Where things live

- Backend domain: `backend/src/`
- Backend shared (config keys, environment names): `backend/shared/`
- Migration framework (abstractions / runtime / migrations-foundation): `backend/migration-abstractions/`, `backend/migrator-runtime/`, `backend/migrations-foundation/`
- Tests: `backend/tests/`
- Frontend (npm package): `frontend/`
- Keycloak image + theme: `keycloak/`

## What to read first

1. `README.md` — the public-facing structure
2. `frontend/ARCHITECTURE.md` — rendering split + multi-product routing
3. `.github/workflows/release-ci.yml` — release & dispatch model
4. `orkyo-infra/docs/structural-hardening-2026-05.md` — current cross-repo hardening plan

## Things not to do

- Don't move code out of foundation without checking the placement rule first.
- Don't add runtime services (Serilog sinks, Prometheus exporters, etc.) here; expose them as opt-in helpers that products call.
- Don't break backward compatibility silently. Bump major + open downstream PRs.
- Don't modify `.githooks/pre-push` ad-hoc; the broader plan replaces it with `pre-commit`.
