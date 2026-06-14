# Dialog & mutation feedback

Single source of truth for how a save/create/update/delete reports back to the user (success toast,
error toast) and refreshes the UI (react-query cache invalidation). **Do not hand-roll `toast.*` or
`invalidateQueries` in a dialog's `onSuccess`/`onError`** — declare intent via `meta` and let the
central `MutationCache` do it once, in one place.

## Shared dialog scaffolds (don't hand-roll the shell)

- **Simple form dialogs** use [`FormDialog`](../frontend/src/components/ui/FormDialog.tsx): it owns the
  Dialog shell, header, scrollable body, inline `ErrorAlert`, and the Cancel/Submit footer with
  `canEdit` gating (via [`DialogFormFooter`](../frontend/src/components/ui/DialogFormFooter.tsx)). Pass
  `title`/`description` (ReactNode), the fields as `children`, and
  `onSubmit`/`isSubmitting`/`submitLabel`/`submitDisabled`/`error`. Don't re-create `Dialog` +
  `DialogFooter` + Save button by hand. Examples: Department/JobTitle/Site/Criterion/User dialogs.
- **Criterion/skill/capability assignment editors** use the shared
  [`CriterionAssignmentEditor`](../frontend/src/components/capabilities/CriterionAssignmentEditor.tsx)
  (PersonSkillsEditor, SpaceCapabilitiesEditor, GroupCapabilitiesEditor are thin wrappers). The
  diff-and-batch save logic lives in [`capability-diff.ts`](../frontend/src/components/capabilities/capability-diff.ts)
  (`diffCapabilityAssignments(existing, desired, mode)`); `mode` is `upsert` (person/space) or
  `add-new` (group, backend insert-only).
- **Genuinely special dialogs are exempt** — multi-tab/wizard (RequestFormDialog, TourDialog),
  list/multi-select pickers (MoveToDialog, AddExistingRequestsDialog, ResourceGroupMembersEditor),
  read-only/per-item-state-machine views (RequestDetailsDialog, AutoSchedulePreviewDialog,
  PersonAssignmentDialog), and compound in-place sub-forms (AvailabilityEventDialog, TemplateDialogBase,
  PersonEditDialog). Forcing these onto `FormDialog` would hurt clarity, not help it.

## The mechanism

The shared `queryClient` ([query-client.ts](../frontend/src/lib/core/query-client.ts)) has a
`MutationCache` (`createFeedbackMutationCache`) that reads each mutation's `meta`:

```ts
useMutation({
  mutationFn: () => saveThing(...),
  meta: {
    successMessage: 'Thing saved',          // success toast (omit to stay silent)
    errorMessage: 'Failed to save thing',   // error toast title (defaults to "Something went wrong")
    invalidates: [['things'], ['thing', id]], // query keys to refetch, prefix-style (exact:false)
  },
  // onSuccess/onError are ONLY for non-feedback side-effects:
  onSuccess: () => onOpenChange(false),     // close dialog, reset form, call onSaved()
  onError: (err) => setError(err.message),  // inline ErrorAlert (kept alongside the toast)
});
```

The `mutationMeta` shape is typed via a `declare module '@tanstack/react-query'` augmentation in
`query-client.ts`, so `meta` is checked at compile time.

## Rules

1. A mutation that wants feedback declares `meta`; it must **not** also call `toast.*` itself
   (that would double-fire — the cache runs in addition to per-mutation callbacks).
2. The global **error** toast only fires when a mutation opted in (`successMessage` or `errorMessage`
   present). Mutations with no `meta` are untouched — safe to leave legacy/silent mutations alone.
3. Set `meta.invalidates` to the query key(s) the data is actually read under — grep the consuming
   `useQuery` before guessing. Editors that load via `useEffect` (not `useQuery`) have no cached
   consumer; omit `invalidates` (the toast is the only feedback).
4. Keep the in-dialog `ErrorAlert` for persistent, in-context errors; the error toast is the
   transient confirmation. The dialog's `onError` keeps `setError(...)`, just drops `toast.error`.
5. Client-side validation that never calls the mutation (e.g. "email required") may toast directly —
   it isn't a mutation result.

## Exceptions

- **`createCrudHooks`** ([useMutations.ts](../frontend/src/hooks/useMutations.ts)) self-centralizes
  toast + invalidation via `entityLabel` for full-CRUD entities (criteria, sites). It carries no
  `meta`, so the global cache never double-fires. Use it for new full-CRUD entities; use `meta` for
  everything else (composed saves, batch editors, single mutations).
- **Page-level callback handlers** that aren't `useMutation` (e.g. `RequestsPage` request CRUD uses
  `useCallback` + manual `loadRequests()` orchestration) keep their own inline toast/invalidation.

## Tests

Components whose save flows through `useMutation` must render under
`createFeedbackTestQueryWrapper()` ([test-utils.tsx](../frontend/src/test-utils.tsx)) so the
MutationCache fires in tests exactly as in production. Mock `sonner` and assert `toast.success`/
`toast.error` as before — the toast now originates from the cache, not the component. The mechanism
itself is covered by [query-client.test.ts](../frontend/src/lib/core/query-client.test.ts).
