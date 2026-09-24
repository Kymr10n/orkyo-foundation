# QR stickers on resources — specification and implementation plan

Status: **implemented** (phases 1 and 2), written 2026-09-24 on branch
`claude/orkyo-qr-resource-linking-u77if7`. Section 10 records where the implementation
differs from the first draft.

This document connects Orkyo to physical objects. A user puts a QR sticker on a resource.
The user links the sticker to the resource with the phone camera. A later scan of the
sticker opens that resource.

Three product decisions apply:

| Decision | Value |
|---|---|
| Sticker source | Any existing sticker: bought label rolls, vendor asset tags. Orkyo reads codes. Orkyo does not create or print codes. |
| Scan target | A read-only status sheet with an Edit button. Both phases shipped together, so the edit dialog is never the scan target. |
| Plan gating | None. The feature is in every SaaS plan and in Community. The resource type setting is the only switch. |

## 1. Evaluation

The idea fits the current model. Resource types already carry behaviour flags, and dialogs
already open from a URL parameter. The source idea needs nine corrections or additions.

1. **The camera is blocked today.** The nginx snippet `csp-app.conf` in orkyo-infra sends
   `Permissions-Policy: camera=()` on the app document. That snippet must send
   `camera=(self)`. The API also sends `camera=()`, but on JSON responses only. A header on
   a JSON response has no effect on the page, so the API header stays.
2. **The camera needs a secure context.** Browsers give camera access only on HTTPS or on
   `localhost`. A Community installation on a plain-HTTP LAN address cannot scan. The scanner
   shows the message "Scanning needs HTTPS" in that case.
3. **The code does not go into `external_reference`.** That field holds an ERP identifier or
   a serial number. It is not unique, and a duplicated resource loses it. A new table holds
   the codes. One resource can have more than one code, for example a replacement sticker.
   One code links to one resource only.
4. **A code is an opaque string.** Orkyo does not create the stickers, so it cannot know the
   format. Orkyo stores the decoded text exactly, after it removes leading and trailing
   spaces. Orkyo never opens a URL that a code contains.
5. **Linking needs two entry points.** The source idea puts the scan button in the resource
   dialog only. That is correct for one resource. For a walk through a workshop, a global
   Scan action is faster: scan an unknown sticker, select the resource, and the link is done.
6. **A conflict is explicit.** A code can already belong to a different resource. Orkyo then
   shows the name of that resource. An Editor can move the link, but Orkyo asks first.
   Orkyo never moves a link silently.
7. **The type setting is on by default, except for people.** The new flag is
   `scan_codes_enabled`. The migration sets it to `true` for every type without directory
   details, and to `false` for types with directory details. When an Admin turns the flag
   off, Orkyo hides the scan actions for that type. The links stay in the database.
8. **The access rules follow the three roles.** A Viewer can scan to find a resource. The
   edit dialog then opens read-only, because `FormDialog` already disables Save. Only an
   Editor can link or unlink a code. A code is not a secret. Every lookup needs a signed-in
   member and runs in the tenant database. There is no anonymous lookup. The status sheet is
   read-only for all roles, and only an Editor sees its Edit button.
9. **The resource list ignores `?edit=<id>` today.** The command palette already links to
   `…/instances?edit=<id>`, but `ResourceList.tsx` does not read the parameter. The scan
   result uses the same link. The fix connects `useEditQueryParam` in `ResourceList.tsx`, so
   the command palette link works too.

## 2. Scope

In scope:

- QR codes on any resource type that has the flag on
- Link, unlink, and look up codes
- A global Scan action and a scan action in the resource edit dialog
- Phase 2: a read-only status sheet for one resource

Out of scope:

- Label creation and label printing
- 1D barcodes (EAN, Code 128). The decoder can read them, but phase 1 accepts QR only.
- Offline scanning
- Scanning with the phone's own camera application. That needs Orkyo-printed URL labels.

## 3. Domain model

### 3.1 Resource type flag

`resource_types.scan_codes_enabled BOOLEAN NOT NULL DEFAULT true`.

The migration sets the value to `NOT has_directory_profile` for rows that exist. The API
exposes the flag as `scanCodesEnabled` on `ResourceTypeInfo`, on `CreateResourceTypeRequest`,
and as `bool?` on `UpdateResourceTypeRequest`.

### 3.2 Scan code table

```sql
CREATE TABLE public.resource_scan_codes (
    id                 uuid DEFAULT gen_random_uuid() NOT NULL,
    resource_id        uuid NOT NULL,   -- FK to resources, ON DELETE CASCADE
    code               text NOT NULL,   -- CHECK length 1..512, UNIQUE
    created_by_user_id uuid NULL,       -- no FK, like updated_by_user_id in 1920
    created_at         timestamptz DEFAULT now() NOT NULL
);
CREATE INDEX CONCURRENTLY idx_resource_scan_codes_resource ON public.resource_scan_codes (resource_id);
```

