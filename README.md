<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset=".github/orkyo-logo-dark.png">
    <img src=".github/orkyo-logo-light.png" alt="Orkyo" width="120">
  </picture>
</p>

<h3 align="center">Orkyo Foundation</h3>
<p align="center">The shared domain layer for Orkyo — consumed by both <a href="https://github.com/Kymr10n/orkyo-community">orkyo-community</a> (single-org) and orkyo-saas (multi-tenant).</p>

<p align="center">
  <a href="https://github.com/Kymr10n/orkyo-foundation/actions/workflows/release-ci.yml"><img src="https://github.com/Kymr10n/orkyo-foundation/actions/workflows/release-ci.yml/badge.svg?branch=main" alt="CI"></a>
  <a href="https://github.com/Kymr10n/orkyo-foundation/releases/latest"><img src="https://img.shields.io/github/v/release/Kymr10n/orkyo-foundation?sort=semver" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/license-AGPL--3.0-blue" alt="AGPL-3.0">
  <img src="https://img.shields.io/badge/.NET-10-purple" alt=".NET 10">
  <img src="https://img.shields.io/badge/React-19-blue" alt="React 19">
</p>

<p align="center">
  <a href="https://github.com/Kymr10n/orkyo-foundation/issues">Issues</a> ·
  <a href="https://github.com/Kymr10n/orkyo-foundation/discussions">Discussions</a> ·
  <a href=".github/SECURITY.md">Security</a> ·
  <a href=".github/SUPPORT.md">Support</a>
</p>

---

Foundation is the feature core shared by the two Orkyo product editions. It contains **no runtime wiring**
— no `Program.cs`, no DI registration, no middleware. The product repos supply the composition and the
tenant/hosting adapters; foundation supplies the behaviour.

If you're here to **use or self-host Orkyo**, start at [orkyo-community](https://github.com/Kymr10n/orkyo-community).
This repo is the shared library those editions build on.

## What's included

- **Domain models** — spaces, requests, criteria, scheduling, feedback, templates, sites, announcements
- **Service interfaces** — `ISpaceService`, `IRequestService`, `ISchedulingService`, etc.
- **Repository interfaces + implementations** — Postgres-backed via Npgsql
- **OrgContext + IOrgContextAccessor** — pluggable org identity (injected by product repos)
- **Validators** — FluentValidation rules for all request/command types
- **Scheduler engine** — constraint-based space assignment
- **Shared frontend components** — domain React components, hooks, pages, and routing infrastructure (see `frontend/` and [ARCHITECTURE.md](frontend/ARCHITECTURE.md))
- **Migrations** — domain schema published as `Orkyo.Foundation.Migrations`

## How it's consumed

Foundation is published to **GitHub Packages** and pinned by the product repos:

- **Backend** — NuGet packages `Orkyo.Foundation.*` (`Core`, `Web`, `Migrations`, …)
- **Frontend** — npm package `@kymr10n/foundation`

`orkyo-community` and `orkyo-saas` pin an exact foundation version and bump it through their release
pipelines when a new version is published. Neither runs foundation directly — each composes it with its own
runtime wiring: Community with single-tenant adapters, SaaS with multi-tenant machinery. Package versions
follow SemVer; consumer-facing changes are recorded in [CHANGELOG.md](CHANGELOG.md).

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

See [CONTRIBUTING.md](.github/CONTRIBUTING.md) for the full placement workflow and the downstream-impact checklist.

## Contributing & support

Contributions follow [CONTRIBUTING.md](.github/CONTRIBUTING.md) and the [Code of Conduct](.github/CODE_OF_CONDUCT.md). Questions → [Discussions](https://github.com/Kymr10n/orkyo-foundation/discussions); bugs → [Issues](https://github.com/Kymr10n/orkyo-foundation/issues); vulnerabilities → [SECURITY.md](.github/SECURITY.md); help routing → [SUPPORT.md](.github/SUPPORT.md).

## License

[GNU Affero General Public License v3.0](LICENSE) — same licence as the Community edition it powers.
