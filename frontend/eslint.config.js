import js from '@eslint/js';
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';
import eslintReact from '@eslint-react/eslint-plugin';
import reactHooks from 'eslint-plugin-react-hooks';
import globals from 'globals';
import uiPrimitives from './eslint-rules/ui-primitives.js';

// Date/time convergence guardrail — every date/time string flows through
// src/lib/formatters.ts (DATE_FORMATS tokens / formatLocalized), so the app
// renders one consistent 24h house style. See docs/UI-GUIDELINES.md.
const banInlineDateFormat = {
  // date-fns `format(date, "…")` with an inline token string.
  selector: "CallExpression[callee.name='format'] > Literal.arguments",
  message:
    'Inline date-fns format token: use a DATE_FORMATS token or formatLocalized from src/lib/formatters.ts.',
};
const banToLocaleDateTime = [
  {
    property: 'toLocaleDateString',
    message:
      'Raw toLocaleDateString for display: use formatDateDisplay / formatLocalized from src/lib/formatters.ts.',
  },
  {
    property: 'toLocaleTimeString',
    message:
      'Raw toLocaleTimeString for display: use formatCompactTime / formatLocalized from src/lib/formatters.ts.',
  },
];

// Mutation-feedback guardrail (G1):
// toast/invalidation inside useMutation callbacks belongs in meta
// {successMessage, errorMessage, invalidates} — the central MutationCache fires
// it once. onSuccess/onError stay for non-feedback side effects only. The
// documented exemption is optimistic-rollback mutations (onMutate snapshot),
// which the meta convention can't express — those carry an eslint-disable with
// a reason citing docs/dialog-feedback.md.
// Known gap: the selector inspects useMutation() props only, so it cannot see a
// double-toast at a mutateAsync call site (try/catch around mutateAsync that
// toasts again) — code review remains the net for that class.
const banMutationCallbackFeedback = [
  {
    selector:
      "CallExpression[callee.name='useMutation'] Property[key.name=/^(onSuccess|onError|onSettled)$/] CallExpression[callee.object.name='toast']",
    message:
      'Toast inside a useMutation callback: declare meta { successMessage, errorMessage } instead — the central MutationCache toasts once. Optimistic-rollback mutations are the documented exemption (eslint-disable with reason). See docs/dialog-feedback.md.',
  },
  {
    selector:
      "CallExpression[callee.name='useMutation'] Property[key.name=/^(onSuccess|onError|onSettled)$/] CallExpression[callee.property.name='invalidateQueries']",
    message:
      'invalidateQueries inside a useMutation callback: declare meta.invalidates (prefix-style) instead. Optimistic-rollback mutations are the documented exemption (eslint-disable with reason). See docs/dialog-feedback.md.',
  },
];

// Import bans, as shared arrays because flat config's `no-restricted-imports` does not
// merge across config objects — the last matching object wins, so a narrower scope has to
// restate everything it still wants banned. Same reason the date/mutation
// `no-restricted-syntax` arrays above are shared.
const banHeavyAndRawDialogImports = [
  {
    name: '@zxing/browser',
    message:
      'The QR decoder is heavy: only src/lib/scan/qr-decoder.ts may load it, and QrScannerDialog reaches that file through a dynamic import(). A static import drags it into the main chunk.',
  },
  {
    name: '@foundation/src/components/ui/dialog',
    importNames: ['Dialog', 'DialogContent'],
    message:
      'Hand-rolled dialog shell: use FormDialog — its default shape for a form, or `footer={null}` for a tall multi-region body. Composition helpers (DialogFooter, DialogHeader, ScrollableDialogBody, …) remain allowed. Genuinely-special dialogs (command palette, list pickers, read-only views) get an exemption entry once triaged. See docs/dialog-feedback.md (G1).',
  },
];

// Data-fetching boundary: a component or page never talks to the server itself. Every query
// and mutation lives in a domain hook under src/hooks/use*.ts and the component consumes the
// hook's result. See ARCHITECTURE.md ("Data fetching"). hooks/, lib/ and contexts/ are
// exactly where these imports belong, so the ban is scoped to components/ and pages/.
const banDirectDataAccess = [
  {
    name: '@tanstack/react-query',
    message:
      'A component must not call useQuery/useMutation/useQueryClient: put the query or mutation in a domain hook under src/hooks/use*.ts and consume its result. See ARCHITECTURE.md ("Data fetching").',
  },
  {
    name: '@foundation/src/lib/core/api-client',
    message:
      'A component must not call the HTTP client: the request belongs in src/lib/api/*-api.ts, reached through a domain hook in src/hooks/. See ARCHITECTURE.md ("Data fetching").',
  },
];

