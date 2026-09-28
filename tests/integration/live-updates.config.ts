import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './live-updates',
  testMatch: '*.spec.ts',
  outputDir: './test-results/live-updates',
  workers: 1,
  timeout: 60_000,
  use: { baseURL: 'http://127.0.0.1:15476', trace: 'retain-on-failure' },
  webServer: {
    command: 'node live-updates/server.cjs',
    url: 'http://127.0.0.1:15476/__qa/ready',
    timeout: 90_000,
    reuseExistingServer: false,
  },
});
