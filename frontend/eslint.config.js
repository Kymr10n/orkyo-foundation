import js from '@eslint/js';
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';

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

// Mutation-feedback guardrail (G1, deferred from Wave 0 to the Wave 2 flip):
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
      react.configs.flat.recommended,
      react.configs.flat['jsx-runtime'],
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
      react: { version: 'detect' },
    },
    plugins: {
      react,
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      // Foundation is a library — react-refresh rules don't apply but the plugin
      // must be registered so inline disable comments in source files are valid.
      'react-refresh/only-export-components': 'off',
      '@typescript-eslint/non-nullable-type-assertion-style': 'off',
      ...reactHooks.configs.recommended.rules,
      'react/prop-types': 'off',
      'react/display-name': 'off',
      'react/no-unescaped-entities': 'off',
      '@typescript-eslint/no-unused-vars': ['error', {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
        caughtErrorsIgnorePattern: '^_',
      }],
      '@typescript-eslint/no-misused-promises': 'off',
      '@typescript-eslint/no-floating-promises': 'off',
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
      'src/components/ui/ValidationIssueList.tsx',
      'src/components/utilization/RequestCalendar.tsx',
      'src/components/utilization/TimelineGridShell.tsx',
      'src/components/break-glass/BreakGlassBanner.tsx',
    ],
    rules: {
      'no-restricted-syntax': 'off',
    },
  },

  // ── Convention guardrails (enforcing since the Wave 2 sweep) ───────────────
  // Dialog-shell + native-dialog + heavy-dep import bans. Landed as `warn` in
  // Wave 0; flipped to `error` after the Wave 2 convention sweep converged the
  // violators (alert() eradicated, form dialogs on FormDialog/ConfirmDialog) and
  // the W1.2 barrel fix (jspdf). The triaged genuinely-special dialogs are
  // enumerated in the exemption block below. These use `no-restricted-imports` /
  // `no-restricted-globals`, distinct rules from the `no-restricted-syntax`
  // colour/date bans above, so they compose without the flat-config
  // last-match-wins clobber.
  // See orkyo-infra/docs/optimization-plan-2026-07.md §Guardrails (G1, G3).
  {
    files: ['src/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-imports': ['error', {
        paths: [
          {
            name: 'jspdf',
            message:
              'jspdf is heavy: only src/lib/utils/gantt-pdf-export.ts may load it, via the existing dynamic import(). A static import drags it into the main chunk. See plan G3.',
          },
          {
            name: '@foundation/src/components/ui/dialog',
            importNames: ['Dialog', 'DialogContent'],
            message:
              'Hand-rolled dialog shell: use FormDialog (simple form dialogs) or ScaffoldDialog (multi-tab wizards). Composition helpers (DialogFooter, DialogHeader, ScrollableDialogBody, …) remain allowed. Genuinely-special dialogs (command palette, list pickers, read-only views) get an exemption entry once triaged. See docs/dialog-feedback.md (G1).',
          },
        ],
      }],
      'no-restricted-globals': ['error',
        { name: 'alert', message: 'Native alert() blocks the UI and bypasses the toast convention: use toast (sonner) for feedback or ErrorAlert for in-context errors. See docs/dialog-feedback.md.' },
        { name: 'confirm', message: 'Native confirm(): use ConfirmDialog (destructive, isPending). See docs/dialog-feedback.md.' },
        { name: 'prompt', message: 'Native prompt(): use a FormDialog. See docs/dialog-feedback.md.' },
      ],
    },
  },
  // The sanctioned dialog shells and the sole jspdf loader are exempt — they
  // ARE the primitives the bans steer everything else toward.
  {
    files: [
      'src/components/ui/FormDialog.tsx',
      'src/components/ui/ScaffoldDialog.tsx',
      'src/lib/utils/gantt-pdf-export.ts',
    ],
    rules: {
      'no-restricted-imports': 'off',
    },
  },
  // Triaged raw-Dialog consumers (G1 exemption list, enumerated at the Wave 2
  // flip per docs/dialog-feedback.md's "genuinely special" categories). Adding a
  // file here requires the same triage — most new dialogs belong on FormDialog /
  // ScaffoldDialog / ConfirmDialog.
  {
    files: [
      // Command palette — cmdk composition, named exempt in the rule message.
      'src/components/layout/CommandPalette.tsx',
      // Shared criterion/skill/capability assignment editor (doc-exempt shell).
      'src/components/capabilities/CriterionAssignmentEditor.tsx',
      // List / multi-select pickers and list-management views.
      'src/components/resource-groups/ResourceGroupMembersEditor.tsx',
      'src/components/people/PersonAbsenceList.tsx',
      // Read-only / per-item-state-machine views.
      'src/components/utilization/PersonAssignmentDialog.tsx',
      'src/components/utilization/ScheduleSlotDialog.tsx',
      'src/components/settings/ReportingApiSettings.tsx', // RawTokenDialog: show-once token view
      'src/components/admin/FeedbackTab.tsx',
      'src/components/admin/AnnouncementsTab.tsx',
      // Compound in-place sub-forms / special flows — FormDialog convergence is
      // tracked as follow-up work, not forced here (W2.2 backlog).
      'src/components/layout/FeedbackButton.tsx',
      'src/components/settings/PasswordSection.tsx',
      'src/components/settings/PresetSettings.tsx',
      'src/components/system/ImportExportDialog.tsx',
      'src/components/requests/FloorplanUploadDialog.tsx',
      'src/pages/AccountPage.tsx',
    ],
    rules: {
      'no-restricted-imports': 'off',
    },
  },

  // G3 (W1.2): the utils barrel must never re-export gantt-pdf-export — that is
  // the exact line that dragged jspdf into the main chunk. `error` here (0
  // violations since the re-export was removed) locks the fix. Scoped to the one
  // file, so it overrides — not merges with — the date-format no-restricted-syntax
  // ban above (index.ts has no date formatting, so nothing is lost).
  {
    files: ['src/lib/utils/index.ts'],
    rules: {
      'no-restricted-syntax': ['error', {
        selector: "ExportAllDeclaration[source.value=/gantt-pdf-export/]",
        message:
          'Do not re-export gantt-pdf-export from the utils barrel: it statically imports jspdf, and this barrel is on the cn import path of ~48 modules. Reach it via the dynamic import() in export-handlers.ts. See plan G3.',
      }],
    },
  },

  {
    files: ['**/*.test.{ts,tsx}', '**/*.spec.{ts,tsx}'],
    rules: {
      '@typescript-eslint/no-floating-promises': 'off',
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-unsafe-return': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/no-unnecessary-condition': 'off',
      // Tests may mirror production date formatting to build expected values.
      'no-restricted-syntax': 'off',
      'no-restricted-properties': 'off',
      // Tests may stub dialogs and mirror native prompts freely.
      'no-restricted-imports': 'off',
      'no-restricted-globals': 'off',
      'no-console': 'off',
    },
  },
);
