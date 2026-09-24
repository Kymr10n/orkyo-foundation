# QR stickers on resources — specification and implementation plan

Status: **draft for approval**, written 2026-09-24 on branch
`claude/orkyo-qr-resource-linking-u77if7`.

This document connects Orkyo to physical objects. A user puts a QR sticker on a resource.
The user links the sticker to the resource with the phone camera. A later scan of the
sticker opens that resource.

Three product decisions apply:

| Decision | Value |
|---|---|
| Sticker source | Any existing sticker: bought label rolls, vendor asset tags. Orkyo reads codes. Orkyo does not create or print codes. |
| Scan target | Phase 1 opens the resource edit dialog. Phase 2 opens a read-only status sheet with an Edit button. |
| Plan gating | None. The feature is in every SaaS plan and in Community. The resource type setting is the only switch. |

## 1. Evaluation

The idea fits the current model. Resource types already carry behaviour flags, and dialogs
already open from a URL parameter. The source idea needs nine corrections or additions.

1. **The camera is blocked today.** Three places send `Permissions-Policy: camera=()`:
   `backend/src/Middleware/SecurityHeadersMiddleware.cs`, and in orkyo-infra
   `nginx/snippets/security-headers.conf` and `nginx/snippets/csp-app.conf`. All three must
   send `camera=(self)`.
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
   member and runs in the tenant database. There is no anonymous lookup.
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
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    resource_id UUID NOT NULL REFERENCES public.resources(id) ON DELETE CASCADE,
    code        TEXT NOT NULL,
    created_by  UUID NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);
-- after COMMIT:
CREATE UNIQUE INDEX CONCURRENTLY ux_resource_scan_codes_code ON public.resource_scan_codes (code);
CREATE INDEX CONCURRENTLY ix_resource_scan_codes_resource ON public.resource_scan_codes (resource_id);
```

Each tenant has its own database, so a unique index on `code` is unique per tenant. Two
tenants can use the same sticker text.

The migration is `tenant/1990.foundation.resource_scan_codes.sql` with the header
`-- @migration-class: expand`. It follows the layout of
`1700.foundation.dissolve_side_tables_expand.sql`: changes in a transaction, then the
indexes with `CONCURRENTLY` after `COMMIT`.

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

`TopBar` gets a Scan icon button. On a phone, the Scan item is in the overflow menu. The
action increments a new `openScanner` tick in `store/ui-actions-store.ts`, and `AppLayout`
opens the scanner. This is the same pattern as `openAssistant`. There is no floating button,
because `FeedbackButton` already uses the bottom-right corner.

After a scan, the result decides the next step:

| Lookup status | Viewer | Editor |
|---|---|---|
| `linked` | Opens the resource. | Opens the resource. |
| `unknown` | Shows "This code is not linked". | Shows "Link to a resource…" with a resource picker. The picker lists types with the flag on. |
| `type_disabled` | Shows "Scanning is off for this resource type". | Same as Viewer. |

"Opens the resource" means navigation to `typeRoute(type, 'instances')?edit=<id>`.
`ResourceList` resolves the parameter with `useEditQueryParam` and `resolveMissing`, because
the resource is not always on the loaded page.

### 5.4 Resource type dialog

`ResourceTypeEditDialog` gets a fourth entry in `BEHAVIOUR_FLAGS`:

- Label: "Can have QR codes"
- Hint: "Lets users link QR stickers to these resources and scan them to open a resource."

## 6. Security

- `Permissions-Policy` changes from `camera=()` to `camera=(self)` in the three places in
  section 1. Microphone, geolocation and payment stay blocked.
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
| Open conflicts count | `ConflictService`, filtered to the resource |
| Utilization, last 30 days | `UtilizationService.GetResourceUtilizationAsync` |

`ConflictService` works for the full tenant today. The endpoint must not load all tenant
conflicts for one resource. Phase 2 therefore adds a resource filter to the conflict query.

### 7.2 Frontend

`ResourceStatusSheet` is a bottom sheet on a phone and a side panel on larger screens. It
also opens from the resource list row menu. One constant selects the scan target. There is
no user setting for it.

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

## 9. Open questions

- Does a scan of a code with the type flag off show the resource name? This document says
  no. The flag then works as a real off switch.
- Are link and unlink events written to the audit log? This depends on the audit scope for
  resource changes.
- Does phase 1 accept 1D barcodes? Asset tags from some vendors are Code 128 only.
