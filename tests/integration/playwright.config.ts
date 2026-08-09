import { defineConfig, devices } from '@playwright/test';

const baseURL = process.env['MAGPIE_BASE_URL'] ?? 'http://localhost:8080';

export default defineConfig({
    testDir: './specs',
    fullyParallel: false,
    workers: 1,
    timeout: 120_000,
    expect: { timeout: 15_000 },
    forbidOnly: !!process.env['CI'],
    retries: 0,
    reporter: process.env['CI'] ? [['github'], ['html', { open: 'never' }]] : 'list',
    globalSetup: './helpers/global-setup.ts',
    use: {
        baseURL,
        actionTimeout: 15_000,
        contextOptions: { reducedMotion: 'reduce' },
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
        video: 'retain-on-failure',
    },
    projects: [
        {
            name: 'chromium',
            use: { ...devices['Desktop Chrome'] },
        },
    ],
});
