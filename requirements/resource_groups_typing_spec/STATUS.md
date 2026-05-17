# Resource Group Typing Enforcement — Status

| Step | Area | Status | Completed | Notes |
|------|------|--------|-----------|-------|
| 1 | Migration 1440 (expand) | ✅ | 2026-05 | Additive: UNIQUE constraints + resource_type_id column on members |
| 2 | Migration 1450 (data) | ✅ | 2026-05 | Backfill resource_type_id on members |
| 3 | Migration 1460 (contract) | ✅ | 2026-05 | NOT NULL + composite FKs + DROP DEFAULT. Requires APPROVE_UNSAFE_MIGRATION=1 |
| 4 | Backend: ResourceGroupInfo model | ✅ | 2026-05 | Added ResourceTypeKey, Color?, DisplayOrder? |
| 5 | Backend: Validators | ✅ | 2026-05 | Color hex regex + DisplayOrder ≥ 0 |
| 6 | Backend: ResourceGroupRepository | ✅ | 2026-05 | Named ordinals, CTE pattern, new params, JOIN resource_types |
| 7 | Backend: SetMembersAsync cross-type guard | ✅ | 2026-05 | Pre-validation query → ArgumentException → HTTP 400 |
| 8 | Backend: Delete SpaceGroup* src + tests | ✅ | 2026-05 | 13 files deleted from foundation + saas |
| 9 | Backend: Remove SpaceGroup DI | ✅ | 2026-05 | 3 Program files + FoundationWebApplicationFactory updated |
| 10 | Backend: ResourceGroupTypingTests | ✅ | 2026-05 | 3 new integration tests |
| 11 | Backend: PresetApplier.CreateSpaceGroupAsync | ✅ | 2026-05 | Added resource_type_id subselect for 'space' type |
| 12 | Frontend: resource-groups-api.ts | ✅ | 2026-05 | ResourceGroupInfo + request types extended |
| 13 | Frontend: ResourceGroupList + test | ✅ | 2026-05 | Generic component; 9 tests |
| 14 | Frontend: ResourceGroupEditDialog + test | ✅ | 2026-05 | Generic component; 8 tests |
| 15 | Frontend: ResourceGroupMembersEditor + test | ✅ | 2026-05 | Generic component; 7 tests |
| 16 | Frontend: GroupSettings.tsx migration | ✅ | 2026-05 | Migrated from useGroups + space-groups-api; 19 tests pass |
| 17 | Frontend: SchedulerGrid.tsx migration | ✅ | 2026-05 | getResourceGroups('space') + ResourceGroupInfo type |
| 18 | Frontend: PeoplePage.tsx migration | ✅ | 2026-05 | PeopleGroupList → ResourceGroupList resourceTypeKey="person" |
| 19 | Frontend: Delete dead files | ✅ | 2026-05 | 11 files deleted + barrel exports removed |
| 20 | Tests | ✅ | 2026-05 | 2306 backend + 2417 frontend; all pass. Typecheck clean. |

## Post-deploy

Migration 1460 requires `APPROVE_UNSAFE_MIGRATION=1` at deploy time.  
No data loss risk: migration 1450 backfills all members before 1460 adds the NOT NULL constraint.
