# Calendar feed: correct tenant URL + move off the Account page (2026-08)

Handoff doc. The change is **implemented and largely verified** in the working
tree; what remains is listed under [Remaining](#remaining).

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

**`backend/shared/TenantHostnamePolicy.cs`** — new `BuildOrigin(appBaseUrl,
baseDomain, subdomainPrefix, slug)`: the tenant's own origin, falling back to
`appBaseUrl` unchanged when `baseDomain` is unset. That fallback is what keeps
community and local dev correct with no product-specific branching.

Deliberately added *here* rather than moving orkyo-saas's equivalent
`TenantLifecycleLinkPolicy.BuildWorkspaceUrl` into shared code: `Orkyo.Shared`
already lives in orkyo-foundation and is consumed by SaaS as a versioned
package, so moving that class would have forced a foundation release +
`OrkyoFoundationVersion` bump before the fix could land. `BuildWorkspaceUrl`
is left untouched; collapsing it onto `BuildOrigin` is an optional follow-up
once a version bump happens anyway.

**`backend/src/Endpoints/CalendarFeedEndpoints.cs`** — private `TenantOrigin()`
helper reads `ICurrentTenant.TenantSlug` plus
`ConfigKeys.TenantResolution{BaseDomain,SubdomainPrefix}` (indexer, null-safe,
unset in community) and feeds `BuildOrigin`. Both the POST create handler
(`FeedUrl`) and the GET feed handler (`.ics` `PRODID` domain) now use it.
`ICurrentTenant` was already DI-registered in both products —
no new wiring.

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
  last-registered-wins.
- **`hooks/useImportExport.ts`** — `useCalendarFeedHandler(context, offer)`.
  Register on mount, unregister on unmount; presence is the whole contract.
- **`components/utilization/CalendarFeedDialog.tsx`** (new) — adapted from the
  old Account panel. Reads `selectedSiteId` from `app-store`, passes it into
  `createCalendarSubscription` (the API supported `siteId` all along; the old
  panel never sent it), and filters the list to the selected site since the
  endpoint returns every site's subscriptions. Clears the revealed URL on
  close, so reopening can't re-show an address the user already had their one
  look at.
- **`components/layout/TopBar.tsx`** — `CalendarPlus` icon button beside
  Export (tablet+), a "Subscribe in calendar" overflow item (phone), plus
  lazy-loaded dialog. Gated on the registry exactly like Export.
- **`pages/UtilizationPage.tsx`** — registers `'utilization'` next to its
  existing `useExportHandler`. Page-level, not Calendar-tab-level: the feed
  serves the whole site schedule regardless of the active tab.
- **`pages/AccountPage.tsx`** — "Calendar" tab removed;
  `components/account/CalendarSubscriptions.{tsx,test.tsx}` deleted.

## Verification status

| Check | Result |
| --- | --- |
| `dotnet build Orkyo.Foundation.slnx` | passes, 0 warnings |
| Backend tests (`TenantHostnamePolicy`, `CalendarFeed`, `ICalendarWriter`) | 31/31 pass |
| `CalendarFeedDialog.test.tsx` + `TopBar.test.tsx` | 32/32 pass |

New frontend tests cover: site-scoped create, cross-site subscriptions hidden,
one-time URL reveal, clipboard copy, revoke-behind-confirm, and TopBar gating
(disabled unmounted / named from registration / independent of export).

Note: this machine OOM-killed both `npm ci` and `npm run typecheck` (exit 137).
`npm ci --maxsockets 4` worked. An early test run also failed on 5s timeouts
purely from cold-start slowness — the same tests pass in ~5s warm.

## Remaining

1. **`npm run typecheck`** — never completed here (OOM). Run it.
2. **`npm run lint`** on the touched files.
3. **Full suites** — only the two affected frontend files were run; the whole
   `vitest run` and the full `dotnet test` have not been.
4. **Existing prod subscriptions** — any created before this fix carry the
   broken apex URL and cannot be re-revealed (stored hashed). Those users must
   revoke and re-create. Worth checking whether any exist and noting it in
   release notes.
5. **`orkyo-documentation`** — the commit added a "See your schedule in
   Outlook" page. If it shows an example URL or says the feature lives on the
   Account page, both are now wrong.
6. **Downstream** — `orkyo-saas` / `orkyo-community` pick this up on the next
   foundation version bump; nothing to change in those repos.

## Working state

Branch `main` at `dc613af`, uncommitted:

```
 M backend/shared/TenantHostnamePolicy.cs
 M backend/src/Endpoints/CalendarFeedEndpoints.cs
 M backend/tests/Constants/TenantHostnamePolicyTests.cs
 D frontend/src/components/account/CalendarSubscriptions.test.tsx
 D frontend/src/components/account/CalendarSubscriptions.tsx
 M frontend/src/components/layout/TopBar.test.tsx
 M frontend/src/components/layout/TopBar.tsx
 M frontend/src/hooks/useImportExport.ts
 M frontend/src/pages/AccountPage.tsx
 M frontend/src/pages/UtilizationPage.tsx
 M frontend/src/store/ui-actions-store.ts
?? frontend/src/components/utilization/CalendarFeedDialog.test.tsx
?? frontend/src/components/utilization/CalendarFeedDialog.tsx
```
