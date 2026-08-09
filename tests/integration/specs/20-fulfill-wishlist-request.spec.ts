import { expect, test } from '@playwright/test';

import { acquireContent } from '../helpers/acquisition';
import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { waitForJob } from '../helpers/jobs';
import { mocks } from '../helpers/mocks';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface Wishlist {
    id: string;
    name: string;
    enabled: boolean;
    createdAt: string;
    lastSyncedAt?: string | null;
    rootFolderId?: string | null;
}

interface DiscoveredRow {
    discoveredId: string;
    source: string;
    externalId: string;
    title?: string | null;
    linkId?: string | null;
    status?: string | null;
    discoveredAt: string;
}

interface Ledger {
    page: Paged<DiscoveredRow>;
    counts: { totalDiscovered: number; inLibrary: number; notInLibrary: number; missing: number };
}

interface JobExecution {
    id: string;
    jobType: string;
    status: string;
    progress?: number | null;
    parentExecutionId?: string | null;
    parameters: Record<string, string>;
}

interface ContentItem {
    id: string;
    name: string;
    pendingMetadataRefresh: boolean;
    contentItem?: {
        fulfillment: string;
        mediaFiles: Array<{ fileKind: string; filePath: string; mediaInfo?: unknown }>;
    } | null;
}

test.describe.serial('Journey 20: fulfill a wishlist request', () => {
    test('discovers, hydrates, acquires and fulfills one deterministic wishlist match', async ({ page }) => {
        test.setTimeout(420_000);

        let movieRoot!: { id: string };
        const api = await restoreCheckpoint('configured-baseline', async (restored) => {
            movieRoot = (await restored.get<Array<{ id: string; name: string }>>('/api/root-folders')).find(
                (root) => root.name === 'Movies',
            )!;
            expect(movieRoot.id).toBeTruthy();
            expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
        });

        await armWishlistMetadata();

        const wishlist = await test.step('create and read the wishlist contract before its first refresh', async () => {
            const created = await api.post<Wishlist>('/api/wishlists', {
                name: 'Journey requested feature',
                criteria: {
                    contentHierarchyItem: {
                        and: [{ contentType: { and: [{ operator: 'Equals', value: 'movie' }] } }],
                    },
                },
                rootFolderId: movieRoot.id,
                enabled: true,
            });
            expect(created).toMatchObject({
                name: 'Journey requested feature',
                enabled: true,
                rootFolderId: movieRoot.id,
                lastSyncedAt: null,
            });
            expect(new Date(created.createdAt).getTime()).not.toBeNaN();

            await loginUi(page);
            await page.goto('/wishlists');
            await expect(page.locator(`[data-wishlist-id="${created.id}"]`)).toContainText('Journey requested feature');
            await page.goto(`/wishlists/${created.id}`);
            await expect(page.getByRole('heading', { name: 'Journey requested feature' })).toBeVisible();
            await expect(page.getByText('Monitored', { exact: true })).toBeVisible();

            const createdByProduct = await until(
                `wishlist ${created.id} creation refresh job`,
                async () => {
                    const jobs = await cli.json<Paged<JobExecution>>([
                        'jobs',
                        'list',
                        '--job-type',
                        'WishlistRefreshItem',
                        '--page-size',
                        '100',
                    ]);
                    return jobs.items.find((job) => job.parameters['wishlistId'] === created.id);
                },
                30_000,
                250,
            );
            expect((await waitForJob(api, createdByProduct.id, 120_000)).status).toBe('Completed');
            expect((await api.get<Wishlist>(`/api/wishlists/${created.id}`)).lastSyncedAt).toBeTruthy();
            return created;
        });

        const firstJob = await test.step('refresh from the UI and read the queued job through the CLI', async () => {
            const before = await cli.json<Paged<JobExecution>>([
                'jobs',
                'list',
                '--job-type',
                'WishlistRefreshItem',
                '--page-size',
                '100',
            ]);
            const known = new Set(before.items.map((job) => job.id));
            await page.locator('[data-action="refresh"]').click();
            await expect(page.getByTestId('toast-title').filter({ hasText: /^Refresh Queued$/ }).last()).toBeVisible();
            const execution = await until(
                `wishlist ${wishlist.id} refresh job`,
                async () => {
                    const current = await cli.json<Paged<JobExecution>>([
                        'jobs',
                        'list',
                        '--job-type',
                        'WishlistRefreshItem',
                        '--page-size',
                        '100',
                    ]);
                    return current.items.find(
                        (job) => !known.has(job.id) && job.parameters['wishlistId'] === wishlist.id,
                    );
                },
                30_000,
                250,
            );
            const completed = await waitForJob(api, execution.id, 120_000);
            expect(completed).toMatchObject({ status: 'Completed', progress: 100 });
            return completed;
        });

        const content = await test.step('read the discovered library match and its materialized metadata', async () => {
            const ledger = await until(
                'the wishlist match to hydrate into the library',
                async () => {
                    const current = await api.get<Ledger>(`/api/wishlists/${wishlist.id}/discovered`);
                    return current.page.items[0]?.linkId ? current : undefined;
                },
                90_000,
                500,
            );
            expect(ledger.counts).toMatchObject({
                totalDiscovered: 1,
                inLibrary: 1,
                notInLibrary: 0,
                missing: 1,
            });
            expect(ledger.page.items[0]).toMatchObject({
                source: 'tmdb',
                externalId: 'wishlist-feature',
                title: 'Wishlist Journey Feature',
                status: 'Missing',
            });
            expect(new Date(ledger.page.items[0]!.discoveredAt).getTime()).not.toBeNaN();

            const id = ledger.page.items[0]!.linkId!;
            const hydrated = await until(
                'wishlist metadata refresh to complete',
                async () => {
                    const item = await api.get<ContentItem>(`/api/content/${id}`);
                    return item.name === 'Wishlist Journey Feature' && !item.pendingMetadataRefresh ? item : undefined;
                },
                60_000,
                500,
            );
            expect(hydrated.contentItem?.fulfillment).toBe('Missing');
            expect(await api.get(`/api/content/${id}/metadata`)).toMatchObject({
                title: 'Wishlist Journey Feature',
                externalIds: expect.arrayContaining([expect.objectContaining({ value: 'wishlist-feature' })]),
            });
            const refreshed = await api.get<Wishlist>(`/api/wishlists/${wishlist.id}`);
            expect(refreshed.lastSyncedAt).toBeTruthy();
            return hydrated;
        });

        const secondJob = await test.step('refresh again through the CLI without duplicating discovered or library state', async () => {
            const executionId = await cli.json<string>([
                'jobs',
                'enqueue',
                '--json',
                JSON.stringify({ jobType: 'WishlistRefreshItem', parameters: { wishlistId: wishlist.id } }),
            ]);
            const completed = await waitForJob(api, executionId, 120_000);
            expect(completed.status).toBe('Completed');
            const ledger = await api.get<Ledger>(`/api/wishlists/${wishlist.id}/discovered`);
            expect(ledger.page.totalItems).toBe(1);
            expect(ledger.page.items[0]?.linkId).toBe(content.id);
            const library = await api.get<Paged<ContentItem>>(
                `/api/content?search=${encodeURIComponent('Wishlist Journey Feature')}&pageSize=100`,
            );
            expect(library.items.filter((item) => item.name === 'Wishlist Journey Feature')).toHaveLength(1);

            await page.reload();
            const row = page.locator(`[data-row-id="${ledger.page.items[0]!.discoveredId}"]`);
            await expect(row).toContainText('Wishlist Journey Feature');
            await expect(row.locator('[data-action="open-content"]')).toHaveAttribute('href', `/library/${content.id}`);
            return completed;
        });

        const acquired = await test.step('continue the request through release search, download and import', async () => {
            const result = await acquireContent(api, {
                scenario: 'wishlist-acquisition',
                contentId: content.id,
                searchTitle: content.name,
                releaseTitle: 'Wishlist Journey Feature 2026 720p WEB-DL',
                releaseExternalId: 'wishlist-release',
                downloadExternalId: 'wishlist-download',
                categories: ['movies'],
                landedFiles: [
                    { source: 'movie-720p.mkv', target: 'Wishlist Journey Feature/Wishlist Journey Feature.mkv' },
                ],
            });
            expect(result.download).toMatchObject({ status: 'Imported', progress: 100 });
            expect(result.content.contentItem).toMatchObject({ fulfillment: 'Upgradable' });
            expect(result.content.contentItem?.mediaFiles).toContainEqual(
                expect.objectContaining({
                    fileKind: 'Video',
                    filePath: expect.stringMatching(/Wishlist Journey Feature\.mkv$/),
                }),
            );
            return result;
        });

        await test.step('read final wishlist, content, job, release, download and file state', async () => {
            const ledger = await until(
                'the wishlist ledger to reflect fulfilled library state',
                async () => {
                    const current = await api.get<Ledger>(`/api/wishlists/${wishlist.id}/discovered`);
                    return current.page.items[0]?.status === 'Found' ? current : undefined;
                },
                30_000,
                500,
            );
            expect(ledger.counts).toMatchObject({
                totalDiscovered: 1,
                inLibrary: 1,
                notInLibrary: 0,
                missing: 0,
            });
            expect(await api.get<Wishlist>(`/api/wishlists/${wishlist.id}`)).toMatchObject({
                id: wishlist.id,
                name: 'Journey requested feature',
                enabled: true,
                lastSyncedAt: expect.any(String),
            });
            expect(await api.get<ContentItem>(`/api/content/${content.id}`)).toMatchObject({
                name: 'Wishlist Journey Feature',
                contentItem: { fulfillment: 'Upgradable' },
            });
            expect(await api.get<Array<{ title: string; acquisitionStatus: string }>>(
                `/api/content/${content.id}/releases`,
            )).toContainEqual(
                expect.objectContaining({
                    title: 'Wishlist Journey Feature 2026 720p WEB-DL',
                    acquisitionStatus: 'Imported',
                }),
            );
            expect(await api.get(`/api/downloads/${acquired.download.id}`)).toMatchObject({
                externalId: 'wishlist-download',
                status: 'Imported',
                progress: 100,
            });
            expect(await cli.json<ContentItem>(['content', 'get', content.id])).toMatchObject({
                id: content.id,
                contentItem: { fulfillment: 'Upgradable' },
            });
            expect(firstJob.id).not.toBe(secondJob.id);

            const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
            const wishlistJobs = jobs.items.filter(
                (job) => job.id === firstJob.id || job.id === secondJob.id || job.parameters?.['wishlistId'] === wishlist.id,
            );
            expect(wishlistJobs.map((job) => job.id)).toEqual(expect.arrayContaining([firstJob.id, secondJob.id]));
            expect(wishlistJobs.every((job) => job.status === 'Completed')).toBe(true);
            expect(wishlistJobs.every((job) => job.progress === 100)).toBe(true);

            const importJobs = jobs.items.filter(
                (job) => job.parameters?.['downloadId'] === acquired.download.id || job.jobType.startsWith('DownloadImport'),
            );
            expect(importJobs.length).toBeGreaterThan(0);
            expect(importJobs.every((job) => ['Completed', 'Cancelled'].includes(job.status))).toBe(true);

            const childJobs = jobs.items.filter((job) => job.parentExecutionId != null);
            expect(childJobs.length).toBeGreaterThan(0);
            expect(
                childJobs.every((job) => jobs.items.some((parent) => parent.id === job.parentExecutionId)),
            ).toBe(true);

            await page.goto(`/wishlists/${wishlist.id}`);
            await expect(
                page.locator(`[data-row-id="${ledger.page.items[0]!.discoveredId}"]`),
            ).toContainText('Fulfilled');
            await page.goto(`/library/${content.id}?tab=files`);
            await expect(page.getByRole('tabpanel')).toContainText('Wishlist Journey Feature.mkv');
        });
    });
});

