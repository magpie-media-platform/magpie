import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './session-renewal',
  outputDir: './test-results/session-renewal',
  workers: 1,
  timeout: 30_000,
  use: { baseURL: 'http://127.0.0.1:14325', trace: 'retain-on-failure' },
  webServer: [{
    command: 'pnpm --dir ../../ui exec vite --host 127.0.0.1 --port 14325 --strictPort',
    url: 'http://127.0.0.1:14325/e2e/session.html',
    reuseExistingServer: false,
    env: { VITE_API_URL: '/api' },
  }, {
    command: 'pnpm --dir ../../ui exec vite --host 127.0.0.1 --port 14326 --strictPort',
    url: 'http://127.0.0.1:14326/e2e/session.html',
    reuseExistingServer: false,
    env: { VITE_API_URL: '/api' },
  }],
});