export default defineConfig(
  {
    ignores: [
      '**/dist/**',
      '**/coverage/**',
      '**/node_modules/**',
      '**/.tsbuild/**',
      // Compiled artifacts that tsc can emit into src/ or contracts/ when run
      // without --outDir (e.g. bare `tsc` instead of `tsc -b`). Git ignores these
      // too (.gitignore), but ESLint ignores are independent.
      'src/**/*.js',
      'src/**/*.d.ts.map',
      'contracts/**/*.js',
      'contracts/**/*.d.ts',
      'contracts/**/*.d.ts.map',
    ],
  },

  {
    files: ['src/**/*.{ts,tsx}', 'contracts/**/*.ts'],
    extends: [
      js.configs.recommended,
      ...tseslint.configs.strictTypeChecked,
      ...tseslint.configs.stylisticTypeChecked,
    ],
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'module',
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    settings: {
      // Stated, not detected: the floor the rules assume moves with the react peer range.
      'react-x': { version: '19.3.1' },
    },
    plugins: {
      '@eslint-react': eslintReact,
      'react-hooks': reactHooks,
    },
    rules: {
      '@typescript-eslint/non-nullable-type-assertion-style': 'off',
      // react-hooks rules are listed one by one rather than spread from
      // `reactHooks.configs.recommended.rules`. The spread inherits whatever the installed
      // plugin happens to consider recommended, so the 5 → 7 bump silently grew the set from
      // 2 rules to 16 (the React Compiler rules joined the preset in v6) and turned a routine
      // dependency refresh into 80 findings with no diff to review. Enumerating them means a
      // future plugin major shows up as a diff to THIS list instead. Same principle as the
      // repo-wide "no silent defaults" rule for env config.
      //
      // All at 'error': the lint script runs --max-warnings=0, so a 'warn' would block the
      // build anyway while reading as optional. Three deliberately deviate from the preset,
      // which ships them at 'warn' — exhaustive-deps, incompatible-library and
      // unsupported-syntax. Keep this list sorted as the plugin lists it.
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'error',
      'react-hooks/static-components': 'error',
      'react-hooks/use-memo': 'error',
      'react-hooks/preserve-manual-memoization': 'error',
      'react-hooks/incompatible-library': 'error',
      'react-hooks/immutability': 'error',
      'react-hooks/globals': 'error',
      'react-hooks/refs': 'error',
      'react-hooks/set-state-in-effect': 'error',
      'react-hooks/error-boundaries': 'error',
      'react-hooks/purity': 'error',
      'react-hooks/set-state-in-render': 'error',
      'react-hooks/unsupported-syntax': 'error',
      'react-hooks/config': 'error',
      'react-hooks/gating': 'error',
      // @eslint-react rules, listed one by one for the same reason as react-hooks above: a
      // plugin bump that grows `recommended` shows up as a diff here, not as new findings.
      // This is the plugin's `recommended` set minus the nine rules that duplicate the
      // react-hooks list (error-boundaries, exhaustive-deps, purity, rules-of-hooks,
      // set-state-in-effect, set-state-in-render, static-components, unsupported-syntax,
      // use-memo): react-hooks is the React team's own implementation and stays the
      // source. Its `disable-conflict-eslint-plugin-react-hooks` preset goes the other way
      // (turns react-hooks off), so it is not used. All at 'error' (--max-warnings=0).
      // Successors of the eslint-plugin-react rules this replaced: jsx-key → no-missing-key,
      // no-children-prop → jsx-no-children-prop, no-danger-with-children →
      // dom-no-dangerously-set-innerhtml-with-children. jsx-no-undef, jsx-uses-vars,
      // no-unknown-property and prop-types are TypeScript's job.
      '@eslint-react/no-access-state-in-setstate': 'error',
      // Off: every index-keyed list here is static (error lines, SVG points, tour steps),
      // never reordered or filtered, and the rule cannot tell a static list from a live one.
      '@eslint-react/no-array-index-key': 'off',
      '@eslint-react/no-children-count': 'error',
      '@eslint-react/no-children-for-each': 'error',
      '@eslint-react/no-children-map': 'error',
      '@eslint-react/no-children-only': 'error',
      '@eslint-react/no-children-to-array': 'error',
      '@eslint-react/no-clone-element': 'error',
      '@eslint-react/no-component-will-mount': 'error',
      '@eslint-react/no-component-will-receive-props': 'error',
      '@eslint-react/no-component-will-update': 'error',
      '@eslint-react/no-context-provider': 'error',
      '@eslint-react/no-create-ref': 'error',
      '@eslint-react/no-direct-mutation-state': 'error',
      // Off for now: 54 forwardRef wrappers in the published components/ui primitives.
      // Moving to ref-as-a-prop changes their exported types for saas and community, so it
      // is its own change, not lint fallout.
      '@eslint-react/no-forward-ref': 'off',
      '@eslint-react/no-missing-key': 'error',
      '@eslint-react/no-nested-component-definitions': 'error',
      '@eslint-react/no-nested-lazy-component-declarations': 'error',
      '@eslint-react/no-set-state-in-component-did-mount': 'error',
      '@eslint-react/no-set-state-in-component-did-update': 'error',
      '@eslint-react/no-set-state-in-component-will-update': 'error',
      '@eslint-react/no-unnecessary-use-prefix': 'error',
      '@eslint-react/no-unsafe-component-will-mount': 'error',
      '@eslint-react/no-unsafe-component-will-receive-props': 'error',
      '@eslint-react/no-unsafe-component-will-update': 'error',
      '@eslint-react/no-unused-class-component-members': 'error',
      '@eslint-react/no-use-context': 'error',
      '@eslint-react/use-state': 'error',
      '@eslint-react/jsx-no-children-prop': 'error',
      '@eslint-react/jsx-no-children-prop-with-children': 'error',
      '@eslint-react/jsx-no-comment-textnodes': 'error',
      '@eslint-react/jsx-no-key-after-spread': 'error',
      '@eslint-react/jsx-no-leaked-dollar': 'error',
      '@eslint-react/jsx-no-leaked-semicolon': 'error',
      '@eslint-react/jsx-no-namespace': 'error',
      '@eslint-react/rsc-function-definition': 'error',
      '@eslint-react/dom-no-dangerously-set-innerhtml': 'error',
      '@eslint-react/dom-no-dangerously-set-innerhtml-with-children': 'error',
      '@eslint-react/dom-no-find-dom-node': 'error',
      '@eslint-react/dom-no-flush-sync': 'error',
      '@eslint-react/dom-no-hydrate': 'error',
      '@eslint-react/dom-no-render': 'error',
      '@eslint-react/dom-no-render-return-value': 'error',
      '@eslint-react/dom-no-script-url': 'error',
      '@eslint-react/dom-no-unsafe-iframe-sandbox': 'error',
      '@eslint-react/dom-no-use-form-state': 'error',
      '@eslint-react/dom-no-void-elements-with-children': 'error',
      '@eslint-react/web-api-no-leaked-event-listener': 'error',
      '@eslint-react/web-api-no-leaked-fetch': 'error',
      '@eslint-react/web-api-no-leaked-intersection-observer': 'error',
      '@eslint-react/web-api-no-leaked-interval': 'error',
      '@eslint-react/web-api-no-leaked-resize-observer': 'error',
      '@eslint-react/web-api-no-leaked-timeout': 'error',
      '@eslint-react/naming-convention-context-name': 'error',
      '@eslint-react/naming-convention-id-name': 'error',
      // Off: a naming style the codebase has not adopted (18 refs without a Ref suffix);
      // renaming finds no defects.
      '@eslint-react/naming-convention-ref-name': 'off',
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
        caughtErrorsIgnorePattern: '^_',
      }],
      '@typescript-eslint/no-unnecessary-condition': 'off',
      '@typescript-eslint/prefer-nullish-coalescing': 'off',
      '@typescript-eslint/consistent-type-imports': ['error', { prefer: 'type-imports', fixStyle: 'inline-type-imports' }],
      '@typescript-eslint/no-non-null-assertion': 'off',
      '@typescript-eslint/no-confusing-void-expression': 'off',
      '@typescript-eslint/no-empty-function': 'off',
      '@typescript-eslint/explicit-function-return-type': 'off',
      '@typescript-eslint/explicit-module-boundary-types': 'off',
      '@typescript-eslint/require-await': 'off',
      '@typescript-eslint/no-invalid-void-type': 'off',
      '@typescript-eslint/restrict-template-expressions': 'off',
      '@typescript-eslint/no-unnecessary-type-assertion': 'off',
      '@typescript-eslint/no-unnecessary-type-arguments': 'off',
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-unsafe-return': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      'no-void': 'off',
      'no-console': 'off',
      eqeqeq: ['error', 'always', { null: 'ignore' }],
    },
  },

  // UI feedback-colour guardrail — keep section feedback flowing through the
  // sanctioned primitives instead of hand-rolled boxes. See docs/UI-GUIDELINES.md §7.
  {
    files: ['src/components/**/*.tsx'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: 'Literal[value=/bg-destructive\\/10/]',
          message:
            'Hand-rolled feedback box: use <ErrorAlert>, <Alert variant="destructive">, or a <Badge> variant instead of bg-destructive/10. See docs/UI-GUIDELINES.md §7.',
        },
        {
          selector: 'TemplateElement[value.raw=/bg-destructive\\/10/]',
          message:
            'Hand-rolled feedback box: use <ErrorAlert>, <Alert variant="destructive">, or a <Badge> variant instead of bg-destructive/10. See docs/UI-GUIDELINES.md §7.',
        },
        {
          selector: 'Literal[value=/bg-(red|amber)-50(?!0)/]',
          message:
            'Hand-rolled feedback box: use <Alert variant="warning|destructive"> or a <Badge> variant instead of a light semantic background. See docs/UI-GUIDELINES.md §7.',
        },
        {
          selector: 'TemplateElement[value.raw=/bg-(red|amber)-50(?!0)/]',
          message:
            'Hand-rolled feedback box: use <Alert variant="warning|destructive"> or a <Badge> variant instead of a light semantic background. See docs/UI-GUIDELINES.md §7.',
        },
        // Date/time convergence — component .tsx files also carry the colour ban,
        // so the format ban is appended here (flat config's no-restricted-syntax
        // does not merge across config objects — last match wins).
        banInlineDateFormat,
        // Mutation-feedback convergence (appended for the same non-merge reason).
        ...banMutationCallbackFeedback,
      ],
    },
  },

  // Date/time convergence for everything the colour block above doesn't cover
  // (src .ts files + contracts). formatters.ts is the single source and is exempt.
  {
    files: ['src/**/*.{ts,tsx}', 'contracts/**/*.ts'],
    ignores: ['src/components/**/*.tsx', 'src/lib/formatters.ts'],
    rules: {
      'no-restricted-syntax': ['error', banInlineDateFormat, ...banMutationCallbackFeedback],
    },
  },

  // Ban raw locale date/time display calls everywhere but formatters.ts.
  {
    files: ['src/**/*.{ts,tsx}', 'contracts/**/*.ts'],
    ignores: ['src/lib/formatters.ts'],
    rules: {
      'no-restricted-properties': ['error', ...banToLocaleDateTime],
    },
  },

  // Sanctioned colour-source primitives + deliberate banners are exempt: they ARE
  // the single source of truth the guardrail steers everything else toward.
  {
    files: [
      'src/components/ui/alert.tsx',
      'src/components/ui/badge.tsx',
      'src/components/ui/ErrorAlert.tsx',
      'src/components/requests/ValidationIssueList.tsx',
      'src/components/utilization/RequestCalendar.tsx',
      // The column tint/header helpers moved here from TimelineGridShell — this file is now
      // the colour source the shell and the Requests timeline both read from.
      'src/components/utilization/TimelineRow.tsx',
      'src/components/break-glass/BreakGlassBanner.tsx',
    ],
    rules: {
      'no-restricted-syntax': 'off',
    },
  },

  // UI primitives convergence (2026-09 review, F3): hand-rolled empty states and raw
  // <Loader2> outside components/ui. A local rule, so the legacy sites carry a file-level
  // disable that names exactly this rule; the disables are the baseline and only go down.
  {
    files: ['src/**/*.tsx'],
    ignores: ['**/*.test.tsx'],
    plugins: { orkyo: { rules: { 'ui-primitives': uiPrimitives } } },
    rules: { 'orkyo/ui-primitives': 'error' },
  },

  // ── Convention guardrails ──────────────────────────────────────────────────
  // Dialog-shell + native-dialog + heavy-dep import bans. The triaged
  // genuinely-special dialogs are enumerated in the exemption block below. These use `no-restricted-imports` /
  // `no-restricted-globals`, distinct rules from the `no-restricted-syntax`
  // colour/date bans above, so they compose without the flat-config
  // last-match-wins clobber.
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', { paths: banHeavyAndRawDialogImports }],
      'no-restricted-globals': ['error',
        { name: 'alert', message: 'Native alert() blocks the UI and bypasses the toast convention: use toast (sonner) for feedback or ErrorAlert for in-context errors. See docs/dialog-feedback.md.' },
        { name: 'confirm', message: 'Native confirm(): use ConfirmDialog (destructive, isPending). See docs/dialog-feedback.md.' },
        { name: 'prompt', message: 'Native prompt(): use a FormDialog. See docs/dialog-feedback.md.' },
      ],
    },
  },
  // Components and pages carry the dialog ban AND the data-fetching ban.
  {
    files: ['src/components/**/*.{ts,tsx}', 'src/pages/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', {
        paths: [...banHeavyAndRawDialogImports, ...banDirectDataAccess],
      }],
    },
  },
  // The sanctioned dialog shells and the sole QR-decoder loader are exempt from the raw-Dialog
  // and heavy-dep bans — they ARE the primitives those bans steer everything else toward.
  // They are under components/, so they restate the data-fetching ban rather than
  // switching the rule off wholesale.
  {
    files: ['src/components/ui/FormDialog.tsx'],
    rules: {
      'no-restricted-imports': ['error', { paths: banDirectDataAccess }],
    },
  },
  {
    files: ['src/lib/scan/qr-decoder.ts'],
    rules: {
      'no-restricted-imports': 'off',
    },
  },
  // Triaged raw-Dialog consumers (G1 exemption list, per docs/dialog-feedback.md's
  // "genuinely special" categories). Adding a file here requires the same triage —
  // most new dialogs belong on FormDialog / ConfirmDialog.
  //
  // These are exempt from the raw-Dialog ban ONLY. They restate the data-fetching ban
  // instead of switching the rule off, so a dialog exemption can never hand a component
  // its own useQuery back.
  {
    files: [
      // Command palette — cmdk composition, named exempt in the rule message.
      'src/components/layout/CommandPalette.tsx',
      // Shared criterion/skill/capability assignment editor (doc-exempt shell).
      'src/components/capabilities/CriterionAssignmentEditor.tsx',
      // List / multi-select pickers and list-management views.
      'src/components/resource-groups/ResourceGroupMembersEditor.tsx',
      // Read-only / per-item-state-machine views.
      'src/components/utilization/ResourceAssignmentDialog.tsx',
      'src/components/utilization/ScheduleSlotDialog.tsx',
      // RawTokenDialog: show-once token view. Was ReportingApiSettings.tsx's; extracted here when
      // the API-access (MCP) token screen needed the same one-time secret reveal, so the exemption
      // covers one shared dialog instead of a copy per token class.
      'src/components/settings/api-tokens/token-ui.tsx',
      'src/components/admin/FeedbackTab.tsx',
      // Compound in-place sub-forms / special flows — FormDialog convergence is
      // tracked as follow-up work, not forced here.
      'src/components/layout/FeedbackButton.tsx',
      'src/components/settings/PasswordSection.tsx',
      'src/components/settings/PresetSettings.tsx',
      'src/components/system/ImportExportDialog.tsx',
      'src/components/requests/FloorplanUploadDialog.tsx',
    ],
    rules: {
      'no-restricted-imports': ['error', { paths: banDirectDataAccess }],
    },
  },

  {
    files: ['**/*.test.{ts,tsx}', '**/*.spec.{ts,tsx}'],
    rules: {
      // Tests may mirror production date formatting to build expected values.
      'no-restricted-syntax': 'off',
      'no-restricted-properties': 'off',
      // Tests may stub dialogs and mirror native prompts freely.
      'no-restricted-imports': 'off',
      'no-restricted-globals': 'off',
    },
  },
);
