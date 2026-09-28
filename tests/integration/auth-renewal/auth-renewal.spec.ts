import { test, expect, type Page } from '@playwright/test';

const password = 'Local-live-update-fixture-only!';

for (const accountChange of [false, true]) {
  test(`pending startup survives a real ${accountChange ? 'different-account' : 'same-account new-session'} login`, async ({ page, context }) => {
    await login(page);
    const username = accountChange ? 'startup-other-' + test.info().repeatEachIndex : 'live-updates-fixture';
    if (accountChange) {
      const created = await page.evaluate(async (account) => (await fetch('/api/users', {
        method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') },
        body: JSON.stringify({ username: account.username, password: account.password, role: 'Viewer', enabled: true }),
      })).status, { username, password });
      expect(created).toBe(201);
    }
    const other = await context.newPage();
    let releaseSetup!: () => void;
    let releasePermissions!: () => void;
    const setupHold = new Promise<void>((resolve) => { releaseSetup = resolve; });
    const permissionsHold = new Promise<void>((resolve) => { releasePermissions = resolve; });
    let setupPending = false;
    let permissionsPending = false;
    const observed: { setupStatus?: number; permissionsStatus?: number; setupReleased?: boolean } = {};
    await other.route('**/api/system/setup/status', async (route) => {
      const response = await route.fetch();
      observed.setupStatus = response.status();
      setupPending = true;
      await setupHold;
      await route.fulfill({ response });
      observed.setupReleased = true;
    });
    await other.route('**/api/auth/permissions', async (route) => {
      const response = await route.fetch();
      observed.permissionsStatus = response.status();
      permissionsPending = true;
      await permissionsHold;
      await route.fulfill({ response });
    });
    try {
      await other.goto('/');
      await expect.poll(() => setupPending && permissionsPending).toBe(true);
      await page.getByRole('button', { name: 'User menu for live-updates-fixture' }).click();
      await page.getByRole('menuitem', { name: /log out/i }).click();
      await expect(page.locator('[data-action="sign-in"]')).toBeVisible();
      await page.locator('[data-field="username"]').fill(username);
      await page.locator('[data-field="password"]').fill(password);
      await page.locator('[data-action="sign-in"]').click();
      await expect(page.getByRole('button', { name: 'User menu for ' + username })).toBeVisible();
      releaseSetup();
      releasePermissions();
      await expect.poll(() => observed.setupReleased).toBe(true);
      expect(observed.setupStatus).toBe(200);
      expect(observed.permissionsStatus).toBe(200);
      await expect(other.getByRole('button', { name: 'User menu for ' + username })).toBeVisible();
      if (accountChange) await expect(other.getByRole('link', { name: 'Admin', exact: true })).toHaveCount(0);
    } finally {
      releaseSetup();
      releasePermissions();
      await other.screenshot({ path: test.info().outputPath('pending-startup-final.png'), fullPage: true });
    }
  });
}