Each tenant has its own database, so a unique index on `code` is unique per tenant. Two
tenants can use the same sticker text.

The migration is `tenant/1990.foundation.resource_scan_codes.sql` with the header
`-- @migration-class: expand`. The table is new and empty, so the unique constraint is part
of `CREATE TABLE`. Only the index on `resource_id` uses `CONCURRENTLY`.

A soft-deactivated resource keeps its codes. A lookup of such a code returns the resource,
and the dialog shows it as inactive.

## 4. API

All routes are in the `/api/resources` group, which uses `RequireMemberReadEditorWrite()`.
No new group convention is necessary.

| Method and route | Result |
|---|---|
| `GET /api/resources/scan-codes/lookup?code=` | `{ status, resource? }`. `status` is `linked`, `unknown` or `type_disabled`. `resource` holds `id`, `name`, `resourceTypeKey`. |
| `GET /api/resources/{id}/scan-codes` | The codes of one resource. |
| `POST /api/resources/{id}/scan-codes` | Body `{ code, moveFromOtherResource }`. Returns 201. If a different resource owns the code and `moveFromOtherResource` is false, returns 409 with that resource. If the type flag is off, returns 422. |
| `DELETE /api/resources/{id}/scan-codes/{codeId}` | Returns 204. If the code does not belong to the resource, returns 404. |

The lookup uses a query parameter, not a path segment, because a code can contain `/`,
`?` and other characters.

Validation follows `docs/validation.md`:

- A FluentValidation validator checks the shape: the code is not empty after trimming, and
  it has 512 characters or fewer.
- `ResourceScanCodeService` checks the rules that need the database: the type flag, and a
  code that belongs to a different resource.

## 5. User interface

All screens are phone-first. They use `useBreakpoint()` and no page-local breakpoints.

### 5.1 Scanner dialog

`QrScannerDialog` is a full-screen dialog on a phone and a centred dialog on larger screens.

- It shows the live camera image and a frame for the code.
- If the camera supports a torch, it shows a torch button.
- It stops the camera at the first decoded code. It also stops the camera at dialog close.
- It shows one message for each failure: permission refused, no camera, or no HTTPS.
- It loads the decoder only at dialog open, so the main bundle does not grow.

### 5.2 Resource edit dialog

If the type flag is on, `ResourceEditDialog` gets a "QR codes" section. The section lists
the linked codes. An Editor sees "Scan to link" and an unlink button for each code. A Viewer
sees the list only.

After a scan, the dialog does one of three things:

- The code is new: Orkyo links it and shows a success toast.
- The code already belongs to this resource: Orkyo shows "Already linked".
- The code belongs to a different resource: Orkyo shows that resource name and a "Move link"
  button.

### 5.3 Global Scan action

`TopBar` gets a Scan icon button on every screen size. The phone is where users scan, so
the button is not in the phone overflow menu. The action increments a new `scanTick` in
`store/ui-actions-store.ts`, and `AppLayout` loads `GlobalScanFlow` and opens the scanner. This is the same pattern as `openAssistant`. There is no floating button,
because `FeedbackButton` already uses the bottom-right corner.

After a scan, the result decides the next step:

| Lookup status | Viewer | Editor |
|---|---|---|
| `linked` | Opens the status sheet. | Opens the status sheet. |
| `unknown` | Shows "This QR code is not linked to a resource." | Opens "Link QR code" with a resource picker. The picker lists resources of types with the flag on. |
| `type_disabled` | Shows "Scanning is off for this resource type." | Same as Viewer. |

The Edit button of the status sheet navigates to `typeRoute(type, 'instances')?edit=<id>`.
`ResourceList` resolves the parameter with `useEditQueryParam` and `resolveMissing`, because
the resource is not always on the loaded page. `resolveMissing` opens only a resource of the
type of the list.

### 5.4 Resource type dialog

`ResourceTypeEditDialog` gets a fourth entry in `BEHAVIOUR_FLAGS`:

- Label: "Can have QR codes"
- Hint: "Lets users link QR stickers to these resources and scan them to open a resource."

## 6. Security

- `Permissions-Policy` changes from `camera=()` to `camera=(self)` in `csp-app.conf` only.
  Microphone, geolocation and payment stay blocked. Other hosts keep `camera=()`.
- Orkyo never opens, fetches or renders a code as a link. The UI shows a code as escaped text.
- A lookup needs an authenticated member of the tenant. The tenant database is the only
  data source, so one tenant cannot find a resource of a different tenant.
- The authorization conformance test covers the new routes, because they are in an
  existing group. A test must show that a Viewer gets 403 on POST and DELETE.

## 7. Phase 2 — resource status sheet

The scan target changes from the edit dialog to a read-only status sheet. The sheet has an
Edit button that opens the edit dialog. A scan is usually a lookup, so a read-only target
prevents accidental edits. It also gives a Viewer useful information.

