import { defineConfig } from '@playwright/test';

const baseURL = `http://127.0.0.1:${process.env.MAGPIE_FILTER_TEST_PORT ?? '15423'}`;

export default defineConfig({
    testDir: './content-filters',
    testMatch: '*.spec.ts',
    workers: 1,
    timeout: 60_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { outputFolder: 'content-filters-report', open: 'never' }]],
    outputDir: 'content-filters-results',
    use: { baseURL, browserName: 'chromium', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
    webServer: { command: 'node content-filters/server.cjs', url: baseURL + '/__qa/ready', timeout: 60_000, reuseExistingServer: false },
});
