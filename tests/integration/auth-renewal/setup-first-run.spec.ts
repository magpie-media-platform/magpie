import { expect, test } from '@playwright/test';

test('first administrator claim retains loading and retry state, then skips unavailable metadata in wizard order', async ({ page, request }, testInfo) => {
  let releaseSettings!: () => void;
  const settingsHeld = new Promise<void>((resolve) => { releaseSettings = resolve; });
  let settingsStarted = false;
  let claims = 0;
  let logins = 0;
  page.on('request', (call) => {
    if (call.url().endsWith('/api/system/setup/admin')) claims++;
    if (call.url().endsWith('/api/auth/login')) logins++;
  });
  await page.route('**/api/settings/groups', async (route) => {
    settingsStarted = true;
    await settingsHeld;
    await route.fulfill({ status: 503, contentType: 'application/json', body: '{"message":"Temporary settings failure"}' });
  });
  try {
    expect((await (await request.get('/api/system/setup/status')).json()).requiresAdminClaim).toBe(true);
    await page.goto('/setup');
    await expect(page.locator('[data-step="admin"]')).toBeVisible();
    await page.locator('#setup-username').fill('setup-fixture');
    await page.locator('#setup-password').fill('Local-Setup-Fixture!123');
    await page.locator('#setup-confirm-password').fill('Local-Setup-Fixture!123');
    await page.getByRole('button', { name: 'Create administrator' }).click();
    await expect.poll(() => settingsStarted).toBe(true);
    await expect(page.locator('[data-action="create-admin"]')).toBeDisabled();
    await expect(page.locator('[data-action="create-admin"]')).toContainText('Continue setup');
    await expect(page.locator('#setup-username')).toBeDisabled();
    expect(claims).toBe(1);
    expect(logins).toBe(1);
    await page.screenshot({ path: testInfo.outputPath('claimed-settings-pending.png'), fullPage: true });
    releaseSettings();
    await expect(page.getByTestId('setup-error')).toContainText('Administrator created, but setup failed to continue');
    await expect(page.getByRole('button', { name: 'Continue setup' })).toBeEnabled();
    await expect(page.locator('#setup-username')).toBeDisabled();
    await page.screenshot({ path: testInfo.outputPath('claimed-settings-failed.png'), fullPage: true });
    await page.unroute('**/api/settings/groups');
    await page.getByRole('button', { name: 'Continue setup' }).click();
    await expect(page.locator('[data-step="indexer"]')).toBeVisible();
    await expect(page.locator('[data-step="metadata"]')).toHaveCount(0);
    expect(claims).toBe(1);
    expect(logins).toBe(1);
    expect((await (await request.get('/api/system/setup/status')).json()).requiresAdminClaim).toBe(false);
    await page.getByRole('button', { name: /skip for now/i }).click();
    await expect(page.locator('[data-step="download"]')).toBeVisible();
    await page.getByRole('button', { name: /skip for now/i }).click();
    await expect(page.locator('[data-step="root"]')).toBeVisible();
    await page.getByRole('button', { name: /skip for now/i }).click();
    await expect(page.locator('[data-step="server"]')).toBeVisible();
    await page.getByRole('button', { name: /skip for now/i }).click();
    await expect(page.locator('[data-step="done"]')).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath('wizard-complete.png'), fullPage: true });
  } finally { releaseSettings(); }
});
