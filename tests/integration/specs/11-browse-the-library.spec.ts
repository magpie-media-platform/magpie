import { rm } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { createCheckpoint, restoreCheckpoint } from '../helpers/checkpoints';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { mocks } from '../helpers/mocks';
import { buildRichLibrary } from '../helpers/rich-library';
import { expectScopedEventRefresh, loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    pageNumber: number;
    pageSize: number;
    totalItems: number;
}

interface ContentItem {
    id: string;
    name: string;
    level: string;
    contentType: string;
    parentId?: string | null;
    monitoring: 'Unmonitored' | 'Monitored' | 'Shotgun' | null;
    effectiveMonitoring: 'Unmonitored' | 'Monitored' | 'Shotgun';
    contentItem?: {
        fulfillment: string;
        mediaFiles: Array<{ filePath: string }>;
    } | null;
}

interface RichIds {
    series: string;
    season: string;
    episode: string;
    movie: string;
    artist: string;
    album: string;
    track: string;
    book: string;
    subject: string;
    upcoming: string;
    recent: string;
}

interface SystemInfo {
    version: string;
    runtime: string;
    operatingSystem: string;
    architecture: string;
}

const checkpointName = 'journey-rich-library';
const mediaLinkScenario = 'journey-11-media-link';
const savedFilterName = 'Journey feature view';

const richScenarioDirectories = [
    ...['tv', 'movie', 'artist', 'book'].map((name) => [mocks.hierarchyPrimary.slug, `rich-rich-${name}`]),
    ...Array.from({ length: 48 }, (_, index) => [
        mocks.hierarchyPrimary.slug,
        `rich-rich-catalog-${String(index + 1).padStart(2, '0')}`,
    ]),
    ...['movie', 'tv', 'music', 'book'].flatMap((name) => [
        [mocks.indexer.slug, `rich-${name}-acquisition`],
        [mocks.downloadClient.slug, `rich-${name}-acquisition`],
        [mocks.mediaServer.slug, `rich-${name}-acquisition`],
    ]),
    [mocks.mediaServer.slug, mediaLinkScenario],
] as const;