### 7.1 Endpoint

`GET /api/resources/{id}/status` returns one summary, so a phone makes one request:

| Field | Source |
|---|---|
| Current assignment | `ResourceAssignmentService.GetByResourceAsync` |
| Next assignment | `ResourceAssignmentService.GetByResourceAsync` |
| Active absence | `IResourceAbsenceRepository` |
| Open conflicts count | `ConflictService.CountResourceConflictsAsync` |
| Utilization, last 30 days | `UtilizationService.GetResourceUtilizationAsync` |

`ConflictService.GetAllAsync` works for the full tenant. The status endpoint does not call
it. `CountResourceConflictsAsync` checks only the bookings of the resource in the next 30
days. It counts overlap, capacity, absence, off-time and site conflicts. Capability, timing
and precedence conflicts belong to a request, so the count does not include them.

The active absence uses the rule of the scheduler: an enabled absence whose start and end
contain the current time.

### 7.2 Frontend

`ResourceStatusSheet` is a bottom sheet on a phone and a side panel on larger screens.
`AppLayout` mounts it once. The store field `statusResourceId` opens it. The scanner and the
"Show status" row action of the resource list both set this field.

## 8. Implementation plan

Each step is one pull request. The saas and community repositories get new behaviour through
the normal foundation version bump.

### Phase 1a — backend (orkyo-foundation)

1. Migration `1990.foundation.resource_scan_codes.sql`: flag, table, backfill, indexes.
2. Flag in `backend/core/Models/Resource.cs` and in `ResourceTypeRepository` (select list,
   insert, `SetIfNotNull`, `Map`).
3. `ResourceScanCodeRepository`, `ResourceScanCodeService`, and the validator in
   `backend/core/Validators/`.
4. Routes in `backend/src/Endpoints/ResourceEndpoints.cs` with `EndpointHelpers.ExecuteAsync`.
   Registration in `FoundationServiceExtensions.cs`.
5. `SecurityHeadersMiddleware` sends `camera=(self)`.
6. Tests: lookup (linked, unknown, type off), link, conflict, move, unlink, Viewer 403,
   deactivated resource. Patch coverage 80% or more with `scripts/ci/patch-coverage.sh`.
7. `./scripts/test-downstream.sh`, because the route table changes.

### Phase 1b — frontend (orkyo-foundation npm package)

1. A short test of the decoder on iOS Safari and Android Chrome. The candidate is
   `@zxing/browser`. It is plain JavaScript, with no WebAssembly and no worker, so the CSP
   needs no change other than the camera.
2. `lib/api/resource-scan-codes-api.ts`, entries in `API_PATHS` and in the query keys.
3. `hooks/useResourceScanCodes.ts`. Mutations use `meta: { successMessage, invalidates }`.
4. `components/scan/QrScannerDialog.tsx`, with the decoder loaded on demand.
5. The "QR codes" section in `ResourceEditDialog`, and the flag in `ResourceTypeEditDialog`.
6. The Scan action in `TopBar`, `AppLayout` and `ui-actions-store.ts`.
7. `useEditQueryParam` in `ResourceList.tsx`.
8. Tests with `createTestQueryWrapper({ feedback: true })` and `test-utils/viewport.ts`. The
   tests replace the decoder with a mock, because the vendor camera code cannot run in
   happy-dom.

### Phase 1c — infrastructure and documentation

1. orkyo-infra: `camera=(self)` in `security-headers.conf` and `csp-app.conf`. This change
   must deploy before or with the foundation release that contains the scanner.
2. orkyo-community: a note in `release/docs/OPERATIONS.md` that scanning needs HTTPS.
3. orkyo-documentation: a user-guide page for QR stickers. The product commits carry a
   `Docs-impact:` trailer that points to that page.

### Phase 2 — status sheet

1. Resource filter on the conflict query, and the `GET /api/resources/{id}/status` endpoint.
2. `ResourceStatusSheet`, the row-menu entry, and the change of the scan target.

## 9. Decisions and open questions

- A scan of a code with the type flag off does not show the resource name. Decided.
- The scanner accepts QR codes only. Decided.
- Link and unlink events are not in the audit log. This is open.

## 10. Differences from the first draft

- The API security header stays `camera=()`. Only the nginx app snippet changes (section 1).
- `ScanCodesEnabled` on `ResourceTypeInfo` is not `required` in C#. Code that a product
  compiled against an earlier minor version still compiles.
- A link that loses a race uses `ON CONFLICT DO NOTHING` and a second read. The loser gets
  the result of the winner, not an error.
- The Scan button is visible on the phone, not in the overflow menu.
- If the loaded list is empty, `useEditQueryParam` now calls `resolveMissing`. Before, a
  deep link to a resource on an empty site list did nothing.
