# Changelog

Notable changes to the shared **Orkyo Foundation** packages (`Orkyo.Foundation.*` on NuGet,
`@kymr10n/foundation` on npm). Because foundation has no runtime of its own, these entries describe
behaviour delivered to the consuming editions ([orkyo-community](https://github.com/Kymr10n/orkyo-community),
orkyo-saas). The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
