# Reusable UI Candidates — Orkyo Frontend

**Scope:** `orkyo-foundation/frontend`, `orkyo-saas/frontend`, `orkyo-community/frontend`  
**Date:** 2026-05-26  
**Nature:** Analysis only — no code changes.

---

## Classification key

| Label | Meaning |
|---|---|
| **Strong** | Same pattern appears in 3+ places, visual structure **and** behavior are shared. Abstract now. |
| **Moderate** | UI is similar, behavior diverges enough that abstraction needs care. Abstract with a clear spec. |
| **Weak** | Mostly visual similarity or only 1–2 occurrences. Wait for a third occurrence before extracting. |

---

## 1. `OrkyoConfirmDialog` — **Strong**

### Pattern
Destructive-action confirmation: "Are you sure you want to delete X?" with a Cancel and a destructive-labelled confirm button.

### Occurrences

| Location | Current implementation |
|---|---|
| `saas/src/components/admin/UsersTab.tsx:317` | Uses `ConfirmDialog` (SaaS-local) |
| `saas/src/components/admin/ConfirmDialog.tsx` | Exists but scoped to SaaS |
| `foundation/src/pages/PersonList.tsx:76` | `window.confirm()` |
| `saas/src/components/admin/MembershipsTab.tsx:90` | `window.confirm()` |
| `foundation/src/components/settings/CriteriaSettings.tsx:76` | `window.confirm()` |
| `saas/src/components/admin/TenantsTab.tsx:325` | Bespoke `<Dialog>` with slug-confirmation input |

### What to extract
Move `ConfirmDialog` from SaaS into Foundation. The slug-confirmation variant in TenantsTab is a specialisation (`confirmValue` prop) not a separate component. `window.confirm()` calls in Foundation should be replaced.

### Proposed API
```tsx
<OrkyoConfirmDialog
  open={boolean}
  onOpenChange={(open) => void}
  title="Delete user"
  description="This cannot be undone."
  confirmLabel="Delete"          // defaults to "Confirm"
  destructive                    // red confirm button
  confirmValue="acme"            // optional: user must type this slug to unlock
  onConfirm={() => Promise<void>}
/>
```

---

## 2. `OrkyoEmptyState` / `OrkyoLoadingState` / `OrkyoErrorState` — **Strong**

### Pattern
Every list/table in every repo has the same three-branch conditional:

```tsx
{loading
  ? <div className="text-center py-8 text-muted-foreground">Loading...</div>
  : error
  ? <div className="text-center py-8 text-destructive">{error}</div>
  : data.length === 0
  ? <div className="text-center py-8 text-muted-foreground">No items found.</div>
  : <Table ... />}
```

### Occurrences (20+, sample)

| Location |
|---|
| `saas/src/components/admin/TenantsTab.tsx:177` |
| `saas/src/components/admin/UsersTab.tsx:164` |
| `saas/src/components/admin/MembershipsTab.tsx:133` |
| `foundation/src/pages/PersonList.tsx:109` |
| `foundation/src/components/settings/ResourceGroupList.tsx:68` |
| `foundation/src/components/settings/PersonAbsenceList.tsx:71` |
| `foundation/src/components/settings/CriteriaSettings.tsx:125` |

### What to extract
Three lightweight components (or one unified `<DataStateContainer>`). The loading and error messages are always `text-center py-8`; the empty message varies but follows the same structure. `OrkyoErrorState` already has a full-page variant in `RouteErrorBoundary`; inline list errors are a distinct size.

### Proposed API
```tsx
// Standalone
<OrkyoLoadingState message="Loading tenants..." />   // optional message
<OrkyoEmptyState message="No tenants yet." action={<Button>Create</Button>} />
<OrkyoErrorState message={error} />

// Or unified
<OrkyoDataState
  isLoading={loading}
  error={error}
  isEmpty={data.length === 0}
  emptyMessage="No tenants found."
  emptyAction={<Button>Create Tenant</Button>}
>
  <Table ... />
</OrkyoDataState>
```

---

## 3. Admin tab card structure — **Strong**

### Pattern
Every SaaS admin tab (and Foundation settings cards) follows:

