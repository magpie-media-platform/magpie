/// <reference types="vitest/config" />
import path from 'path';

import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
    // Load env file based on mode
    const env = loadEnv(mode, process.cwd(), '');

    return {
        // Base path for assets - defaults to '/' for embedded, can be set via VITE_BASE_URL for CDN
        base: env.VITE_BASE_URL || '/',
        plugins: [react()],
        resolve: {
            alias: {
                '@/auth': path.resolve(__dirname, './src/auth'),
                '@/components': path.resolve(__dirname, './src/components'),
                '@/editor': path.resolve(__dirname, './src/editor'),
                '@/extensions': path.resolve(__dirname, './src/extensions'),
                '@/hooks': path.resolve(__dirname, './src/hooks'),
                '@/layouts': path.resolve(__dirname, './src/layouts'),
                '@/lib': path.resolve(__dirname, './src/lib'),
                '@/pages': path.resolve(__dirname, './src/pages'),
                '@/realtime': path.resolve(__dirname, './src/realtime'),
                '@/router': path.resolve(__dirname, './src/router'),
                '@/styles': path.resolve(__dirname, './src/styles'),
                '@/types': path.resolve(__dirname, './src/types'),
            },
        },
        test: {
            globals: true,
            environment: 'jsdom',
            setupFiles: './src/test/setup.ts',
            exclude: ['**/node_modules/**', '**/e2e/**'],
        },
    };
});
