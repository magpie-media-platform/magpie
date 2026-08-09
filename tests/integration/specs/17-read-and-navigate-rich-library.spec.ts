import { expect, test, type Request } from '@playwright/test';

import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { loginUi } from '../helpers/ui';
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
    parentId?: string | null;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    metadataContainer?: {
        contributions?: Array<{ sequenceNumber?: number | null; publishDate?: string | null }>;
    } | null;
    contentItem?: {
        fulfillment: string;
        mediaFiles: Array<{ filePath: string; mediaInfo?: { tracks?: Array<{ type: string }> } | null }>;
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
}

test.describe.serial('Journey 17: read and navigate a populated library', () => {
    test('reads every current domain, hierarchy, user route and scoped event refresh', async ({ page }) => {
        test.setTimeout(600_000);

        let ids!: RichIds;
        const api = await restoreCheckpoint('rich-library', async (restored) => {
            const stats = await restored.get<{ total: number; fulfilled: number; missing: number }>('/api/content/stats');
            expect(stats.total).toBeGreaterThan(65);
            expect(stats.fulfilled).toBeGreaterThanOrEqual(1);
            expect(stats.missing).toBeGreaterThan(25);
            ids = await findRichIds(restored);
            expect((await restored.get<ContentItem>(`/api/content/${ids.episode}`)).parentId).toBe(ids.season);
            expect((await restored.get<ContentItem>(`/api/content/${ids.movie}`)).contentItem?.mediaFiles).toEqual(
                expect.arrayContaining([
                    expect.objectContaining({ filePath: expect.stringMatching(/Journey Feature\.mkv$/) }),
                    expect.objectContaining({ filePath: expect.stringMatching(/Journey Feature\.en\.srt$/) }),
                ]),
            );
        });
        await loginUi(page);

        await test.step('browse, filter, sort, page, save, select and change responsive presentation', async () => {
            await page.setViewportSize({ width: 1365, height: 900 });
            await page.goto('/library/all');
            const grid = page.getByRole('grid');
            await expect(grid).toBeVisible();
            await expect(page.getByTestId('library-page__mobile-list')).toBeHidden();

            await page.getByRole('button', { name: 'Filter by Title' }).click();
            await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Feature');
            await page.getByRole('button', { name: 'Apply' }).click();
            await expect(grid.locator(`[data-row-id="${ids.movie}"]`)).toContainText('Journey Feature');
            await page.getByRole('button', { name: 'Title filtered, edit filter' }).click();
            await page.getByRole('button', { name: 'Clear' }).click();

            await page.getByRole('button', { name: 'Filter by Type' }).click();
            await page.getByRole('menuitemradio', { name: 'Movie', exact: true }).click();
            await expect(grid.locator('tbody tr').first()).toContainText('Movie');
            await page.getByRole('button', { name: 'Type filtered, edit filter' }).click();
            await page.getByRole('menuitemradio', { name: 'Any' }).click({ force: true });

            await page.getByRole('button', { name: 'Filter by Status' }).click();
            await page.getByRole('menuitemradio', { name: 'Upgradable', exact: true }).click();
            await expect(grid.locator(`[data-row-id="${ids.movie}"]`)).toContainText('Upgradable');
            await page.getByRole('button', { name: 'Status filtered, edit filter' }).click();
            await page.getByRole('menuitemradio', { name: 'Any' }).click({ force: true });

            await page.getByRole('button', { name: 'Sort by Title' }).click();
            await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                'aria-sort',
                'ascending',
            );
            await page.getByRole('button', { name: 'Sort by Title' }).click();
            await expect(grid.getByRole('columnheader').filter({ hasText: 'Title' })).toHaveAttribute(
                'aria-sort',
                'descending',
            );

            const pager = page.getByRole('navigation', { name: 'Pagination' });
            await expect(pager).toContainText('Page 1 of 2');
            await pager.getByRole('button', { name: 'Go to next page' }).click();
            await expect(pager).toContainText('Page 2 of 2');
            await pager.getByRole('button', { name: 'Go to first page' }).click();

            const filter = await api.post<{ id: string }>('/api/content-filters', {
                name: 'Rich movies',
                criteria: {
                    contentHierarchyItem: {
                        and: [
                            { contentType: { and: [{ operator: 'Equals', value: 'movie' }] } },
                        ],
                    },
                },
            });
            await page.goto('/filters');
            const filterRow = page.locator(`[data-filter-id="${filter.id}"]`);
            await expect(filterRow).toContainText('Rich movies');
            await filterRow.getByRole('button', { name: 'View Rich movies in Library' }).click();
            await expect(page.getByTestId('library-page__applied-saved-filter')).toContainText('Rich movies');
            await expect(page.getByRole('grid').locator(`[data-row-id="${ids.movie}"]`)).toBeVisible();

            await page.setViewportSize({ width: 390, height: 844 });
            await page.goto('/library/all');
            await expect(page.getByRole('grid')).toBeHidden();
            const mobile = page.getByTestId('library-page__mobile-list');
            await expect(mobile).toBeVisible();
            await page.getByRole('button', { name: 'Filters' }).click();
            await page.getByRole('textbox', { name: 'Title', exact: true }).fill('Journey Feature');
            await expect(mobile.getByText('Journey Feature', { exact: true })).toBeVisible();
            await page.locator('[data-action="enter-select-mode"]').click();
            await mobile.getByRole('button', { name: 'Select Journey Feature card' }).click();
            await page.locator('.library-page__mobile-selection-actions [data-action="bulk-unmonitor"]').click();
            await expect.poll(async () => (await api.get<ContentItem>(`/api/content/${ids.movie}`)).monitoring).toBe(false);
            await api.patch(`/api/content/${ids.movie}`, { monitoring: { value: true } });

            await page.goto('/library/content/tv');
            await expect(
                page.getByTestId('library-page__mobile-list').getByText('Journey Series', { exact: true }),
            ).toBeVisible();
        });

        await test.step('read every supported tab and semantic field across current domains', async () => {
            await page.setViewportSize({ width: 1365, height: 900 });
            const representatives: Array<[string, string, string]> = [
                [ids.series, 'Journey Series', 'Show'],
                [ids.movie, 'Journey Feature', 'Movie'],
                [ids.artist, 'Journey Artist', 'Artist'],
                [ids.book, 'Journey Book', 'Book'],
            ];
            for (const [id, title, level] of representatives) {
                await page.goto(`/library/${id}`);
                const header = page.locator('[data-component="content-details-header"]');
                await expect(header.getByRole('heading', { name: new RegExp(title) })).toBeVisible();
                await expect(header).toContainText(level);
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
            await expect(page.getByRole('tabpanel')).toContainText('rich-movie');

            await page.getByRole('tab', { name: /files/i }).click();
            const files = page.getByRole('tabpanel');
            await expect(files).toContainText('Journey Feature.mkv');
            await expect(files).toContainText('Journey Feature.en.srt');
            await expect(files.locator('[data-testid="files-tab-track-video"]')).toBeVisible();

            const movieReleases = await until(
                'Journey Feature imported release',
                async () => {
                    const releases = await api.get<Array<{ title: string; acquisitionStatus: string }>>(
                        `/api/content/${ids.movie}/releases`,
                    );
                    return releases.find(
                        (release) =>
                            release.title === 'Journey Feature 2024 720p WEB-DL' &&
                            release.acquisitionStatus === 'Imported',
                    );
                },
                60_000,
                500,
            );
            expect(movieReleases).toMatchObject({
                title: 'Journey Feature 2024 720p WEB-DL',
                acquisitionStatus: 'Imported',
            });
            await page.getByRole('tab', { name: /releases/i }).click();
            await expect(page.getByRole('tabpanel')).toContainText('Journey Feature 2024 720p WEB-DL');
            await expect(page.getByRole('tabpanel')).toContainText('Imported');

            await page.getByRole('tab', { name: /metadata/i }).click();
            await expect(page.getByRole('tabpanel')).toContainText('mock-hierarchy-metadata-primary');
            await expect(page.getByRole('tabpanel')).toContainText('Journey Feature catalog');
            const links = await api.get<Array<{ serverName: string; url: string }>>(
                `/api/content/${ids.movie}/media-server-links`,
            );
            expect(links).toContainEqual(
                expect.objectContaining({ serverName: 'Local Media Server', url: expect.stringContaining('mock') }),
            );
        });

        await test.step('navigate TV and music hierarchies, parent context and subject relationships', async () => {
            const bySequence = await api.get<Paged<ContentItem>>(
                `/api/content?parentId=${ids.season}&sortBy=SequenceNumber&sortDirection=Ascending&pageSize=100`,
            );
            expect(bySequence.totalItems).toBe(30);
            expect(bySequence.items.map((item) => item.name)).toEqual([
                'Journey Episode 01',
                'Journey Episode 02',
                'Journey Episode 03',
                'Journey Episode 04',
                'Journey Episode 05',
                'Journey Episode 06',
                'Journey Episode 07',
                'Journey Episode 08',
                'Journey Episode 09',
                'Journey Episode 10',
                'Journey Episode 11',
                'Journey Episode 12',
                'Journey Episode 13',
                'Journey Episode 14',
                'Journey Episode 15',
                'Journey Episode 16',
                'Journey Episode 17',
                'Journey Episode 18',
                'Journey Episode 19',
                'Journey Episode 20',
                'Journey Episode 21',
                'Journey Episode 22',
                'Journey Episode 23',
                'Journey Episode 24',
                'Journey Episode 25',
                'Journey Episode 26',
                'Journey Episode 27',
                'Journey Episode 28',
                'Journey Episode 29',
                'Journey Episode 30',
            ]);
            expect(
                await api.get<{ sequenceNumber?: number | null }>(`/api/content/${bySequence.items[0]!.id}/metadata`),
            ).toMatchObject({ sequenceNumber: 1 });
            expect(
                await api.get<{ sequenceNumber?: number | null }>(`/api/content/${bySequence.items[27]!.id}/metadata`),
            ).toMatchObject({ sequenceNumber: 28 });
            expect(
                await api.get<{ sequenceNumber?: number | null }>(`/api/content/${bySequence.items[28]!.id}/metadata`),
            ).toMatchObject({ sequenceNumber: null });
            expect(
                await api.get<{ sequenceNumber?: number | null }>(`/api/content/${bySequence.items[29]!.id}/metadata`),
            ).toMatchObject({ sequenceNumber: null });

            const byPublishDate = await api.get<Paged<ContentItem>>(
                `/api/content?parentId=${ids.season}&sortBy=PublishDate&sortDirection=Descending&pageSize=100`,
            );
            expect(byPublishDate.items.slice(0, 2).map((item) => item.name)).toEqual([
                'Journey Episode 30',
                'Journey Episode 29',
            ]);

            expect(await api.get<ContentItem>(`/api/content/${ids.episode}`)).toMatchObject({
                id: ids.episode,
                name: 'Journey Episode 01',
                parentId: ids.season,
            });
            expect(await api.get<ContentItem>(`/api/content/${ids.season}`)).toMatchObject({
                id: ids.season,
                name: 'Journey Season 1',
                parentId: ids.series,
            });
            const tracks = await api.get<Paged<ContentItem>>(`/api/content/${ids.album}/children?pageSize=100`);
            expect(tracks.items.map((item) => item.name)).toEqual([
                'Journey Track 1',
                'Journey Track 2',
                'Journey Track 3',
            ]);
            expect(await api.get<ContentItem>(`/api/content/${ids.track}`)).toMatchObject({
                id: ids.track,
                name: 'Journey Track 1',
                parentId: ids.album,
            });

            await page.goto(`/library/${ids.movie}?tab=related`);
            await page.getByRole('link', { name: /Journey Performer/ }).click();
            await expect(page).toHaveURL(new RegExp(`/subjects/${ids.subject}`));
            const subjectContent = page.locator('[data-component="subject-content-tab"]');
            await expect(subjectContent.getByText('Journey Feature', { exact: true })).toBeVisible();
            await subjectContent.getByText('Journey Feature', { exact: true }).click();
            await expect(page).toHaveURL(new RegExp(`/library/${ids.movie}`));

            await page.goto(`/subjects/${ids.subject}`);
            await expect(page.getByRole('heading', { name: 'Journey Performer' })).toBeVisible();
            await expect(subjectContent.getByText('Journey Feature', { exact: true })).toBeVisible();
        });

        await test.step('read dashboard, downloads, wanted, wishlist, calendar, filters, jobs and extension routes', async () => {
            await page.goto('/');
            await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible();
            await expect(page.locator('[data-component="content-stats-widget"]')).toContainText('Journey');
            await expect(page.locator('[data-component="upcoming-content-widget"]')).toContainText('Journey Catalog 26');

            await page.goto('/library/subjects/all');
            await expect(page.getByText('Journey Performer', { exact: true }).first()).toBeVisible();
            await page.goto(`/subjects/${ids.subject}`);
            await expect(
                page.locator('[data-component="subject-content-tab"]').getByText('Journey Feature', { exact: true }),
            ).toBeVisible();

            const downloadHistory = await api.get<Paged<{ title: string; status: string }>>(
                '/api/downloads?pageSize=100',
            );
            expect(downloadHistory.items).toContainEqual(
                expect.objectContaining({
                    title: expect.stringContaining('Journey Feature'),
                    status: 'Imported',
                }),
            );
            await page.goto('/downloads');
            await page.getByRole('tab', { name: /History/ }).click();
            await expect(page.getByText(/Journey Feature/)).toBeVisible();
            await page.goto('/wanted');
            await expect(page.getByText('Journey Catalog 01', { exact: true })).toBeVisible();

            const roots = await api.get<Array<{ id: string; contentType: string }>>('/api/root-folders');
            const wishlist = await api.post<{ id: string }>('/api/wishlists', {
                name: 'Rich library wishlist',
                criteria: {
                    contentHierarchyItem: {
                        and: [{ contentType: { and: [{ operator: 'Equals', value: 'movie' }] } }],
                    },
                },
                rootFolderId: roots.find((root) => root.contentType === 'movie')!.id,
                enabled: false,
            });
            await page.goto('/wishlists');
            await expect(page.locator(`[data-wishlist-id="${wishlist.id}"]`)).toContainText('Rich library wishlist');
            await page.goto(`/wishlists/${wishlist.id}`);
            await expect(page.getByRole('heading', { name: 'Rich library wishlist' })).toBeVisible();
            await expect(page.getByText('Unmonitored', { exact: true })).toBeVisible();

            const upcomingMetadata = await api.get<{ publishDate?: string | null }>(
                `/api/content/${ids.upcoming}/metadata`,
            );
            await page.goto(`/calendar?view=list&date=${upcomingMetadata.publishDate!.slice(0, 10)}`);
            const calendarEntry = page.locator(`[data-content-id="${ids.upcoming}"]`);
            await expect(calendarEntry).toContainText('Journey Catalog 26');
            await calendarEntry.click();
            await expect(page).toHaveURL(new RegExp(`/library/${ids.upcoming}`));

            await page.goto('/filters');
            await expect(page.getByText('Rich movies', { exact: true })).toBeVisible();
            await page.goto('/my-jobs');
            await expect(page.getByRole('heading', { name: 'My Jobs' })).toBeVisible();
            const myJobs = await api.get<Paged<{ jobType: string; status: string }>>('/api/jobs?mine=true&pageSize=100');
            expect(myJobs.items.some((job) => job.status === 'Completed')).toBe(true);
            await expect(page.getByRole('list', { name: 'Job history' }).getByRole('listitem').first()).toBeVisible();

            await page.goto(`/library/${ids.movie}?tab=activity`);
            const activity = page.locator('[data-component="activity-tab"]');
            await expect(activity.locator('[data-action="view-detail"]').first()).toBeVisible();
            await activity.locator('[data-action="view-detail"]').first().click();
            const detail = page.getByRole('dialog');
            await expect(detail.getByRole('tab', { name: 'Details' })).toBeVisible();
            await detail.getByRole('tab', { name: 'Trace context' }).click();
            await expect(detail.getByRole('tabpanel')).toBeVisible();
            await page.keyboard.press('Escape');
        });

        await test.step('scope event-stream network refreshes to relevant visible records exactly once', async () => {
            await page.goto(`/library/${ids.movie}?tab=details`);
            await expect(page.locator('[data-component="content-details-header"]')).toContainText(
                'A feature with metadata, a main file, and a sidecar.',
            );
            await page.waitForTimeout(750);
            const observed: string[] = [];
            const record = (request: Request) => {
                const url = new URL(request.url());
                if (request.method() === 'GET' && (url.pathname.startsWith('/api/content') || url.pathname.includes('/metadata'))) {
                    observed.push(`${url.pathname}${url.search}`);
                }
            };
            page.on('request', record);

            const unrelated = await api.get<ContentItem>(`/api/content/${ids.upcoming}`);
            await api.patch(`/api/content/${ids.upcoming}`, { monitoring: { value: !unrelated.effectiveMonitoring } });
            await page.waitForTimeout(750);
            expect(observed.filter((url) => url === `/api/content/${ids.upcoming}`)).toHaveLength(0);
            expect(observed.filter((url) => url === `/api/content/${ids.movie}`)).toHaveLength(0);

            const movie = await api.get<ContentItem>(`/api/content/${ids.movie}`);
            await api.patch(`/api/content/${ids.movie}`, { monitoring: { value: !movie.effectiveMonitoring } });
            await until(
                'the visible movie to refresh once after its event',
                async () => observed.filter((url) => url === `/api/content/${ids.movie}`).length || undefined,
                15_000,
                100,
            );
            await page.waitForTimeout(750);
            expect(observed.filter((url) => url === `/api/content/${ids.movie}`)).toHaveLength(1);
            const metadataRequests = observed.filter((url) => url.startsWith(`/api/content/${ids.movie}/metadata`));
            expect(new Set(metadataRequests).size).toBe(metadataRequests.length);
            page.off('request', record);
        });
    });
});

