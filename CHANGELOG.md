# Changelog

Notable changes to the shared **Orkyo Foundation** packages (`Orkyo.Foundation.*` on NuGet,
`@kymr10n/foundation` on npm). Because foundation has no runtime of its own, these entries describe
behaviour delivered to the consuming editions ([orkyo-community](https://github.com/Kymr10n/orkyo-community),
orkyo-saas). The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **`MigrationCliOptions` and `RunMigrationCliAsync(args, options)`.** The migrator's connection string,
  app version and lock timeout are a value object a product can build from its own configuration.
  The argv-only overload keeps reading the environment (`MigrationCliOptions.FromEnvironment()`), so
  existing migrators are unchanged.
- **`AddOrkyoValkey(configuration)`.** Registers the process's `IConnectionMultiplexer` from
  `VALKEY_CONNECTION` and fails at startup when it is missing; replaces the three identical lines in each
  product's `Program.cs`. The `IBreakGlassSessionStore` choice stays product-side.
- **`FoundationWorkerLoop` and `AddFoundationWorkerLoop(jobs)`.** The worker loop both editions carried as
  their own `BackgroundService` (run each job through `IWorkerJobCoordinator`, jittered sleep, error retry)
  lives in core. A product declares its `WorkerJob`s and keeps a few-line hosted service that awaits
  `RunAsync`. No hosting dependency in core.
- **`AddOrgContextFromHttpContext()` and `IOrgContextAccessor`.** One registration of the
  request-scoped `OrgContext` for both editions, replacing the per-product factory. `IOrgContextAccessor.Current`
  is `null` when no tenant is resolved (site-admin scope); resolving `OrgContext` there throws the typed
  `TenantContextUnavailableException` (mapped to a 500 with code `tenant_context_unavailable`) instead of
  returning a sentinel with an empty connection string. `TenantSettingsService` now takes the accessor.
- **User-definable resource types.** Tenants can create their own resource types (cars, cameras, …)
  alongside the built-in space/person/tool types, each with its own custom fields — text, number,
  boolean, date, or choice list, with optional required/min/max/pattern constraints. Field values are
  stored per resource and validated server-side against the type's definitions. Custom types flow
  through the existing machinery unchanged: criteria/capabilities, availability, off-times,
  assignments, groups, and sites all work for them.
  - New endpoints on `/api/resource-types`: create, update, delete (deactivates instead when
    resources still reference the type), plus `…/{id}/fields` CRUD for field definitions.
  - `CreateResourceRequest` / `UpdateResourceRequest` / `ResourceInfo` gained an optional `metadata`
    document. On update, a supplied document replaces the stored one; omitting it leaves values alone.
  - Settings gains a **Resource Types** tab (type builder + field editor); user-defined types get a
    generic management page at `/resources/:typeKey` and their own sidebar entry.
  - Built-in types stay protected — their identity and lifecycle are read-only — but they may gain
    custom fields, which is immediately useful for `tool`.
- **Per-type icons.** A resource type can carry a `lucide` icon name (`resource_types.icon`, settable
  on create and update), chosen from a curated picker in the type dialog and shown in the sidebar and
  the type list. Unknown or absent names fall back to a default, so tenant data and the frontend
  allow-list can drift safely.

### Changed
- Resource type keys are no longer validated against a hard-coded list. Criteria applicability accepts
  any type that exists in the database, so criteria can be attached to user-defined types.
  `ResourceTypeKeys` now documents the *system* types only.
- **Criterion applicability is no longer limited to spaces and people.** The "Applies to" checkboxes
  and the criteria filter tabs are generated from the resource types that exist, rather than from a
  hard-coded two-entry list — which had made it impossible to tag a criterion for `tool`, a seeded
  type since the resource-model migration. Labels now come from each type's display name, so they read
  "Space"/"Person"/"Tool" (and whatever tenants name their own types) instead of "Spaces"/"People".

## [0.6.14] — 2026-07-05

### Fixed
- **BFF auth redirects** now build their base from the configured app origin (`APP_BASE_URL`) instead of the
  host-only allow-list, so error/default redirects keep the port. Browser back/forward/refresh during
  sign-in no longer dead-ends on an unreachable URL — it lands on a graceful "session expired" page.
- **Scheduling board** re-anchors a stale view to today when the tab regains focus/visibility, so a
  long-lived tab no longer opens on a past week.

### Changed
- Transient auth URLs use `location.replace`, keeping the BFF login/return steps out of browser history.
- **People assignment** dialog gained an "Eligible only" filter and now surfaces error toasts when an
  assign/cancel/validate/reparent action fails, instead of failing silently.

## [0.6.13] — 2026-07-04

### Fixed
- Floorplan/audit read paths that could return **500** in the single-tenant composition.

### Changed
- Rate-limit policies are owned by foundation, so every edition enforces an identical, complete set.

---

Older releases are on the [Releases page](https://github.com/Kymr10n/orkyo-foundation/releases).

[0.6.14]: https://github.com/Kymr10n/orkyo-foundation/releases/tag/v0.6.14
[0.6.13]: https://github.com/Kymr10n/orkyo-foundation/releases/tag/v0.6.13