```tsx
<Card>
  <CardHeader>
    <div className="flex items-center justify-between">
      <div><CardTitle/><CardDescription/></div>
      <Button>Primary action</Button>
    </div>
  </CardHeader>
  <CardContent>
    {loading/error/empty/table}
  </CardContent>
  {/* dialogs at end */}
</Card>
```

### Occurrences

| Location |
|---|
| `saas/src/components/admin/TenantsTab.tsx:166` |
| `saas/src/components/admin/UsersTab.tsx:148` |
| `saas/src/components/admin/MembershipsTab.tsx:125` |
| `foundation/src/components/settings/CriteriaSettings.tsx` |
| `foundation/src/components/settings/ResourceGroupList.tsx:50` |

### What to extract
A `OrkyoListCard` (or `OrkyoAdminCard`) that owns the Card + CardHeader layout and accepts `title`, `description`, `action` (slot for primary button), and `children`. Not a full page — just the card shell, eliminating the `flex items-center justify-between` boilerplate.

### Proposed API
```tsx
<OrkyoListCard
  title="Tenants"
  description="Manage tenant organizations"
  action={<Button onClick={...}>Create Tenant</Button>}
>
  <OrkyoDataState ...>
    <Table ... />
  </OrkyoDataState>
</OrkyoListCard>
```

---

## 4. Capability/criterion selector — **Strong**

### Pattern
A dialog or collapsible section that: loads a reference list (criteria or resources), renders a `Select` or checkbox list to add entries, maintains a Map-based selection state, and saves via mutation or callback.

### Occurrences (nearly identical code)

| Location | Entity |
|---|---|
| `foundation/src/components/settings/SpaceCapabilitiesEditor.tsx` | Criteria → Space capabilities |
| `foundation/src/components/settings/GroupCapabilitiesEditor.tsx` | Criteria → Group capabilities |
| `foundation/src/components/requests/RequestRequirementsSection.tsx` | Criteria → Request requirements |
| `foundation/src/components/settings/ResourceGroupMembersEditor.tsx` | Resources → Group members |
| `foundation/src/components/requests/AddExistingRequestsDialog.tsx` | Requests → sub-requests |

### What to extract
`SpaceCapabilitiesEditor` and `GroupCapabilitiesEditor` are 95% identical — different API calls, same UI and state shape. Extract a `useCriteriaSelector` hook that owns the Map state, add/remove logic, and dirty-tracking. Both editors then become thin wrappers.

The checkbox-list pattern in `AddExistingRequestsDialog` and `ResourceGroupMembersEditor` is also a shared behavior (load list → filter by search → checkbox multi-select → confirm) worth a `OrkyoMultiSelectDialog`.

---

## 5. `OrkyoFormField` — **Moderate**

### Pattern
Every form dialog uses the same `<div className="space-y-2"><Label /><Input /></div>` (or Select, Textarea) cluster, always in a `<div className="grid gap-4">` parent.

### Occurrences (30+, sample)

| Location |
|---|
| `foundation/src/components/settings/CreateCriterionDialog.tsx:108` |
| `foundation/src/components/settings/EditCriterionDialog.tsx` |
| `foundation/src/components/settings/ResourceGroupEditDialog.tsx:73` |
| `foundation/src/components/settings/PersonEditDialog.tsx` (8+ fields) |
| `saas/src/components/admin/TenantsTab.tsx:454` (CreateTenantDialog) |
| `saas/src/components/admin/MembershipsTab.tsx:240` (AddMemberDialog) |

### Why moderate
The `space-y-2` + `Label` + `Input` structure is purely visual — there's no shared validation, state, or behavior logic. The value of extracting it is layout consistency and reducing noise, not eliminating behavior duplication. Worth extracting, but not urgent.

### Proposed API
```tsx
<OrkyoFormField label="Display Name" htmlFor="displayName">
  <Input id="displayName" ... />
</OrkyoFormField>
```

---

## 6. `OrkyoSearchBar` — **Moderate**

### Pattern
`<Input placeholder="Search by ...">` paired with a `<Button>Search</Button>` (or enter-to-search), sometimes alongside a `<Select>` filter. The trigger behavior varies: some filter on keystroke, some require button press.

