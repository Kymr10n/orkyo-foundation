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
 * Types whose resources are reached through a purpose-built page rather than the generic
 * `/resources/:typeKey` one. Identity, not behaviour — it names which pages exist, so it is
 * a key list and not a `resource_types` flag.
 *
 * Only person remains: its directory tabs (job titles, departments, teams) are genuinely more
 * than the generic page offers. Space left the set when the /spaces page became the Floorplan —
 * a *site* surface holding every placeable type — rather than a page about one type; the space
 * type now gets the same generic page any placeable type gets, and appears on the floorplan
 * exactly the way a mill does.
 */
export const TYPES_WITH_DEDICATED_PAGES: ReadonlySet<string> = new Set([
  RESOURCE_TYPE_KEY.PERSON,
]);

/** Where a search hit of each dedicated type opens. Generic types fall through to /resources. */
export const DEDICATED_TYPE_ROUTES: Readonly<Record<string, { list: string; groups: string }>> = {
  [RESOURCE_TYPE_KEY.PERSON]: { list: "/people/list", groups: "/people/teams" },
};
