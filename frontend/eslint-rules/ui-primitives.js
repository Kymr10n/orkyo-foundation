/**
 * orkyo/ui-primitives — converge empty and loading states on the shared primitives.
 *
 * Two checks, both from docs/UI-GUIDELINES.md §17 ("Empty states", "Loading states"):
 *
 *   1. A className literal that combines `text-center` and `text-muted-foreground` is the
 *      hand-rolled "nothing here" block the guideline forbids; use <EmptyState>.
 *   2. A raw <Loader2> outside components/ui is the third loading idiom beside <Skeleton>
 *      and <LoadingSpinner>; use one of those (or Button's `loading` prop, §11).
 *
 * A local rule rather than two more `no-restricted-syntax` selectors so a legacy site can carry
 * a file-level `eslint-disable orkyo/ui-primitives` that names exactly this rule, and nothing
 * else in the file loses its guards. The 2026-09 design review (F3) counted the legacy sites;
 * the disables are the baseline, and the number only goes down.
 */

const EMPTY_STATE_MESSAGE =
  'Hand-rolled empty state (text-center + text-muted-foreground): use <EmptyState> from components/ui. See docs/UI-GUIDELINES.md §17.';
const LOADER_MESSAGE =
  'Raw <Loader2>: use <Skeleton> for a known layout, <LoadingSpinner> for a region, or Button `loading`. See docs/UI-GUIDELINES.md §17 and §11.';

function isHandRolledEmptyState(text) {
  return /\btext-center\b/.test(text) && /\btext-muted-foreground\b/.test(text);
}

/** @type {import('eslint').Rule.RuleModule} */
export default {
  meta: {
    type: 'suggestion',
    docs: { description: 'Use the shared EmptyState / loading primitives instead of hand-rolled markup.' },
    schema: [],
    messages: { emptyState: EMPTY_STATE_MESSAGE, loader: LOADER_MESSAGE },
  },
  create(context) {
    const inUiDir = /[\\/]components[\\/]ui[\\/]/.test(context.filename ?? context.getFilename());
    if (inUiDir) return {};

    return {
      Literal(node) {
        if (typeof node.value === 'string' && isHandRolledEmptyState(node.value)) {
          context.report({ node, messageId: 'emptyState' });
        }
      },
      TemplateElement(node) {
        if (isHandRolledEmptyState(node.value.raw)) {
          context.report({ node, messageId: 'emptyState' });
        }
      },
      JSXOpeningElement(node) {
        if (node.name.type === 'JSXIdentifier' && node.name.name === 'Loader2') {
          context.report({ node, messageId: 'loader' });
        }
      },
    };
  },
};