test('expired-token startup observer survives another tab publishing renewal', async ({ page, context }) => {
  await login(page);
  const other = await context.newPage();
  await other.clock.setFixedTime(new Date(Date.now() + 16 * 60_000));
  let releaseSetup!: () => void;
  let releaseRenewal!: () => void;
  const setupHold = new Promise<void>((resolve) => { releaseSetup = resolve; });
  const renewalHold = new Promise<void>((resolve) => { releaseRenewal = resolve; });
  let setupPending = false;
  let renewalPending = false;
  await other.route('**/api/system/setup/status', async (route) => {
    const response = await route.fetch();
    setupPending = true;
    await setupHold;
    await route.fulfill({ response });
  });
  await other.route('**/api/auth/refresh', async (route) => {
    renewalPending = true;
    await renewalHold;
    await route.continue();
  });
  await other.goto('/');
  await expect.poll(() => setupPending && renewalPending).toBe(true);
  await other.clock.setFixedTime(new Date());
  const renewed = await page.evaluate(async () => {
    const response = await fetch('/api/auth/refresh', {
      method: 'POST', headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') },
    });
    const session = await response.json();
    localStorage.setItem('magpie_auth_token', session.accessToken);
    localStorage.setItem('magpie_token_expires', session.accessTokenExpiresAt);
    return response.status;
  });
  expect(renewed).toBe(200);
  releaseRenewal();
  releaseSetup();
  await expect(other.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
});

async function login(page: Page) {
  await page.goto('/login');
  await page.locator('[data-field="username"]').fill('live-updates-fixture');
  await page.locator('[data-field="password"]').fill(password);
  await page.locator('[data-action="sign-in"]').click();
  await expect(page.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
}

test.beforeAll(async ({ request }) => {
  const status = await (await request.get('/api/system/setup/status')).json();
  if (status.requiresAdminClaim) expect((await request.post('/api/system/setup/admin', { data: { username: 'live-updates-fixture', password } })).ok()).toBe(true);
  if (!status.isComplete) expect((await request.post('/api/system/setup/complete')).ok()).toBe(true);
});

test.beforeEach(async ({ request }) => {
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(0);
  await request.get('/__qa/control?mode=normal&renewal=normal&account=enable&reset=1');
});

test('repeated two-tab reload overlap retains auth and exactly one shared stream', async ({ page, context, request }) => {
  test.setTimeout(120_000);
  await login(page);
  const other = await context.newPage();
  await other.goto('/');
  for (let attempt = 0; attempt < 10; attempt++) {
    await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
    await page.evaluate(() => localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z'));
    await Promise.all([page.reload(), other.reload()]);
    for (const tab of [page, other]) {
      await expect(tab.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
      await expect(tab.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
      expect(await tab.evaluate(async () => (await fetch('/api/auth/permissions', { headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') } })).status)).toBe(200);
    }
    expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
  }
});

for (const mode of ['drop-before', 'drop-after', 'drop-body', '503']) {
  test(`recovers ${mode} without a worker, lost credentials, or another login`, async ({ page, context, request }) => {
    await context.addInitScript(() => Object.defineProperty(window, 'SharedWorker', { value: undefined }));
    await login(page);
    const other = await context.newPage();
    await other.goto('/');
    await expect(other.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
    const previous = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
    await request.get('/__qa/control?renewal=' + mode);
    await page.evaluate(() => localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z'));
    await page.reload();
    await expect.poll(() => page.evaluate(() => localStorage.getItem('magpie_auth_token')), { timeout: 20_000 }).not.toBe(previous);
    for (const tab of [page, other]) {
      await expect(tab.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible({ timeout: 20_000 });
      await expect(tab.getByRole('button', { name: /not supported in this browser/ })).toBeVisible();
      expect(await tab.evaluate(async () => (await fetch('/api/auth/permissions', { headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') } })).status)).toBe(200);
    }
    expect((await (await request.get('/__qa/state')).json()).active).toBe(0);
  });
}

test('keeps the mounted startup observer usable when another tab publishes renewal', async ({ page, context, request }) => {
  await login(page);
  const other = await context.newPage();
  let releaseSetup!: () => void;
  let releasePermissions!: () => void;
  const setupHold = new Promise<void>((resolve) => { releaseSetup = resolve; });
  const permissionsHold = new Promise<void>((resolve) => { releasePermissions = resolve; });
  let setupPending = false;
  let permissionsPending = false;
  await other.route('**/api/system/setup/status', async (route) => { const response = await route.fetch(); setupPending = true; await setupHold; await route.fulfill({ response }); });
  await other.route('**/api/auth/permissions', async (route) => { const response = await route.fetch(); permissionsPending = true; await permissionsHold; await route.fulfill({ response }); });
  await other.goto('/');
  await expect.poll(() => setupPending && permissionsPending).toBe(true);
  const previous = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  await other.evaluate(() => localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z'));
  await expect.poll(() => other.evaluate(() => localStorage.getItem('magpie_auth_token'))).not.toBe(previous);
  releaseSetup(); releasePermissions();
  await expect(other.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
});

test('explicit logout revokes a committed response held in flight', async ({ page, context, request }) => {
  await login(page);
  const proof = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  const cookie = (await context.cookies()).find((value) => value.name === 'magpie.refresh.15423')!;
  await request.get('/__qa/control?renewal=hold-after');
  await page.evaluate(() => {
    localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z');
    window.dispatchEvent(new StorageEvent('storage', { key: 'magpie_token_expires', newValue: '2020-01-01T00:00:00Z' }));
  });
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).heldRenewals).toBe(1);
  await page.getByRole('button', { name: 'User menu for live-updates-fixture' }).click();
  await page.getByRole('menuitem', { name: /log out/i }).click();
  await request.get('/__qa/control?releaseRenewal=1');
  await expect(page.locator('[data-action="sign-in"]')).toBeVisible();
  await expect.poll(() => page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
  const replay = await request.post('/api/auth/refresh', { headers: { Cookie: `${cookie.name}=${cookie.value}`, Authorization: `Bearer ${proof}` } });
  expect(replay.status()).toBe(401);
  await page.reload();
  await expect(page.locator('[data-action="sign-in"]')).toBeVisible();
});

for (const boundary of ['revoke', 'disable', 'lock', 'expire', 'expire-original']) {
  test(`rejects interrupted-response recovery after ${boundary}`, async ({ page, context, request }) => {
    await login(page);
    const proof = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
    const cookie = (await context.cookies()).find((value) => value.name === 'magpie.refresh.15423')!;
    const headers = { Cookie: `${cookie.name}=${cookie.value}`, Authorization: `Bearer ${proof}` };
    expect((await request.post('/api/auth/refresh', { headers })).status()).toBe(200);
    await request.get('/__qa/control?account=' + boundary);
    expect((await request.post('/api/auth/refresh', { headers })).status()).toBe(401);
    expect((await request.post('/api/auth/refresh', { headers: { ...headers, Authorization: 'Bearer different-proof' } })).status()).toBe(401);
  });
}

test('concurrent retry requests recover one rotation while different proofs stay rejected', async ({ page, context, request }) => {
  await login(page);
  const proof = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  const cookie = (await context.cookies()).find((value) => value.name === 'magpie.refresh.15423')!;
  const headers = { Cookie: `${cookie.name}=${cookie.value}`, Authorization: `Bearer ${proof}` };
  const responses = await Promise.all([request.post('/api/auth/refresh', { headers }), request.post('/api/auth/refresh', { headers })]);
  expect(responses.map((response) => response.status())).toEqual([200, 200]);
  const first = await responses[0]!.json();
  expect(await responses[1]!.json()).toEqual(first);
  expect((await request.post('/api/auth/refresh', { headers: { ...headers, Authorization: 'Bearer different-proof' } })).status()).toBe(401);
  expect((await request.post('/api/auth/refresh', { headers: { Cookie: headers.Cookie } })).status()).toBe(401);
});

test('account switch waits for pending renewal and cannot revive the explicitly logged-out session', async ({ page, context, request }) => {
  await login(page);
  const username = 'renewal-other-' + test.info().repeatEachIndex;
  const created = await page.evaluate(async (account) => (await fetch('/api/users', {
    method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') },
    body: JSON.stringify({ username: account.username, password: account.password, role: 'Viewer', enabled: true }),
  })).status, { username, password });
  expect(created).toBe(201);
  const proof = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  const cookie = (await context.cookies()).find((value) => value.name === 'magpie.refresh.15423')!;
  await request.get('/__qa/control?renewal=hold-after');
  await page.evaluate(() => {
    localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z');
    window.dispatchEvent(new StorageEvent('storage', { key: 'magpie_token_expires', newValue: '2020-01-01T00:00:00Z' }));
  });
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).heldRenewals).toBe(1);
  await page.getByRole('button', { name: 'User menu for live-updates-fixture' }).click();
  await page.getByRole('menuitem', { name: /log out/i }).click();
  await expect(page.locator('[data-action="sign-in"]')).toBeVisible();
  await page.locator('[data-field="username"]').fill(username);
  await page.locator('[data-field="password"]').fill(password);
  await page.locator('[data-action="sign-in"]').click();
  await request.get('/__qa/control?releaseRenewal=1');
  await expect(page.getByRole('button', { name: 'User menu for ' + username })).toBeVisible();
  expect((await request.post('/api/auth/refresh', { headers: { Cookie: `${cookie.name}=${cookie.value}`, Authorization: `Bearer ${proof}` } })).status()).toBe(401);
  await page.reload();
  await expect(page.getByRole('button', { name: 'User menu for ' + username })).toBeVisible();
});

test('same-host origins cannot use another instance cookie and proof', async ({ page, context, request }) => {
  await login(page);
  const proof = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  const cookie = (await context.cookies()).find((value) => value.name === 'magpie.refresh.15423')!;
  const response = await request.post('http://127.0.0.1:15424/api/auth/refresh', {
    headers: { Cookie: `${cookie.name}=${cookie.value}`, Authorization: `Bearer ${proof}` },
  });
  expect(response.status()).toBe(401);
  expect((await request.post('/api/auth/refresh', { headers: { Cookie: `${cookie.name}=${cookie.value}`, Authorization: `Bearer ${proof}` } })).status()).toBe(200);
});

test('a suspended page resumes after interrupted renewal in another tab without losing its session', async ({ page, context, request }) => {
  await login(page);
  const other = await context.newPage();
  await other.goto('/');
  await expect(other.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const previous = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  const cdp = await context.newCDPSession(page);
  await cdp.send('Page.setWebLifecycleState', { state: 'frozen' });
  await request.get('/__qa/control?renewal=drop-after');
  await other.evaluate(() => {
    localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z');
    window.dispatchEvent(new StorageEvent('storage', { key: 'magpie_token_expires', newValue: '2020-01-01T00:00:00Z' }));
  });
  await expect.poll(() => other.evaluate(() => localStorage.getItem('magpie_auth_token')), { timeout: 20_000 }).not.toBe(previous);
  await cdp.send('Page.setWebLifecycleState', { state: 'active' });
  await page.bringToFront();
  await expect(page.getByRole('button', { name: 'User menu for live-updates-fixture' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  expect(await page.evaluate(async () => (await fetch('/api/auth/permissions', { headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') } })).status)).toBe(200);
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
});

test('ten participating tabs retain one stream and usable ordinary requests after renewal', async ({ page, context, request }) => {
  await login(page);
  const tabs = [page];
  for (let index = 1; index < 10; index++) {
    const tab = await context.newPage();
    await tab.goto('/');
    await expect(tab.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
    tabs.push(tab);
  }
  const previous = await page.evaluate(() => localStorage.getItem('magpie_auth_token'));
  await request.get('/__qa/control?renewal=drop-after');
  await page.evaluate(() => localStorage.setItem('magpie_token_expires', '2020-01-01T00:00:00Z'));
  await expect.poll(() => page.evaluate(() => localStorage.getItem('magpie_auth_token')), { timeout: 20_000 }).not.toBe(previous);
  for (const tab of tabs) await expect(tab.getByRole('button', { name: 'Live updates connected.' })).toBeVisible();
  const statuses = await Promise.all(tabs.map((tab) => tab.evaluate(async () => (await fetch('/api/auth/permissions', { headers: { Authorization: 'Bearer ' + localStorage.getItem('magpie_auth_token') } })).status)));
  expect(statuses).toEqual(Array(10).fill(200));
  expect((await (await request.get('/__qa/state')).json()).active).toBe(1);
  await Promise.all(tabs.map((tab) => tab.close()));
  await expect.poll(async () => (await (await request.get('/__qa/state')).json()).active).toBe(0);
});
