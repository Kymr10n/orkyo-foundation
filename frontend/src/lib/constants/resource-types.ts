export const RESOURCE_TYPE_KEYS = {
  SPACE: 'space',
  PERSON: 'person',
  TOOL: 'tool',
} as const;

export type ResourceTypeKey = typeof RESOURCE_TYPE_KEYS[keyof typeof RESOURCE_TYPE_KEYS];
