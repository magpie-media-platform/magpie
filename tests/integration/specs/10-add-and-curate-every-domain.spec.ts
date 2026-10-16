import { mkdir, rm, stat } from 'node:fs/promises';
import { resolve } from 'node:path';

import { expect, test, type Page } from '@playwright/test';

import {
    ADMIN_PASSWORD,
    ADMIN_USERNAME,
    type ApiClient,
    loginAdmin,
} from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { integrationEnvironment } from '../helpers/environment';
import { FIXTURES_ROOT, writeScenarioFile } from '../helpers/fixtures';
import {
    drainContentMetadataRefreshItems,
    drainDownloadRefresh,
    type JobExecution,
    waitForJob,
} from '../helpers/jobs';
import { mocks, type LandedFileFixture, type ReleaseFixture } from '../helpers/mocks';
import {
    type HierarchyItemFixture,
    writeHierarchyFixtures,
} from '../helpers/rich-library';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface RootFolder {
    id: string;
    name: string;
    path: string;
    contentType: string;
    isDefault: boolean;
    defaultMonitoring: 'Unmonitored' | 'Monitored' | 'Shotgun' | null;
    defaults: Record<string, { qualityTierListId: string }>;
}

interface MediaFile {
    fileKind: string;
    filePath: string;
    mimeType?: string | null;
    mediaInfo?: { tracks?: Array<{ type: string }> } | null;
}

interface ContentItem {
    id: string;
    name: string;
    level: string;
    contentType: string;
    parentId?: string | null;
    monitoring: 'Unmonitored' | 'Monitored' | 'Shotgun' | null;
    effectiveMonitoring: 'Unmonitored' | 'Monitored' | 'Shotgun';
    rootFolderId?: string | null;
    effectiveRootFolderId: string;
    qualityTierListId?: string | null;
    effectiveQualityTierListId: string;
    pendingMetadataRefresh: boolean;
    contentItem?: {
        id: string;
        fulfillment: string;
        qualityStatus?: string | null;
        mediaFiles: MediaFile[];
    } | null;
}

interface Metadata {
    title: string;
    overview?: string | null;
    images: Array<{ url: string }>;
    externalIds: Array<{ source: string; value: string }>;
}

interface MetadataContribution {
    id: string;
    ordinal: number;
    providerType: string;
    title?: string | null;
}

interface Indexer {
    id: string;
    name: string;
}

interface IndexerConnection {
    id: string;
    name: string;
    enabled: boolean;
    status: string;
    settings: { apiUrl: string };
}

interface InteractiveCandidate {
    release: ReleaseFixture & { id: string; externalId: string };
}

interface Download {
    id: string;
    externalId: string;
    progress?: number | null;
    status: string;
    title: string;
}

interface AuditEntry {
    kind: string;
    label: string;
    category: string;
    detail?: string | null;
    entityId?: string | null;
    entityType?: string | null;
    userId?: string | null;
}

interface User {
    id: string;
    username: string;
}

interface DomainFixture {
    categories: string[];
    children: HierarchyItemFixture[];
    contentType: string;
    downloadExternalId: string;
    expectedFileKind: string;
    expectedFulfillment: string;
    externalId: string;
    landedFiles: LandedFileFixture[];
    level: string;
    option: string;
    releaseExternalId: string;
    releaseTitle: string;
    title: string;
}

interface AddedDomain {
    fixture: DomainFixture;
    root: RootFolder;
    topId: string;
    targetId: string;
    content?: ContentItem;
    download?: Download;
}

const primaryScenario = 'journey-10-primary';
const unarmedAdvancedScenario = 'journey-10-unarmed-advanced';
const secondaryScenario = 'journey-10-secondary';
const indexerScenario = 'journey-10-indexer';
const downloadScenario = 'journey-10-download';
const mediaScenario = 'journey-10-media';

const chosenTitle = 'Curation Chosen Folder Movie';
const chosenExternalId = 'curation-chosen-folder';
const chosenRootDirectory = 'journey-10-chosen-movies';
const defaultTitle = 'Curation Default Policy Movie';
const defaultExternalId = 'curation-default-policy';
const guardReleaseExternalId = 'curation-guard-release';
const guardDownloadExternalId = 'curation-guard-download';
const packReleaseTitle = 'Curation Journey Series S01 720p Season Pack';
const packReleaseExternalId = 'curation-tv-season-pack-release';
const packDownloadExternalId = 'curation-tv-season-pack-download';

const domains: DomainFixture[] = [
    {
        contentType: 'movie',
        option: 'Movie',
        level: 'movie',
        title: 'Curation Journey Movie',
        externalId: 'curation-movie',
        categories: ['movies'],
        releaseTitle: 'Curation Journey Movie 2025 720p WEB-DL',
        releaseExternalId: 'curation-movie-release',
        downloadExternalId: 'curation-movie-download',
        expectedFileKind: 'Video',
        expectedFulfillment: 'Upgradable',
        children: [],
        landedFiles: [
            { source: 'movie-720p.mkv', target: 'Curation Journey Movie/Curation Journey Movie.mkv' },
            { source: 'movie.en.srt', target: 'Curation Journey Movie/Curation Journey Movie.en.srt' },
        ],
    },
    {
        contentType: 'music',
        option: 'Music',
        level: 'artist',
        title: 'Curation Journey Artist',
        externalId: 'curation-artist',
        categories: ['music'],
        releaseTitle: 'Curation Journey Artist - Curation Journey Track 1 FLAC',
        releaseExternalId: 'curation-music-release',
        downloadExternalId: 'curation-music-download',
        expectedFileKind: 'Audio',
        expectedFulfillment: 'Fulfilled',
        children: [
            {
                level: 'album',
                name: 'Curation Journey Album',
                slug: 'curation-journey-album',
                alignmentKey: 'release-group:curation-album',
                externalIds: { musicbrainz: 'curation-album' },
                initialContribution: contribution('curation-album', 'Curation Journey Album', 1),
                children: [1, 2].map((number) => ({
                    level: 'track',
                    name: `Curation Journey Track ${number}`,
                    slug: `curation-journey-track-${number}`,
                    ordinal: number,
                    alignmentKey: `track:curation-track-${number}`,
                    externalIds: { musicbrainz: `curation-track-${number}` },
                    initialContribution: contribution(
                        `curation-track-${number}`,
                        `Curation Journey Track ${number}`,
                        number,
                    ),
                })),
            },
        ],
        landedFiles: [{ source: 'track.flac', target: 'Curation Journey Artist - Track 1.flac' }],
    },
    {
        contentType: 'book',
        option: 'Book',
        level: 'ebook',
        title: 'Curation Journey Book',
        externalId: 'curation-book',
        categories: ['books'],
        releaseTitle: 'Curation Journey Book 2025 PDF',
        releaseExternalId: 'curation-book-release',
        downloadExternalId: 'curation-book-download',
        expectedFileKind: 'Unknown',
        expectedFulfillment: 'Upgradable',
        children: [],
        landedFiles: [{ source: 'book.pdf', target: 'Curation Journey Book.pdf' }],
    },
    {
        contentType: 'tv',
        option: 'TV',
        level: 'show',
        title: 'Curation Journey Series',
        externalId: 'curation-series',
        categories: ['tv'],
        releaseTitle: 'Curation Journey Series S01E01 720p WEB-DL',
        releaseExternalId: 'curation-tv-release',
        downloadExternalId: 'curation-tv-download',
        expectedFileKind: 'Video',
        expectedFulfillment: 'Upgradable',
        children: [
            {
                level: 'season',
                name: 'Curation Journey Season 1',
                slug: 'curation-journey-season-1',
                ordinal: 1,
                alignmentKey: 'season:curation:1',
                externalIds: { tvdb: 'curation-season-1' },
                initialContribution: contribution('curation-season-1', 'Curation Journey Season 1', 1),
                children: [1, 2, 3, 4].map((number) => ({
                    level: 'episode',
                    name: `Curation Journey Episode ${number}`,
                    slug: `curation-journey-episode-${number}`,
                    ordinal: number,
                    alignmentKey: `episode:curation:1:${number}`,
                    externalIds: { tvdb: `curation-episode-${number}` },
                    initialContribution: {
                        ...contribution(
                            `curation-episode-${number}`,
                            `Curation Journey Episode ${number}`,
                            number,
                        ),
                        publishDate: '2025-04-05T00:00:00Z',
                    },
                })),
            },
        ],
        landedFiles: [{ source: 'episode-720p.mkv', target: 'Curation Journey Series S01E01.mkv' }],
    },
];

