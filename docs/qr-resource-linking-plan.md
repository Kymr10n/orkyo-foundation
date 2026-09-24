# QR sticker linking and resource status sheet — implementation plan

Status: **implemented** on branch `claude/orkyo-qr-resource-linking-u77if7`.

- orkyo-foundation: `a19a032` (spec), `53918a2` (backend), `e4f0b3c` (frontend),
  `abf51df` (spec update), `88b95af` (tests)
- orkyo-infra: `f9679a3` (camera header)
- orkyo-community: `b5caa6b` (HTTPS note)
- orkyo-documentation: `fb6672d` (user guide page)

Specification: [qr-resource-linking-spec.md](qr-resource-linking-spec.md).

## Context

The spec was approved for implementation. User decisions for this round:

- **Phase 1 and phase 2 together.**
- A code whose type has QR off shows "Scanning is off for this resource type" **without the
  resource name**.
- **QR only**, no 1D barcodes.
- No plan gating: every SaaS plan and Community.

Because both phases land together, the scan target for a linked code is the **status sheet**,
with an Edit button that deep-links to the edit dialog. saas and community get the feature
through the normal foundation version bump, with no code change there.

## Deviations from this plan

Section 10 of the spec records them. In short:

- The camera header changes only in `orkyo-infra/nginx/snippets/csp-app.conf`. The API
  `SecurityHeadersMiddleware` stays `camera=()`, because it only covers JSON responses.
  `security-headers.conf` also stays, because it covers Keycloak, status and docs hosts.
- The Scan button is visible on phones, not in the overflow menu.
- A link that loses a race uses `ON CONFLICT DO NOTHING` plus a re-read, not a 409.
- `ScanCodesEnabled` is not `required` in C#, for backward compatibility.
- `created_by_user_id` has no foreign key, like 1920/1940.
- The status sheet's Edit button is shown to Editors only.

## Backend — orkyo-foundation

### Migration `tenant/1990.foundation.resource_scan_codes.sql`

`-- @migration-class: expand`.

- `resource_types.scan_codes_enabled BOOLEAN NOT NULL DEFAULT true`, backfilled to
  `NOT has_directory_profile`.
- `resource_scan_codes (id, resource_id → resources ON DELETE CASCADE, code text CHECK 1..512
  UNIQUE, created_by_user_id, created_at)`.
- `CREATE INDEX CONCURRENTLY` on `resource_id`.

### Type flag

`ScanCodesEnabled` goes everywhere the three existing flags appear:

- `core/Models/Resource.cs`
- `core/Repositories/ResourceTypeRepository.cs`: select, insert, `SetIfNotNull`, `Map`
- `ResourceTypeCatalogService`: `false` for directory types

### Scan codes

Copies the `ResourceCustomField*` shape.

- **Models:** `core/Models/ResourceScanCode.cs`
- **Repository:** `core/Repositories/ResourceScanCodeRepository.cs`
  - `GetByCodeAsync`, `GetByResourceAsync`
  - `InsertAsync` uses `ON CONFLICT DO NOTHING`
  - `ReassignAsync`
  - `DeleteAsync` is scoped to the owning resource
- **Service:** `core/Services/ResourceScanCodeService.cs`
  - Lookup returns linked / unknown / type_disabled.
  - Link returns an outcome enum: Linked, AlreadyLinked, Moved, ResourceNotFound,
    TypeDisabled, OwnedByOther.
  - Unlink.
- **Validator:** `core/Validators/ResourceScanCodeValidators.cs`, with the length limit from
  `DomainLimits.ResourceScanCodeMaxLength`.
- **Routes** in `src/Endpoints/ResourceEndpoints.cs` (existing group,
  `RequireMemberReadEditorWrite`):
  - `GET scan-codes/lookup?code=`
  - `GET /{id}/scan-codes`
  - `POST /{id}/scan-codes`: 201, 200 already linked, 404, 409 names the owner, 422 type off
  - `DELETE /{id}/scan-codes/{codeId}`

### Status (phase 2)

