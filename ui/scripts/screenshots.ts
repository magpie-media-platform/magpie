import { chromium, type Page } from '@playwright/test';
import { execSync, spawn, type ChildProcess } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { preview, type PreviewServer } from 'vite';

interface RouteConfig {
    path: string;
    name: string;
    action?: (page: Page) => Promise<void>;
}

const routes: RouteConfig[] = [
    { path: '/', name: 'dashboard' },
    {
        path: '/',
        name: 'mobile-sidebar-open',
        action: async (page) => {
            const viewport = page.viewportSize();
            if (viewport && viewport.width < 768) {
                const menuBtn = page.locator('.header__menu-button');
                await menuBtn.click();
                await page.waitForTimeout(500);
            }
        },
    },
    { path: '/library', name: 'library-landing' },
    { path: '/library/all', name: 'library' },
    { path: '/calendar', name: 'calendar' },
    { path: '/library/subjects/all', name: 'subjects' },
    { path: '/subjects/subject-1', name: 'subject-detail' },
    { path: '/downloads', name: 'downloads' },
    { path: '/admin/general', name: 'admin-general' },
    { path: '/admin/indexers', name: 'admin-indexers' },
    { path: '/admin/download-clients', name: 'admin-download-clients' },
    { path: '/admin/media-servers', name: 'admin-media-servers' },
    { path: '/admin/root-folders', name: 'admin-root-folders' },
    {
        path: '/admin/root-folders',
        name: 'admin-root-folders-form',
        action: async (page) => {
            await page.click('[data-action="add-folder"]');
            await page.waitForTimeout(500);
            await page.fill('[data-field="name"]', 'Movies Library');
            await page.waitForTimeout(200);
        },
    },
    { path: '/wishlists', name: 'wishlists' },
    { path: '/wishlists/wl-1', name: 'wishlists-detail' },
    { path: '/my-jobs', name: 'my-jobs' },
    {
        path: '/my-jobs',
        name: 'my-jobs-executions',
        action: async (page) => {
            await page.click('[role="tab"]:has-text("Executions")');
            await page.waitForTimeout(300);
        },
    },
    { path: '/filters', name: 'filters' },
    {
        path: '/filters',
        name: 'filters-form',
        action: async (page) => {
            await page.click('[data-action="add-filter"]');
            await page.waitForTimeout(500);
            await page.fill('[data-field="name"]', 'Test Filter');
            await page.waitForTimeout(200);
        },
    },
    {
        path: '/filters',
        name: 'filters-form-autocomplete',
        action: async (page) => {
            await page.keyboard.press('Escape');
            await page.waitForTimeout(500);

            await page.waitForSelector('[data-action="add-filter"]', { state: 'visible', timeout: 5000 });

            await page.click('[data-action="add-filter"]');
            await page.waitForSelector('[data-component="filter-form"]', { state: 'visible', timeout: 5000 });
            await page.waitForTimeout(300);

            await page.fill('[data-field="name"]', 'My Custom Filter');
            await page.waitForTimeout(200);

            const advancedBtn = page.locator('[data-action="switch-to-advanced"]');
            await advancedBtn.click();
            await page.waitForTimeout(400);

            const editor = page.locator('.filter-query-editor .cm-content');
            await editor.click();
            await page.waitForTimeout(300);

            await page.keyboard.press('g');
            await page.waitForTimeout(100);
            await page.keyboard.press('e');
            await page.waitForTimeout(100);

            await page.keyboard.press('Control+Space');

            try {
                await page.waitForSelector('.cm-tooltip-autocomplete', { state: 'visible', timeout: 3000 });
                console.log('  [info] Autocomplete tooltip visible');
            } catch {
                console.log('  [warn] Autocomplete tooltip not visible, trying again');
                await page.keyboard.press('n');
                await page.waitForTimeout(100);
                await page.keyboard.press('Control+Space');
                await page.waitForTimeout(300);
            }

        },
    },
    {
        path: '/filters',
        name: 'filters-builder-populated',
        action: async (page) => {
            await page.keyboard.press('Escape');
            await page.waitForTimeout(500);

            await page.waitForSelector('[data-action="add-filter"]', { state: 'visible', timeout: 5000 });

            await page.click('[data-action="add-filter"]');
            await page.waitForSelector('[data-component="filter-form"]', { state: 'visible', timeout: 5000 });
            await page.waitForTimeout(300);

            await page.fill('[data-field="name"]', 'HD Sci-Fi Recent Releases');
            await page.waitForTimeout(200);

            await page.click('#sfb-content-type');
            await page.waitForTimeout(300);
            await page.click('[role="option"]:has-text("movie")', { force: true });
            await page.waitForTimeout(300);

            await page.click('#sfb-monitoring');
            await page.waitForTimeout(300);
            await page.click('[role="option"]:has-text("Active")', { force: true });
            await page.waitForTimeout(300);

            await page.fill('#sfb-title', 'Avengers');
            await page.waitForTimeout(200);

            await page.fill('#sfb-genre', 'Sci-Fi');
            await page.waitForTimeout(200);

            await page.fill('#sfb-tag', 'favorite');
            await page.waitForTimeout(200);
        },
    },
    { path: '/admin/tags', name: 'admin-tags' },
    {
        path: '/admin/tags',
        name: 'admin-tags-form',
        action: async (page) => {
            await page.click('[data-action="add-tag"]');
            await page.waitForTimeout(500);
            await page.fill('[data-field="name"]', 'new-tag');
            await page.waitForTimeout(200);
        },
    },
    { path: '/admin/quality-tiers', name: 'admin-quality-tiers' },
    { path: '/admin/quality-tiers/qtl-1', name: 'admin-quality-tiers-detail' },
    {
        path: '/admin/quality-tiers',
        name: 'admin-quality-tiers-form',
        action: async (page) => {
            await page.waitForSelector('[data-action="add-tier-list"]', { state: 'visible', timeout: 10000 });
            await page.waitForTimeout(300);
            await page.click('[data-action="add-tier-list"]');
            await page.waitForTimeout(500);
            await page.fill('[data-field="name"]', 'Custom HD');
            await page.waitForTimeout(200);
        },
    },
    {
        path: '/wishlists',
        name: 'wishlists-form',
        action: async (page) => {
            await page.click('[data-action="add-wishlist"]');
            await page.waitForTimeout(500);
            await page.fill('[data-field="name"]', 'Test Wishlist');
            await page.waitForTimeout(200);
            const formBody = page.locator('.wishlist-form__body');
            await formBody.evaluate((el) => el.scrollTop = 0);
            await page.waitForTimeout(200);
        },
    },
    { path: '/admin/jobs', name: 'admin-jobs' },
    {
        path: '/admin/jobs',
        name: 'admin-jobs-executions',
        action: async (page) => {
            await page.click('[role="tab"]:has-text("Executions")');
            await page.waitForTimeout(300);
        },
    },
    {
        path: '/admin/jobs',
        name: 'admin-jobs-executions-expanded',
        action: async (page) => {
            await page.click('[role="tab"]:has-text("Executions")');
            await page.waitForTimeout(300);
            const triggers = page.locator('.jobs-page__execution-trigger');
            const count = await triggers.count();
            if (count >= 4) {
                await triggers.nth(3).click();
                await page.waitForTimeout(500);
                await triggers.nth(3).scrollIntoViewIfNeeded();
                await page.waitForTimeout(300);
            }
        },
    },
    {
        path: '/admin/jobs',
        name: 'admin-jobs-schedules',
        action: async (page) => {
            await page.click('[role="tab"]:has-text("Schedules")');
            await page.waitForTimeout(300);
        },
    },
    {
        path: '/admin/jobs',
        name: 'admin-jobs-events',
        action: async (page) => {
            await page.click('[role="tab"]:has-text("Events")');
            await page.waitForTimeout(300);
        },
    },
    { path: '/admin/system', name: 'admin-system-status' },
    { path: '/about', name: 'about' },
    { path: '/admin/users', name: 'admin-users' },
    { path: '/admin/extensions', name: 'admin-extensions' },
    { path: '/admin/backups', name: 'admin-backups' },
    { path: '/login', name: 'login' },
    { path: '/not-found', name: '404' },
    { path: '/forbidden', name: '403' },
];