const packLandedFiles: LandedFileFixture[] = [
    { source: 'episode-720p.mkv', target: 'Curation Journey Series S01E02.mkv' },
    { source: 'episode-720p.mkv', target: 'Curation Journey Series S01E03.mkv' },
    { source: 'movie.en.srt', target: 'Curation Journey Series S01E04.txt' },
];

test.describe.serial('Journey 10: bring four collections in and make the metadata right', () => {
    test('adds, acquires and curates every content domain', async ({ page }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        let roots!: RootFolder[];
        let indexerId!: string;
        let degradedConnectionId!: string;
        let unhealthyConnectionId!: string;
        let chosenId!: string;
        let chosenRoot!: RootFolder;
        const added: AddedDomain[] = [];
        const initialJobIds = new Set<string>();
        const scenarioDirectories = [
            [mocks.hierarchyPrimary.slug, primaryScenario],
            [mocks.hierarchyPrimary.slug, unarmedAdvancedScenario],
            [mocks.hierarchySecondary.slug, secondaryScenario],
            [mocks.indexer.slug, indexerScenario],
            [mocks.downloadClient.slug, downloadScenario],
            [mocks.mediaServer.slug, mediaScenario],
        ] as const;
        const allDownloadIds = [
            ...domains.map((fixture) => fixture.downloadExternalId),
            packDownloadExternalId,
            guardDownloadExternalId,
        ];

        try {
            await test.step('restore the configured baseline', async () => {
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    roots = await restored.get<RootFolder[]>('/api/root-folders');
                    expect(roots.map((root) => root.name).sort()).toEqual(['Books', 'Movies', 'Music', 'TV']);
                    expect(roots.every((root) => root.isDefault && root.defaultMonitoring === 'Monitored')).toBe(true);
                    expect(roots.flatMap((root) => Object.keys(root.defaults)).sort()).toEqual([
                        'artist',
                        'audiobook',
                        'ebook',
                        'movie',
                        'show',
                    ]);
                    expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
                });
                await armAllFixtures();
                ({ indexerId, degradedConnectionId, unhealthyConnectionId } = await configureIndexerHealth(api));
                const initialJobs = (await api.get<Paged<JobExecution>>('/api/jobs?pageSize=500')).items;
                expect(initialJobs.length).toBeGreaterThan(0);
                for (const job of initialJobs) {
                    initialJobIds.add(job.id);
                }
                await loginUi(page);
            });

            for (const fixture of domains) {
                const root = rootFor(roots, fixture.contentType);
                const topId = await test.step(
                    `${fixture.contentType}: search, preview and add without losing search state`,
                    async () => addDomainFromSearch(page, api, fixture, root),
                );

                await test.step(
                    `${fixture.contentType}: wait for metadata, hierarchy, subject and inherited policy`,
                    async () => assertHydratedDomain(api, fixture, root, topId),
                );

                const target = await acquisitionTarget(api, fixture, topId);
                await drainContentMetadataRefreshItems(api);
                const acquisition = await test.step(
                    `${fixture.contentType}: search equivalent releases, grab and import`,
                    async () => acquireFixture(api, fixture, target.id),
                );
                added.push({
                    fixture,
                    root,
                    topId,
                    targetId: target.id,
                    content: acquisition.content,
                    download: acquisition.download,
                });
            }

            await test.step(
                'television: a season pack distributes its files and retains the release only where a file landed',
                async () => {
                    const television = domainByType(added, 'tv');
                    const season = await childNamed(api, television.topId, 'Curation Journey Season 1');
                    const episode1 = await childNamed(api, season.id, 'Curation Journey Episode 1');
                    const episode2 = await childNamed(api, season.id, 'Curation Journey Episode 2');
                    const episode3 = await childNamed(api, season.id, 'Curation Journey Episode 3');
                    const episode4 = await childNamed(api, season.id, 'Curation Journey Episode 4');

                    await acquireRelease(api, {
                        contentId: season.id,
                        importedContentId: episode2.id,
                        searchTitle: season.name,
                        releaseTitle: packReleaseTitle,
                        releaseExternalId: packReleaseExternalId,
                        downloadExternalId: packDownloadExternalId,
                        categories: ['tv'],
                    });
                    const hasPack = async (id: string) =>
                        (await api.get<Array<{ title: string }>>(`/api/content/${id}/releases`)).some(
                            (release) => release.title === packReleaseTitle,
                        );
                    expect(await hasPack(episode1.id)).toBe(false);
                    expect(await hasPack(episode2.id)).toBe(true);
                    expect(await hasPack(episode3.id)).toBe(true);
                    expect(await hasPack(episode4.id)).toBe(false);
                },
            );

            await test.step('an unarmed advanced search completes empty without error', async () => {
                await mocks.hierarchyPrimary.useScenario(unarmedAdvancedScenario);
                await page.goto('/library/add');
                await selectContentType(page, 'Movie');
                await page.locator('#sfb-title').fill('Curation Basic Fallback');
                await page.getByRole('region', { name: 'Filter options' }).getByRole('button', { name: /^search$/i }).click();
                await expect(page.getByTestId('library-add-page__empty-no-results')).toBeVisible();
                await expect(page.getByRole('grid').locator('tbody tr')).toHaveCount(0);
                await expect(page.locator('[data-component="toast"][data-variant="error"]')).toHaveCount(0);
                await mocks.hierarchyPrimary.useScenario(primaryScenario);
            });

            await test.step('a search that returns nothing renders the empty state', async () => {
                await searchAddPage(page, 'Movie', 'Curation Nothing Here');
                await expect(page.getByTestId('library-add-page__empty-no-results')).toBeVisible();
            });

            await test.step('page through more results than one page returns', async () => {
                await searchAddPage(page, 'Movie', 'Curation Catalog');
                const rows = page.getByRole('grid').locator('tbody tr');
                const pager = page.getByTestId('library-add-page__pagination');
                const next = pager.locator('[data-action="next-page"]');
                await expect(pager.locator('.pager__page')).toHaveText('Page 1');
                await expect(next).toBeEnabled();
                await expect(rows).toHaveCount(25);
                await next.click();
                await expect(pager.locator('.pager__page')).toHaveText('Page 2');
                await expect(next).toBeDisabled();
                await expect(rows).toHaveCount(5);
                await expect(page.getByRole('grid').getByRole('row').filter({ hasText: 'Curation Catalog 30' }))
                    .toBeVisible();
            });

            const movieRoot = rootFor(roots, 'movie');
            chosenId = await test.step('add one item to a chosen folder from the preview drawer', async () => {
                await mkdir(resolve(integrationEnvironment.paths.library, chosenRootDirectory), { recursive: true });
                chosenRoot = await api.post<RootFolder>('/api/root-folders', {
                    name: 'Journey 10 chosen movies',
                    path: `/library/${chosenRootDirectory}`,
                    contentType: 'movie',
                    defaultMonitoring: 'Monitored',
                    defaults: { movie: { qualityTierListId: movieRoot.defaults.movie!.qualityTierListId } },
                });
                return addNamedMovie(page, api, chosenTitle, chosenRoot.id);
            });

            const defaultId = await test.step(
                'add one item with no explicit folder and watch the defaults apply',
                async () => {
                    const id = await api.post<string>('/api/content/from-external-id', {
                        providerType: mocks.hierarchyPrimary.slug,
                        externalIdKey: 'tmdb',
                        externalId: defaultExternalId,
                        contentType: 'movie',
                        levelSlug: 'movie',
                    });
                    const item = await waitForHydratedItem(api, id, defaultTitle);
                    expect(item).toMatchObject({
                        rootFolderId: null,
                        monitoring: null,
                        effectiveRootFolderId: movieRoot.id,
                        effectiveMonitoring: 'Monitored',
                        effectiveQualityTierListId: movieRoot.defaults.movie!.qualityTierListId,
                    });
                    return id;
                },
            );

            await test.step(
                'a second metadata provider contributes to the same item and precedence resolves the conflict',
                async () => {
                    await armSecondaryProvider();
                    await refreshMetadata(api, chosenId);
                    const contributions = await api.get<MetadataContribution[]>(
                        `/api/content/${chosenId}/metadata/contributions`,
                    );
                    expect(contributions).toEqual(
                        expect.arrayContaining([
                            expect.objectContaining({ providerType: mocks.hierarchyPrimary.slug, title: chosenTitle }),
                            expect.objectContaining({
                                providerType: mocks.hierarchySecondary.slug,
                                title: 'Secondary Chosen Folder Movie',
                            }),
                        ]),
                    );
                    expect((await api.get<Metadata>(`/api/content/${chosenId}/metadata`)).title).toBe(chosenTitle);
                },
            );

            await test.step('reorder the providers and the effective value changes', async () => {
                const contributions = await api.get<MetadataContribution[]>(
                    `/api/content/${chosenId}/metadata/contributions`,
                );
                const primary = contributions.find(
                    (contribution) => contribution.providerType === mocks.hierarchyPrimary.slug,
                )!;
                const secondary = contributions.find(
                    (contribution) => contribution.providerType === mocks.hierarchySecondary.slug,
                )!;
                await api.patch(`/api/content/${chosenId}/metadata`, {
                    contributionIds: [secondary.id, primary.id],
                });
                expect((await api.get<Metadata>(`/api/content/${chosenId}/metadata`)).title).toBe(
                    'Secondary Chosen Folder Movie',
                );
            });

            await test.step('a hand-written override is saved, locked, and survives the next metadata refresh', async () => {
                await api.patch(`/api/content/${chosenId}/metadata`, {
                    title: { value: 'Hand Curated Chosen Folder Movie' },
                });
                await refreshMetadata(api, chosenId);
                expect((await api.get<Metadata>(`/api/content/${chosenId}/metadata`)).title).toBe(
                    'Hand Curated Chosen Folder Movie',
                );
                expect(await api.get<MetadataContribution[]>(`/api/content/${chosenId}/metadata/contributions`))
                    .toContainEqual(
                        expect.objectContaining({ providerType: 'user', title: 'Hand Curated Chosen Folder Movie' }),
                    );
            });

            await test.step("revert the override and the provider's value comes back", async () => {
                await api.patch(`/api/content/${chosenId}/metadata`, { title: { value: null } });
                expect((await api.get<Metadata>(`/api/content/${chosenId}/metadata`)).title).toBe(
                    'Secondary Chosen Folder Movie',
                );
                expect(
                    (await api.get<MetadataContribution[]>(`/api/content/${chosenId}/metadata/contributions`))
                        .some((contribution) => contribution.providerType === 'user'),
                ).toBe(false);
            });

            await test.step(
                'monitoring set on the series reaches its episodes, and one explicit child override wins',
                async () => {
                    const television = domainByType(added, 'tv');
                    const season = await childNamed(api, television.topId, 'Curation Journey Season 1');
                    const episode4 = await childNamed(api, season.id, 'Curation Journey Episode 4');
                    await api.patch(`/api/content/${television.topId}`, { monitoring: { value: 'Unmonitored' } });
                    await waitForNoReadyJobs(api, ['ReleaseAutoGrabScan', 'ReleaseAutoGrabItem']);
                    for (const number of [1, 2, 3]) {
                        expect(
                            await childNamed(api, season.id, `Curation Journey Episode ${number}`),
                        ).toMatchObject({ monitoring: null, effectiveMonitoring: 'Unmonitored' });
                    }
                    await api.patch(`/api/content/${episode4.id}`, { monitoring: { value: 'Monitored' } });
                    await waitForNoReadyJobs(api, ['ReleaseAutoGrabScan', 'ReleaseAutoGrabItem']);
                    expect(await api.get<ContentItem>(`/api/content/${episode4.id}`)).toMatchObject({
                        monitoring: 'Monitored',
                        effectiveMonitoring: 'Monitored',
                    });
                },
            );

            await test.step('clear the child override and inheritance is restored', async () => {
                const television = domainByType(added, 'tv');
                const season = await childNamed(api, television.topId, 'Curation Journey Season 1');
                const episode4 = await childNamed(api, season.id, 'Curation Journey Episode 4');
                await api.patch(`/api/content/${episode4.id}`, { monitoring: { value: null } });
                await waitForNoReadyJobs(api, ['ReleaseAutoGrabScan', 'ReleaseAutoGrabItem']);
                expect(await api.get<ContentItem>(`/api/content/${episode4.id}`)).toMatchObject({
                    monitoring: null,
                    effectiveMonitoring: 'Unmonitored',
                });
            });

            await test.step('select several items and patch monitoring, folder and tier list in one action', async () => {
                await page.setViewportSize({ width: 1440, height: 900 });
                await page.goto('/library/all');
                await expect(page.getByRole('grid')).toBeVisible();
                const ids = [domainByType(added, 'movie').topId, chosenId, defaultId];
                for (const id of ids) {
                    const row = page.locator(`[data-row-id="${id}"]`);
                    await expect(row).toBeVisible();
                    await row.getByRole('checkbox').click();
                }
                await expect(page.locator('[data-component="bulk-actions-toolbar"]')).toContainText('3 selected');
                for (const id of ids) {
                    await api.patch(`/api/content/${id}`, {
                        monitoring: { value: 'Unmonitored' },
                        rootFolderId: { value: null },
                        qualityTierListId: { value: null },
                    });
                }
                const result = await api.patch<{ count: number; error?: string | null }>('/api/content/bulk', {
                    ids,
                    patch: {
                        monitoring: { value: 'Monitored' },
                        rootFolderId: { value: movieRoot.id },
                        qualityTierListId: { value: movieRoot.defaults.movie!.qualityTierListId },
                    },
                });
                expect(result).toEqual({ count: 3, error: null });
                for (const id of ids) {
                    expect(await api.get<ContentItem>(`/api/content/${id}`)).toMatchObject({
                        monitoring: 'Monitored',
                        rootFolderId: movieRoot.id,
                        qualityTierListId: movieRoot.defaults.movie!.qualityTierListId,
                    });
                }
                await mocks.indexer.waitForCalls<{ search: { title?: string | null } }>(1, {
                    method: 'search',
                    predicate: (call) => call.request.search.title === defaultTitle,
                });
                await waitForNoReadyJobs(api, ['ReleaseAutoGrabScan', 'ReleaseAutoGrabItem']);
            });

            await test.step('two sessions patch the same item and neither change is silently lost', async () => {
                const secondSession = (await loginAdmin(ADMIN_USERNAME, ADMIN_PASSWORD)).api;
                await Promise.all([
                    api.patch(`/api/content/${chosenId}`, { monitoring: { value: 'Unmonitored' } }),
                    secondSession.patch(`/api/content/${chosenId}`, { rootFolderId: { value: null } }),
                ]);
                expect(await api.get<ContentItem>(`/api/content/${chosenId}`)).toMatchObject({
                    monitoring: 'Unmonitored',
                    rootFolderId: null,
                });
            });

            await test.step('the audit trail names who changed what', async () => {
                const changedName = `${movieRoot.name} Curated`;
                const book = domainByType(added, 'book');
                await api.patch(`/api/root-folders/${movieRoot.id}`, { name: { value: changedName } });
                await api.patch(`/api/content/${book.topId}`, { monitoring: { value: 'Unmonitored' } });
                const admin = (await api.get<Paged<User>>('/api/users?pageSize=100')).items.find(
                    (user) => user.username === ADMIN_USERNAME,
                )!;
                const history = await api.get<Paged<AuditEntry>>('/api/audit-history?pageSize=500');
                expect(history.items).toContainEqual(
                    expect.objectContaining({
                        kind: 'AuditRecord',
                        label: 'Patch',
                        category: 'System',
                        entityId: movieRoot.id,
                        entityType: 'RootFolder',
                        detail: `Updated root folder ${changedName}`,
                        userId: admin.id,
                    }),
                );
                expect(history.items).toContainEqual(
                    expect.objectContaining({
                        kind: 'AuditRecord',
                        label: 'Patch',
                        category: 'Content',
                        entityId: book.topId,
                        entityType: 'ContentHierarchyItem',
                        detail: 'Updated content item Curation Journey Book.',
                        userId: admin.id,
                    }),
                );
            });

            await test.step('prove domain-specific file semantics and completed job lineage', async () => {
                await proveFileSemantics(added);
                await proveRejectedConcurrentGrab(api, defaultId);

                const jobs = await api.get<Paged<JobExecution>>('/api/jobs?pageSize=500');
                const journeyJobs = jobs.items.filter((job) => !initialJobIds.has(job.id));
                const completed = journeyJobs.filter((job) => job.status === 'Completed');
                expect(completed.some((job) => job.jobType === 'DownloadImportItem')).toBe(true);
                expect(completed.some((job) => job.parentExecutionId)).toBe(true);
                for (const child of completed.filter((job) => job.parentExecutionId)) {
                    expect(jobs.items.some((parent) => parent.id === child.parentExecutionId)).toBe(true);
                }

                const connections = await indexerConnections(api);
                expect(connections).toContainEqual(
                    expect.objectContaining({ id: degradedConnectionId, status: 'Degraded' }),
                );
            });
        } finally {
            const cleanupResults: PromiseSettledResult<unknown>[] = [];
            if (api && indexerId && unhealthyConnectionId) {
                cleanupResults.push(
                    ...(await Promise.allSettled([
                        api.patch(`/api/indexers/${indexerId}/connections/${unhealthyConnectionId}`, {
                            enabled: { value: true },
                        }),
                    ])),
                );
            }
            if (api && chosenId) {
                cleanupResults.push(...(await Promise.allSettled([api.delete(`/api/content/${chosenId}`)])));
            }
            if (api && chosenRoot) {
                cleanupResults.push(...(await Promise.allSettled([api.delete(`/api/root-folders/${chosenRoot.id}`)])));
            }
            cleanupResults.push(
                ...(await Promise.allSettled([
                    mocks.hierarchyPrimary.useScenario('default'),
                    mocks.hierarchySecondary.useScenario('default'),
                    mocks.indexer.useScenario('default'),
                    mocks.downloadClient.useScenario('default'),
                    mocks.mediaServer.useScenario('default'),
                ])),
            );
            cleanupResults.push(
                ...(await Promise.allSettled([
                    ...scenarioDirectories.map(([slug, scenario]) =>
                        rm(resolve(FIXTURES_ROOT, slug, 'scenarios', scenario), { recursive: true, force: true }),
                    ),
                    ...allDownloadIds.map((id) =>
                        rm(resolve(integrationEnvironment.paths.downloads, id), { recursive: true, force: true }),
                    ),
                    rm(resolve(integrationEnvironment.paths.library, chosenRootDirectory), {
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

async function armAllFixtures(): Promise<void> {
    await writeScenarioFile(mocks.downloadClient.slug, downloadScenario, 'TestConnectionAsync.json', { success: true });
    await writeScenarioFile(mocks.indexer.slug, indexerScenario, 'testConnection.json', { success: true });
    await writeScenarioFile(mocks.mediaServer.slug, mediaScenario, 'TestConnectionAsync.json', { success: true });
    await mocks.downloadClient.useScenario(downloadScenario);
    for (const fixture of domains) {
        await armDownload(
            fixture.releaseExternalId,
            fixture.downloadExternalId,
            fixture.landedFiles,
        );
    }
    await armDownload(packReleaseExternalId, packDownloadExternalId, packLandedFiles);
    await armDownload(guardReleaseExternalId, guardDownloadExternalId, [
        { source: 'movie-720p.mkv', target: 'Curation Default Policy Movie/Curation Default Policy Movie.mkv' },
    ]);
    await mocks.downloadClient.armRemove(true);
    await mocks.downloadClient.clearCalls();

    await mocks.indexer.useScenario(indexerScenario);
    await mocks.indexer.armSearchFallback([]);
    for (const emptySearch of [
        ...domains.map(acquisitionSearchTitle),
        'Curation Journey Season 1',
        defaultTitle,
        'Curation Journey Episode 2',
        'Curation Journey Episode 3',
        'Curation Journey Episode 4',
        'Curation Journey Track 2',
    ]) {
        await mocks.indexer.armSearch(emptySearch, []);
    }
    await mocks.indexer.clearCalls();

    await mocks.mediaServer.useScenario(mediaScenario);
    await mocks.mediaServer.armRefresh(true);
    await mocks.mediaServer.clearCalls();

    await mocks.hierarchyPrimary.useScenario(primaryScenario);
    const candidates = [
        ...domains.map(candidateFor),
        candidate('movie', 'movie', chosenTitle, chosenExternalId),
        candidate('movie', 'movie', defaultTitle, defaultExternalId),
        ...Array.from({ length: 30 }, (_, index) => {
            const label = String(index + 1).padStart(2, '0');
            return candidate('movie', 'movie', `Curation Catalog ${label}`, `curation-catalog-${label}`);
        }),
    ];
    await mocks.hierarchyPrimary.write('advancedSearchAsync.json', candidates);
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchyPrimary.slug,
        source: 'journey-10-primary-fallback',
        language: 'en',
    });
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', []);
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', []);

    for (const fixture of domains) {
        const relatedSubjects = [relatedSubject(fixture)];
        await mocks.hierarchyPrimary.write(`hydrateAsync-${fixture.externalId}.json`, candidateFor(fixture));
        await writeHierarchyFixtures(
            fixture.externalId,
            metadata(fixture.externalId, fixture.title),
            fixture.children,
            relatedSubjects,
        );
    }
    for (const [title, externalId] of [
        [chosenTitle, chosenExternalId],
        [defaultTitle, defaultExternalId],
    ] as const) {
        await mocks.hierarchyPrimary.write(
            `hydrateAsync-${externalId}.json`,
            candidate('movie', 'movie', title, externalId),
        );
        await writeHierarchyFixtures(externalId, metadata(externalId, title), [], []);
    }
    await mocks.hierarchyPrimary.clearCalls();

    await mocks.hierarchyPrimary.useScenario(unarmedAdvancedScenario);
    await mocks.hierarchyPrimary.write('searchAsync.json', [
        candidate('movie', 'movie', 'Curation Basic Fallback', 'curation-basic-fallback'),
    ]);
    await mocks.hierarchyPrimary.clearCalls();
    await mocks.hierarchyPrimary.useScenario(primaryScenario);
}

async function configureIndexerHealth(api: ApiClient): Promise<{
    indexerId: string;
    degradedConnectionId: string;
    unhealthyConnectionId: string;
}> {
    const indexer = (await api.get<Indexer[]>('/api/indexers')).find((item) => item.name === 'Local Indexer')!;
    const initial = await api.get<IndexerConnection[]>(`/api/indexers/${indexer.id}/connections`);
    const local = initial.find((connection) => connection.settings.apiUrl === 'https://indexer.invalid/local')!;
    const unhealthy = initial.find(
        (connection) => connection.settings.apiUrl === 'https://unhealthy.indexer.invalid',
    )!;

    await mocks.indexer.armTestConnection({
        success: false,
        message: 'journey 10 fatal failure',
        failureKind: 'Fatal',
    });
    await api.post(`/api/indexers/${indexer.id}/connections/${unhealthy.id}/test`);
    await api.patch(`/api/indexers/${indexer.id}/connections/${unhealthy.id}`, {
        enabled: { value: false },
    });
    await mocks.indexer.armTestConnection({ success: true, message: 'journey 10 healthy' });
    await api.post(`/api/indexers/${indexer.id}/connections/${local.id}/test`);
    const degraded = await api.post<IndexerConnection>(`/api/indexers/${indexer.id}/connections`, {
        name: 'Journey 10 degraded indexer connection',
        enabled: true,
        settings: { apiUrl: 'https://degraded.indexer.invalid', apiKey: 'journey-10-indexer-secret' },
    });
    await mocks.indexer.armTestConnection({
        success: false,
        message: 'journey 10 transient failure',
        failureKind: 'Transient',
    });
    await api.post(`/api/indexers/${indexer.id}/connections/${degraded.id}/test`);
    await mocks.indexer.armTestConnection({ success: true, message: 'journey 10 default' });

    const connections = await api.get<IndexerConnection[]>(`/api/indexers/${indexer.id}/connections`);
    expect(connections).toContainEqual(expect.objectContaining({ id: local.id, status: 'Healthy' }));
    expect(connections).toContainEqual(expect.objectContaining({ id: degraded.id, status: 'Degraded' }));
    expect(connections).toContainEqual(
        expect.objectContaining({ id: unhealthy.id, enabled: false, status: 'Unhealthy' }),
    );
    return {
        indexerId: indexer.id,
        degradedConnectionId: degraded.id,
        unhealthyConnectionId: unhealthy.id,
    };
}

async function addDomainFromSearch(
    page: Page,
    api: ApiClient,
    fixture: DomainFixture,
    root: RootFolder,
): Promise<string> {
    await searchAddPage(page, fixture.option, fixture.title);
    const row = page.getByRole('grid').getByRole('row').filter({ hasText: fixture.title });
    await expect(row).toBeVisible();
    await row.getByRole('button', { name: fixture.title, exact: true }).click();
    const preview = page.getByTestId('preview-drawer');
    await expect(preview).toContainText(`${fixture.title} provider overview.`);
    await expect(preview.getByTestId('preview-drawer-artwork')).toHaveAttribute(
        'src',
        `https://assets.journey.invalid/${fixture.externalId}.jpg`,
    );

    const addedResponse = page.waitForResponse(
        (response) =>
            response.url().includes('/api/content/from-external-id') && response.request().method() === 'POST',
    );
    await preview.locator('[data-action="preview-drawer-add"]').click();
    const response = await addedResponse;
    expect(response.ok()).toBe(true);
    const id = (await response.json()) as string;

    await expect(page).toHaveURL(/\/library\/add/);
    await expect(page.locator('#sfb-title')).toHaveValue(fixture.title);
    await expect(row.locator('[data-action="open-library-item"]').first()).toHaveAttribute(
        'href',
        `/library/${id}`,
    );

    const duplicate = await api.post<string>(`/api/content/from-external-id?rootFolderId=${root.id}`, {
        providerType: mocks.hierarchyPrimary.slug,
        externalIdKey: 'tmdb',
        externalId: fixture.externalId,
        contentType: fixture.contentType,
        levelSlug: fixture.level,
    });
    expect(duplicate).toBe(id);
    return id;
}

async function assertHydratedDomain(
    api: ApiClient,
    fixture: DomainFixture,
    root: RootFolder,
    topId: string,
): Promise<void> {
    const top = await waitForHydratedItem(api, topId, fixture.title);
    await drainContentMetadataRefreshItems(api);
    expect(top).toMatchObject({
        monitoring: null,
        effectiveMonitoring: 'Monitored',
        rootFolderId: root.id,
        effectiveRootFolderId: root.id,
        qualityTierListId: null,
        effectiveQualityTierListId: root.defaults[fixture.level]!.qualityTierListId,
    });

    const currentMetadata = await api.get<Metadata>(`/api/content/${topId}/metadata`);
    expect(currentMetadata).toMatchObject({ title: fixture.title, overview: `${fixture.title} provider overview.` });
    expect(currentMetadata.images).toContainEqual(
        expect.objectContaining({ url: `https://assets.journey.invalid/${fixture.externalId}.jpg` }),
    );
    expect(currentMetadata.externalIds).toContainEqual(
        expect.objectContaining({ source: 'tmdb', value: fixture.externalId }),
    );

    const target = await acquisitionTarget(api, fixture, topId);
    if (target.id !== topId) {
        expect(target).toMatchObject({
            monitoring: null,
            effectiveMonitoring: 'Monitored',
            rootFolderId: null,
            effectiveRootFolderId: root.id,
            qualityTierListId: null,
            effectiveQualityTierListId: root.defaults[fixture.level]!.qualityTierListId,
        });
    }

    const subject = await until(
        `${fixture.title} related subject`,
        async () => {
            const result = await api.get<Paged<{ id: string }>>(
                `/api/subjects?filter=${encodeURIComponent(`title = "${fixture.title} Subject"`)}&pageSize=10`,
            );
            return result.items[0];
        },
        60_000,
        500,
    );
    expect(subject.id).toBeTruthy();
    const related = await api.get<Paged<ContentItem>>(
        `/api/content?filter=${encodeURIComponent(
            `Relationships has (Name = "${fixture.title} Subject" and Type = "person")`,
        )}&pageSize=100`,
    );
    expect(related.items).toContainEqual(expect.objectContaining({ id: topId }));
}

async function acquireFixture(
    api: ApiClient,
    fixture: DomainFixture,
    contentId: string,
): Promise<{ content: ContentItem; download: Download }> {
    const result = await acquireRelease(api, {
        contentId,
        searchTitle: acquisitionSearchTitle(fixture),
        releaseTitle: fixture.releaseTitle,
        releaseExternalId: fixture.releaseExternalId,
        downloadExternalId: fixture.downloadExternalId,
        categories: fixture.categories,
    });
    expect(result.candidates.filter(
        (candidate) => candidate.release.externalId === fixture.releaseExternalId,
    )).toHaveLength(1);
    expect(result.download).toMatchObject({ status: 'Imported', progress: 100 });
    expect(result.content.contentItem?.fulfillment).toBe(fixture.expectedFulfillment);
    expect(result.content.contentItem?.mediaFiles).toContainEqual(
        expect.objectContaining({ fileKind: fixture.expectedFileKind }),
    );
    expect(result.content.contentItem?.qualityStatus).not.toBeUndefined();
    expect(await api.get<Array<{ title: string; acquisitionStatus: string }>>(
        `/api/content/${contentId}/releases`,
    )).toContainEqual(
        expect.objectContaining({ title: fixture.releaseTitle, acquisitionStatus: 'Imported' }),
    );
    return { content: result.content, download: result.download };
}

async function acquireRelease(
    api: ApiClient,
    fixture: {
        contentId: string;
        importedContentId?: string;
        searchTitle: string;
        releaseTitle: string;
        releaseExternalId: string;
        downloadExternalId: string;
        categories: string[];
    },
): Promise<{ candidates: InteractiveCandidate[]; content: ContentItem; download: Download }> {
    await mocks.indexer.armSearch(fixture.searchTitle, [
        release(fixture.releaseTitle, fixture.releaseExternalId, fixture.categories),
    ]);
    await mocks.indexer.armSearchFallback([
        release(fixture.releaseTitle, fixture.releaseExternalId, fixture.categories),
    ]);
    await expect(
        stat(resolve(integrationEnvironment.paths.downloads, fixture.downloadExternalId)),
    ).rejects.toMatchObject({ code: 'ENOENT' });
    await mocks.indexer.clearCalls();

    const candidates = await until(
        `${fixture.searchTitle} release search`,
        async () => {
            const { candidates: found } = await api.post<{ candidates: InteractiveCandidate[] }>(
                `/api/interactive-search/${fixture.contentId}`,
            );
            return found.some((candidate) => candidate.release.externalId === fixture.releaseExternalId)
                ? found
                : undefined;
        },
        60_000,
        500,
    );
    const candidate = candidates.find(
        (item) => item.release.externalId === fixture.releaseExternalId,
    )!;
    expect(await api.post<string>(`/api/interactive-search/${fixture.contentId}/grab/${candidate.release.id}`)).toMatch(
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );

    const queued = await until(
        `${fixture.downloadExternalId} queued download`,
        async () => {
            const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
            const match = downloads.items.find((download) => download.externalId === fixture.downloadExternalId);
            return match?.status === 'Queued' ? match : undefined;
        },
        30_000,
        250,
    );

    await mocks.downloadClient.armStatus(fixture.downloadExternalId, [
        { externalId: fixture.downloadExternalId, status: 'Downloading', progressPercent: 42 },
    ]);
    await drainDownloadRefresh(api);
    expect(await api.get<Download>(`/api/downloads/${queued.id}`)).toMatchObject({
        status: 'Downloading',
        progress: 42,
    });

    await mocks.downloadClient.armStatus(fixture.downloadExternalId, [
        { externalId: fixture.downloadExternalId, status: 'Downloaded', progressPercent: 100 },
    ]);
    await drainDownloadRefresh(api);
    const imported = await until(
        `${fixture.downloadExternalId} imported download`,
        async () => {
            const current = await api.get<Download>(`/api/downloads/${queued.id}`);
            return current.status === 'Imported' ? current : undefined;
        },
        120_000,
        500,
    );

    const importedContentId = fixture.importedContentId ?? fixture.contentId;
    let content = await until(
        `${fixture.searchTitle} imported content`,
        async () => {
            const current = await api.get<ContentItem>(`/api/content/${importedContentId}`);
            return current.contentItem?.mediaFiles.length ? current : undefined;
        },
        60_000,
        500,
    );
    if (content.contentItem!.mediaFiles.some((file) => ['Audio', 'Video'].includes(file.fileKind))) {
        const executionId = await api.post<string>('/api/jobs/enqueue', {
            jobType: 'MediaInfoExtractScan',
            parameters: { contentHierarchyItemId: importedContentId },
        });
        expect((await waitForJob(api, executionId)).status).toBe('Completed');
        content = await until(
            `${fixture.searchTitle} media information`,
            async () => {
                const current = await api.get<ContentItem>(`/api/content/${importedContentId}`);
                return current.contentItem?.mediaFiles.some((file) => file.mediaInfo?.tracks?.length)
                    ? current
                    : undefined;
            },
            60_000,
            500,
        );
    }

    const searchCalls = await mocks.indexer.calls<{
        search: { title?: string | null };
        settings: { apiUrl: string };
    }>('search');
    expect([...new Set(searchCalls.map((call) => call.request.settings.apiUrl))].sort()).toEqual([
        'https://degraded.indexer.invalid',
        'https://indexer.invalid/local',
    ]);
    expect(searchCalls.some(
        (call) => call.request.settings.apiUrl === 'https://unhealthy.indexer.invalid',
    )).toBe(false);
    await mocks.indexer.armSearch(fixture.searchTitle, []);
    await mocks.indexer.armSearchFallback([]);
    return { candidates, content, download: imported };
}

async function armSecondaryProvider(): Promise<void> {
    await mocks.hierarchySecondary.useScenario(secondaryScenario);
    await mocks.hierarchySecondary.write('tryGetMetadataAsync.json', {
        providerType: mocks.hierarchySecondary.slug,
        source: 'secondary:journey-10-fallback',
        title: 'Secondary Chosen Folder Movie',
        language: 'en',
    });
    await mocks.hierarchySecondary.write('expandChildrenAsync.json', []);
    await mocks.hierarchySecondary.write('expandRelatedSubjectsAsync.json', []);
    await mocks.hierarchySecondary.clearCalls();
}

async function refreshMetadata(api: ApiClient, contentHierarchyItemId: string): Promise<void> {
    const executionId = await api.post<string>('/api/jobs/enqueue', {
        jobType: 'ContentMetadataRefreshItem',
        parameters: { contentHierarchyItemId },
    });
    expect((await waitForJob(api, executionId)).status).toBe('Completed');
    await drainContentMetadataRefreshItems(api);
}

async function addNamedMovie(
    page: Page,
    api: ApiClient,
    title: string,
    rootFolderId: string,
): Promise<string> {
    await searchAddPage(page, 'Movie', title);
    const row = page.getByRole('grid').getByRole('row').filter({ hasText: title });
    await row.getByRole('button', { name: title, exact: true }).click();
    const preview = page.getByTestId('preview-drawer');
    const responsePromise = page.waitForResponse(
        (response) =>
            response.url().includes('/api/content/from-external-id') && response.request().method() === 'POST',
    );
    await preview.locator('[data-action="preview-drawer-add"]').click();
    const folderOption = page.locator(
        `[data-action="preview-drawer-add-to-folder"][data-folder-id="${rootFolderId}"]`,
    );
    await expect(folderOption).toBeVisible();
    await folderOption.click();
    const response = await responsePromise;
    expect(response.ok()).toBe(true);
    const id = (await response.json()) as string;
    const item = await waitForHydratedItem(api, id, title);
    expect(item).toMatchObject({ rootFolderId, effectiveRootFolderId: rootFolderId });
    return id;
}

async function proveFileSemantics(added: AddedDomain[]): Promise<void> {
    const movie = domainByType(added, 'movie').content!;
    expect(movie.contentItem?.mediaFiles).toEqual(
        expect.arrayContaining([
            expect.objectContaining({ fileKind: 'Video', filePath: expect.stringMatching(/\.mkv$/) }),
            expect.objectContaining({ fileKind: 'Subtitle', filePath: expect.stringMatching(/\.srt$/) }),
        ]),
    );
    expect(movie.contentItem?.mediaFiles.find((file) => file.fileKind === 'Video')?.mediaInfo?.tracks)
        .toContainEqual(expect.objectContaining({ type: 'Video' }));

    const music = domainByType(added, 'music').content!;
    expect(music.level).toBe('track');
    expect(music.contentItem?.mediaFiles).toContainEqual(
        expect.objectContaining({ fileKind: 'Audio', filePath: expect.stringMatching(/\.flac$/) }),
    );
    expect(music.contentItem?.mediaFiles.find((file) => file.fileKind === 'Audio')?.mediaInfo?.tracks)
        .toContainEqual(expect.objectContaining({ type: 'Audio' }));

    const book = domainByType(added, 'book').content!;
    expect(book.contentItem?.mediaFiles).toContainEqual(
        expect.objectContaining({
            fileKind: 'Unknown',
            filePath: expect.stringMatching(/\.pdf$/),
            mimeType: 'application/pdf',
        }),
    );

    const television = domainByType(added, 'tv').content!;
    expect(television.level).toBe('episode');
    expect(television.contentItem?.mediaFiles.find((file) => file.fileKind === 'Video')?.mediaInfo?.tracks)
        .toContainEqual(expect.objectContaining({ type: 'Video' }));

    expect(movie.contentItem?.fulfillment).toBe('Upgradable');
    expect(music.contentItem?.fulfillment).toBe('Fulfilled');
    expect(book.contentItem?.fulfillment).toBe('Upgradable');
    expect(television.contentItem?.fulfillment).toBe('Upgradable');
    for (const item of added) {
        for (const file of item.content!.contentItem!.mediaFiles) {
            await expect(stat(hostLibraryPath(file.filePath))).resolves.toBeDefined();
        }
    }
}

async function proveRejectedConcurrentGrab(api: ApiClient, hierarchyItemId: string): Promise<void> {
    await mocks.indexer.armSearch(defaultTitle, [
        release('Curation Default Policy Movie 2025 720p WEB-DL', guardReleaseExternalId, ['movies']),
    ]);
    const item = await api.get<ContentItem>(`/api/content/${hierarchyItemId}`);
    const { candidates } = await api.post<{ candidates: InteractiveCandidate[] }>(
        `/api/interactive-search/${hierarchyItemId}`,
    );
    const candidate = candidates.find((entry) => entry.release.externalId === guardReleaseExternalId)!;
    const callsBefore = (await mocks.downloadClient.calls('DownloadReleaseAsync')).length;
    expect(await api.post<string>(`/api/interactive-search/${hierarchyItemId}/grab/${candidate.release.id}`)).toMatch(
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    await until(
        'the guard download to remain in flight',
        async () => {
            const downloads = await api.get<Paged<Download>>('/api/downloads?pageSize=100');
            return downloads.items.some(
                (download) => download.externalId === guardDownloadExternalId && download.status === 'Queued',
            )
                ? true
                : undefined;
        },
        30_000,
        250,
    );

    const executionId = await api.post<string>('/api/jobs/enqueue', {
        jobType: 'ReleaseAutoGrabItem',
        parameters: { contentItemId: item.contentItem!.id },
    });
    expect((await waitForJob(api, executionId)).status).toBe('Completed');
    await mocks.indexer.armSearch(defaultTitle, []);
    expect((await mocks.downloadClient.calls('DownloadReleaseAsync')).length).toBe(callsBefore + 1);

    const audit = await api.get<Paged<AuditEntry>>('/api/audit-history?pageSize=500');
    expect(audit.items).toContainEqual(
        expect.objectContaining({
            kind: 'AuditRecord',
            label: 'GrabSkipped',
            category: 'Jobs',
            entityId: hierarchyItemId,
            entityType: 'ContentHierarchyItem',
            detail: expect.stringMatching(/already in flight on download/),
        }),
    );
}

async function waitForNoReadyJobs(api: ApiClient, jobTypes: string[]): Promise<void> {
    await until(
        `${jobTypes.join(', ')} runnable executions to become terminal`,
        async () => {
            const active = await api.get<Paged<JobExecution & { notBeforeAt: string | null }>>(
                '/api/jobs?status=Queued&status=Running&pageSize=100',
            );
            return active.items.every((job) =>
                !jobTypes.includes(job.jobType) ||
                (job.status === 'Queued' && job.notBeforeAt !== null && Date.parse(job.notBeforeAt) > Date.now()),
            ) ? true : undefined;
        },
        60_000,
        250,
    );
}

async function indexerConnections(api: ApiClient): Promise<IndexerConnection[]> {
    const indexer = (await api.get<Indexer[]>('/api/indexers')).find((item) => item.name === 'Local Indexer')!;
    return api.get<IndexerConnection[]>(`/api/indexers/${indexer.id}/connections`);
}

async function searchAddPage(page: Page, option: string, query: string): Promise<void> {
    await page.goto('/library/add');
    await selectContentType(page, option);
    await page.locator('#sfb-title').fill(query);
    await page.getByRole('region', { name: 'Filter options' }).getByRole('button', { name: /^search$/i }).click();
}

async function selectContentType(page: Page, option: string): Promise<void> {
    await page.getByRole('combobox', { name: 'Content Type' }).click();
    await page.getByRole('option', { name: option, exact: true }).click();
}

async function waitForHydratedItem(api: ApiClient, id: string, title: string): Promise<ContentItem> {
    return until(
        `${title} metadata hydration`,
        async () => {
            const current = await api.get<ContentItem>(`/api/content/${id}`);
            return current.name === title && !current.pendingMetadataRefresh ? current : undefined;
        },
        90_000,
        500,
    );
}

async function acquisitionTarget(
    api: ApiClient,
    fixture: DomainFixture,
    topId: string,
): Promise<ContentItem> {
    if (fixture.contentType === 'tv') {
        const season = await childNamed(api, topId, 'Curation Journey Season 1');
        return childNamed(api, season.id, 'Curation Journey Episode 1');
    }
    if (fixture.contentType === 'music') {
        const album = await childNamed(api, topId, 'Curation Journey Album');
        return childNamed(api, album.id, 'Curation Journey Track 1');
    }
    return api.get<ContentItem>(`/api/content/${topId}`);
}

async function childNamed(api: ApiClient, parentId: string, name: string): Promise<ContentItem> {
    return until(
        `${name} hierarchy child`,
        async () => {
            const children = await api.get<Paged<ContentItem>>(`/api/content/${parentId}/children?pageSize=100`);
            return children.items.find((item) => item.name === name);
        },
        60_000,
        500,
    );
}

async function armDownload(
    releaseExternalId: string,
    downloadExternalId: string,
    landedFiles: LandedFileFixture[],
): Promise<void> {
    await mocks.downloadClient.write(`DownloadReleaseAsync-${releaseExternalId}.json`, {
        externalId: downloadExternalId,
        status: 'Queued',
        progressPercent: 0,
        land: landedFiles,
    });
    await mocks.downloadClient.armStatus(downloadExternalId, [
        { externalId: downloadExternalId, status: 'Queued', progressPercent: 0 },
    ]);
}

function rootFor(roots: RootFolder[], contentType: string): RootFolder {
    const root = roots.find((candidate) => candidate.contentType === contentType);
    if (!root) {
        throw new Error(`The configured baseline has no ${contentType} root folder.`);
    }
    return root;
}

function domainByType(added: AddedDomain[], contentType: string): AddedDomain {
    const domain = added.find((candidate) => candidate.fixture.contentType === contentType);
    if (!domain) {
        throw new Error(`The ${contentType} domain was not added.`);
    }
    return domain;
}

function acquisitionSearchTitle(fixture: DomainFixture): string {
    if (fixture.contentType === 'music') {
        return 'Curation Journey Track 1';
    }
    if (fixture.contentType === 'tv') {
        return 'Curation Journey Episode 1';
    }
    return fixture.title;
}

function candidateFor(fixture: DomainFixture): Record<string, unknown> {
    return candidate(fixture.contentType, fixture.level, fixture.title, fixture.externalId);
}

function candidate(contentType: string, level: string, title: string, externalId: string): Record<string, unknown> {
    return {
        providerType: mocks.hierarchyPrimary.slug,
        externalIdSource: 'tmdb',
        externalId,
        contentType,
        level,
        title,
        year: 2025,
        releaseDate: '2025-04-05T00:00:00Z',
        overview: `${title} provider overview.`,
        language: 'en',
        thumbnailUrl: `https://assets.journey.invalid/${externalId}.jpg`,
    };
}

function metadata(externalId: string, title: string): Record<string, unknown> {
    return {
        ...contribution(externalId, title),
        overview: `${title} provider overview.`,
        publishDate: '2025-04-05T00:00:00Z',
        externalIds: [{ source: 'tmdb', value: externalId, ordinal: 0 }],
        genres: [{ name: 'Journey Curation', ordinal: 0 }],
        images: [
            {
                type: 'poster',
                url: `https://assets.journey.invalid/${externalId}.jpg`,
                mimeType: 'image/jpeg',
                source: 'journey-10',
                width: 600,
                height: 900,
                isPrimary: true,
                ordinal: 0,
            },
        ],
    };
}

function contribution(externalId: string, title: string, sequenceNumber?: number): Record<string, unknown> {
    return {
        providerType: mocks.hierarchyPrimary.slug,
        source: `tmdb:${externalId}`,
        title,
        language: 'en',
        sequenceNumber,
    };
}

function relatedSubject(fixture: DomainFixture): Record<string, unknown> {
    return {
        relationshipKind: 'creator',
        relationshipQualifier: 'primary',
        relationshipOrdinal: 0,
        subjectKind: 'person',
        subjectName: `${fixture.title} Subject`,
        subjectExternalIds: { imdb: `subject-${fixture.externalId}` },
        initialSubjectContribution: contribution(
            `subject-${fixture.externalId}`,
            `${fixture.title} Subject`,
        ),
    };
}

function release(title: string, externalId: string, categories: string[]): ReleaseFixture {
    return {
        title,
        protocol: 'torrent',
        externalId,
        infoUrl: `https://indexer.journey.invalid/releases/${externalId}`,
        size: 734_003_200,
        publishDate: '2026-01-02T00:00:00Z',
        categories,
        languages: ['en'],
    };
}

function hostLibraryPath(filePath: string): string {
    if (!filePath.startsWith('/library/')) {
        throw new Error(`Imported file ${filePath} is outside the fixture library.`);
    }
    return resolve(integrationEnvironment.paths.library, filePath.slice('/library/'.length));
}
