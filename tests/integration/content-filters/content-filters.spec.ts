import { expect, test } from '@playwright/test';

let subjectId = '';
let showId = '';
test.beforeAll(async ({ request }) => {
    if ((await (await request.get('/api/system/setup/status')).json()).isRequired) {
        expect((await request.post('/api/system/setup/admin', { data: { username: 'filter-admin', password: 'Isolated-filter-322!' } })).ok()).toBeTruthy();
        expect((await request.post('/api/system/setup/complete')).ok()).toBeTruthy();
    }
    const seed = await request.post('/__qa/seed');
    expect(seed.ok()).toBeTruthy();
    const fixture = await seed.json();
    subjectId = fixture.subjectId;
    showId = fixture.items.find((item: { title: string }) => item.title === 'Fixture Show').id;
});

test.beforeEach(async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('Username').fill('filter-admin');
    await page.locator('[data-field="password"]').fill('Isolated-filter-322!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page).not.toHaveURL(/login/);
});

for (const recovery of ['retry', 'clear']) {
    test(`saved-filter formatting failure blocks unrestricted requests until ${recovery}`, async ({ page }) => {
        if (recovery === 'clear') await page.setViewportSize({ width: 320, height: 700 });
        await page.goto('/library/all?filter=' + encodeURIComponent('monitoring != Unmonitored'));
        await page.locator('[data-action="save-filter"]').click();
        await page.getByLabel('Filter Name', { exact: true }).fill('322 format failure ' + recovery);
        await page.locator('[data-action="save-new-filter"]').click();
        await expect(page).toHaveURL(/savedFilter=/);
        const savedUrl = page.url();
        let failFormatting = true;
        await page.route('**/api/magpie-ql/content/format', async (route) => {
            if (failFormatting) {
                await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Injected formatting failure' }) });
            } else {
                await route.continue();
            }
        });
        const contentRequests: string[] = [];
        page.on('request', (request) => {
            if (new URL(request.url()).pathname === '/api/content') contentRequests.push(request.url());
        });

        await page.goto(savedUrl);
        await expect(page.getByRole('alert').filter({ hasText: 'Could not load saved filter' })).toBeVisible();
        await expect(page).toHaveURL(savedUrl);
        expect(contentRequests).toEqual([]);
        await expect(page.getByRole('radio', { name: /^Monitored:/ })).toBeDisabled();
        await expect(page.getByText('Saved filter not found')).toHaveCount(0);
        await page.screenshot({ path: `content-filters-results/format-failure-${recovery}.png`, fullPage: true });
        expect((await page.getByRole('button', { name: 'Retry saved filter', exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();

        if (recovery === 'retry') {
            failFormatting = false;
            await page.getByRole('button', { name: 'Retry saved filter', exact: true }).click();
            await expect(page.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true');
            await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
            await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toHaveCount(0);
            expect(contentRequests.length).toBeGreaterThan(0);
            expect(contentRequests.every((url) => /monitoring\s*!=\s*Unmonitored/i.test(new URL(url).searchParams.get('filter') ?? ''))).toBeTruthy();
            await expect(page).toHaveURL(savedUrl);
        } else {
            await page.getByRole('button', { name: 'Clear saved filter', exact: true }).click();
            await expect(page).not.toHaveURL(/savedFilter=/);
            await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toBeVisible();
            expect(contentRequests.length).toBeGreaterThan(0);
        }
    });
}

test('repeated relationship conditions survive independent controls', async ({ page }) => {
    const query = 'Relationships has (Name contains "Fixture" and Name contains "Person") and fulfillment = Missing';
    await page.goto('/library/all?filter=' + encodeURIComponent(query));
    await expect(page.locator('.cm-content')).toContainText(query);
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    await page.getByRole('radio', { name: /^Monitored:/ }).click();
    await expect(page.locator('.cm-content')).toContainText(query + ' and monitoring != Unmonitored');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
});

test('desktop headers, controls, saved query and gallery share status and monitoring', async ({ page }) => {
    let releaseFormat!: () => void;
    const formatResponse = new Promise<void>((resolve) => { releaseFormat = resolve; });
    await page.route('**/api/magpie-ql/content/format', async (route) => {
        const response = await route.fetch();
        await formatResponse;
        await route.fulfill({ response });
    });
    await page.goto('/library/all');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    await page.getByRole('button', { name: 'Filter by Status', exact: true }).click();
    await page.getByRole('menuitemradio', { name: 'Missing', exact: true }).click();
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveText('Missing');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toHaveCount(0);
    await page.getByRole('radio', { name: /^Monitored:/ }).click();
    await expect(page.locator('.filter-bar__query-text')).toContainText('monitoring != Unmonitored');
    await expect(page.locator('.filter-bar__query-text')).toContainText('fulfillment = Missing');
    await page.getByLabel('Release year', { exact: true }).fill('2020');
    await page.locator('[data-action="save-filter"]').click();
    await page.getByLabel('Filter Name', { exact: true }).fill('322 monitored missing');
    await page.locator('[data-action="save-new-filter"]').click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    const savedUrl = page.url();
    await page.getByRole('radio', { name: /^Any:/ }).click();
    await expect(page.locator('.filter-bar__query-text')).not.toContainText('monitoring');
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveText('Missing');
    releaseFormat();
    await page.goto(savedUrl);
    await expect(page.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveText('Missing');
    await expect(page.getByLabel('Release year', { exact: true })).toHaveValue('2020');
    await page.getByRole('tab', { name: 'Gallery', exact: true }).click();
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveText('Missing');
    await expect(page.locator('.library-gallery-tile__title', { hasText: 'Alpha Movie' })).toBeVisible();
    await expect(page.locator('.library-gallery-tile__title', { hasText: 'Beta Movie' })).toHaveCount(0);
    await page.screenshot({ path: 'content-filters-results/desktop.png', fullPage: true });
});

test('complex MQL and exact dates survive independent edits with real query results', async ({ page }) => {
    const query = 'title contains "Movie" and publishDate >= "2020-01-01" and publishDate <= "2021-12-31"';
    await page.goto('/library/all?filter=' + encodeURIComponent(query));
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toBeVisible();
    await page.getByRole('radio', { name: /^Unmonitored:/ }).click();
    await expect(page.locator('.cm-content')).toContainText(query + ' and monitoring = Unmonitored');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toHaveCount(0);
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Published filtered, edit filter' })).toBeVisible();
    await page.locator('.cm-content').fill('fulfillment = Upgradable and monitoring != Unmonitored');
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveText('Upgradable');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Gamma Movie' })).toBeVisible();
});

test('mobile controls remain touch sized and usable when results are empty', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/library/all?filter=' + encodeURIComponent('fulfillment = Missing and monitoring = Unmonitored'));
    await expect(page.getByLabel('Status filter', { exact: true })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toHaveCount(0);
    await page.getByRole('radio', { name: /^Any:/ }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    expect((await page.getByRole('radio', { name: /^Any:/ }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
    expect((await page.getByLabel('Status filter', { exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(44);
    await page.getByLabel('Status filter', { exact: true }).focus();
    await page.keyboard.press('Enter');
    await page.getByRole('option', { name: 'Any status', exact: true }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toBeVisible();
    await page.getByLabel('Release year', { exact: true }).fill('2020');
    await expect(page.locator('.filter-bar__query-text')).toContainText('publishDate = "2020"');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toHaveCount(0);
    await page.getByRole('checkbox', { name: 'before', exact: true }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Gamma Movie' })).toBeVisible();
    await page.screenshot({ path: 'content-filters-results/mobile.png', fullPage: true });
    await page.locator('[data-action="clear-filter"]').click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.setViewportSize({ width: 320, height: 700 });
    await page.screenshot({ path: 'content-filters-results/mobile-320.png', fullPage: true });
    expect(await page.evaluate(() => [...document.querySelectorAll('.filter-bar, .filter-bar input, .filter-bar button')].filter((element) => element.getBoundingClientRect().right > window.innerWidth).map((element) => ({ className: element.className, right: element.getBoundingClientRect().right })))).toEqual([]);
});

test('unsupported scalar OR is preserved and remains an explicit query error', async ({ page }) => {
    const query = 'title contains "Alpha" or title contains "Beta"';
    await page.goto('/library/all?filter=' + encodeURIComponent(query));
    await expect(page.getByText('Invalid filter', { exact: true })).toBeVisible();
    await page.getByRole('radio', { name: /^Monitored:/ }).click();
    await expect(page.locator('.cm-content')).toHaveText('(' + query + ') and monitoring != Unmonitored');
    await expect(page.getByText('Invalid filter', { exact: true })).toBeVisible();
    await page.locator('[data-action="clear-filter"]').click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
});

test('provider search and non-content tables do not receive local content controls', async ({ page }) => {
    await page.goto('/library/add');
    await expect(page.locator('[data-page="library-add"]')).toBeVisible();
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('radiogroup', { name: 'Monitoring filter' })).toHaveCount(0);
    await page.goto('/library/subjects/all');
    await expect(page.getByText('Fixture Person', { exact: true }).first()).toBeVisible();
    await expect(page.getByLabel('Status filter', { exact: true })).toHaveCount(0);
});

test('subject content and Wanted retain their underlying scopes', async ({ page }) => {
    await page.goto('/subjects/' + subjectId);
    await page.getByRole('tab', { name: /Content/ }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Gamma Movie' })).toBeVisible();
    await page.locator('[data-action="switch-to-advanced"]').click();
    await page.locator('.cm-content').fill('title contains "Alpha" and publishDate >= "2020-01-01"');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Gamma Movie' })).toHaveCount(0);
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toHaveCount(0);
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    await page.getByRole('radio', { name: /^Unmonitored:/ }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toHaveCount(0);
    await expect(page.getByLabel('Status filter', { exact: true })).toBeVisible();
    await page.goto('/wanted?filter=' + encodeURIComponent('fulfillment = Upgradable'));
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Gamma Movie' })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toHaveCount(0);
    await page.getByRole('radio', { name: /^Unmonitored:/ }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Gamma Movie' })).toHaveCount(0);
    await page.locator('[data-action="clear-filter"]').click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Beta Movie' })).toHaveCount(0);
});

test('Wanted preserves legacy child-level filters and parent scope on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/wanted?parentId=' + showId + '&type=Season');
    await expect(page.getByLabel('Content Type', { exact: true })).toContainText('Season');
    await expect(page.locator('.filter-bar__query-text')).toContainText('level = "season"');
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Fixture Season' })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toHaveCount(0);
    await page.getByLabel('Content Type', { exact: true }).click();
    await page.getByRole('option', { name: /Episode$/ }).click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Fixture Season' })).toHaveCount(0);
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Fixture Episode' })).toHaveCount(0);
    await page.locator('[data-action="clear-filter"]').click();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Fixture Season' })).toBeVisible();
    await expect(page.locator('.content-lineage-title__segment:visible').filter({ hasText: 'Alpha Movie' })).toHaveCount(0);
});
