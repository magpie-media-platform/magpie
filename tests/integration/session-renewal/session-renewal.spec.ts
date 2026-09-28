import { test, expect } from '@playwright/test';

for (const fallback of [false, true]) {
  test.describe(fallback ? 'HTTP-compatible IndexedDB coordination' : 'Web Locks coordination', () => {
    test.beforeEach(async ({ context, page }) => {
      if (fallback) await context.addInitScript(() => {
        Object.defineProperty(navigator, 'locks', { value: undefined });
        Object.defineProperty(crypto, 'randomUUID', { value: undefined });
      });
      await page.goto('/e2e/session.html');
      await page.evaluate(async () => {
        const path = '/src/lib/tokenStorage.ts';
        const storage = await import(path);
        storage.storeAuthTokens('old-token', '2020-01-01T00:00:00Z');
      });
      await context.addCookies([{ name: 'magpie.refresh.14325', value: 'r0', domain: '127.0.0.1', path: '/api/auth', httpOnly: true, sameSite: 'Strict' }]);
    });

    for (const responseOrder of ['authentication-failure-first', 'renewal-first']) {
      test(`retains two tabs with ${responseOrder}`, async ({ context, page }) => {
        const other = await context.newPage();
        await other.goto('/e2e/session.html');
        let releaseRefresh!: () => void;
        let releaseUnauthorized!: () => void;
        const refreshGate = new Promise<void>((resolve) => { releaseRefresh = resolve; });
        const unauthorizedGate = new Promise<void>((resolve) => { releaseUnauthorized = resolve; });
        let refreshCalls = 0;
        let apiCalls = 0;
        await context.route('**/api/auth/refresh', async (route) => {
          refreshCalls++;
          expect((await route.request().allHeaders()).cookie).toContain('magpie.refresh.14325=r0');
          await refreshGate;
          await route.fulfill({ json: { accessToken: 'renewed-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': 'magpie.refresh.14325=r1; Path=/api/auth; HttpOnly; SameSite=Strict' } });
        });
        await context.route('**/api/content', async (route) => {
          apiCalls++;
          if (route.request().headers().authorization === 'Bearer old-token') {
            await unauthorizedGate;
            await route.fulfill({ status: 401, json: { title: 'Expired access token' } });
          } else {
            expect(route.request().headers().authorization).toBe('Bearer renewed-token');
            await route.fulfill({ json: { ok: true } });
          }
        });
        const refresh = page.evaluate(async () => { const path = '/src/lib/api.ts'; return (await import(path)).renewAccessToken(); });
        await expect.poll(() => refreshCalls).toBe(1);
        const request = other.evaluate(async () => { const path = '/src/lib/api.ts'; return (await import(path)).apiFetch('/content'); });
        await expect.poll(() => apiCalls).toBe(1);
        if (responseOrder === 'authentication-failure-first') {
          releaseUnauthorized();
          await other.waitForTimeout(100);
          releaseRefresh();
        } else {
          releaseRefresh();
          await refresh;
          releaseUnauthorized();
        }
        await expect(refresh).resolves.toBe('renewed-token');
        await expect(request).resolves.toEqual({ ok: true });
        expect(refreshCalls).toBe(1);
        expect(apiCalls).toBe(2);
        expect(await page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBe('renewed-token');
        expect(await other.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBe('renewed-token');
        expect((await context.cookies()).find((cookie) => cookie.name === 'magpie.refresh.14325')?.value).toBe('r1');
      });
    }

    test('serializes two simultaneous renewals of a single-use cookie', async ({ context, page }) => {
      const other = await context.newPage();
      await other.goto('/e2e/session.html');
      let release!: () => void;
      const gate = new Promise<void>((resolve) => { release = resolve; });
      let calls = 0;
      await context.route('**/api/auth/refresh', async (route) => {
        calls++;
        await gate;
        await route.fulfill({ json: { accessToken: 'renewed-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': 'magpie.refresh.14325=r1; Path=/api/auth; HttpOnly; SameSite=Strict' } });
      });
      const first = page.evaluate(async () => { const path = '/src/lib/api.ts'; return (await import(path)).renewAccessToken(); });
      await expect.poll(() => calls).toBe(1);
      await other.evaluate(async () => {
        const path = '/src/lib/api.ts';
        const api = await import(path);
        (window as unknown as { renewal: Promise<string> }).renewal = api.renewAccessToken();
      });
      release();
      expect(await first).toBe('renewed-token');
      expect(await other.evaluate(() => (window as unknown as { renewal: Promise<string> }).renewal)).toBe('renewed-token');
      expect(calls).toBe(1);
    });

    test('recovers after connection interruption and HTTP 503 without a login', async ({ context, page }) => {
      await page.evaluate(async () => { const path = '/src/lib/api.ts'; await import(path); });
      await context.setOffline(true);
      const offline = await page.evaluate(async () => {
        const path = '/src/lib/api.ts';
        try { await (await import(path)).renewAccessToken(); return 'unexpected success'; } catch { return localStorage.getItem('magpie_auth_token'); }
      });
      expect(offline).toBe('old-token');
      await context.setOffline(false);
      let calls = 0;
      await context.route('**/api/auth/refresh', async (route) => {
        calls++;
        if (calls === 1) await route.fulfill({ status: 503, json: { title: 'Temporarily unavailable' } });
        else await route.fulfill({ json: { accessToken: 'recovered-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': 'magpie.refresh.14325=r1; Path=/api/auth; HttpOnly; SameSite=Strict' } });
      });
      const temporary = await page.evaluate(async () => {
        const path = '/src/lib/api.ts';
        try { await (await import(path)).renewAccessToken(); return 'unexpected success'; } catch { return localStorage.getItem('magpie_auth_token'); }
      });
      expect(temporary).toBe('old-token');
      expect(await page.evaluate(async () => { const path = '/src/lib/api.ts'; return (await import(path)).renewAccessToken(); })).toBe('recovered-token');
    });

    test('keeps logout effective when a successful renewal is in flight', async ({ context, page }) => {
      const other = await context.newPage();
      await other.goto('/e2e/session.html');
      let release!: () => void;
      const gate = new Promise<void>((resolve) => { release = resolve; });
      let refreshStarted = false;
      let logoutCookie = '';
      await context.route('**/api/auth/refresh', async (route) => {
        refreshStarted = true;
        await gate;
        await route.fulfill({ json: { accessToken: 'late-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': 'magpie.refresh.14325=r1; Path=/api/auth; HttpOnly; SameSite=Strict' } });
      });
      await context.route('**/api/auth/logout', async (route) => {
        logoutCookie = (await route.request().allHeaders()).cookie;
        await route.fulfill({ status: 204, headers: { 'Set-Cookie': 'magpie.refresh.14325=; Path=/api/auth; Max-Age=0; HttpOnly; SameSite=Strict' } });
      });
      const renewal = page.evaluate(async () => {
        const path = '/src/lib/api.ts';
        try { return await (await import(path)).renewAccessToken(); } catch { return 'superseded'; }
      });
      await expect.poll(() => refreshStarted).toBe(true);
      const logout = other.evaluate(async () => { const path = '/src/lib/authApi.ts'; await (await import(path)).logout(); });
      await expect.poll(() => page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
      release();
      expect(await renewal).toBe('superseded');
      await logout;
      expect(logoutCookie).toContain('magpie.refresh.14325=r1');
      expect((await context.cookies()).find((cookie) => cookie.name === 'magpie.refresh.14325')).toBeUndefined();
      expect(await page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
    });

    test('does not replace a newer login with an older renewal', async ({ context, page }) => {
      const other = await context.newPage();
      await other.goto('/e2e/session.html');
      let release!: () => void;
      const gate = new Promise<void>((resolve) => { release = resolve; });
      let started = false;
      const otherToken = `e30.${Buffer.from(JSON.stringify({ sub: 'other-account', name: 'other', role: 0, exp: 4070908800 })).toString('base64url')}.signature`;
      await context.route('**/api/auth/refresh', async (route) => {
        started = true;
        await gate;
        await route.fulfill({ json: { accessToken: 'late-token', accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': 'magpie.refresh.14325=r1; Path=/api/auth; HttpOnly; SameSite=Strict' } });
      });
      await context.route('**/api/auth/login', async (route) => {
        expect((await route.request().allHeaders()).cookie).toContain('magpie.refresh.14325=r1');
        await route.fulfill({ json: { accessToken: otherToken, accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': 'magpie.refresh.14325=other-session; Path=/api/auth; HttpOnly; SameSite=Strict' } });
      });
      const renewal = page.evaluate(async () => {
        const path = '/src/lib/api.ts';
        try { return await (await import(path)).renewAccessToken(); } catch { return 'superseded'; }
      });
      await expect.poll(() => started).toBe(true);
      const login = other.evaluate(async () => { const path = '/src/lib/authApi.ts'; return (await import(path)).login('other', 'test-only'); });
      await expect.poll(() => page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
      release();
      expect(await renewal).toBe('superseded');
      expect(await login).toEqual({ id: 'other-account', username: 'other', role: 0 });
      expect(await page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBe(otherToken);
      expect((await context.cookies()).find((cookie) => cookie.name === 'magpie.refresh.14325')?.value).toBe('other-session');
    });

    test('keeps two same-host instances isolated through renewal and logout', async ({ context, page }) => {
      const other = await context.newPage();
      await other.goto('http://127.0.0.1:14326/e2e/session.html');
      await other.evaluate(async () => {
        const path = '/src/lib/tokenStorage.ts';
        (await import(path)).storeAuthTokens('other-old', '2020-01-01T00:00:00Z');
      });
      await context.addCookies([{ name: 'magpie.refresh.14326', value: 'other-r0', domain: '127.0.0.1', path: '/api/auth', httpOnly: true, sameSite: 'Strict' }]);
      await context.route('**/api/auth/refresh', async (route) => {
        const port = new URL(route.request().url()).port;
        const token = port === '14325' ? 'renewed-token' : 'other-renewed';
        await route.fulfill({ json: { accessToken: token, accessTokenExpiresAt: '2099-01-01T00:00:00Z' }, headers: { 'Set-Cookie': `magpie.refresh.${port}=r1-${port}; Path=/api/auth; HttpOnly; SameSite=Strict` } });
      });
      const renew = page.evaluate(async () => { const path = '/src/lib/api.ts'; return (await import(path)).renewAccessToken(); });
      const otherRenew = other.evaluate(async () => { const path = '/src/lib/api.ts'; return (await import(path)).renewAccessToken(); });
      expect(await renew).toBe('renewed-token');
      expect(await otherRenew).toBe('other-renewed');
      await context.route('**/api/auth/logout', async (route) => {
        expect(new URL(route.request().url()).port).toBe('14325');
        await route.fulfill({ status: 204, headers: { 'Set-Cookie': 'magpie.refresh.14325=; Path=/api/auth; Max-Age=0; HttpOnly; SameSite=Strict' } });
      });
      await page.evaluate(async () => { const path = '/src/lib/authApi.ts'; await (await import(path)).logout(); });
      expect(await page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
      expect(await other.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBe('other-renewed');
      expect((await context.cookies()).find((cookie) => cookie.name === 'magpie.refresh.14325')).toBeUndefined();
      expect((await context.cookies()).find((cookie) => cookie.name === 'magpie.refresh.14326')?.value).toBe('r1-14326');
    });

    test('does not share authentication with a different instance or browser session', async ({ browser, context }) => {
      const instance = await context.newPage();
      await instance.goto('http://localhost:14325/e2e/session.html');
      expect(await instance.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
      expect(await context.cookies('http://localhost:14325/api/auth/refresh')).toHaveLength(0);
      const separate = await browser.newContext();
      try {
        const page = await separate.newPage();
        await page.goto('http://127.0.0.1:14325/e2e/session.html');
        expect(await page.evaluate(() => localStorage.getItem('magpie_auth_token'))).toBeNull();
        expect(await separate.cookies()).toHaveLength(0);
      } finally {
        await separate.close();
      }
    });
  });
}
