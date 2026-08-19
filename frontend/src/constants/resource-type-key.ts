/**
 * The seeded *system* resource type keys — mirrors the backend `ResourceTypeKeys`
 * constants. This is not the universe of valid keys: tenants define their own types,
 * which live only in the database. Use these solely to address the built-in pages and
 * behaviours that are written against a specific system type; resolve anything else
 * through `useResourceTypes()`.
 */
export const RESOURCE_TYPE_KEY = {
  SPACE: "space",
  PERSON: "person",
  TOOL: "tool",
} as const;

/**
 * What a group of this type is called. Person groups have always been "Teams", and that word is
 * worth keeping. Everything else is a Group. A frontend map rather than a column on the type:
 * one label does not justify a schema change, and a tenant can already rename the type itself.
 */
export const GROUP_ENTITY_LABELS: Record<string, string> = {
  [RESOURCE_TYPE_KEY.PERSON]: 'Team',
};

/**
 * What criterion values are called for this type. People call theirs "skills"; the mechanism is
 * the shared one every type uses, so the difference is wording only.
 */
export const CAPABILITY_LABELS: Record<string, { plural: string; singular: string }> = {
  [RESOURCE_TYPE_KEY.PERSON]: { plural: 'Skills', singular: 'Skill' },
};
