import js from '@eslint/js';
import boundaries from 'eslint-plugin-boundaries';
import globals from 'globals';
import queryPlugin from '@tanstack/eslint-plugin-query';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default tseslint.config(
    { ignores: ['dist', '**/*.gen.ts', '**/*.gen.terms.ts'] },
    {
        extends: [js.configs.recommended, ...tseslint.configs.recommended],
        files: ['**/*.{ts,tsx}'],
        languageOptions: {
            ecmaVersion: 2020,
            globals: globals.browser,
            parserOptions: {
                projectService: true,
                tsconfigRootDir: import.meta.dirname,
            },
        },
        settings: {
            'boundaries/elements': [
                { type: 'types', pattern: 'src/types' },
                { type: 'lib', pattern: 'src/lib' },
                { type: 'ui-hook', pattern: 'src/hooks/ui' },
                { type: 'hooks', pattern: 'src/hooks' },
                { type: 'ui', pattern: 'src/components/ui' },
                { type: 'components', pattern: 'src/components' },
                { type: 'page', pattern: 'src/pages/*', capture: ['page'] },
            ],
            'boundaries/root-path': import.meta.dirname,
            'import/resolver': {
                typescript: {
                    project: './tsconfig.app.json',
                },
            },
            react: {
                version: 'detect',
            },
        },
        plugins: {
            '@tanstack/query': queryPlugin,
            boundaries,
            'jsx-a11y': jsxA11y,
            react,
            'react-hooks': reactHooks,
        },
        rules: {
            ...reactHooks.configs.recommended.rules,
            '@tanstack/query/exhaustive-deps': 'error',
            '@tanstack/query/no-unstable-deps': 'error',
            '@tanstack/query/no-void-query-fn': 'error',
            '@tanstack/query/stable-query-client': 'error',
            '@typescript-eslint/await-thenable': 'error',
            '@typescript-eslint/no-unused-vars': [
                'error',
                {
                    argsIgnorePattern: '^_',
                    varsIgnorePattern: '^_',
                },
            ],
            '@typescript-eslint/no-deprecated': 'error',
            '@typescript-eslint/no-base-to-string': 'error',
            '@typescript-eslint/no-floating-promises': 'error',
            '@typescript-eslint/no-misused-promises': [
                'error',
                {
                    checksVoidReturn: {
                        attributes: false,
                    },
                },
            ],
            '@typescript-eslint/no-unsafe-enum-comparison': 'error',
            '@typescript-eslint/only-throw-error': 'error',
            '@typescript-eslint/restrict-plus-operands': 'error',
            '@typescript-eslint/switch-exhaustiveness-check': 'error',
            '@typescript-eslint/use-unknown-in-catch-callback-variable': 'error',
            'boundaries/dependencies': [
                'error',
                {
                    default: 'allow',
                    policies: [
                        {
                            from: { element: { types: ['lib', 'types'] } },
                            disallow: { to: { element: { types: ['hooks', 'components', 'page'] } } },
                            message: 'Foundational modules cannot depend on React hooks, components, or pages.',
                        },
                        {
                            from: { element: { types: 'hooks' } },
                            disallow: { to: { element: { types: ['components', 'page'] } } },
                            message: 'Hooks cannot depend on components or pages.',
                        },
                        {
                            from: { element: { types: 'ui' } },
                            disallow: {
                                to: {
                                    element: {
                                        types: 'hooks',
                                    },
                                },
                            },
                            message: 'Generic UI cannot depend on feature or data hooks.',
                        },
                        {
                            from: { element: { types: 'page' } },
                            disallow: { to: { element: { types: 'page' } } },
                            message: 'Page entry modules cannot depend on another page entry module.',
                        },
                    ],
                },
            ],
            'jsx-a11y/alt-text': 'error',
            'jsx-a11y/anchor-has-content': 'error',
            'jsx-a11y/anchor-is-valid': [
                'error',
                {
                    components: ['Link', 'NavLink'],
                    specialLink: ['to'],
                    aspects: ['noHref', 'invalidHref', 'preferButton'],
                },
            ],
            'jsx-a11y/aria-props': 'error',
            'jsx-a11y/aria-proptypes': 'error',
            'jsx-a11y/aria-role': ['error', { ignoreNonDOM: true }],
            'jsx-a11y/aria-unsupported-elements': 'error',
            'jsx-a11y/autocomplete-valid': 'error',
            'jsx-a11y/heading-has-content': 'error',
            'jsx-a11y/iframe-has-title': 'error',
            'jsx-a11y/label-has-associated-control': 'error',
            'jsx-a11y/no-aria-hidden-on-focusable': 'error',
            'jsx-a11y/role-has-required-aria-props': 'error',
            'jsx-a11y/role-supports-aria-props': 'error',
            'jsx-a11y/tabindex-no-positive': 'error',
            'react/button-has-type': 'error',
            'react/iframe-missing-sandbox': 'error',
            'react/jsx-key': 'error',
            'react/jsx-no-comment-textnodes': 'error',
            'react/jsx-no-constructed-context-values': 'error',
            'react/jsx-no-script-url': [
                'error',
                [
                    { name: 'Link', props: ['to'] },
                    { name: 'NavLink', props: ['to'] },
                ],
            ],
            'react/jsx-no-target-blank': 'error',
            'react/no-danger': 'error',
            'react/no-danger-with-children': 'error',
            'react/no-invalid-html-attribute': 'error',
            'react/no-object-type-as-default-prop': 'error',
            'react/no-unknown-property': 'error',
            'react/no-unstable-nested-components': ['error', { allowAsProps: true }],
            'react/void-dom-elements-no-children': 'error',
        },
    },
    {
        files: ['src/components/ui/Button/Button.tsx'],
        rules: {
            'react/button-has-type': 'off',
        },
    },
    {
        files: ['**/*.{test,spec}.{ts,tsx}', 'src/test/**/*.{ts,tsx}'],
        rules: {
            '@tanstack/query/stable-query-client': 'off',
            '@typescript-eslint/no-base-to-string': 'off',
            'boundaries/dependencies': 'off',
            'react/button-has-type': 'off',
            'react/no-object-type-as-default-prop': 'off',
        },
    },
);