### Occurrences

| Location | Trigger |
|---|---|
| `saas/src/components/admin/UsersTab.tsx:152` | Button press (appliedSearch state) |
| `foundation/src/pages/RequestsPage.tsx:54` | Keystroke |
| `community/src/components/admin/CommunityMembersTab.tsx` | Likely keystroke |

### Why moderate
The visual shape is consistent but the trigger model (immediate vs. debounced vs. explicit submit) varies by context and is a meaningful behavior difference. Extract when there are 4+ instances with a clear preferred model.

---

## 7. Table action column buttons — **Moderate**

### Pattern
Icon buttons in the last table column, wrapped in `<TooltipProvider>`, always `variant="ghost" size="icon" className="h-8 w-8"` with a `<Tooltip>` label.

### Occurrences

| Location | Actions |
|---|---|
| `saas/src/components/admin/TenantsTab.tsx:241` | Enter, Suspend/Unsuspend, Delete |
| `saas/src/components/admin/UsersTab.tsx:270` | View, Toggle active, Delete |
| `foundation/src/components/settings/PersonList.tsx:128` | Edit, Skills, Absences, Delete |
| `foundation/src/components/settings/ResourceGroupList.tsx:86` | Members, Edit, Delete |
| `foundation/src/components/settings/PersonAbsenceList.tsx` | Edit, Delete |

### Why moderate
The button appearance and tooltip wrapper are identical; the actions themselves differ per context. An `OrkyoRowActions` wrapper that handles the `TooltipProvider` + `flex items-center gap-1` layout would save boilerplate but the action set is always custom.

---

## 8. CRUD list page shape — **Moderate**

### Pattern
Full page or tab that: fetches a list with `useQuery`, renders loading/error/empty/table, opens a create dialog from a header button, uses `useMutation` for CUD operations, and invalidates the same query key on success.

### Occurrences (6)

Covered above under items 2 and 3. The pattern is consistent enough to extract a `useListPage` hook that owns `{ data, isLoading, error, refetch }` + `{ mutationError, setMutationError, captureError }` + `{ invalidate }`. Currently every tab reimplements this boilerplate independently.

### Why moderate (not strong)
The fetch params, mutation set, and dialog set are always different. The shared logic is relatively thin (query + error state + invalidation helper). Worth a hook extraction into Foundation, but the UI layer stays per-component.

---

## 9. `OrkyoPageHeader` — **Weak**

### Pattern
Page-level `<h1>` or title + optional description + optional action button at the top of full-page views.

### Occurrences
`RequestsPage`, `PeoplePage`, `UtilizationPage`, settings pages — each has a slightly different header structure. Inconsistent across Foundation pages. No pattern strong enough to extract yet.

---

## 10. Pagination — **Weak**

No pagination UI anywhere. All lists render full results. Not a candidate today.

---

## Cross-repo gaps (not patterns — action items)

These are missing abstractions that exist in one repo but should be in Foundation:

| Gap | Current state | Fix |
|---|---|---|
| `ConfirmDialog` | SaaS-only | Move to Foundation as `OrkyoConfirmDialog` (item 1 above) |
| `window.confirm()` calls | Foundation/SaaS (5+ places) | Replace with `OrkyoConfirmDialog` once extracted |
| `FormDialog` adoption | Exists in Foundation, underused in SaaS | SaaS create dialogs should use it — TenantsTab does, UsersTab does not |

---

## Recommended extraction order

1. **`OrkyoConfirmDialog`** — replaces `window.confirm()` everywhere and consolidates ConfirmDialog. Clear API, zero ambiguity.
2. **`OrkyoEmptyState` / `OrkyoLoadingState` / `OrkyoErrorState`** (or unified `OrkyoDataState`) — touches the most files, reduces the most visual noise.
3. **`useCriteriaSelector` hook** — eliminates the SpaceCapabilitiesEditor / GroupCapabilitiesEditor duplication directly.
4. **`OrkyoListCard`** — small, reduces the Card+CardHeader boilerplate in every admin tab.
5. **`OrkyoFormField`** — lowest urgency; purely cosmetic but high volume.