async function armWishlistMetadata(): Promise<void> {
    const candidate = {
        providerType: mocks.hierarchyPrimary.slug,
        externalIdSource: 'tmdb',
        externalId: 'wishlist-feature',
        contentType: 'movie',
        level: 'movie',
        title: 'Wishlist Journey Feature',
        year: 2026,
        releaseDate: '2026-04-05T00:00:00Z',
        overview: 'A deterministic feature requested through a wishlist.',
        language: 'en',
        thumbnailUrl: 'https://assets.journey.invalid/wishlist-feature.jpg',
    };
    await mocks.hierarchyPrimary.useScenario('wishlist-journey');
    await mocks.hierarchyPrimary.write('advancedSearchAsync.json', [candidate]);
    await mocks.hierarchyPrimary.write('hydrateAsync-wishlist-feature.json', candidate);
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchyPrimary.slug,
        source: 'tmdb:wishlist-feature',
        title: 'Wishlist Journey Feature',
        overview: 'A deterministic feature requested through a wishlist.',
        language: 'en',
        publishDate: '2026-04-05T00:00:00Z',
        externalIds: [{ source: 'tmdb', value: 'wishlist-feature', ordinal: 0 }],
        images: [
            {
                type: 'poster',
                url: 'https://assets.journey.invalid/wishlist-feature.jpg',
                mimeType: 'image/jpeg',
                source: 'journey',
                width: 600,
                height: 900,
                isPrimary: true,
                ordinal: 0,
            },
        ],
    });
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);
    await mocks.hierarchyPrimary.clearCalls();
}
