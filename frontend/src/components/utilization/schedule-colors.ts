// Tailwind class maps — shared between space and people utilization views.
// Keep canvas hex values in sync with the Tailwind tokens below.

export type BucketStatus = 'available' | 'partial' | 'assigned' | 'overbooked' | 'non-working';
export type SpaceStatus  = 'available' | 'occupied' | 'conflict';

export const STATUS_CELL_CLASS: Record<BucketStatus, string> = {
  available:     'bg-emerald-100/60 dark:bg-emerald-950/40',
  partial:       'bg-amber-100/60   dark:bg-amber-950/40',
  assigned:      'bg-blue-100/60    dark:bg-blue-950/40',
  overbooked:    'bg-red-100/60     dark:bg-red-950/40',
  'non-working': 'bg-muted/40',
};

export const STATUS_BORDER_CLASS: Record<BucketStatus, string> = {
  available:     'border-emerald-200 dark:border-emerald-800',
  partial:       'border-amber-200   dark:border-amber-800',
  assigned:      'border-blue-200    dark:border-blue-800',
  overbooked:    'border-red-200     dark:border-red-800',
  'non-working': 'border-muted-foreground/30',
};

// Canvas/SVG fill + stroke — must stay in sync with the Tailwind tokens above.
// available → emerald-500, occupied → blue-500 (= assigned), conflict → red-500 (= overbooked)
export const SPACE_CANVAS_COLORS: Record<SpaceStatus, { fill: string; stroke: string }> = {
  available: { fill: 'rgba(16, 185, 129, 0.25)', stroke: '#10b981' },
  occupied:  { fill: 'rgba(59, 130, 246, 0.25)', stroke: '#3b82f6' },
  conflict:  { fill: 'rgba(239, 68, 68, 0.35)',  stroke: '#ef4444' },
};

// Legend dot component classes keyed by space status — maps space statuses onto
// the bucket-status colour tokens so the two legends always look identical.
export const SPACE_LEGEND_CELL_CLASS: Record<SpaceStatus, string> = {
  available: STATUS_CELL_CLASS.available,
  occupied:  STATUS_CELL_CLASS.assigned,
  conflict:  STATUS_CELL_CLASS.overbooked,
};

export const SPACE_LEGEND_BORDER_CLASS: Record<SpaceStatus, string> = {
  available: STATUS_BORDER_CLASS.available,
  occupied:  STATUS_BORDER_CLASS.assigned,
  conflict:  STATUS_BORDER_CLASS.overbooked,
};