const viewports = [
    { name: 'desktop', width: 1280, height: 800 },
    { name: 'mobile', width: 375, height: 667 },
] as const;

const MAX_DIMENSION = parseInt(process.env.SCREENSHOT_MAX_DIM || '800', 10);

async function captureWithScrolling(page: Page, basePath: string, baseName: string): Promise<void> {
    const viewport = page.viewportSize();
    if (!viewport) {
        throw new Error('No viewport set');
    }

    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(100);

    const scrollHeight = await page.evaluate(() => document.documentElement.scrollHeight);
    const viewportHeight = viewport.height;
    const totalScrolls = Math.ceil(scrollHeight / viewportHeight);

    if (totalScrolls <= 1) {
        await page.screenshot({ path: `${basePath}.png` });
        console.log(`  [ok] ${baseName}.png`);
        return;
    }

    for (let i = 0; i < totalScrolls; i++) {
        const scrollY = i * viewportHeight;
        await page.evaluate((y) => window.scrollTo(0, y), scrollY);
        await page.waitForTimeout(200);

        const suffix = `-${i + 1}`;
        await page.screenshot({ path: `${basePath}${suffix}.png` });
        console.log(`  [ok] ${baseName}${suffix}.png`);
    }

    await page.evaluate(() => window.scrollTo(0, 0));
    await page.waitForTimeout(200);
}

