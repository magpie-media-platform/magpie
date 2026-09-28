import { test, expect, type Page } from '@playwright/test';

const password = 'Local-live-update-fixture-only!';

async function login(page: Page, origin = 'http://127.0.0.1:15476') {
  await page.goto(origin + '/login');
  await page.locator('[data-field="username"]').fill('live-updates-fixture');
  await page.locator('[data-field="password"]').fill(password);
  await page.locator('[data-action="sign-in"]').click();
  await expect(page.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
}

test.beforeAll(async ({ request }) => {
  const status = await (await request.get('/api/system/setup/status')).json();
  if (status.requiresAdminClaim) {
    const response = await request.post('/api/system/setup/admin', { data: { username: 'live-updates-fixture', password } });
    expect(response.ok()).toBe(true);
  }
  if (!status.isComplete) expect((await request.post('/api/system/setup/complete')).ok()).toBe(true);
});

test.beforeEach(async ({ request }) => {
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(0);
  await request.get('/__qa/control?mode=normal&reset=1');
});

test('ten production tabs share one authenticated HTTP/1 stream and keep ordinary requests usable', async ({ context, page, request }) => {
  await login(page);
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const tabs = [page];
  for (let index = 1; index < 10; index++) {
    const tab = await context.newPage();
    await tab.goto('/');
    await expect(tab.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
    tabs.push(tab);
  }
  const ordinary = await Promise.all(tabs.map((tab) => tab.evaluate(async () => (await fetch('/api/auth/permissions', { headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') } })).status)));
  expect(ordinary).toEqual(Array(10).fill(200));
  expect(await (await request.get('/__qa/state')).json()).toMatchObject({ active: 1, maxStreams: 1, streamStarts: 1 });
  await tabs[0]!.close();
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
  await request.get('/__qa/control?deploy=1');
  const newer = await context.newPage();
  await newer.goto('/');
  await expect(newer.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect((await (await request.get('/__qa/state')).json()).streamStarts).toBe(1);
  await Promise.all([...tabs.slice(1), newer].map((tab) => tab.close()));
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(0);
});

test('reconnects and refreshes the current view after a real stream interruption', async ({ page, request }) => {
  await login(page);
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await page.goto('/admin/jobs');
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const before = (await (await request.get('/__qa/state')).json()).calls;
  await request.get('/__qa/control?close=1');
  await expect(page.getByRole('button', { name: /Connection lost/ })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible({ timeout: 15000 });
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).calls['/api/jobs']).toBeGreaterThan(before['/api/jobs'] ?? 0);
  expect((await (await request.get('/__qa/state')).json()).maxStreams).toBe(1);
});

test('shows terminal failures in red with retry and keeps login independent', async ({ page, request }) => {
  await request.get('/__qa/control?mode=terminal');
  await login(page);
  const indicator = page.getByRole('button', { name: 'Live updates failed', exact: true });
  await expect(indicator).toBeVisible({ timeout: 15000 });
  await indicator.hover();
  await expect(page.getByRole('dialog', { name: 'Live updates' })).toBeVisible();
  await page.getByRole('button', { name: 'Retry live updates' }).focus();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Live updates' })).toHaveCount(0);
  await expect(indicator).toBeFocused();
  await indicator.click();
  await request.get('/__qa/control?mode=normal');
  await page.getByRole('button', { name: 'Retry live updates' }).click();
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect(await page.evaluate(async () => (await fetch('/api/auth/permissions', { headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') } })).status)).toBe(200);
});

test('keeps unsupported browsers usable and shows mobile modal with a separate 44px target', async ({ context, page }) => {
  await context.addInitScript(() => Object.defineProperty(window, 'SharedWorker', { value: undefined }));
  await page.setViewportSize({ width: 390, height: 844 });
  await login(page);
  const indicator = page.getByRole('button', { name: /not supported in this browser/ });
  await expect(indicator).toBeVisible();
  const box = await indicator.boundingBox();
  expect(box!.width).toBeGreaterThanOrEqual(44);
  expect(box!.height).toBeGreaterThanOrEqual(44);
  await indicator.click();
  await expect(page.getByRole('dialog', { name: 'Live updates' })).toBeVisible();
  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await expect(indicator).toBeFocused();
  await page.screenshot({ path: 'test-results/live-updates/mobile.png', fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.setViewportSize({ width: 320, height: 700 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('distinguishes worker startup errors from unsupported browsers', async ({ page, request }) => {
  await request.get('/__qa/control?mode=worker-failure');
  await login(page);
  await expect(page.getByRole('button', { name: 'Live updates failed', exact: true })).toBeVisible();
  expect(await page.getByRole('button', { name: /not supported/ }).count()).toBe(0);
  expect((await (await request.get('/__qa/state')).json()).active).toBe(0);
});

test('isolates same-host ports and stops the old stream after logout', async ({ context, page, request }) => {
  await login(page);
  const other = await context.newPage();
  await login(other, 'http://127.0.0.1:15478');
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(2);
  await page.getByRole('button', { name: 'User menu for live-updates-fixture' }).click();
  await page.getByRole('menuitem', { name: /log out/i }).click();
  await expect(page.locator('[data-action="sign-in"]')).toBeVisible();
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(1);
});

test('recovers after the browser terminates the actual SharedWorker', async ({ context, page, request }) => {
  test.setTimeout(90000);
  await login(page);
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const cdp = await context.newCDPSession(page);
  const targets = await cdp.send('Target.getTargets');
  const own = await cdp.send('Target.getTargetInfo');
  const worker = targets.targetInfos.find((target: { type: string; url: string; browserContextId?: string }) => target.type === 'shared_worker' && target.url.includes('magpie-events-worker.js') && target.browserContextId === own.targetInfo.browserContextId);
  expect(worker).toBeTruthy();
  const browserCdp = await context.browser()!.newBrowserCDPSession();
  const attached = await browserCdp.send('Target.attachToTarget', { targetId: worker!.targetId, flatten: false });
  await browserCdp.send('Target.sendMessageToTarget', { sessionId: attached.sessionId, message: JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: 'self.close()' } }) });
  await browserCdp.send('Target.detachFromTarget', { sessionId: attached.sessionId }).catch(() => {});
  await browserCdp.detach();
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(0);
  try {
    await expect.poll(async () => (await (await request.get('/__qa/state')).json()).streamStarts, { timeout: 60000 }).toBe(2);
  } finally {
    console.log('Worker recovery:', await page.evaluate(() => ({ visible: document.visibilityState, indicator: document.querySelector('.live-update-status')?.getAttribute('aria-label') })), await (await request.get('/__qa/state')).json());
  }
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect((await (await request.get('/__qa/state')).json()).maxStreams).toBe(1);
});

test('reconciles a genuinely frozen page and a page restored through browser history', async ({ context, page, request }) => {
  await login(page);
  await page.goto('/admin/jobs');
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const other = await context.newPage();
  await other.goto('/');
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const cdp = await context.newCDPSession(page);
  const before = (await (await request.get('/__qa/state')).json()).calls['/api/jobs'] ?? 0;
  await cdp.send('Page.setWebLifecycleState', { state: 'frozen' });
  await request.get('/__qa/control?close=1');
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await cdp.send('Page.setWebLifecycleState', { state: 'active' });
  await page.bringToFront();
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).calls['/api/jobs']).toBeGreaterThan(before);
  await page.goto('/offline.html');
  await page.goBack();
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
});

test('opens desktop details on click and keyboard focus without changing layout', async ({ page }) => {
  await login(page);
  const indicator = page.getByRole('button', { name: 'Live updates connected.' });
  await expect(indicator).toBeVisible();
  const before = await page.locator('[data-component="header"]').boundingBox();
  await indicator.click();
  await expect(page.getByRole('dialog', { name: 'Live updates' })).toBeVisible();
  expect(await page.locator('[data-component="header"]').boundingBox()).toEqual(before);
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Live updates' })).toHaveCount(0);
  await page.getByRole('button', { name: 'User menu for live-updates-fixture' }).focus();
  await indicator.focus();
  await expect(page.getByRole('dialog', { name: 'Live updates' })).toBeVisible();
  await page.screenshot({ path: 'test-results/live-updates/desktop.png' });
});

test('keeps session renewal independent while two participating pages reload', async ({ context, page, request }) => {
  await login(page);
  const other = await context.newPage();
  await other.goto('/');
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await page.evaluate(() => localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z'));
  await Promise.all([page.reload(), other.reload()]);
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
});

test('recovers automatically from a temporary 503 handshake without showing red', async ({ page, request }) => {
  await login(page);
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await request.get('/__qa/control?mode=temporary&close=1');
  await expect(page.getByRole('button', { name: /Connection lost/ })).toBeVisible();
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).calls['/api/events/stream']).toBeGreaterThan(1);
  await expect(page.getByRole('button', { name: /Connection lost/ })).toBeVisible();
  await request.get('/__qa/control?mode=normal');
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible({ timeout: 15000 });
  expect((await (await request.get('/__qa/state')).json()).maxStreams).toBe(1);
});

test('replaces the session in both tabs after an account change without retaining the old stream', async ({ context, page, request }) => {
  await login(page);
  const created = await page.evaluate(async (fixturePassword) => (await fetch('/api/users', {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') },
    body: JSON.stringify({ username: 'live-updates-other', password: fixturePassword, role: 'Viewer', enabled: true }),
  })).status, password);
  expect(created).toBe(201);
  const other = await context.newPage();
  await other.goto('/');
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  await page.getByRole('button', { name: 'User menu for live-updates-fixture' }).click();
  await page.getByRole('menuitem', { name: /log out/i }).click();
  await expect(other.locator('[data-action="sign-in"]')).toBeVisible();
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(0);
  await page.locator('[data-field="username"]').fill('live-updates-other');
  await page.locator('[data-field="password"]').fill(password);
  await page.locator('[data-action="sign-in"]').click();
  await expect(page.getByRole('button', { name: 'User menu for live-updates-other' })).toBeVisible();
  await expect(other.getByRole('button', { name: 'User menu for live-updates-other' })).toBeVisible();
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
});
