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
- **Shared frontend components** — domain React components, hooks, and pages (see `frontend/`)
- **Migrations** — domain schema published as `Orkyo.Foundation.Migrations`

## Structure

```
backend/
  src/           ← Orkyo.Foundation (domain library)
  tests/         ← Foundation unit tests (262 tests)
  migrations/    ← Orkyo.Migrations (domain schema)
  shared/        ← Orkyo.Shared (config keys, environment names)
  OrkyoFoundation.slnx
frontend/
  src/           ← Shared domain components and hooks
  contracts/     ← API types (no tenant headers)
```

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
cd backend
dotnet build OrkyoFoundation.slnx
dotnet test tests/Orkyo.Foundation.Tests.csproj
```

## License

MIT — see [LICENSE](LICENSE).
