# Changelog

Notable changes to the shared **Orkyo Foundation** packages (`Orkyo.Foundation.*` on NuGet,
`@kymr10n/foundation` on npm). Because foundation has no runtime of its own, these entries describe
behaviour delivered to the consuming editions ([orkyo-community](https://github.com/Kymr10n/orkyo-community),
orkyo-saas). The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Presets carry resource types, applicability, typed groups and sample resources (schema 1.1.0).**
  `PresetContents` gains `ResourceTypes` (a catalog key is activated from the catalog spec, any other
  key is an ad-hoc type) and `Resources` (named rooms, people, machines, tools with capabilities and
  memberships); `PresetCriterion.ResourceTypeKeys` writes `criterion_resource_types`;
  `PresetSpaceGroup.ResourceTypeKey` types a group explicitly. `PresetApplier` applies them in
  dependency order, adopts existing rows by key, code or name, and still never deletes. 1.0.0 files
  import unchanged. `PresetApplicationStats` gains `ResourceTypesActivated`, `ResourcesCreated`,
  `ResourcesUpdated`.
- **`office` starter template**, and the `manufacturing` and `office` preset files ship embedded in
  `Orkyo.Foundation.Core` — before this no `.preset.json` existed and every preset-backed key threw.
  Both are flagged `IncludesDemoData` because they carry named sample resources.

### Changed
- **`StarterTemplateService` applies a preset once.** When `preset_applications` already records the
  preset it logs and returns, so a product can call it at every start (Community's
  `ORKYO_STARTER_TEMPLATE`). A shipped file that fails `PresetValidator` throws with the errors.
- **Preset export emits resource types, criterion applicability and group typing**, fixing a
  round-trip loss (groups lost their type on import). Resources are not exported. The export declares
  version `1.1.0`; older apps refuse it as unsupported instead of dropping the new sections.
- `manufacturing` preset file renamed from `manufacturing-ch.preset.json`; its catalog description
  and `IncludesDemoData` now describe the small workshop it contains.
- **`MigrationScope` on `MigrationScript`.** A tenant-phase migration that must not run where control
  plane and tenant share one database declares `-- @scope: tenant-database-only` in the file;
  `FoundationMigrationModule.TenantDatabaseOnlyIds` marks the two legacy feedback migrations that predate
  the directive. A shared-database edition filters on `Scope` instead of on a filename substring.
  Additive: the record gains a defaulted last parameter.
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

### Fixed
- **Utilization reported every resource as "Off" at Month and Year scale.** Two independent causes,
  both a boolean where the truth was a fraction. The frontend asked whether any off-time range
  *overlapped* a bucket; at Month scale a bucket is a week and the page supplies one range per
  weekend day, so every bucket was non-working and every row read 0%. And `UtilizationService`
  zeroed a bucket's whole availability when any blocked period touched it, so one public holiday or
  one day of leave zeroed a whole week while its allocation was still computed normally — a bucket
  could report "Off" while carrying booked time. The frontend no longer reasons about off-time when
  deriving bucket status or the row average; the backend already folds weekends, working hours and
  blocked time into what it reports. Day and Week scale were unaffected and are unchanged.

### Changed
- **`effectiveAvailabilityPercent` is now a proportion, not a switch.** It was either the resource's
  base availability or zero. It is now the base scaled by the share of the bucket's working minutes
  that no blocked period removes, with overlapping periods merged so a day is subtracted once. Zero
  bookable minutes still reads zero. This is what `SchedulingEngine.WorkingMinutesInWindow`'s
  contract already asked of its callers. Readers that compare the field against zero get more
  accurate: a resource blocked for part of a window is no longer reported unavailable for all of it.
- **BREAKING — two list endpoints moved to the `PagedResult` envelope.** `GET /api/resources` and
  `GET /api/admin/feedback` now answer with `items` plus page metadata, replacing `{data,total}` and
  `{items,total}`. The feedback endpoint's query parameters change from `limit`/`offset` to
  `page`/`pageSize`, **and its maximum page size narrows from 200 to 100** (`PageRequest.MaxPageSize`);
  the shipped frontend never requested more than 50. The npm client tolerated the old shapes through
  `normalizePagedResult` from 0.26.0 on; that tolerance is removed (see Removed).
  `GET /api/search` is unchanged — it is a relevance-ranked union, where offset paging is meaningless,
  and only its row clamp converged.
- **The MCP `list_resources` result reports truncation explicitly.** The tool answers with at most
  `PageRequest.MaxUnpagedItems` rows and now returns `returned`, `totalMatching` and `truncated`
  instead of a single `count`. The old field carried the unpaged total beside a shorter list, which a
  model reads as a complete answer of that size.
- Resource type keys are no longer validated against a hard-coded list. Criteria applicability accepts
  any type that exists in the database, so criteria can be attached to user-defined types.
  `ResourceTypeKeys` now documents the *system* types only.
- **Criterion applicability is no longer limited to spaces and people.** The "Applies to" checkboxes
  and the criteria filter tabs are generated from the resource types that exist, rather than from a
  hard-coded two-entry list — which had made it impossible to tag a criterion for `tool`, a seeded
  type since the resource-model migration. Labels now come from each type's display name, so they read
  "Space"/"Person"/"Tool" (and whatever tenants name their own types) instead of "Spaces"/"People".
- **TestSupport: `TestConstants.TestBearerToken` carries the `admin` role.** It said `user` and was
  Admin only because the test host turned any unknown role into Admin; an unknown role is now no
  membership. A test that needs a plain member builds one with `BearerTokenForRole("user")`. The
  package's `Version` and `Authors` come from `Directory.Build.props` like every other package.
- **TestSupport: `MockKeycloakAdminService.UpdateEmailAsync`, `UpdateEmailCallCount` and
  `LastUpdateEmailCall` are removed** (no callers; `UpdateEmailForAccountAsync` and its counters stay).

### Removed
- **Declutter, backend (package API).** Deleted with zero callers in either product: `ICriteriaService`
  and `CriteriaService` (endpoints take `ICriteriaRepository`), `EffectiveConfig`, the `Tenant`, `Site`
  and `UserIdentity` models in `Auth.cs`, `PredecessorLogics`, `UpsertResourceCapabilityRequest` and
  its validator, `UpdateRequestRequirementRequest`, `EnumMapper.ToPlanningMode`,
  `KeycloakTokenProfile.HasRealmRole`, `ConfigurationValidator.LogConfigurationStatus`,
  `TenantCacheKeyPolicy.Canonicalize`, `LifecyclePolicyConstants.UserWarningReminderDays`,
  `ISessionService.GetSessionByUserIdAsync` (use `BuildSessionResponseAsync`),
  `CreateResourceRequestValidator.KnownAllocationModes` (use `AllocationModes.All`), and the
  `TokenPrefix`/`TokenScheme` constants on the two token auth handlers (the services own the scheme).
  Merged: `CreateRoutingRequest` and `UpdateRoutingRequest` into `SaveRoutingRequest`;
  `AddRequirementRequest` into `CreateRequestRequirementRequest` (identical shape). `AuditEventListFilter`
  is internal. `OrgContextExtensions.FromTenant` stays and delegates to `ToOrgContext`.
- **Declutter, seed package.** The random seed path is gone: `--floorplans` is no longer an option, the
  curated floorplan sites are the only path, and `manufacturing` is the only profile (an unknown profile
  exits with code 2). The `IScale` classes are one `ScaleSpec` record; `Sites`, `SpacesPerSite`,
  `ResourceGroups`, `Criteria`, `TimeWindowDays` and `Templates` are removed. Seeded person emails are
  now deterministic for a given `--seed`.
- **Declutter, npm package.** Removed with zero importers in either product: `contracts/index`,
  `contracts/roles`, `contracts/claims`, `contracts/errorCodes` (its `ValidationError` value was wrong)
  and `src/contracts`; the `components/ui/index` barrel and `components/ui/collapsible`;
  `domain/scheduling/working-time` and `duration-calculator`; `endpoint()` in `lib/core/api-client`;
  `normalizePagedResult` and `LegacyListShape`; `getAllRequests` (utilization-api);
  `deleteResourceAbsence`; the `FeedbackType` export of `feedback-admin-api` (import it from
  `feedback-api`); the `PlanCode` re-export of `AuthContext`; `Create`/`EditTemplateDialog` (use
  `TemplateDialogBase`). The `@radix-ui/react-collapsible` peer dependency is dropped.
  `useImportHandler` requires its options argument.

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
