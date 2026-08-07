/// <reference types="vitest/config" />
import path from 'path';

import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

const reactDomVendorPackages = new Set(['react-dom', 'scheduler']);
const routerVendorPackages = new Set(['react-router', 'react-router-dom']);

function getNodeModulePackageName(id: string): string | undefined {
    const normalizedId = id.replace(/\\/g, '/');

    if (!normalizedId.includes('/node_modules/')) {
        return undefined;
    }

    const modulePath = normalizedId.slice(normalizedId.lastIndexOf('/node_modules/') + '/node_modules/'.length);
    const [scopeOrName, scopedName] = modulePath.split('/');

    return scopeOrName.startsWith('@') ? `${scopeOrName}/${scopedName}` : scopeOrName;
}

export default defineConfig(({ mode }) => {
    const env = loadEnv(mode, process.cwd(), '');

    return {
        base: env.VITE_BASE_URL || '/',
        plugins: [react()],
        resolve: {
            dedupe: ['react', 'react-dom', '@codemirror/state'],
            alias: {
                '@/auth': path.resolve(__dirname, './src/auth'),
                '@/components': path.resolve(__dirname, './src/components'),
                '@/editor': path.resolve(__dirname, './src/editor'),
                '@/extensions': path.resolve(__dirname, './src/extensions'),
                '@/hooks': path.resolve(__dirname, './src/hooks'),
                '@/lib': path.resolve(__dirname, './src/lib'),
                '@/pages': path.resolve(__dirname, './src/pages'),
                '@/router': path.resolve(__dirname, './src/router'),
                '@/styles': path.resolve(__dirname, './src/styles'),
                '@/test': path.resolve(__dirname, './src/test'),
                '@/types': path.resolve(__dirname, './src/types'),
            },
        },
        server: {
            proxy: {
                '/api': {
                    target: env.VITE_API_PROXY_TARGET || 'http://localhost:5000',
                    changeOrigin: true,
                },
            },
        },
        build: {
            rolldownOptions: {
                output: {
                    manualChunks(id) {
                        const packageName = getNodeModulePackageName(id);

                        if (!packageName) {
                            return undefined;
                        }

                        if (packageName.startsWith('@codemirror/') || packageName.startsWith('@lezer/')) {
                            return 'editor-vendor';
                        }

                        if (packageName === '@phosphor-icons/react') {
                            return 'icons-vendor';
                        }

                        if (packageName.startsWith('@floating-ui/') || packageName.startsWith('@radix-ui/')) {
                            return 'radix-vendor';
                        }

                        if (packageName.startsWith('@tanstack/')) {
                            return 'tanstack-vendor';
                        }

                        if (packageName === 'react') {
                            return 'react-vendor';
                        }

                        if (reactDomVendorPackages.has(packageName)) {
                            return 'react-dom-vendor';
                        }

                        if (routerVendorPackages.has(packageName)) {
                            return 'router-vendor';
                        }

                        return 'vendor';
                    },
                },
            },
        },
        test: {
            globals: true,
            environment: 'jsdom',
            setupFiles: ['./src/test/react-act-environment.ts', './src/test/setup.ts'],
            exclude: ['**/node_modules/**/*', '**/e2e/**'],
            maxWorkers: 4,
        },
    };
});
