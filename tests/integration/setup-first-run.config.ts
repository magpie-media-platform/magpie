import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './auth-renewal', testMatch: 'setup-first-run.spec.ts', outputDir: './test-results/setup-first-run',
  workers: 1, timeout: 60_000,
  use: { baseURL: 'http://127.0.0.1:15423', trace: 'retain-on-failure' },
  webServer: { command: 'node auth-renewal/server.cjs', url: 'http://127.0.0.1:15423/__qa/ready', timeout: 90_000, reuseExistingServer: false },
});