- `core/Models/ResourceStatus.cs`.
- `core/Services/ResourceStatusService.cs`, with `TimeProvider`:
  - current and next booking from `ResourceAssignmentService` (now → +30 days), with request
    names from `IRequestRepository.GetByIdsAsync`
  - the active enabled absence
  - average daily `AllocatedPercent` over the last 30 days from `UtilizationService`
  - conflicts from the new `ConflictService.CountResourceConflictsAsync`, which validates only
    this resource's assignments
- Route `GET /api/resources/{id}/status`.

### Backend tests

- `tests/Services/ResourceScanCodeServiceTests.cs`, including the race branches
- `tests/Services/ResourceStatusServiceTests.cs` (`FakeTimeProvider`)
- `CountResourceConflicts` cases in `ConflictServiceTests.cs`
- `tests/Endpoints/ResourceScanCodeEndpointTests.cs`: round trip, 409, 422, 400, 404, Viewer
  403, Editor allowed, type flag round trip, status endpoint

## Frontend — orkyo-foundation (`frontend/src/`)

- **Dependency:** `@zxing/browser` + `@zxing/library`.
  - Only `lib/scan/qr-decoder.ts` imports them, and it is loaded via dynamic `import()`.
  - The ESLint ban mirrors the jspdf G3 rule.
- **API:** `lib/api/resource-scan-codes-api.ts` and `lib/api/resource-status-api.ts`, with
  `API_PATHS` entries and `qk.resources.scanCodes/scanCodesAll/status` keys.
- **Hooks:** `hooks/useResourceScanCodes.ts`
  - list, link (with an `inlineErrors` option), unlink
  - `useScanLinkCandidates`, `useResourceStatus`
- **`components/scan/QrScannerDialog.tsx`:** a `FormDialog` scaffold that is full screen on a
  phone.
  - Shows the camera view, a torch when the camera has one, and Cancel.
  - Stops the camera at the first decode and on close.
  - Shows an error for insecure context, no camera API, permission denied, no camera, or
    camera busy.
- **`components/resources/ResourceScanCodesSection.tsx`** in `ResourceEditDialog`, shown when
  a resource exists and its type has the flag on.
  - The lookup runs first.
  - An unknown code links. A code already on this resource shows "already linked". A code on
    another resource asks to confirm the move.
- **`ResourceTypeEditDialog`:** "Can have QR codes" flag, on for new types.
- **Global scan:**
  - `ui-actions-store` gains `scanTick`/`openScanner` and
    `statusResourceId`/`openResourceStatus`.
  - `TopBar` has a Scan button at every width.
  - `AppLayout` lazily mounts `components/scan/GlobalScanFlow.tsx` and mounts
    `ResourceStatusSheet` once.
- **`components/resources/ResourceStatusSheet.tsx`:** a bottom sheet on a phone and a right
  panel otherwise. Edit for Editors goes to `${typeRoute(type)}?edit=${id}`.
- **`ResourceList`:**
  - `useEditQueryParam` with `resolveMissing`, limited to the list's own type.
  - A "Show status" row action.
  - `useEditQueryParam` resolves on an empty list.
- **Tests:** scanner states, the section flows, the global flow per role, the status sheet,
  the type flag, TopBar, AppLayout, the deep link, the API clients, and the decoder wrapper.

## Other repos

- **orkyo-infra:** `nginx/snippets/csp-app.conf` sends `camera=(self)`. It must deploy with or
  before the release that ships the scanner.
- **orkyo-community:** `release/docs/OPERATIONS.md` Common issues row: scanning needs HTTPS.
- **orkyo-documentation:** `src/content/docs/user-guide/qr-codes.md`.

## Verification (done)

- Backend:
  - `dotnet build` and `dotnet format --verify-no-changes` pass.
  - The new and changed tests pass against a local Postgres 16 (`CI=true`).
  - `lint-migration-headers.sh`, `check-dead-registrations.py` and `check-stale-markers.sh`
    pass.
- Frontend: `eslint --max-warnings=0`, `tsc -b`, and the full vitest suite (4,237 tests) pass.
- `scripts/ci/patch-coverage.sh`: 94.5% (backend 100%), measured before the API-client and
  decoder tests were added.
- The Community backend suite passes against this foundation (57 tests).
- The SaaS backend suite needs Docker and did not run locally.
- orkyo-documentation: `npm run build` and `format:check` pass.
- Still open: a real-device scan on iOS Safari and Android Chrome.