function shrinkScreenshots(dir: string, maxDim: number): void {
    const files = fs.readdirSync(dir).filter(f => f.endsWith('.png'));
    console.log(`\nResizing ${files.length} screenshots (max dimension: ${maxDim}px)...`);

    let resized = 0;
    for (const file of files) {
        const filepath = path.join(dir, file);
        try {
            execSync(`magick "${filepath}" -resize "${maxDim}x${maxDim}>" "${filepath}"`, { stdio: 'pipe' });
            resized++;
        } catch {
            console.log(`  [warn] Could not resize ${file}`);
        }
    }

    console.log(`Resized ${resized}/${files.length} screenshots.`);
}

function ensureScreenshotsDir(): string {
    const screenshotsDir = path.join(process.cwd(), 'screenshots');
    if (!fs.existsSync(screenshotsDir)) {
        fs.mkdirSync(screenshotsDir, { recursive: true });
    }

    const existingFiles = fs.readdirSync(screenshotsDir).filter(file => file.endsWith('.png'));
    for (const file of existingFiles) {
        fs.unlinkSync(path.join(screenshotsDir, file));
    }

    if (existingFiles.length > 0) {
        console.log(`Cleaned ${existingFiles.length} old screenshot(s) from ${screenshotsDir}`);
    }

    return screenshotsDir;
}

async function waitForPageReady(page: Page): Promise<void> {
    await page.waitForLoadState('networkidle');
    try {
        await page.waitForSelector('[data-page]', { timeout: 5000 });
    } catch {
    }
    await page.waitForTimeout(500);
}

function setupConsoleLogging(page: Page, routeName: string): void {
    page.on('console', (msg) => {
        if (msg.type() === 'error') {
            console.log(`  [console error] ${routeName}: ${msg.text()}`);
        }
    });
    page.on('pageerror', (err) => {
        console.log(`  [page error] ${routeName}: ${err.message}`);
    });
}

const PRISM_PORT = 4010;
const PRISM_URL = `http://127.0.0.1:${PRISM_PORT}`;

async function waitForEndpoint(url: string, maxAttempts = 30, intervalMs = 500): Promise<void> {
    for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            const response = await fetch(url);
            if (response.ok) {
                return;
            }
        } catch {
        }
        if (attempt < maxAttempts) {
            await new Promise(resolve => setTimeout(resolve, intervalMs));
        }
    }
    throw new Error(`Endpoint ${url} did not become ready after ${maxAttempts} attempts`);
}

