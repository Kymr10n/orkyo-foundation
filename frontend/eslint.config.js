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
      ],
    },
  },

  // Date/time convergence for everything the colour block above doesn't cover
  // (src .ts files + contracts). formatters.ts is the single source and is exempt.
  {
    files: ['src/**/*.{ts,tsx}', 'contracts/**/*.ts'],
    ignores: ['src/components/**/*.tsx', 'src/lib/formatters.ts'],
    rules: {
      'no-restricted-syntax': ['error', banInlineDateFormat],
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
      'no-console': 'off',
    },
  },
);
