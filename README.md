# Orkyo Foundation

The shared domain layer for Orkyo — consumed by both **orkyo-community** (single-org) and **orkyo-saas** (multi-tenant).

Contains NO runtime wiring. No `Program.cs`, no DI registration, no middleware.

## What's included

- **Domain models** — spaces, requests, criteria, scheduling, feedback, templates, sites, announcements
- **Service interfaces** — `ISpaceService`, `IRequestService`, `ISchedulingService`, etc.
- **Repository interfaces + implementations** — Postgres-backed via Npgsql
- **OrgContext + IOrgContextAccessor** — pluggable org identity (injected by product repos)
- **Validators** — FluentValidation rules for all request/command types
- **Scheduler engine** — constraint-based space assignment
- **Shared frontend components** — domain React components, hooks, pages, and routing infrastructure (see `frontend/` and [ARCHITECTURE.md](frontend/ARCHITECTURE.md))
- **Migrations** — domain schema published as `Orkyo.Foundation.Migrations`

## Structure

```
Orkyo.Foundation.slnx   ← Solution at repo root
backend/
  src/           ← Orkyo.Foundation (domain library)
  tests/         ← Foundation tests (unit + integration)
  migrations/    ← Orkyo.Migrations (domain schema)
  shared/        ← Orkyo.Shared (config keys, environment names)
frontend/
  src/           ← Shared domain components and hooks
  contracts/     ← API types (no tenant headers)
  vitest.config.ts
```

Foundation-frontend intentionally has no `vite.config.ts` — it is a contracts + test-harness
package, not a runtime app. `vite` is only used via `vitest` for the test runner.

## Usage

### As a NuGet package (backend)

```xml
<PackageReference Include="Orkyo.Foundation" Version="0.*" />
```

### As an npm package (frontend)

```bash
npm install @orkyo/foundation
```

## Building

```bash
dotnet build Orkyo.Foundation.slnx
dotnet test Orkyo.Foundation.slnx
```

**Integration tests require Docker.** The `backend/tests/Integration/*` tests use [Testcontainers](https://dotnet.testcontainers.org/) to boot a real Postgres container and apply the foundation-owned migrations against it. If your host doesn't have Docker (or the daemon isn't running), scope the run to unit tests only:

```bash
dotnet test Orkyo.Foundation.slnx --filter "FullyQualifiedName!~Integration"
```

## Placement rule

`orkyo-foundation` holds every behavior that applies identically to both **orkyo-saas** (multi-tenant hosted) and **orkyo-community** (single-tenant standalone). Any new code whose behavior has no Community analogue — cross-tenant admin, tenant provisioning, break-glass impersonation, service tiers, subdomain→tenant resolution — belongs in `orkyo-saas` instead.

The full rationale, file-by-file audit table, and migration history live in [orkyo-saas/requirements/orkyo-platform-extraction-plan.md](https://github.com/Kymr10n/orkyo-saas/blob/main/requirements/orkyo-platform-extraction-plan.md) (see the "Backend placement audit" table in slice 67 and the final classification in slice 73).

## License

MIT — see [LICENSE](LICENSE).