test.describe.serial('Journey 11: browse it like a member of the household', () => {
    test('browses the rich library through every household-facing route', async ({ page }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        let ids!: RichIds;
        let rich: Awaited<ReturnType<typeof buildRichLibrary>> | undefined;
        let savedFilterId: string | undefined;

        try {
            await test.step('restore the configured baseline, hydrate a rich library through public surfaces, and checkpoint it', async () => {
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
                });
                rich = await buildRichLibrary(api);
                const backup = await createCheckpoint(api, checkpointName, rich.fileAnchors);
                expect(backup.name).toBe(checkpointName);
                expect((await api.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(87);
                expect(rich.contentIds.movie).toBeTruthy();
                ids = await findRichIds(api);
                await loginUi(page);
            });

            await test.step('browse, filter, sort, page, save a view, select, and switch to the phone layout', async () => {
                await page.setViewportSize({ width: 1365, height: 900 });
                await page.goto('/library/all');
                const grid = page.getByRole('grid');
                await expect(grid).toBeVisible();
                await expect(page.getByTestId('library-page__mobile-list')).toBeHidden();

                await page.getByRole('button', { name: 'Filter by Title' }).click();
                await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Feature');
                await page.getByRole('button', { name: 'Apply' }).click();
                await expect(grid.locator('tbody tr')).toHaveCount(1);
                await expect(grid.locator(`[data-row-id="${ids.movie}"]`)).toContainText('Journey Feature');
                await page.getByRole('button', { name: 'Title filtered, edit filter' }).click();
                await page.getByRole('button', { name: 'Clear' }).click();
                await expect(grid.locator('tbody tr')).toHaveCount(50);

                await page.getByRole('button', { name: 'Filter by Type' }).click();
                await page.getByRole('menuitemradio', { name: 'Movie', exact: true }).click();
                await expect(grid.locator('tbody tr')).toHaveCount(49);
                await page.getByRole('button', { name: 'Type filtered, edit filter' }).click();
                await page.getByRole('menuitemradio', { name: 'Any' }).click();
                await expect(grid.locator('tbody tr')).toHaveCount(50);

                await page.getByRole('button', { name: 'Filter by Status' }).click();
                await page.getByRole('menuitemradio', { name: 'Upgradable', exact: true }).click();
                await expect(grid.locator('tbody tr')).toHaveCount(2);
                await expect(grid.locator(`[data-row-id="${ids.movie}"]`)).toContainText('Upgradable');
                await page.getByRole('button', { name: 'Status filtered, edit filter' }).click();
                await page.getByRole('menuitemradio', { name: 'Any' }).click();
                await expect(grid.locator('tbody tr')).toHaveCount(50);

                await page.getByRole('button', { name: 'Sort by Title' }).click();
                await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                    'aria-sort',
                    'ascending',
                );
                await expect(grid.locator('tbody tr').first()).toContainText('Journey Artist');
                await page.getByRole('button', { name: 'Sort by Title' }).click();
                await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                    'aria-sort',
                    'descending',
                );
                await expect(grid.locator('tbody tr').first()).toContainText('Journey Series');

                const pager = page.getByRole('navigation', { name: 'Pagination' });
                await expect(pager.locator('.pager__page')).toHaveText('Page 1 of 2');
                await pager.getByRole('button', { name: 'Go to next page' }).click();
                await expect(pager.locator('.pager__page')).toHaveText('Page 2 of 2');
                await pager.getByRole('button', { name: 'Go to first page' }).click();

                await page.getByRole('button', { name: 'Filter by Title' }).click();
                await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Feature');
                await page.getByRole('button', { name: 'Apply' }).click();
                await page.locator('[data-action="save-filter"]').click();
                const saveDialog = page.getByRole('dialog', { name: 'Save Filter' });
                await saveDialog.getByLabel('Filter Name').fill(savedFilterName);
                await saveDialog.locator('[data-action="save-new-filter"]').click();
                await expect(saveDialog).toBeHidden();
                savedFilterId = await until(
                    'the saved journey view',
                    async () =>
                        (await api.get<Array<{ id: string; name: string }>>('/api/content-filters')).find(
                            (filter) => filter.name === savedFilterName,
                        )?.id,
                    30_000,
                    250,
                );

                await page.goto('/filters');
                const filterRow = page.locator(`[data-filter-id="${savedFilterId}"]`);
                await expect(filterRow).toContainText(savedFilterName);
                await filterRow.getByRole('button', { name: `View ${savedFilterName} in Library` }).click();
                await expect(page.getByTestId('library-page__applied-saved-filter')).toContainText(savedFilterName);
                await expect(page.getByRole('grid').locator(`[data-row-id="${ids.movie}"]`)).toBeVisible();

                await page.setViewportSize({ width: 390, height: 844 });
                await page.goto('/library/all');
                await expect(page.getByRole('grid')).toBeHidden();
                const mobile = page.getByTestId('library-page__mobile-list');
                await expect(mobile).toBeVisible();
                await page.getByRole('button', { name: 'Filters' }).click();
                await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Feature');
                await expect(mobile.getByText('Journey Feature', { exact: true })).toBeVisible();
                await page.locator('[data-action="enter-select-mode"]').first().click();
                await mobile.getByRole('button', { name: 'Select Journey Feature card' }).click();
                await page.locator('.library-page__mobile-selection-actions [data-action="bulk-unmonitor"]').click();
                await expect.poll(async () => (await api.get<ContentItem>(`/api/content/${ids.movie}`)).monitoring)
                    .toBe('Unmonitored');
                await api.patch(`/api/content/${ids.movie}`, { monitoring: { value: 'Monitored' } });
            });

            await test.step('the library landing page offers each content type as its own entry point', async () => {
                await page.setViewportSize({ width: 1365, height: 900 });
                for (const [typeId, path] of [
                    ['movie', '/library/content/movie'],
                    ['tv', '/library/content/tv'],
                    ['music', '/library/content/music'],
                    ['book', '/library/content/book'],
                ] as const) {
                    await page.goto('/library');
                    const link = page.locator(`[data-component="type-card"][data-type-id="${typeId}"]`);
                    await expect(link).toHaveAttribute('href', path);
                    await link.click();
                    await expect(page).toHaveURL(new RegExp(`${path.replaceAll('/', '\\/')}$`));
                }
            });

            await test.step('each content type and level route scopes to its own contents', async () => {
                for (const [path, heading, present, absent] of [
                    ['/library/content/movie', 'Movies', 'Journey Feature', 'Journey Series'],
                    ['/library/content/tv', 'TV Shows', 'Journey Series', 'Journey Feature'],
                    ['/library/content/music', 'Music', 'Journey Artist', 'Journey Feature'],
                    ['/library/content/movie/movie', 'Movie', 'Journey Feature', 'Journey Series'],
                    ['/library/content/tv/show', 'Show', 'Journey Series', 'Journey Feature'],
                    ['/library/content/music/artist', 'Artist', 'Journey Artist', 'Journey Feature'],
                    ['/library/content/book/ebook', 'E-book', 'Journey Book', 'Journey Feature'],
                ] as const) {
                    await page.goto(path);
                    await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible();
                    await expect(page.getByText(present, { exact: true }).first()).toBeVisible();
                    await expect(page.getByText(absent, { exact: true })).toHaveCount(0);
                }

                await page.goto('/library/content/book');
                await expect(page.getByRole('link', { name: /^E-book\b/ })).toHaveAttribute(
                    'href',
                    '/library/content/book/ebook',
                );
                await expect(page.getByRole('link', { name: /^Audiobook\b/ })).toHaveAttribute(
                    'href',
                    '/library/content/book/audiobook',
                );
                await page.goto('/library/content/book/audiobook');
                await expect(page.getByRole('heading', { name: 'Audiobook', level: 1 })).toBeVisible();
                await expect(page.getByText('Your library is empty', { exact: true })).toBeVisible();
            });

            await test.step('an advanced query typed into the query bar filters the grid', async () => {
                const query = 'Title = "Journey Feature" and Genres has (Name = "Journey Genre")';
                const endpoint = await api.get<Paged<ContentItem>>(
                    `/api/content?filter=${encodeURIComponent(query)}&topLevelOnly=true&pageSize=50`,
                );
                expect(endpoint.items.map((item) => item.id)).toEqual([ids.movie]);

                await page.goto('/library/all');
                await page.locator('[data-action="switch-to-advanced"]').click();
                const filtered = page.waitForResponse((response) => {
                    const url = new URL(response.url());
                    return (
                        response.request().method() === 'GET' &&
                        url.pathname === '/api/content' &&
                        url.searchParams.get('filter') === query
                    );
                });
                await page.locator('[data-component="filter-query-editor"] .cm-content').fill(query);
                expect((await filtered).ok()).toBe(true);
                expect(new URL(page.url()).searchParams.get('filter')).toBe(query);
                const rows = page.getByRole('grid').locator('tbody tr');
                await expect(rows).toHaveCount(1);
                await expect(rows.first()).toHaveAttribute('data-row-id', ids.movie);
            });

            await test.step('filter, sort and page survive navigating away and back', async () => {
                await page.goto('/library/all');
                await page.getByRole('button', { name: 'Filter by Title' }).click();
                await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey');
                await page.getByRole('button', { name: 'Apply' }).click();
                await page.getByRole('button', { name: 'Sort by Title' }).click();
                const pager = page.getByRole('navigation', { name: 'Pagination' });
                await pager.getByRole('button', { name: 'Go to next page' }).click();

                await expect(page).toHaveURL(/page=2/);
                const presentationParams = new URL(page.url()).searchParams;
                expect(presentationParams.get('filter')).toContain('Journey');
                expect(presentationParams.get('sort')).toBe('title');
                expect(presentationParams.get('sortDir')).toBe('asc');
                expect(presentationParams.get('page')).toBe('2');
                const presentationQuery = presentationParams.toString();
                const grid = page.getByRole('grid');
                await expect(pager.locator('.pager__page')).toHaveText('Page 2 of 2');
                await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                    'aria-sort',
                    'ascending',
                );
                await expect(grid.locator('tbody tr')).toHaveCount(2);
                await expect(grid.locator('tbody tr').nth(0)).toHaveAttribute('data-row-id', ids.movie);
                await expect(grid.locator('tbody tr').nth(1)).toHaveAttribute('data-row-id', ids.series);

                await page.goto('/about');
                await expect(page.getByRole('heading', { name: 'Magpie' })).toBeVisible();
                await page.goBack();
                expect(new URL(page.url()).searchParams.toString()).toBe(presentationQuery);
                await expect(pager.locator('.pager__page')).toHaveText('Page 2 of 2');
                await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                    'aria-sort',
                    'ascending',
                );
                await expect(grid.locator('tbody tr')).toHaveCount(2);
                await expect(grid.locator('tbody tr').nth(0)).toHaveAttribute('data-row-id', ids.movie);
                await expect(grid.locator('tbody tr').nth(1)).toHaveAttribute('data-row-id', ids.series);

                await page.reload();
                expect(new URL(page.url()).searchParams.toString()).toBe(presentationQuery);
                await expect(pager.locator('.pager__page')).toHaveText('Page 2 of 2');
                await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                    'aria-sort',
                    'ascending',
                );
                await expect(grid.locator('tbody tr')).toHaveCount(2);
                await expect(grid.locator('tbody tr').nth(0)).toHaveAttribute('data-row-id', ids.movie);
                await expect(grid.locator('tbody tr').nth(1)).toHaveAttribute('data-row-id', ids.series);

                await page.goto('/filters');
                await page
                    .locator(`[data-filter-id="${savedFilterId}"]`)
                    .getByRole('button', { name: `View ${savedFilterName} in Library` })
                    .click();
                await expect(page).toHaveURL(/savedFilter=/);
                expect(new URL(page.url()).searchParams.get('savedFilter')).toBe(savedFilterId);
                await expect(page.getByTestId('library-page__applied-saved-filter')).toContainText(savedFilterName);
                await page.goto('/about');
                await page.goBack();
                expect(new URL(page.url()).searchParams.get('savedFilter')).toBe(savedFilterId);
                await expect(page.getByTestId('library-page__applied-saved-filter')).toContainText(savedFilterName);
            });

            await test.step('read every supported tab and semantic field across the domains', async () => {
                const representatives: Array<[string, string, string]> = [
                    [ids.series, 'Journey Series', 'Show'],
                    [ids.season, 'Journey Season 1', 'Season'],
                    [ids.episode, 'Journey Episode 01', 'Episode'],
                    [ids.movie, 'Journey Feature', 'Movie'],
                    [ids.artist, 'Journey Artist', 'Artist'],
                    [ids.album, 'Journey Album', 'Album'],
                    [ids.book, 'Journey Book', 'E-book'],
                ];
                for (const [id, title, level] of representatives) {
                    await page.goto(`/library/${id}`);
                    const header = page.locator('[data-component="content-details-header"]');
                    await expect(header.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
                    await expect(
                        header.locator('.content-details-header__badges [data-component="badge"]').first(),
                    ).toHaveText(level);
                    const tabs = page.getByRole('tab');
                    for (let index = 0; index < (await tabs.count()); index += 1) {
                        const tab = tabs.nth(index);
                        await tab.click();
                        await expect(tab).toHaveAttribute('aria-selected', 'true');
                        await expect(page.getByRole('tabpanel')).toBeVisible();
                    }
                }

                await page.goto(`/library/${ids.movie}?tab=details`);
                await expect(page.locator('[data-component="content-details-header"]')).toContainText(
                    'A feature with metadata, a main file, and a sidecar.',
                );
                await expect(page.locator('[data-component="content-genres"]')).toContainText('Journey Genre');
                await expect(page.getByTestId('content-publish-date-row')).toContainText('May 6, 2024');
                await expect(page.getByRole('tabpanel')).toContainText('rich-movie');

                await page.getByRole('tab', { name: /files/i }).click();
                await expect(page.getByRole('tabpanel')).toContainText('Journey Feature.mkv');
                await expect(page.getByRole('tabpanel')).toContainText('Journey Feature.en.srt');
                await expect(page.getByRole('tabpanel').locator('[data-testid="files-tab-track-video"]')).toBeVisible();

                await page.getByRole('tab', { name: /releases/i }).click();
                await expect(page.getByRole('tabpanel')).toContainText('Journey Feature 2024 720p WEB-DL');
                await expect(page.getByRole('tabpanel')).toContainText('Imported');

                await page.getByRole('tab', { name: /metadata/i }).click();
                await expect(page.getByRole('tabpanel')).toContainText('mock-hierarchy-metadata-primary');
                await expect(page.getByRole('tabpanel')).toContainText('Journey Feature catalog');
            });

            await test.step('navigate the television and music hierarchies and their parent context', async () => {
                await page.goto(`/library/${ids.series}`);
                await page.getByRole('tab', { name: /seasons/i }).click();
                await page.locator(`.children-tab__title[title="Journey Season 1"]`).click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.series}/${ids.season}$`));

                const episodes = page.locator('[data-component="children-tab"] .children-tab__title');
                await expect(episodes).toHaveCount(30);
                await expect(episodes.nth(0)).toHaveAttribute('title', 'Journey Episode 01');
                await expect(episodes.nth(27)).toHaveAttribute('title', 'Journey Episode 28');
                await expect(episodes.nth(28)).toHaveAttribute('title', 'Journey Episode 29');
                await expect(episodes.nth(29)).toHaveAttribute('title', 'Journey Episode 30');
                await episodes.nth(0).click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.series}/${ids.season}/${ids.episode}$`));
                await page.getByTestId('content-details-header__parent-link').click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.series}/${ids.season}$`));
                await page.getByTestId('content-details-header__parent-link').click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.series}$`));

                await page.goto(`/library/${ids.artist}`);
                await page.getByRole('tab', { name: /albums/i }).click();
                await page.locator(`.children-tab__title[title="Journey Album"]`).click();
                await page.locator(`.children-tab__title[title="Journey Track 1"]`).click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.artist}/${ids.album}/${ids.track}$`));
                await expect(page.getByTestId('content-details-header__parent-link')).toContainText('Journey Album');

                await page.goto(`/library/${ids.movie}?tab=related`);
                await page.getByRole('link', { name: /Journey Performer/ }).click();
                await expect(page).toHaveURL(new RegExp(`/subjects/${ids.subject}$`));
                const subjectContent = page.locator('[data-component="subject-content-tab"]');
                await subjectContent.getByText('Journey Feature', { exact: true }).click();
                await page.locator('[data-action="open-content"]').click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.movie}`));
            });

            await test.step('a media server that cannot produce a link drops only its own chip', async () => {
                await mocks.mediaServer.useScenario(mediaLinkScenario);
                await mocks.mediaServer.armDeepLink('rich-movie', null);
                const linkPage = await page.context().newPage();
                try {
                    await linkPage.goto(`/library/${ids.movie}?tab=details`);
                    const linksRow = linkPage.getByTestId('content-links-row');
                    await expect(linksRow).toBeVisible();
                    await expect(linksRow).toContainText('Journey Feature catalog');
                    await expect(linkPage.getByTestId(`media-server-link-${mocks.mediaServer.slug}`)).toHaveCount(0);
                } finally {
                    await linkPage.close();
                    await mocks.mediaServer.useScenario('default');
                }
            });

            await test.step('the dashboard renders real stats and upcoming content', async () => {
                expect(await api.get('/api/content/stats')).toEqual({
                    total: 87,
                    missing: 79,
                    upgradable: 3,
                    fulfilled: 1,
                    fulfilledExternally: 0,
                    partial: 4,
                });
                await page.goto('/');
                const stats = page.locator('[data-component="content-stats-widget"]');
                await expect(stats.locator('.content-stats-widget__total')).toHaveText('87');
                await expect(stats.locator('[data-component="status-row"][data-status="fulfilled"]')).toContainText('1');
                await expect(stats.locator('[data-component="status-row"][data-status="missing"]')).toContainText('79');
                await expect(page.locator('[data-component="upcoming-content-widget"]')).toContainText(
                    'Journey Catalog 26',
                );
            });

            await test.step('the dashboard widgets link through to the pages behind them', async () => {
                await page.goto('/');
                const stats = page.locator('[data-component="content-stats-widget"]');
                await expect(stats.getByRole('link', { name: 'View all content' })).toHaveAttribute('href', '/library');
                await expect(stats.getByRole('link', { name: 'View 79 missing items' })).toHaveAttribute(
                    'href',
                    '/library/all?fulfillment=missing&levels=all',
                );
                await expect(stats.locator(`[data-content-id="${ids.recent}"]`)).toHaveAttribute(
                    'href',
                    `/library/${ids.recent}`,
                );
                await expect(page.getByRole('link', { name: 'View all jobs' })).toHaveAttribute('href', '/admin/jobs');

                const upcoming = page.locator(`[data-component="upcoming-content-widget"] [data-content-id="${ids.upcoming}"]`);
                await expect(upcoming).toHaveAttribute('href', `/library/${ids.upcoming}`);
                await upcoming.click();
                await expect(page).toHaveURL(new RegExp(`/library/${ids.upcoming}$`));

                await page.goto('/');
                await page.getByRole('link', { name: 'View calendar' }).click();
                await expect(page).toHaveURL(/\/calendar$/);
            });

            const upcomingPublishDateMs = new Date(
                (await api.get<{ publishDate: string }>(`/api/content/${ids.upcoming}/metadata`)).publishDate,
            ).getTime();
            const upcomingLocalDate = new Date(upcomingPublishDateMs);
            const upcomingDate = [
                upcomingLocalDate.getFullYear(),
                String(upcomingLocalDate.getMonth() + 1).padStart(2, '0'),
                String(upcomingLocalDate.getDate()).padStart(2, '0'),
            ].join('-');

            await test.step('the calendar shows the same item in month, week and day views', async () => {
                await page.goto(`/calendar?date=${upcomingDate}`);
                await expect(page.locator(`[data-component="month-view"] [data-date="${upcomingDate}"]`)).toContainText(
                    'Journey Catalog 26',
                );

                await page.getByRole('button', { name: 'Week view' }).click();
                await expect(page.locator(`[data-component="week-view"] [data-content-id="${ids.upcoming}"]`)).toContainText(
                    'Journey Catalog 26',
                );

                await page.getByRole('button', { name: 'Day view' }).click();
                await expect(page.locator(`[data-component="day-view"] [data-content-id="${ids.upcoming}"]`)).toContainText(
                    'Journey Catalog 26',
                );
            });

            await test.step('today, next and previous move the visible range', async () => {
                await page.goto(`/calendar?view=day&date=${upcomingDate}`);
                const nextDate = shiftedDate(upcomingDate, 1);
                await page.getByRole('button', { name: 'Next' }).click();
                await expect(page).toHaveURL(new RegExp(`date=${nextDate}`));
                await page.getByRole('button', { name: 'Previous' }).click();
                await expect(page).toHaveURL(new RegExp(`date=${upcomingDate}`));
                await page.getByRole('button', { name: 'Today' }).click();
                await expect(page.locator('.calendar-page__nav-title')).not.toHaveText('');
                expect(new URL(page.url()).searchParams.get('date')).toBeNull();
            });

            await test.step('one calendar range spans several content types', async () => {
                await page.goto('/calendar?view=day&date=2024-05-06');
                const day = page.locator('[data-component="day-view"]');
                await expect(day.getByText('Journey Series', { exact: true })).toBeVisible();
                await expect(day.getByText('Journey Feature', { exact: true })).toBeVisible();
                await expect(day.getByText('Journey Artist', { exact: true })).toBeVisible();
                await expect(day.getByText('Journey Book', { exact: true })).toBeVisible();
            });

            await test.step('a range with nothing in it renders the empty state', async () => {
                await page.goto('/calendar?view=day&date=2038-01-01');
                await expect(page.getByText('No events on this day', { exact: true })).toBeVisible();
                await expect(page.getByText('Try a different date or check other views', { exact: true })).toBeVisible();
            });

            await test.step('the about page states the version and build', async () => {
                const system = await api.get<SystemInfo>('/api/system');
                await page.goto('/about');
                await expect(page.getByRole('heading', { name: 'Magpie', level: 1 })).toBeVisible();
                await expect(page.getByText(`v${system.version}`, { exact: true })).toBeVisible();
                for (const [label, value] of [
                    ['Runtime', system.runtime],
                    ['Operating System', system.operatingSystem],
                    ['Architecture', system.architecture],
                ] as const) {
                    await expect(page.getByText(label, { exact: true }).locator('..')).toContainText(value);
                }
            });

            await test.step('a route that does not exist renders the not-found page', async () => {
                await page.goto('/this-household-route-does-not-exist');
                await expect(page.locator('[data-page="not-found"]')).toBeVisible();
                await expect(page.getByRole('heading', { name: 'Page not found', level: 1 })).toBeVisible();
                await expect(page.getByText('The requested page does not exist.', { exact: true })).toBeVisible();
                await expect(page.getByRole('link', { name: 'Go to Dashboard' })).toHaveAttribute('href', '/');
            });

            await test.step('the event stream refreshes exactly the visible record and nothing else', async () => {
                await page.goto(`/library/${ids.movie}?tab=details`);
                await expect(page.locator('[data-component="content-details-header"]')).toContainText(
                    'A feature with metadata, a main file, and a sidecar.',
                );
                await expectScopedEventRefresh(page, api, ids.movie, ids.upcoming);
            });
        } finally {
            const cleanupActions: Promise<unknown>[] = [
                mocks.hierarchyPrimary.useScenario('default'),
                mocks.indexer.useScenario('default'),
                mocks.downloadClient.useScenario('default'),
                mocks.mediaServer.useScenario('default'),
                ...richScenarioDirectories.map(([slug, scenario]) =>
                    rm(resolve(FIXTURES_ROOT, slug, 'scenarios', scenario), { recursive: true, force: true }),
                ),
            ];
            if (api && savedFilterId) {
                cleanupActions.push(api.delete(`/api/content-filters/${savedFilterId}`));
            }
            if (api && rich) {
                cleanupActions.push(
                    ...rich.patchedTriggers.map((trigger) =>
                        api.patch(`/api/${trigger.endpoint}/${trigger.id}`, { enabled: { value: trigger.enabled } }),
                    ),
                );
            }
            const cleanupResults = await Promise.allSettled(cleanupActions);
            expect(
                cleanupResults
                    .filter((result) => result.status === 'rejected')
                    .map((result) => String((result as PromiseRejectedResult).reason)),
            ).toEqual([]);
        }
    });
});

