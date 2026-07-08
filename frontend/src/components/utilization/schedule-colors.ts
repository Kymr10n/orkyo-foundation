// Tailwind class maps — shared between space and people utilization views.
// Keep canvas hex values in sync with the Tailwind tokens below.

export type BucketStatus = 'available' | 'partial' | 'assigned' | 'overbooked' | 'non-working';
export type SpaceStatus  = 'available' | 'occupied' | 'conflict';

export const STATUS_CELL_CLASS: Record<BucketStatus, string> = {
  available:     'bg-emerald-100 dark:bg-emerald-950',
  partial:       'bg-amber-100   dark:bg-amber-950',
  assigned:      'bg-blue-100    dark:bg-blue-950',
  overbooked:    'bg-red-100     dark:bg-red-950',
  'non-working': 'bg-muted',
};

export const STATUS_BORDER_CLASS: Record<BucketStatus, string> = {
  available:     'border-emerald-200 dark:border-emerald-800',
  partial:       'border-amber-200   dark:border-amber-800',
  assigned:      'border-blue-200    dark:border-blue-800',
  overbooked:    'border-red-200     dark:border-red-800',
  'non-working': 'border-muted-foreground/30',
};

// Solid left-to-right meter fill, painted over STATUS_CELL_CLASS as the track.
// Empty string = no fill (the track tint alone carries the meaning).
export const STATUS_FILL_CLASS: Record<BucketStatus, string> = {
  available:     '',
  partial:       'bg-amber-300   dark:bg-amber-600',
  assigned:      'bg-blue-300    dark:bg-blue-600',
  overbooked:    'bg-red-400     dark:bg-red-600',
  'non-working': '',
};

// Diagonal hatch overlaid on PROBLEM states (overbooked / off-time) so they read
// without relying on colour alone (WCAG 1.4.1). Sets `background-image`, which
// composes on top of the STATUS_CELL_CLASS `background-color` tint rather than
// replacing it. The hatch colour is a CSS var so light/dark stay in one place.
// Opaque off-time cell tint (weekends / holidays / resource off-time). Solid —
// not a translucent overlay — so row separators and bars don't show through.
// color-mix is inlined (not a shared token) because each edition ships its own
// theme :root, but --destructive/--background exist in all of them; this yields
// the same hue as the old destructive/15 tint, opaque, and tracks light/dark.
export const OFFTIME_TINT_CLASS =
  'bg-[color-mix(in_srgb,hsl(var(--destructive))_15%,hsl(var(--background)))]';

export const PROBLEM_HATCH_CLASS =
  'bg-[image:repeating-linear-gradient(45deg,transparent,transparent_11px,var(--hatch-color)_11px,var(--hatch-color)_12px)] [--hatch-color:rgba(0,0,0,0.07)] dark:[--hatch-color:rgba(255,255,255,0.08)]';

// Per-status hatch for BARS/segments. Overbooked (the conflict state) carries it
// as its non-colour cue. Off-time bars stay hatch-free: the off-time *background*
// column already shows the hatch (see TimelineRow), and hatching the "Off" bar on
// top of it doubles the pattern and reads as noise.
export const STATUS_PATTERN_CLASS: Record<BucketStatus, string> = {
  available:     '',
  partial:       '',
  assigned:      '',
  overbooked:    PROBLEM_HATCH_CLASS,
  'non-working': '',
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
