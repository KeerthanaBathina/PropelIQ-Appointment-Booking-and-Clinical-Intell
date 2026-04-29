import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import jsxA11y from 'eslint-plugin-jsx-a11y'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs['recommended-latest'],
      reactRefresh.configs.vite,
      jsxA11y.flatConfigs.recommended,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    rules: {
      // ─── WCAG 2.1 AA accessibility rules (US_100, AC-1) ──────────────────────
      // 'error' severity prevents merging inaccessible code; 'warn' for advisory rules.
      'jsx-a11y/alt-text':                          'error',
      'jsx-a11y/anchor-has-content':                'error',
      'jsx-a11y/aria-props':                        'error',
      'jsx-a11y/aria-proptypes':                    'error',
      'jsx-a11y/aria-role':                         'error',
      'jsx-a11y/aria-unsupported-elements':         'error',
      'jsx-a11y/click-events-have-key-events':      'error',
      'jsx-a11y/heading-has-content':               'error',
      'jsx-a11y/label-has-associated-control':      ['error', { assert: 'either' }],
      'jsx-a11y/no-autofocus':                      ['error', { ignoreNonDOM: true }],
      'jsx-a11y/no-noninteractive-element-interactions': 'warn',
      'jsx-a11y/no-redundant-roles':                'error',
      'jsx-a11y/role-has-required-aria-props':      'error',
      'jsx-a11y/tabindex-no-positive':              'error',
    },
  },
])