async function findRichIds(api: ApiClient): Promise<RichIds> {
    const byName = async (name: string): Promise<ContentItem> => {
        const page = await api.get<Paged<ContentItem>>(`/api/content?search=${encodeURIComponent(name)}&pageSize=100`);
        const item = page.items.find((candidate) => candidate.name === name);
        if (!item) {
            throw new Error(`Journey rich-library anchor ${name} was not found.`);
        }
        return item;
    };
    const [series, season, episode, movie, artist, album, track, book, upcoming, recent] = await Promise.all([
        byName('Journey Series'),
        byName('Journey Season 1'),
        byName('Journey Episode 01'),
        byName('Journey Feature'),
        byName('Journey Artist'),
        byName('Journey Album'),
        byName('Journey Track 1'),
        byName('Journey Book'),
        byName('Journey Catalog 26'),
        byName('Journey Catalog 48'),
    ]);
    const subjects = await api.get<
        Paged<{ id: string; metadataContainer?: { contributions?: Array<{ title?: string }> } }>
    >(`/api/subjects?filter=${encodeURIComponent('title = "Journey Performer"')}&pageSize=10`);
    const subject = subjects.items.find((item) =>
        item.metadataContainer?.contributions?.some((contribution) => contribution.title === 'Journey Performer'),
    );
    if (!subject) {
        throw new Error('Journey rich-library subject anchor Journey Performer was not found.');
    }
    return {
        series: series.id,
        season: season.id,
        episode: episode.id,
        movie: movie.id,
        artist: artist.id,
        album: album.id,
        track: track.id,
        book: book.id,
        subject: subject.id,
        upcoming: upcoming.id,
        recent: recent.id,
    };
}

function shiftedDate(date: string, days: number): string {
    const shifted = new Date(`${date}T00:00:00.000Z`);
    shifted.setUTCDate(shifted.getUTCDate() + days);
    return shifted.toISOString().slice(0, 10);
}
