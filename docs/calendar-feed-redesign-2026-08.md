# Calendar feed: correct tenant URL, move off the Account page, gate to paid plans (2026-08)

The change is implemented and verified on `claude/calendar-feed-tenant-url`.
Open items are listed under [Follow-ups](#follow-ups); none of them block the
branch.

Three things happened here, in order: the feed URL was addressed at the tenant
instead of the apex, the panel moved from the Account page to the schedule, and
the feature became **Professional/Enterprise-only** in SaaS (Community keeps it,
as it keeps everything). The gating work is in
[Tier gating](#tier-gating-professionalenterprise) and spans two repos.

## Why

Commit `45a11b7` added per-user calendar subscriptions: create a token, get a
`.ics` feed URL for Outlook/Google/Apple. Review found two problems.

### 1. The feed 404s for every SaaS tenant

`CalendarFeedEndpoints.cs` built the feed URL from global `APP_BASE_URL`
(`https://orkyo.com` in prod) and never the tenant's subdomain. The apex carries
no slug, `SubdomainResolutionStrategy.ExtractSlugFromHost` returns null for it
by design, and `TenantMiddleware` (orkyo-saas) requires a resolvable slug on
this route — so every subscription created in prod was dead on arrival. The
same root cause put the wrong domain in the `.ics` `PRODID`.

Traced end to end: nginx's apex block does proxy `/api` to the backend
(`app-routing.conf`, `Host: orkyo.com`), so the request arrives and is rejected
by tenant resolution rather than by nginx.

### 2. Wrong mental model

The panel lived on the Account page, implying a personal calendar. It isn't:
`CalendarFeedService.GetEventsAsync` returns a **site's** scheduled requests
with no per-user filtering at all. The per-user token gates *access and
revocation*, not content. So the feed belongs with the site's schedule, not the
user's profile.

## What changed

### Backend

**`backend/shared/TenantHostnamePolicy.cs`** — two additions on top of the
existing `BuildHostname`:

- `BuildOrigin(appBaseUrl, baseDomain, subdomainPrefix, slug)` — the tenant's
  own origin, falling back to `appBaseUrl` unchanged when `baseDomain` is unset.
  That fallback is what keeps community and local dev correct with no
  product-specific branching.
- `BuildHost(...)` — the same answer as a bare host, for the `.ics` `PRODID`,
  which wants a host and not an origin.

`BuildHostname` now throws on an empty slug. `ICurrentTenant.TenantSlug` reads
as `""` when no tenant was resolved, and the previous behaviour was to emit
`.orkyo.com` — a plausible-looking address resolving to nothing, i.e. exactly
the class of bug this change exists to fix. Fail fast instead.

**Duplication to retire (tracked, not optional).** `BuildOrigin` is currently
identical to orkyo-saas's `TenantLifecycleLinkPolicy.BuildWorkspaceUrl`. It was
added here because `Orkyo.Shared` is consumed by SaaS as a versioned package, so
SaaS cannot call it until the next `OrkyoFoundationVersion` bump — but that bump
is unconditional (see [Follow-ups](#follow-ups) 6), so the duplication is not
buying deferred cost, it is just duplication with a scheduled end date. On the
bump: delete `TenantLifecycleLinkPolicy`, point `TenantLifecycleService` at
`TenantHostnamePolicy.BuildOrigin`, drop the duplicate test class.

`BffAuthEndpoints.ResolveTenantReturnTo` builds the same origin shape a third
time, but its fallback is `returnTo` rather than the app base URL, so it stays
on the `BuildHostname` primitive deliberately.

**`backend/src/Endpoints/CalendarFeedEndpoints.cs`** — private `TenantOrigin()`
/ `TenantHost()` helpers read `ICurrentTenant.TenantSlug` plus
`ConfigKeys.TenantResolution{BaseDomain,SubdomainPrefix}` (indexer, null-safe,
unset in community). The POST create handler uses the origin for `FeedUrl`; the
GET feed handler uses the host for the `.ics` `PRODID`. `ICurrentTenant` was
already DI-registered in both products — no new wiring.

No attribute changes: the GET route never had `[SkipTenantResolution]`. Once
the URL is right the calendar client lands on `{slug}.orkyo.com` and resolves
through the normal path.

### Frontend

The export registry (`ui-actions-store.ts` + `useExportHandler`) is built
exclusively around synchronous file downloads — `ImportExportDialog` shows a
read-only filename and closes on submit. A subscription is a conversation
(create → reveal once → copy → revoke), so it was **not** shoehorned into that
dialog. Instead the *registration* idea was reused via a thinner sibling
registry, with no tick/trigger: the new dialog is self-contained, so there is
nothing to relay back to the page.

- **`store/ui-actions-store.ts`** — `CalendarFeedCapability`,
  `calendarFeedRegistry`, `register`/`unregisterCalendarFeed`,
  `selectActiveCalendarFeed`. Mirrors the export registry's shape, including
  last-registered-wins. The "last entry wins" walk both selectors need is a
  shared `lastEntry()` rather than a second copy of the loop.
- **`hooks/useImportExport.ts`** — `useCalendarFeedHandler(context, offer)`.
  Register on mount, unregister on unmount; presence is the whole contract. It
  takes the store's `CalendarFeedCapability` directly — the offer and the
  registered capability are the same shape, so there is no `CalendarFeedOffer`
  twin. (`ExportOffer`/`ExportCapability` *are* such a pair; pre-existing, left
  alone.)
- **`components/system/CalendarFeedDialog.tsx`** (new) — adapted from the old
  Account panel, and filed beside `ImportExportDialog`: the TopBar opens it
  globally and it takes its label/description from the registration, so it is
  not a utilization-specific component. Built on `ScaffoldDialog` (the
  sanctioned shell — a hand-rolled `Dialog`/`DialogContent` is an ESLint G1
  error), with `h-auto` restoring content-sizing for a dialog this short.

  Reads `selectedSiteId` from `app-store` and passes it into
  `createCalendarSubscription` (the API supported `siteId` all along; the old
  panel never sent it). **Create is disabled without a selected site**: a
  site-less token makes `GetEventsAsync(null, …)` serve *every* site's
  schedule, which is not what the dialog offers. The list shows the selected
  site's subscriptions plus any site-less ones — those pre-date site scoping
  and this dialog is the only place left to revoke them, so filtering them out
  would strand them. They are labelled "All sites".

  Clears the revealed URL on close, so reopening can't re-show an address the
  user already had their one look at; the copy-feedback timer is cleared on
  unmount.
- **`components/layout/TopBar.tsx`** — `CalendarPlus` icon button beside
  Export (tablet+), a "Subscribe in calendar" overflow item (phone), plus
  lazy-loaded dialog. Gated on the registry exactly like Export.
- **`pages/UtilizationPage.tsx`** — registers `'utilization'` next to its
  existing `useExportHandler`. Page-level, not Calendar-tab-level: the feed
  serves the whole site schedule regardless of the active tab.
- **`pages/AccountPage.tsx`** — "Calendar" tab removed;
  `components/account/CalendarSubscriptions.{tsx,test.tsx}` deleted.

## Tier gating (Professional/Enterprise)

Calendar subscriptions are a paid-plan feature in SaaS. Community is unaffected:
it never overrides `IFeatureGate`, so `AllFeaturesEnabledGate` keeps everything
on, and it passes no plans-page href, so the upsell would render without a CTA.

**Foundation**

- `FeatureKeys.CalendarFeed = "calendar_feed_enabled"` in
  `backend/core/Security/Features/IFeatureGate.cs`.
- `CalendarFeedEndpoints.cs` — `POST /api/calendar/subscriptions` returns
  **402 `upgrade_required`**, checked ahead of the validator because it is
  authorization, not shape. It goes through a new
  `ErrorResponses.UpgradeRequired(...)` + `ApiErrorCodes.UpgradeRequired`, not
  the hand-rolled `new { error = … }` body `ReportingTokenEndpoints` uses:
  `ErrorShapeContractTests` exempts the reporting surface only because it is a
  versioned contract for external BI tools, and this endpoint is
  frontend-facing. (The ratchet caught this — the first cut copied reporting.)
  The anonymous
  `.ics` GET returns **404**, not 402: that route deliberately gives one answer
  to everything it refuses, and an upgrade prompt would confirm the token
  exists. It works without a principal because `ICurrentTenant` is
  subdomain-resolved there — which is why the route must stay off
  `[SkipTenantResolution]`.
- **List and revoke stay ungated.** A tenant that drops off a paid plan must
  still be able to see and revoke the tokens it already handed out.
- `frontend/src/hooks/useCalendarFeedAvailable.ts` mirrors
  `useReportingApiAvailable` (site admin / break-glass bypass, else
  `isProfessionalOrAbove`). Presentation only — the server is the enforcement.
- `CalendarFeedDialog` swaps the create form for `<FeatureUpsell>` when
  unavailable, and **keeps the subscription list**, matching the ungated
  list/revoke above. The TopBar button stays enabled so the upsell is reachable
  (the plan's alternative — hiding the button — was rejected: a disabled button
  is indistinguishable from "this page has no schedule").
- The upsell's CTA reuses `TenantApp`'s existing
  `reportingApiUnavailableRedirectTo` prop rather than adding a third
  prop carrying the same plans-page href. That prop already serves both the
  Reporting API and the audit log; its docstring now says so. Consequence: **no
  SaaS frontend change was needed.** The name is now wrong for three features —
  a rename is a candidate follow-up, but it is a published-package prop, so not
  something to change in passing.

**SaaS** — `requirements/orkyo-plan-matrix.json` gains a `calendar_feed_enabled`
dimension (free `false`, professional/enterprise `true`), `plan-data.ts` is
regenerated, and `frontend/marketing/pricing.html` gains the matching row by
hand. The seed ships as **new migration
`2200.saas.calendar_feed_quota.sql`** — applied migrations are checksum-immutable
and never re-run, so 2120 must not be edited for a new key.

**Sequencing is load-bearing.** `QuotaService` boolean entitlements default-deny
when no row exists, so shipping the gated foundation without migration 2200 would
kill calendar feeds on *every* tier, not just Free. Land 2200 in the same SaaS
release as the foundation bump, or an earlier one — never later.

## Verification status

| Check | Result |
| --- | --- |
| `dotnet build Orkyo.Foundation.slnx` | passes, 0 warnings |
| `dotnet test Orkyo.Foundation.slnx` (full) | 2886 + 80 pass, 3 skipped, 0 fail |
| `tsc --noEmit` | clean |
| `eslint` on the touched files | clean |
| `vitest run` (full) | 3619 pass / 284 files, 0 fail |
| orkyo-saas `dotnet test` (full) | 636 pass, 0 fail |
| orkyo-saas `vitest run` + `tsc --noEmit` | 185 pass; clean |
| orkyo-saas `generate-plan-data.mjs --check` | in sync |
| orkyo-community `dotnet build` + `dotnet test` | 60 pass, 0 fail |

New backend tests: `CalendarFeedEndpointsTests` (create returns a token-bearing
URL, list omits the token, anonymous fetch serves iCalendar, unknown token 404s,
revoke stops the feed). The entitlement-denied paths are **not** covered there —
`FoundationWebApplicationFactory` pins `AllFeaturesEnabledGate` and does not
support per-test service overrides, the same limitation already documented for
the reporting 402. The denied paths are covered on the frontend and, for the
tier data, by orkyo-saas's `QuotaServiceIntegrationTests`.

New frontend tests cover: site-scoped create, cross-site subscriptions hidden,
site-less subscriptions kept revocable, create refused without a selected site,
one-time URL reveal, clipboard copy, revoke-behind-confirm, TopBar gating
(disabled unmounted / named from registration / independent of export),
`useCalendarFeedAvailable` (6 tier cases), and the upsell (CTA present with a
plans href, absent without one, list still revocable while gated).

`UtilizationPage.test.tsx` mocks `useImportExport` wholesale, so it needed
`useCalendarFeedHandler` added to the mock — the full suite is what caught it.

Note: this machine OOM-kills `npm ci` at default concurrency (exit 137);
`npm ci --maxsockets 4` works.

## Follow-ups

1. **Existing prod subscriptions** — any created before this fix carry the
   broken apex URL and cannot be re-revealed (stored hashed). Those users must
   revoke and re-create; the dialog now keeps those site-less rows visible so
   they can. Worth checking whether any exist and noting it in release notes.
2. **`orkyo-documentation`** — the commit added a "See your schedule in
   Outlook" page. If it shows an example URL or says the feature lives on the
   Account page, both are now wrong.
3. **Retire `TenantLifecycleLinkPolicy`** (orkyo-saas) onto
   `TenantHostnamePolicy.BuildOrigin` — see [What changed](#backend). Do it with
   the version bump in 4, not after.
4. **Downstream** — `orkyo-saas` / `orkyo-community` pick this up on the next
   foundation version bump; nothing else to change in those repos.
5. **Tokens outlive membership** — `FindActiveByHashAsync` checks only
   `revoked_at`, and nothing revokes a user's feed tokens when they are
   deactivated or purged, so a removed member's `.ics` keeps serving the site
   schedule until someone revokes it by hand. Pre-existing, but this change is
   what makes the feed reachable in prod at all. Belongs with the GDPR user
   lifecycle work.
