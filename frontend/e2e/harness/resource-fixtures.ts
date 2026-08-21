import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceTypeInfo } from "@foundation/src/lib/api/resource-types-api";
import type { ResourceCustomField } from "@foundation/src/lib/api/resource-custom-fields-api";
import type { ListDefinition, ListInstance, ListRow } from "@foundation/src/lib/api/lists-api";

/**
 * Fixture data for the ResourceEditDialog harness section.
 *
 * Shaped like the Person type a seeded tenant actually has: a directory profile (email +
 * notes) and four custom fields, one of each layout the form can produce — a plain input, a
 * date, an inline checkbox, and a lookup that renders the row picker. That combination is what
 * the phone-viewport spec measures, because it is where the long field labels and the widest
 * control both live.
 */

const STAMP = { createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z" };

export const PERSON_TYPE_ID = "rt-person";
export const DEPARTMENT_DEFINITION_ID = "ld-department";
export const DEPARTMENT_INSTANCE_ID = "li-department";

export const personTypeFixture: ResourceTypeInfo = {
  id: PERSON_TYPE_ID,
  key: "person",
  displayName: "Person",
  displayNamePlural: "People",
  description: "Operators, fitters and everyone else who does the work.",
  icon: "users",
  hasGeometry: false,
  hasDirectoryProfile: true,
  singleGroupMembership: false,
  isSystem: false,
  isActive: true,
  ...STAMP,
};

/** The station type behind the "Bay 3" fixture the request dialog's Resources tab resolves. */
export const stationTypeFixture: ResourceTypeInfo = {
  id: "type-space",
  key: "space",
  displayName: "Station",
  displayNamePlural: "Stations",
  description: "Machines, benches and bays where the work happens.",
  icon: "factory",
  hasGeometry: true,
  hasDirectoryProfile: false,
  singleGroupMembership: false,
  isSystem: false,
  isActive: true,
  ...STAMP,
};

export const personResourceFixture: ResourceInfo = {
  id: "person-amira",
  resourceTypeId: PERSON_TYPE_ID,
  resourceTypeKey: "person",
  name: "Amira Goodwin",
  allocationMode: "Fractional",
  baseAvailabilityPercent: 100,
  isActive: true,
  email: "amira.goodwin.43277@orkyo.example",
  notes: "Prefers the early rota.",
  customFields: {
    badge_number: "MS-6050",
    certified_until: "2028-04-07",
    night_shift: true,
  },
  ...STAMP,
};

function field(
  overrides: Partial<ResourceCustomField> & Pick<ResourceCustomField, "id" | "key" | "label" | "dataType" | "sortOrder">,
): ResourceCustomField {
  return {
    resourceTypeId: PERSON_TYPE_ID,
    isRequired: false,
    isActive: true,
    ...STAMP,
    ...overrides,
  };
}

export const personCustomFieldsFixture: ResourceCustomField[] = [
  field({ id: "cf-badge", key: "badge_number", label: "Badge number", dataType: "text", sortOrder: 1 }),
  field({ id: "cf-cert", key: "certified_until", label: "Certified until", dataType: "date", sortOrder: 2 }),
  field({ id: "cf-night", key: "night_shift", label: "Night shift", dataType: "boolean", sortOrder: 3 }),
  field({
    id: "cf-department",
    key: "department",
    label: "Department",
    dataType: "list_lookup",
    listInstanceId: DEPARTMENT_INSTANCE_ID,
    sortOrder: 4,
  }),
];

export const departmentDefinitionFixture: ListDefinition = {
  id: DEPARTMENT_DEFINITION_ID,
  name: "Departments",
  scope: "organization",
  isActive: true,
  displayColumnId: null,
  columns: [
    {
      id: "lc-name",
      listDefinitionId: DEPARTMENT_DEFINITION_ID,
      key: "name",
      label: "Name",
      dataType: "text",
      isRequired: true,
      sortOrder: 1,
      isActive: true,
      ...STAMP,
    },
    {
      id: "lc-cost-centre",
      listDefinitionId: DEPARTMENT_DEFINITION_ID,
      key: "cost_centre",
      label: "Cost centre",
      dataType: "text",
      isRequired: false,
      sortOrder: 2,
      isActive: true,
      ...STAMP,
    },
  ],
  ...STAMP,
};

export const departmentInstanceFixture: ListInstance = {
  id: DEPARTMENT_INSTANCE_ID,
  listDefinitionId: DEPARTMENT_DEFINITION_ID,
  kind: "shared",
  name: "Departments",
  ...STAMP,
};

export const departmentRowsFixture: ListRow[] = [
  {
    id: "lr-machining",
    listInstanceId: DEPARTMENT_INSTANCE_ID,
    values: { name: "Engineering Machining", cost_centre: "CC-4180" },
    ...STAMP,
  },
  {
    id: "lr-assembly",
    listInstanceId: DEPARTMENT_INSTANCE_ID,
    values: { name: "Final Assembly", cost_centre: "CC-4210" },
    ...STAMP,
  },
];
