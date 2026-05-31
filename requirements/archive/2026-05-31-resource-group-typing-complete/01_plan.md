# Resource Group Typing Enforcement — Implementation Plan

## Goal

Enforce that resource group membership is type-safe at the database level.  
Replace the legacy `SpaceGroup*` write path with the generic `ResourceGroup*` path.  
Add `ResourceTypeKey`, `Color`, and `DisplayOrder` to `ResourceGroupInfo`.

## Decisions Made

| ID | Decision |
|----|----------|
| D1 | Migrate in three phases: expand → data → contract (standard migration model) |
| D2 | Composite FK on `resource_group_members` enforces cross-type membership is impossible at DB level |
| D3 | `ResourceGroupInfo` gains `ResourceTypeKey`, `Color?`, `DisplayOrder?` |
| D4 | New generic `ResourceGroupList/EditDialog/MembersEditor` components replace all SpaceGroup-specific UI |

## Migration Phases

### 1440 — Expand (additive, safe)
- Add `UNIQUE (id, resource_type_id)` to `resources` and `resource_groups`
- Add `resource_type_id UUID` column to `resource_group_members`

### 1450 — Data (backfill)
- Populate `resource_group_members.resource_type_id` from the group's `resource_type_id`
- Fail loud if any cross-type memberships are detected

### 1460 — Contract (unsafe, requires `APPROVE_UNSAFE_MIGRATION=1`)
- SET NOT NULL on `resource_group_members.resource_type_id`
- Add composite FK: `(resource_id, resource_type_id)` → `(resources.id, resources.resource_type_id)`
- Add composite FK: `(resource_group_id, resource_type_id)` → `(resource_groups.id, resource_groups.resource_type_id)`
- Drop the intermediate column default

## Backend Changes

- `ResourceGroupInfo`: added `ResourceTypeKey`, `Color?`, `DisplayOrder?`
- `CreateResourceGroupRequest`: added `ResourceTypeKey`, `Color?`, `DisplayOrder?`
- `UpdateResourceGroupRequest`: added `Color?`, `DisplayOrder?`
- `ResourceGroupRepository`: updated SELECT/INSERT/UPDATE to include new columns; JOINs `resource_types`
- `ResourceGroupMemberRepository.SetMembersAsync`: pre-validation query detects cross-type members before transaction; throws `ArgumentException` → HTTP 400
- `ExportService` + `PresetService`: migrated from `ISpaceGroupRepository` to `IResourceGroupRepository.GetByTypeKeyAsync("space")`
- `PresetApplier.CreateSpaceGroupAsync`: INSERT now includes `resource_type_id` via subselect from `resource_types WHERE key = 'space'`
- Deleted: all `SpaceGroup*` files (`SpaceGroupEndpoints`, `SpaceGroupRepository`, `ISpaceGroupRepository`, `SpaceGroup` model, validators, tests)

## Frontend Changes

- `resource-groups-api.ts`: `ResourceGroupInfo` gains `resourceTypeKey`, `color?`, `displayOrder?`; request types updated
- New generic components in `components/resource-groups/`: `ResourceGroupList`, `ResourceGroupEditDialog`, `ResourceGroupMembersEditor` (with 3 test files)
- `GroupSettings.tsx`: migrated from `useGroups` hooks + `space-groups-api` to direct `resource-groups-api` calls
- `SchedulerGrid.tsx`: migrated from `SpaceGroup` type + `getSpaceGroups()` to `ResourceGroupInfo` + `getResourceGroups('space')`
- `PeoplePage.tsx`: `<PeopleGroupList />` → `<ResourceGroupList resourceTypeKey="person" />`
- Deleted: `space-groups-api.ts`, `useGroups.ts`, `spaceGroup.ts`, all `PeopleGroup*` components (replaced by generic `ResourceGroupList/EditDialog/MembersEditor`)
- Removed barrel exports from `lib/api/index.ts` and `hooks/index.ts`
