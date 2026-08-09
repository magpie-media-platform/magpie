import { rm } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test } from '@playwright/test';

import { acquireContent } from '../helpers/acquisition';
import { type ApiClient, loginAdmin } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { integrationEnvironment } from '../helpers/environment';
import { FIXTURES_ROOT } from '../helpers/fixtures';
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
    contentFilterId: string;
    ownerUserId: string | null;
    name: string;
    enabled: boolean;
    createdAt: string;
    lastSyncedAt?: string | null;
    rootFolderId?: string | null;
    contentFilter?: {
        id: string;
        criteria: Record<string, unknown>;
    } | null;
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
    parameters?: Record<string, string>;
}

interface ContentItem {
    id: string;
    name: string;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    rootFolderId?: string | null;
    effectiveRootFolderId: string;
    pendingMetadataRefresh: boolean;
    contentItem?: {
        fulfillment: string;
        mediaFiles: Array<{ fileKind: string; filePath: string; mediaInfo?: unknown }>;
    } | null;
}

interface User {
    id: string;
    username: string;
    role: string;
    enabled: boolean;
}

interface ContentFilter {
    id: string;
    ownerUserId: string | null;
    name: string;
    criteria: Record<string, unknown>;
}

const metadataScenario = 'standing-order';
const acquisitionScenario = 'standing-order-acquisition';
const memberUsername = 'standing-order-requester';
const memberPassword = 'standing-order-requester-pa55!';

