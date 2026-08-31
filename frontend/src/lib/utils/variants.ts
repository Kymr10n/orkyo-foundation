/**
 * Variant class builder.
 *
 * Replaces class-variance-authority, of which this app used one call shape:
 * `cva(base, { variants, defaultVariants })` plus the `VariantProps` type. No
 * compound variants, no `class` key, four call sites. The package was a supply
 * chain edge for the forty lines below.
 *
 * Behaviour is kept identical to cva for the surface in use: a variant the caller
 * leaves undefined falls back to `defaultVariants`, an explicit `null` selects no
 * class at all, and a `className` passed in is appended last so it wins under
 * `twMerge`.
 */

type VariantMap = Record<string, Record<string, string>>;

type VariantSelection<V extends VariantMap> = {
  [K in keyof V]?: keyof V[K] | null | undefined;
};

/** The variant props of a builder, minus `className` — components declare that themselves. */
export type VariantProps<T> = T extends (props?: infer P) => string
  ? Omit<NonNullable<P>, "className">
  : never;

export function cva<V extends VariantMap>(
  base: string,
  config?: { variants?: V; defaultVariants?: VariantSelection<V> },
) {
  return (props?: VariantSelection<V> & { className?: string }): string => {
    const classes = [base];

    if (config?.variants) {
      for (const key of Object.keys(config.variants) as (keyof V)[]) {
        const selected = props?.[key];
        // An explicit null opts out of the variant entirely; undefined takes the default.
        if (selected === null) continue;
        const value = selected ?? config.defaultVariants?.[key];
        if (value == null) continue;
        const className = config.variants[key][value as string];
        if (className) classes.push(className);
      }
    }

    if (props?.className) classes.push(props.className);

    return classes.join(" ");
  };
}
