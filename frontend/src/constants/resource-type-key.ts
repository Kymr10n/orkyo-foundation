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