test.describe.serial('Journey 15: a standing order', () => {
    test('fulfills and manages a standing order end to end', async ({ page }) => {
        test.setTimeout(600_000);

        let api!: ApiClient;
        let movieRoot!: { id: string; name: string };
        let wishlist!: Wishlist;
        let creationJob!: JobExecution;
        let uiRefreshJob!: JobExecution;
        let rerunJob!: JobExecution;
        let content!: ContentItem;
        let acquired!: Awaited<ReturnType<typeof acquireContent>>;
        let member!: User;
        let memberWishlist!: Wishlist;
        let disabledWishlist!: Wishlist;
        let scanExecutionId = '';
        let scanKnownJobIds = new Set<string>();
        let refreshedBeforeScan = new Map<string, string | null | undefined>();
        let convertedWishlist!: Wishlist;
        let initialJobIds = new Set<string>();

        try {
            await test.step('restore the configured baseline and create the wishlist', async () => {
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    movieRoot = (await restored.get<Array<{ id: string; name: string }>>('/api/root-folders')).find(
                        (root) => root.name === 'Movies',
                    )!;
                    expect(movieRoot.id).toBeTruthy();
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
                    expect(await restored.get<Wishlist[]>('/api/wishlists')).toEqual([]);
                });

                const candidate = {
                    providerType: mocks.hierarchyPrimary.slug,
                    externalIdSource: 'tmdb',
                    externalId: 'standing-order-feature',
                    contentType: 'movie',
                    level: 'movie',
                    title: 'Standing Order Feature',
                    year: 2026,
                    releaseDate: '2026-04-05T00:00:00Z',
                    overview: 'A deterministic feature discovered through a standing order.',
                    language: 'en',
                    thumbnailUrl: 'https://assets.journey.invalid/standing-order-feature.jpg',
                };
                await mocks.hierarchyPrimary.useScenario(metadataScenario);
                await mocks.hierarchyPrimary.write('advancedSearchAsync.json', [candidate]);
                await mocks.hierarchyPrimary.write('hydrateAsync-standing-order-feature.json', candidate);
                await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
                    providerType: mocks.hierarchyPrimary.slug,
                    source: 'tmdb:standing-order-feature',
                    title: 'Standing Order Feature',
                    overview: 'A deterministic feature discovered through a standing order.',
                    language: 'en',
                    publishDate: '2026-04-05T00:00:00Z',
                    externalIds: [{ source: 'tmdb', value: 'standing-order-feature', ordinal: 0 }],
                    images: [
                        {
                            type: 'poster',
                            url: 'https://assets.journey.invalid/standing-order-feature.jpg',
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

                const initialJobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                initialJobIds = new Set(initialJobs.items.map((job) => job.id));
                wishlist = await api.post<Wishlist>('/api/wishlists', {
                    name: 'Journey standing order',
                    criteria: {
                        contentHierarchyItem: {
                            and: [{ contentType: { and: [{ operator: 'Equals', value: 'movie' }] } }],
                        },
                    },
                    rootFolderId: movieRoot.id,
                    enabled: true,
                });
                expect(wishlist).toMatchObject({
                    name: 'Journey standing order',
                    enabled: true,
                    rootFolderId: movieRoot.id,
                    lastSyncedAt: null,
                });
                expect(new Date(wishlist.createdAt).getTime()).not.toBeNaN();

                creationJob = await until(
                    `wishlist ${wishlist.id} creation refresh job`,
                    async () => {
                        const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                        return jobs.items.find(
                            (job) =>
                                !initialJobIds.has(job.id) &&
                                job.jobType === 'WishlistRefreshItem' &&
                                job.parameters?.['wishlistId'] === wishlist.id,
                        );
                    },
                    30_000,
                    250,
                );
                expect(await waitForJob(api, creationJob.id, 120_000)).toMatchObject({
                    status: 'Completed',
                    progress: 100,
                });
                expect((await api.get<Wishlist>(`/api/wishlists/${wishlist.id}`)).lastSyncedAt).toBeTruthy();

                await loginUi(page);
                await page.goto('/wishlists');
                await expect(page.locator(`[data-wishlist-id="${wishlist.id}"]`)).toContainText(
                    'Journey standing order',
                );
                await page.goto(`/wishlists/${wishlist.id}`);
                await expect(page.getByRole('heading', { name: 'Journey standing order', exact: true })).toBeVisible();
                await expect(page.getByText('Monitored', { exact: true })).toBeVisible();
            });

            await test.step('refresh from the UI and read the queued job through the CLI', async () => {
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
                uiRefreshJob = await until(
                    `wishlist ${wishlist.id} UI refresh job`,
                    async () => {
                        const jobs = await cli.json<Paged<JobExecution>>([
                            'jobs',
                            'list',
                            '--job-type',
                            'WishlistRefreshItem',
                            '--page-size',
                            '100',
                        ]);
                        return jobs.items.find(
                            (job) => !known.has(job.id) && job.parameters?.['wishlistId'] === wishlist.id,
                        );
                    },
                    30_000,
                    250,
                );
                expect(await waitForJob(api, uiRefreshJob.id, 120_000)).toMatchObject({
                    status: 'Completed',
                    progress: 100,
                });
            });

            await test.step('read the discovered ledger and the metadata of the match it hydrated', async () => {
                const ledger = await until(
                    'the standing-order match to hydrate into the library',
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
                    externalId: 'standing-order-feature',
                    title: 'Standing Order Feature',
                    status: 'Missing',
                });
                expect(new Date(ledger.page.items[0]!.discoveredAt).getTime()).not.toBeNaN();

                const id = ledger.page.items[0]!.linkId!;
                content = await until(
                    'standing-order metadata refresh to complete',
                    async () => {
                        const item = await api.get<ContentItem>(`/api/content/${id}`);
                        return item.name === 'Standing Order Feature' && !item.pendingMetadataRefresh ? item : undefined;
                    },
                    60_000,
                    500,
                );
                expect(content.contentItem?.fulfillment).toBe('Missing');
                expect(await api.get(`/api/content/${id}/metadata`)).toMatchObject({
                    title: 'Standing Order Feature',
                    externalIds: expect.arrayContaining([
                        expect.objectContaining({ source: 'tmdb', value: 'standing-order-feature' }),
                    ]),
                });

                await page.reload();
                const row = page.locator(`[data-row-id="${ledger.page.items[0]!.discoveredId}"]`);
                await expect(row).toContainText('Standing Order Feature');
                await expect(row.locator('[data-action="open-content"]')).toHaveAttribute('href', `/library/${id}`);
            });

            await test.step("the hydrated item inherited the wishlist's root folder and monitoring", async () => {
                const hydrated = await api.get<ContentItem>(`/api/content/${content.id}`);
                expect(hydrated).toMatchObject({
                    rootFolderId: movieRoot.id,
                    effectiveRootFolderId: movieRoot.id,
                    monitoring: true,
                    effectiveMonitoring: true,
                });
            });

            await test.step('refresh again without duplicating discovered or library state', async () => {
                const executionId = await api.post<string>('/api/jobs/enqueue', {
                    jobType: 'WishlistRefreshItem',
                    parameters: { wishlistId: wishlist.id },
                });
                rerunJob = await waitForJob(api, executionId, 120_000);
                expect(rerunJob.status).toBe('Completed');
                const ledger = await api.get<Ledger>(`/api/wishlists/${wishlist.id}/discovered`);
                expect(ledger.page.totalItems).toBe(1);
                expect(ledger.page.items[0]?.linkId).toBe(content.id);
                const library = await api.get<Paged<ContentItem>>(
                    `/api/content?search=${encodeURIComponent('Standing Order Feature')}&pageSize=100`,
                );
                expect(library.items.filter((item) => item.name === 'Standing Order Feature')).toHaveLength(1);
            });

            await test.step('carry the request through release search, download and import', async () => {
                acquired = await acquireContent(api, {
                    scenario: acquisitionScenario,
                    contentId: content.id,
                    searchTitle: content.name,
                    releaseTitle: 'Standing Order Feature 2026 720p WEB-DL',
                    releaseExternalId: 'standing-order-release',
                    downloadExternalId: 'standing-order-download',
                    categories: ['movies'],
                    landedFiles: [
                        { source: 'movie-720p.mkv', target: 'Standing Order Feature/Standing Order Feature.mkv' },
                    ],
                });
                expect(acquired.download).toMatchObject({ status: 'Imported', progress: 100 });
                expect(acquired.content.contentItem).toMatchObject({ fulfillment: 'Upgradable' });
                expect(acquired.content.contentItem?.mediaFiles).toContainEqual(
                    expect.objectContaining({
                        fileKind: 'Video',
                        filePath: expect.stringMatching(/Standing Order Feature\.mkv$/),
                    }),
                );
            });

            await test.step("a second household member's wishlist stays theirs", async () => {
                member = await api.post<User>('/api/users', {
                    username: memberUsername,
                    password: memberPassword,
                    role: 'Requester',
                    enabled: true,
                });
                expect(member).toMatchObject({ username: memberUsername, role: 'Requester', enabled: true });
                const { api: memberApi } = await loginAdmin(memberUsername, memberPassword);
                const known = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250')).items.map((job) => job.id),
                );
                memberWishlist = await memberApi.post<Wishlist>('/api/wishlists', {
                    name: 'Member standing order',
                    criteria: {
                        contentHierarchyItem: {
                            and: [{ contentType: { and: [{ operator: 'Equals', value: 'movie' }] } }],
                        },
                    },
                    rootFolderId: movieRoot.id,
                    enabled: true,
                });
                expect(memberWishlist.ownerUserId).toBe(member.id);
                const memberCreationJob = await until(
                    `wishlist ${memberWishlist.id} creation refresh job`,
                    async () => {
                        const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                        return jobs.items.find(
                            (job) =>
                                !known.has(job.id) &&
                                job.jobType === 'WishlistRefreshItem' &&
                                job.parameters?.['wishlistId'] === memberWishlist.id,
                        );
                    },
                    30_000,
                    250,
                );
                expect((await waitForJob(api, memberCreationJob.id, 120_000)).status).toBe('Completed');

                expect(await memberApi.get<Wishlist[]>('/api/wishlists/my')).toContainEqual(
                    expect.objectContaining({ id: memberWishlist.id, ownerUserId: member.id }),
                );
                expect(await memberApi.get<Wishlist[]>('/api/wishlists/my')).not.toContainEqual(
                    expect.objectContaining({ id: wishlist.id }),
                );
                expect(await api.get<Wishlist[]>('/api/wishlists/my')).toContainEqual(
                    expect.objectContaining({ id: wishlist.id }),
                );
                expect(await api.get<Wishlist[]>('/api/wishlists/my')).not.toContainEqual(
                    expect.objectContaining({ id: memberWishlist.id }),
                );
                await expect(memberApi.get(`/api/wishlists/${wishlist.id}`)).rejects.toMatchObject({ status: 404 });
                expect(await api.get<Wishlist>(`/api/wishlists/${memberWishlist.id}`)).toMatchObject({
                    id: memberWishlist.id,
                    ownerUserId: member.id,
                });
            });

            await test.step('a disabled wishlist is skipped by the scan', async () => {
                const knownBeforeCreate = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250')).items.map((job) => job.id),
                );
                disabledWishlist = await api.post<Wishlist>('/api/wishlists', {
                    name: 'Disabled standing order',
                    criteria: {
                        contentHierarchyItem: {
                            and: [{ contentType: { and: [{ operator: 'Equals', value: 'movie' }] } }],
                        },
                    },
                    rootFolderId: movieRoot.id,
                    enabled: false,
                });
                const disabledCreationJob = await until(
                    `wishlist ${disabledWishlist.id} creation refresh job`,
                    async () => {
                        const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                        return jobs.items.find(
                            (job) =>
                                !knownBeforeCreate.has(job.id) &&
                                job.jobType === 'WishlistRefreshItem' &&
                                job.parameters?.['wishlistId'] === disabledWishlist.id,
                        );
                    },
                    30_000,
                    250,
                );
                expect((await waitForJob(api, disabledCreationJob.id, 120_000)).status).toBe('Completed');
                expect((await api.get<Wishlist>(`/api/wishlists/${disabledWishlist.id}`)).lastSyncedAt).toBeNull();

                const jobsBeforeScan = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                scanKnownJobIds = new Set(jobsBeforeScan.items.map((job) => job.id));
                refreshedBeforeScan = new Map([
                    [wishlist.id, (await api.get<Wishlist>(`/api/wishlists/${wishlist.id}`)).lastSyncedAt],
                    [
                        memberWishlist.id,
                        (await api.get<Wishlist>(`/api/wishlists/${memberWishlist.id}`)).lastSyncedAt,
                    ],
                ]);
                scanExecutionId = await api.post<string>('/api/jobs/enqueue', {
                    jobType: 'WishlistRefreshScan',
                    parameters: {},
                });
                expect((await waitForJob(api, scanExecutionId, 120_000)).status).toBe('Completed');
                const jobsAfterScan = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                expect(
                    jobsAfterScan.items.some(
                        (job) =>
                            !scanKnownJobIds.has(job.id) &&
                            job.jobType === 'WishlistRefreshItem' &&
                            job.parameters?.['wishlistId'] === disabledWishlist.id,
                    ),
                ).toBe(false);
                expect((await api.get<Wishlist>(`/api/wishlists/${disabledWishlist.id}`)).lastSyncedAt).toBeNull();
            });

            await test.step('one scan refreshes every enabled wishlist in a single run', async () => {
                const jobs = await until(
                    `wishlist scan ${scanExecutionId} child refresh jobs`,
                    async () => {
                        const current = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                        const targets = current.items
                            .filter(
                                (job) =>
                                    !scanKnownJobIds.has(job.id) &&
                                    job.jobType === 'WishlistRefreshItem' &&
                                    job.parentExecutionId === scanExecutionId,
                            )
                            .map((job) => job.parameters?.['wishlistId']);
                        return targets.includes(wishlist.id) && targets.includes(memberWishlist.id)
                            ? current
                            : undefined;
                    },
                    30_000,
                    250,
                );
                const scanRefreshJobs = jobs.items.filter(
                    (job) =>
                        !scanKnownJobIds.has(job.id) &&
                        job.jobType === 'WishlistRefreshItem' &&
                        job.parentExecutionId === scanExecutionId,
                );
                expect(scanRefreshJobs.map((job) => job.parameters?.['wishlistId']).sort()).toEqual(
                    [wishlist.id, memberWishlist.id].sort(),
                );
                for (const job of scanRefreshJobs) {
                    expect((await waitForJob(api, job.id, 120_000)).status).toBe('Completed');
                }
                expect((await api.get<Wishlist>(`/api/wishlists/${wishlist.id}`)).lastSyncedAt).not.toBe(
                    refreshedBeforeScan.get(wishlist.id),
                );
                expect((await api.get<Wishlist>(`/api/wishlists/${memberWishlist.id}`)).lastSyncedAt).not.toBe(
                    refreshedBeforeScan.get(memberWishlist.id),
                );
            });

            await test.step('convert a saved filter into a wishlist', async () => {
                const criteria = {
                    contentHierarchyItem: {
                        and: [
                            { contentType: { and: [{ operator: 'Equals', value: 'movie' }] } },
                            {
                                metadata: {
                                    and: [{ title: { and: [{ operator: 'ContainsString', value: 'Standing' }] } }],
                                },
                            },
                        ],
                    },
                };
                const filter = await api.post<ContentFilter>('/api/content-filters', {
                    name: 'Standing order movies',
                    criteria,
                });
                expect(filter).toMatchObject({ name: 'Standing order movies', ownerUserId: expect.any(String) });
                const known = new Set(
                    (await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250')).items.map((job) => job.id),
                );

                await page.goto('/filters');
                const row = page.locator(`[data-filter-id="${filter.id}"]`);
                await expect(row).toContainText('Standing order movies');
                await row.locator('[data-action="more-actions"]').click();
                await page.locator(`[data-action="convert-to-wishlist"][data-target="${filter.id}"]`).click();
                await expect(page).toHaveURL(/\/wishlists$/);
                const form = page.locator('[data-component="wishlist-form"]');
                await expect(form).toBeVisible();
                await expect(form.locator('[data-field="name"]')).toHaveValue('Standing order movies');
                await expect(form.locator('[data-field="criteria"] .filter-bar__query-text')).toHaveText(/.+/);
                await form.locator('[data-action="save"]').click();
                await expect(form).toBeHidden();

                convertedWishlist = await until(
                    'the converted content filter wishlist',
                    async () =>
                        (await api.get<Wishlist[]>('/api/wishlists/my')).find(
                            (item) => item.name === 'Standing order movies',
                        ),
                    30_000,
                    250,
                );
                const converted = await api.get<Wishlist>(`/api/wishlists/${convertedWishlist.id}`);
                expect(converted).toMatchObject({
                    name: 'Standing order movies',
                    enabled: true,
                    contentFilterId: expect.any(String),
                });
                expect(converted.contentFilterId).not.toBe(filter.id);
                expect(
                    await api.post<{ query: string }>(
                        '/api/magpie-ql/content/format',
                        converted.contentFilter!.criteria,
                    ),
                ).toEqual({
                    query: 'ContentType = "movie" and Title contains "Standing"',
                });
                const conversionJob = await until(
                    `wishlist ${convertedWishlist.id} creation refresh job`,
                    async () => {
                        const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                        return jobs.items.find(
                            (job) =>
                                !known.has(job.id) &&
                                job.jobType === 'WishlistRefreshItem' &&
                                job.parameters?.['wishlistId'] === convertedWishlist.id,
                        );
                    },
                    30_000,
                    250,
                );
                expect((await waitForJob(api, conversionJob.id, 120_000)).status).toBe('Completed');
            });

            await test.step('edit the wishlist, delete it, and confirm the ledger is gone', async () => {
                const fulfilledLedger = await until(
                    'the standing-order ledger to reflect fulfilled library state',
                    async () => {
                        const current = await api.get<Ledger>(`/api/wishlists/${wishlist.id}/discovered`);
                        return current.page.items[0]?.status === 'Found' ? current : undefined;
                    },
                    30_000,
                    500,
                );
                expect(fulfilledLedger.page.totalItems).toBe(1);

                await page.goto('/wishlists');
                const row = page.locator(`[data-wishlist-id="${wishlist.id}"]`);
                await expect(row).toContainText('Journey standing order');
                await row.locator('[data-action="edit"]').click();
                const form = page.locator('[data-component="wishlist-form"]');
                await expect(form.locator('[data-field="criteria"] .filter-bar__query-text')).toHaveText(/.+/);
                await form.locator('[data-field="name"]').fill('Fulfilled standing order');
                await form.locator('[data-action="save"]').click();
                await expect(form).toBeHidden();
                await expect(row).toContainText('Fulfilled standing order');
                expect(await api.get<Wishlist>(`/api/wishlists/${wishlist.id}`)).toMatchObject({
                    name: 'Fulfilled standing order',
                });

                await row.locator('[data-action="delete"]').click();
                await page.locator('[data-component="delete-confirm-dialog"] [data-action="confirm-delete"]').click();
                await expect(row).toHaveCount(0);
                await expect(api.get(`/api/wishlists/${wishlist.id}`)).rejects.toMatchObject({ status: 404 });
                await expect(api.get(`/api/wishlists/${wishlist.id}/discovered`)).rejects.toMatchObject({
                    status: 404,
                });
            });

            await test.step('read final wishlist, content, job, release, download and file state', async () => {
                expect(await api.get<Wishlist[]>('/api/wishlists/my')).toEqual(
                    expect.arrayContaining([
                        expect.objectContaining({ id: disabledWishlist.id, enabled: false }),
                        expect.objectContaining({ id: convertedWishlist.id, enabled: true }),
                    ]),
                );
                expect(await api.get<ContentItem>(`/api/content/${content.id}`)).toMatchObject({
                    name: 'Standing Order Feature',
                    rootFolderId: movieRoot.id,
                    effectiveRootFolderId: movieRoot.id,
                    monitoring: true,
                    effectiveMonitoring: true,
                    contentItem: { fulfillment: 'Upgradable' },
                });
                expect(
                    await api.get<Array<{ title: string; acquisitionStatus: string }>>(
                        `/api/content/${content.id}/releases`,
                    ),
                ).toContainEqual(
                    expect.objectContaining({
                        title: 'Standing Order Feature 2026 720p WEB-DL',
                        acquisitionStatus: 'Imported',
                    }),
                );
                expect(await api.get(`/api/downloads/${acquired.download.id}`)).toMatchObject({
                    externalId: 'standing-order-download',
                    status: 'Imported',
                    progress: 100,
                });
                expect(await cli.json<ContentItem>(['content', 'get', content.id])).toMatchObject({
                    id: content.id,
                    contentItem: { fulfillment: 'Upgradable' },
                });
                expect(new Set([creationJob.id, uiRefreshJob.id, rerunJob.id]).size).toBe(3);

                const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=250');
                const journeyJobs = jobs.items.filter((job) => !initialJobIds.has(job.id));
                const standingOrderJobs = journeyJobs.filter(
                    (job) =>
                        job.id === scanExecutionId ||
                        job.parameters?.['wishlistId'] === wishlist.id ||
                        job.parameters?.['wishlistId'] === memberWishlist.id ||
                        job.parameters?.['wishlistId'] === disabledWishlist.id ||
                        job.parameters?.['wishlistId'] === convertedWishlist.id,
                );
                expect(
                    standingOrderJobs
                        .map((job) => `${job.jobType}:${JSON.stringify(job.parameters ?? {})}`)
                        .sort(),
                ).toEqual(
                    [
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: wishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: wishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: wishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: wishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: memberWishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: memberWishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: disabledWishlist.id })}`,
                        `WishlistRefreshItem:${JSON.stringify({ wishlistId: convertedWishlist.id })}`,
                        'WishlistRefreshScan:{}',
                    ].sort(),
                );
                expect(standingOrderJobs.every((job) => job.status === 'Completed')).toBe(true);

                const importJobs = journeyJobs.filter(
                    (job) =>
                        job.parameters?.['downloadId'] === acquired.download.id ||
                        job.jobType.startsWith('DownloadImport'),
                );
                expect(importJobs.length).toBeGreaterThan(0);
                expect(importJobs.every((job) => ['Completed', 'Cancelled'].includes(job.status))).toBe(true);

                const childJobs = journeyJobs.filter((job) => job.parentExecutionId != null);
                expect(childJobs.length).toBeGreaterThan(0);
                expect(
                    childJobs.every((job) => jobs.items.some((parent) => parent.id === job.parentExecutionId)),
                ).toBe(true);

                await page.goto(`/library/${content.id}?tab=files`);
                await expect(page.getByRole('tabpanel')).toContainText('Standing Order Feature.mkv');
            });
        } finally {
            const cleanupResults: PromiseSettledResult<unknown>[] = [];
            cleanupResults.push(
                ...(await Promise.allSettled([
                    mocks.hierarchyPrimary.useScenario('default'),
                    mocks.indexer.useScenario('default'),
                    mocks.downloadClient.useScenario('default'),
                    mocks.mediaServer.useScenario('default'),
                ])),
            );
            cleanupResults.push(
                ...(await Promise.allSettled([
                    rm(resolve(FIXTURES_ROOT, mocks.hierarchyPrimary.slug, 'scenarios', metadataScenario), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(FIXTURES_ROOT, mocks.indexer.slug, 'scenarios', acquisitionScenario), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(FIXTURES_ROOT, mocks.downloadClient.slug, 'scenarios', acquisitionScenario), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(FIXTURES_ROOT, mocks.mediaServer.slug, 'scenarios', acquisitionScenario), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(integrationEnvironment.paths.downloads, 'standing-order-download'), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(integrationEnvironment.paths.library, 'movies', 'Standing Order Feature'), {
                        recursive: true,
                        force: true,
                    }),
                ])),
            );
            expect(
                cleanupResults
                    .filter((result) => result.status === 'rejected')
                    .map((result) => String((result as PromiseRejectedResult).reason)),
            ).toEqual([]);
        }
    });
});