async function startPrismServer(): Promise<ChildProcess> {
    console.log('Starting Prism mock server...');
    const openApiPath = path.join(process.cwd(), 'api', 'openapi.yaml');

    const prism = spawn('npx', ['prism', 'mock', openApiPath, '-p', String(PRISM_PORT), '--cors'], {
        stdio: ['ignore', 'pipe', 'pipe'],
        shell: true,
    });

    prism.stdout?.on('data', (data: Buffer) => {
        const lines = data.toString().trim().split('\n');
        for (const line of lines) {
            if (line.includes('error') || line.includes('Error')) {
                console.log(`  [prism] ${line}`);
            }
        }
    });

    prism.stderr?.on('data', (data: Buffer) => {
        console.error(`  [prism error] ${data.toString().trim()}`);
    });

    await waitForEndpoint(`${PRISM_URL}/system/health`);
    console.log(`Prism mock server running at ${PRISM_URL}`);

    return prism;
}

function stopPrismServer(prism: ChildProcess): void {
    if (prism && !prism.killed) {
        console.log('Stopping Prism mock server...');
        prism.kill('SIGTERM');
    }
}

async function startPreviewServer(): Promise<PreviewServer> {
    console.log('Starting Vite preview server...');
    const server = await preview({
        preview: {
            port: 4173,
            strictPort: true,
        },
        server: {
            proxy: {
                '/api': {
                    target: PRISM_URL,
                    rewrite: (reqPath: string) => reqPath.replace(/^\/api/, ''),
                    changeOrigin: true,
                },
            },
        },
    });
    await new Promise(resolve => setTimeout(resolve, 2000));
    console.log(`Preview server running at http://localhost:4173`);
    return server;
}

async function captureScreenshots(): Promise<void> {
    const screenshotsDir = ensureScreenshotsDir();
    let server: PreviewServer | null = null;
    let prism: ChildProcess | null = null;
    let baseUrl = process.env.BASE_URL;

    if (!baseUrl) {
        prism = await startPrismServer();
        server = await startPreviewServer();
        baseUrl = 'http://localhost:4173';
    }

    console.log(`Capturing screenshots from ${baseUrl}`);
    console.log(`Output directory: ${screenshotsDir}\n`);

    const browser = await chromium.launch();

    try {
        for (const viewport of viewports) {
            console.log(`\nViewport: ${viewport.name} (${viewport.width}x${viewport.height})`);

            const context = await browser.newContext({
                viewport: { width: viewport.width, height: viewport.height },
            });
            const page = await context.newPage();

            await page.addInitScript(() => {
                const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
                const payload = btoa(JSON.stringify({
                    sub: 'user-1',
                    name: 'admin',
                    role: '3',
                    exp: Math.floor(Date.now() / 1000) + 3600,
                }));
                const mockToken = `${header}.${payload}.mock-signature`;

                localStorage.setItem('magpie_auth_token', mockToken);
                localStorage.setItem('magpie_refresh_token', 'mock-refresh-token');
                localStorage.setItem('magpie_token_expires', new Date(Date.now() + 3600000).toISOString());
            });

            for (const route of routes) {
                const url = `${baseUrl}${route.path}`;
                const baseName = `${route.name}-${viewport.name}`;
                const basePath = path.join(screenshotsDir, baseName);

                try {
                    setupConsoleLogging(page, route.name);
                    await page.goto(url, { waitUntil: 'domcontentloaded' });
                    await waitForPageReady(page);

                    if (route.action) {
                        await route.action(page);
                    }

                    await captureWithScrolling(page, basePath, baseName);
                } catch (error) {
                    const message = error instanceof Error ? error.message : String(error);
                    console.error(`  [FAIL] ${baseName}: ${message}`);
                }
            }

            await context.close();
        }
    } finally {
        await browser.close();
        if (server) {
            await server.close();
        }
        if (prism) {
            stopPrismServer(prism);
        }
    }

    if (MAX_DIMENSION > 0) {
        try {
            shrinkScreenshots(screenshotsDir, MAX_DIMENSION);
        } catch {
            console.log('\n[info] ImageMagick not available, skipping resize.');
        }
    }

    console.log('\nScreenshot capture complete.');
    console.log(`Screenshots saved to: ${screenshotsDir}`);
}

captureScreenshots().then(() => {
    process.exit(0);
}).catch((error) => {
    console.error('Screenshot capture failed:', error);
    process.exit(1);
});