async function findRichIds(api: ApiClient): Promise<RichIds> {
    const byName = async (name: string): Promise<ContentItem> => {
        const page = await api.get<Paged<ContentItem>>(`/api/content?search=${encodeURIComponent(name)}&pageSize=100`);
        const item = page.items.find((candidate) => candidate.name === name);
        if (!item) {
            throw new Error(`Rich-library anchor ${name} was not found.`);
        }
        return item;
    };
    const series = await byName('Journey Series');
    const season = await byName('Journey Season 1');
    const episode = await byName('Journey Episode 01');
    const movie = await byName('Journey Feature');
    const artist = await byName('Journey Artist');
    const album = await byName('Journey Album');
    const track = await byName('Journey Track 1');
    const book = await byName('Journey Book');
    const upcoming = await byName('Journey Catalog 26');
    const subjects = await api.get<Paged<{ id: string; metadataContainer?: { contributions?: Array<{ title?: string }> } }>>(
        `/api/subjects?filter=${encodeURIComponent('title = "Journey Performer"')}&pageSize=10`,
    );
    const subject = subjects.items.find((item) =>
        item.metadataContainer?.contributions?.some((contribution) => contribution.title === 'Journey Performer'),
    );
    if (!subject) {
        throw new Error('Rich-library subject anchor Journey Performer was not found.');
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
    };
}
