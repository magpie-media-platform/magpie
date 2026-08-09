import { expect, test, type Page } from '@playwright/test';

import { acquireContent, type AcquisitionResult } from '../helpers/acquisition';
import { type ApiClient } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { drainContentMetadataRefreshItems, waitForJob } from '../helpers/jobs';
import { mocks } from '../helpers/mocks';
import {
    directChildren,
    type HierarchyItemFixture,
    writeHierarchyFixtures,
} from '../helpers/rich-library';
import { loginUi } from '../helpers/ui';
import { until } from '../helpers/wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface ContentItem {
    id: string;
    name: string;
    level: string;
    contentType: string;
    parentId?: string | null;
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    rootFolderId?: string | null;
    effectiveRootFolderId: string;
    qualityTierListId?: string | null;
    effectiveQualityTierListId: string;
    pendingMetadataRefresh: boolean;
    contentItem?: {
        fulfillment: string;
        qualityStatus?: string | null;
        mediaFiles: Array<{
            fileKind: string;
            filePath: string;
            mimeType?: string | null;
            mediaInfo?: { tracks?: Array<{ type: string }> } | null;
        }>;
    } | null;
}

interface RootFolder {
    id: string;
    contentType: string;
    defaults: Record<string, { qualityTierListId: string }>;
}

interface DomainFixture {
    categories: string[];
    children: HierarchyItemFixture[];
    contentType: string;
    expectedFileKind: string;
    expectedFulfillment: string;
    externalId: string;
    landedFiles: Array<{ source: string; target: string }>;
    level: string;
    option: string;
    releaseTitle: string;
    title: string;
}

interface AddedDomain {
    fixture: DomainFixture;
    root: RootFolder;
    topId: string;
    targetId: string;
    result: AcquisitionResult;
}

const domains: DomainFixture[] = [
    {
        contentType: 'movie',
        option: 'Movie',
        level: 'movie',
        title: 'Acquisition Journey Movie',
        externalId: 'acquire-movie',
        categories: ['movies'],
        releaseTitle: 'Acquisition Journey Movie 2025 720p WEB-DL',
        expectedFileKind: 'Video',
        expectedFulfillment: 'Upgradable',
        children: [],
        landedFiles: [
            { source: 'movie-720p.mkv', target: 'Acquisition Journey Movie/Acquisition Journey Movie.mkv' },
            { source: 'movie.en.srt', target: 'Acquisition Journey Movie/Acquisition Journey Movie.en.srt' },
        ],
    },
    {
        contentType: 'music',
        option: 'Music',
        level: 'artist',
        title: 'Acquisition Journey Artist',
        externalId: 'acquire-artist',
        categories: ['music'],
        releaseTitle: 'Acquisition Journey Artist - Acquisition Journey Track 1 FLAC',
        expectedFileKind: 'Audio',
        expectedFulfillment: 'Fulfilled',
        children: [
            {
                level: 'album',
                name: 'Acquisition Journey Album',
                slug: 'acquisition-journey-album',
                alignmentKey: 'release-group:acquire-album',
                externalIds: { musicbrainz: 'acquire-album' },
                initialContribution: contribution('acquire-album', 'Acquisition Journey Album', 1),
                children: [
                    {
                        level: 'track',
                        name: 'Acquisition Journey Track 1',
                        slug: 'acquisition-journey-track-1',
                        ordinal: 1,
                        alignmentKey: 'track:acquire-track-1',
                        externalIds: { musicbrainz: 'acquire-track-1' },
                        initialContribution: contribution('acquire-track-1', 'Acquisition Journey Track 1', 1),
                    },
                    {
                        level: 'track',
                        name: 'Acquisition Journey Track 2',
                        slug: 'acquisition-journey-track-2',
                        ordinal: 2,
                        alignmentKey: 'track:acquire-track-2',
                        externalIds: { musicbrainz: 'acquire-track-2' },
                        initialContribution: contribution('acquire-track-2', 'Acquisition Journey Track 2', 2),
                    },
                ],
            },
        ],
        landedFiles: [{ source: 'track.flac', target: 'Acquisition Journey Artist - Track 1.flac' }],
    },
    {
        contentType: 'book',
        option: 'Book',
        level: 'ebook',
        title: 'Acquisition Journey Book',
        externalId: 'acquire-book',
        categories: ['books'],
        releaseTitle: 'Acquisition Journey Book 2025 PDF',
        expectedFileKind: 'Unknown',
        expectedFulfillment: 'Upgradable',
        children: [],
        landedFiles: [{ source: 'book.pdf', target: 'Acquisition Journey Book.pdf' }],
    },
    {
        contentType: 'tv',
        option: 'TV',
        level: 'show',
        title: 'Acquisition Journey Series',
        externalId: 'acquire-series',
        categories: ['tv'],
        releaseTitle: 'Acquisition Journey Series S01E01 720p',
        expectedFileKind: 'Video',
        expectedFulfillment: 'Upgradable',
        children: [
            {
                level: 'season',
                name: 'Acquisition Journey Season 1',
                slug: 'acquisition-journey-season-1',
                ordinal: 1,
                alignmentKey: 'season:1',
                externalIds: { tvdb: 'acquire-season-1' },
                initialContribution: contribution('acquire-season-1', 'Acquisition Journey Season 1', 1),
                children: [1, 2, 3, 4].map((number) => ({
                    level: 'episode',
                    name: `Acquisition Journey Episode ${number}`,
                    slug: `acquisition-journey-episode-${number}`,
                    ordinal: number,
                    alignmentKey: `episode:1:${number}`,
                    externalIds: { tvdb: `acquire-episode-${number}` },
                    initialContribution: contribution(
                        `acquire-episode-${number}`,
                        `Acquisition Journey Episode ${number}`,
                        number,
                    ),
                })),
            },
        ],
        landedFiles: [{ source: 'episode-720p.mkv', target: 'Acquisition Journey Series S01E01.mkv' }],
    },
];

test.describe.serial('Journey 18: search, acquire and import each current content domain', () => {
    test('runs one shared product journey with current domain-specific contracts', async ({ page }) => {
        test.setTimeout(900_000);

        let roots!: RootFolder[];
        const api = await restoreCheckpoint('configured-baseline', async (restored) => {
            roots = await restored.get<RootFolder[]>('/api/root-folders');
            expect(roots.map((root) => root.contentType).sort()).toEqual(['book', 'movie', 'music', 'tv']);
            expect((await restored.get<Paged<unknown>>('/api/content?pageSize=1')).totalItems).toBe(0);
        });
        await loginUi(page);

        const added: AddedDomain[] = [];
        let concurrentGrabStatus: number | undefined;
        let mediaRefreshObserved = false;

        for (const fixture of domains) {
            const root = roots.find((candidate) => candidate.contentType === fixture.contentType)!;
            const topId = await test.step(`${fixture.contentType}: search, preview and add without losing search state`, async () => {
                await armMetadataSearch(fixture);
                await page.goto('/library/add');
                await page.getByRole('combobox', { name: 'Content Type' }).click();
                await page.getByRole('option', { name: fixture.option, exact: true }).click();
                await page.locator('#sfb-title').fill(fixture.title);
                await page.getByRole('button', { name: /^search$/i }).click();

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
                        response.url().includes('/api/content/from-external-id') &&
                        response.request().method() === 'POST',
                );
                await preview.locator('[data-action="preview-drawer-add"]').click();
                const response = await addedResponse;
                expect(response.ok()).toBe(true);
                const id = (await response.json()) as string;

                await expect(page).toHaveURL(/\/library\/add/);
                await expect(page.locator('#sfb-title')).toHaveValue(fixture.title);
                await expect(row.locator('[data-action="open-library-item"]').first())
                    .toHaveAttribute('href', `/library/${id}`);

                const duplicate = await api.post<string>(`/api/content/from-external-id?rootFolderId=${root.id}`, {
                    providerType: mocks.hierarchyPrimary.slug,
                    externalIdKey: 'tmdb',
                    externalId: fixture.externalId,
                    contentType: fixture.contentType,
                    levelSlug: fixture.level,
                });
                expect(duplicate).toBe(id);
                await until(
                    `${fixture.title} searchable title`,
                    async () => {
                        const exact = await api.get<Paged<ContentItem>>(
                            `/api/content?search=${encodeURIComponent(fixture.title)}&pageSize=100`,
                        );
                        return exact.items.some((item) => item.name === fixture.title) ? true : undefined;
                    },
                    60_000,
                    500,
                );
                return id;
            });

            await test.step(`${fixture.contentType}: wait for metadata, hierarchy, subject and inherited policy`, async () => {
                const top = await until(
                    `${fixture.title} metadata job`,
                    async () => {
                        const current = await api.get<ContentItem>(`/api/content/${topId}`);
                        return current.name === fixture.title && !current.pendingMetadataRefresh ? current : undefined;
                    },
                    90_000,
                    500,
                );
                await api.patch(`/api/content/${topId}`, {
                    monitoring: { value: true },
                    rootFolderId: { value: root.id },
                    qualityTierListId: { value: root.defaults[fixture.level]!.qualityTierListId },
                });
                expect(await api.get<ContentItem>(`/api/content/${topId}`)).toMatchObject({
                    monitoring: true,
                    effectiveMonitoring: true,
                    effectiveRootFolderId: root.id,
                    effectiveQualityTierListId: root.defaults[fixture.level]!.qualityTierListId,
                });

                const metadata = await api.get<{
                    title: string;
                    overview: string;
                    images: Array<{ url: string }>;
                    externalIds: Array<{ value: string }>;
                }>(`/api/content/${topId}/metadata`);
                expect(metadata).toMatchObject({ title: fixture.title, overview: `${fixture.title} provider overview.` });
                expect(metadata.images).toContainEqual(
                    expect.objectContaining({ url: `https://assets.journey.invalid/${fixture.externalId}.jpg` }),
                );
                expect(metadata.externalIds).toContainEqual(expect.objectContaining({ value: fixture.externalId }));
                expect(await api.get(`/api/content/${topId}/metadata/contributions`)).toEqual(
                    expect.arrayContaining([expect.objectContaining({ providerType: mocks.hierarchyPrimary.slug })]),
                );

                const subjects = await until(
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
                expect(subjects.id).toBeTruthy();
                const related = await api.get<Paged<ContentItem>>(
                    `/api/content?filter=${encodeURIComponent(
                        `Relationships has (Name = "${fixture.title} Subject" and Type = "person")`,
                    )}&pageSize=100`,
                );
                expect(related.items).toContainEqual(expect.objectContaining({ id: topId }));
                expect(top.id).toBe(topId);
            });

            const target = await acquisitionTarget(api, fixture, topId);
            await drainContentMetadataRefreshItems(api);
            const result = await test.step(`${fixture.contentType}: search equivalent releases, grab and import`, async () => {
                const acquired = await acquireContent(api, {
                    scenario: `acquire-${fixture.contentType}`,
                    contentId: target.id,
                    searchTitle: target.name,
                    releaseTitle: fixture.releaseTitle,
                    releaseExternalId: `release-${fixture.contentType}`,
                    downloadExternalId: `download-${fixture.contentType}`,
                    categories: fixture.categories,
                    landedFiles: fixture.landedFiles,
                    proveConcurrentGrab: fixture.contentType === 'movie',
                });
                if (fixture.contentType === 'movie') {
                    concurrentGrabStatus = acquired.secondGrabStatus;
                }

                expect(acquired.candidates.filter((candidate) => candidate.release.externalId === `release-${fixture.contentType}`)).toHaveLength(1);
                expect(acquired.download).toMatchObject({ status: 'Imported', progress: 100 });
                expect(acquired.content.contentItem?.fulfillment).toBe(fixture.expectedFulfillment);
                expect(acquired.content.contentItem?.mediaFiles).toContainEqual(
                    expect.objectContaining({ fileKind: fixture.expectedFileKind }),
                );
                expect(acquired.content.contentItem?.qualityStatus).not.toBeUndefined();

                const searchCalls = await mocks.indexer.calls<{
                    search: { idSearchLevelSlug?: string | null; title?: string | null };
                    settings: { apiUrl: string };
                }>('search');
                expect([...new Set(searchCalls.map((call) => call.request.settings.apiUrl))].sort()).toEqual([
                    'https://indexer.invalid/local',
                    'https://unhealthy.indexer.invalid',
                ]);
                expect(searchCalls.length).toBeGreaterThanOrEqual(2);
                const releases = await api.get<Array<{ title: string; acquisitionStatus: string }>>(
                    `/api/content/${target.id}/releases`,
                );
                expect(releases).toContainEqual(
                    expect.objectContaining({ title: fixture.releaseTitle, acquisitionStatus: 'Imported' }),
                );
                expect((await api.get<Paged<unknown>>(`/api/audit-history?contentId=${target.id}&pageSize=100`)).totalItems).toBeGreaterThan(0);
                expect(await cli.json<ContentItem>(['content', 'get', target.id])).toMatchObject({
                    id: target.id,
                    contentItem: { fulfillment: fixture.expectedFulfillment },
                });
                mediaRefreshObserved ||= (await mocks.mediaServer.calls('RefreshForContentAsync')).length > 0;
                return acquired;
            });
            added.push({ fixture, root, topId, targetId: target.id, result });

            if (fixture.contentType === 'tv') {
                await test.step('TV: import a season pack and retain its release only on children with main files', async () => {
                    const season = await childNamed(api, topId, 'Acquisition Journey Season 1');
                    const episode2 = await childNamed(api, season.id, 'Acquisition Journey Episode 2');
                    const episode3 = await childNamed(api, season.id, 'Acquisition Journey Episode 3');
                    const episode4 = await childNamed(api, season.id, 'Acquisition Journey Episode 4');
                    await acquireContent(api, {
                        scenario: 'acquire-tv-season-pack',
                        contentId: season.id,
                        importedContentId: episode2.id,
                        searchTitle: season.name,
                        releaseTitle: 'Acquisition Journey Series S01 720p Season Pack',
                        releaseExternalId: 'release-tv-season-pack',
                        downloadExternalId: 'download-tv-season-pack',
                        categories: ['tv'],
                        landedFiles: [
                            { source: 'episode-720p.mkv', target: 'Acquisition Journey Series S01E02.mkv' },
                            { source: 'episode-720p.mkv', target: 'Acquisition Journey Series S01E03.mkv' },
                            { source: 'movie.en.srt', target: 'Acquisition Journey Series S01E04.txt' },
                        ],
                    });
                    const hasPack = async (id: string) =>
                        (await api.get<Array<{ title: string }>>(`/api/content/${id}/releases`)).some(
                            (release) => release.title === 'Acquisition Journey Series S01 720p Season Pack',
                        );
                    expect(await hasPack(episode2.id)).toBe(true);
                    expect(await hasPack(episode3.id)).toBe(true);
                    expect(await hasPack(target.id)).toBe(false);
                    expect(await hasPack(episode4.id)).toBe(false);
                });
            }
        }

        await test.step('cancel one in-flight release search before it returns candidates', async () => {
            const movie = added.find((item) => item.fixture.contentType === 'movie')!;
            await mocks.indexer.useScenario('acquire-movie');
            await mocks.indexer.armSearchFallback([
                {
                    title: 'Acquisition Journey Movie 2025 Cancel Probe',
                    protocol: 'torrent',
                    externalId: 'release-movie-cancel',
                    infoUrl: 'https://indexer.journey.invalid/releases/release-movie-cancel',
                    size: 100,
                    publishDate: '2026-01-02T00:00:00Z',
                    categories: ['movies'],
                    languages: ['en'],
                },
            ]);
            await mocks.indexer.setLatency({ search: 60_000 });
            try {
                const executionId = await api.post<string>('/api/jobs/enqueue', {
                    jobType: 'ReleaseSearchItem',
                    parameters: { contentHierarchyItemId: movie.targetId },
                });
                // Cancel immediately while Queued/Running. Waiting for Running alone is racy when the
                // worker claims the job between status polls.
                await api.post(`/api/jobs/${executionId}/cancel`);
                expect((await waitForJob(api, executionId, 120_000)).status).toBe('Cancelled');
            } finally {
                await mocks.indexer.setLatency({});
            }
        });

        await test.step('prove domain-specific file semantics and completed job lineage', async () => {
            const movie = added.find((item) => item.fixture.contentType === 'movie')!;
            expect(movie.result.content.contentItem?.mediaFiles).toEqual(
                expect.arrayContaining([
                    expect.objectContaining({ fileKind: 'Video', filePath: expect.stringMatching(/\.mkv$/) }),
                    expect.objectContaining({ fileKind: 'Subtitle', filePath: expect.stringMatching(/\.srt$/) }),
                ]),
            );
            expect(movie.result.content.contentItem?.fulfillment).toBe('Upgradable');

            const music = added.find((item) => item.fixture.contentType === 'music')!;
            expect((await api.get<ContentItem>(`/api/content/${music.topId}`)).level).toBe('artist');
            expect((await api.get<ContentItem>(`/api/content/${music.targetId}`)).level).toBe('track');
            const registry = await api.get<Array<{ slug: string; levelDefinitions: Array<{ slug: string; isAcquirable: boolean }> }>>(
                '/api/content-types',
            );
            expect(registry.find((type) => type.slug === 'music')?.levelDefinitions).toContainEqual(
                expect.objectContaining({ slug: 'artist', isAcquirable: false }),
            );

            const book = added.find((item) => item.fixture.contentType === 'book')!;
            expect(book.result.content.contentItem?.mediaFiles).toContainEqual(
                expect.objectContaining({ fileKind: 'Unknown', mimeType: 'application/pdf' }),
            );
            const videoDomains = added.filter((item) => ['movie', 'tv'].includes(item.fixture.contentType));
            for (const item of videoDomains) {
                expect(
                    item.result.content.contentItem?.mediaFiles.find((file) => file.mediaInfo?.tracks?.length)
                        ?.mediaInfo?.tracks,
                ).toContainEqual(expect.objectContaining({ type: 'Video' }));
            }
            expect(
                music.result.content.contentItem?.mediaFiles.find((file) => file.mediaInfo?.tracks?.length)?.mediaInfo
                    ?.tracks,
            ).toContainEqual(
                expect.objectContaining({ type: 'Audio' }),
            );

            expect(mediaRefreshObserved).toBe(true);
            const jobs = await api.get<Paged<{
                id: string;
                status: string;
                progress?: number | null;
                parentExecutionId?: string | null;
            }>>('/api/jobs?pageSize=500');
            expect(jobs.items.filter((job) => job.status === 'Completed').length).toBeGreaterThan(20);
            expect(jobs.items.some((job) => job.parentExecutionId)).toBe(true);
            expect(jobs.items.some((job) => job.progress === 100)).toBe(true);
            if (concurrentGrabStatus !== undefined) {
                expect(concurrentGrabStatus, 'a second grab must be rejected while the first is running').toBe(409);
            }
        });
    });
});

async function armMetadataSearch(fixture: DomainFixture): Promise<void> {
    const candidate = {
        providerType: mocks.hierarchyPrimary.slug,
        externalIdSource: 'tmdb',
        externalId: fixture.externalId,
        contentType: fixture.contentType,
        level: fixture.level,
        title: fixture.title,
        year: 2025,
        releaseDate: '2025-04-05T00:00:00Z',
        overview: `${fixture.title} provider overview.`,
        language: 'en',
        thumbnailUrl: `https://assets.journey.invalid/${fixture.externalId}.jpg`,
    };
    const relatedSubjects = [
        {
            relationshipKind: 'creator',
            relationshipQualifier: 'primary',
            relationshipOrdinal: 0,
            subjectKind: 'person',
            subjectName: `${fixture.title} Subject`,
            subjectExternalIds: { imdb: `subject-${fixture.externalId}` },
            initialSubjectContribution: contribution(`subject-${fixture.externalId}`, `${fixture.title} Subject`),
        },
    ];
    await mocks.hierarchyPrimary.useScenario(`metadata-${fixture.contentType}`);
    await mocks.hierarchyPrimary.write('advancedSearchAsync.json', [candidate]);
    await mocks.hierarchyPrimary.write(`hydrateAsync-${fixture.externalId}.json`, candidate);
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', metadata(fixture.externalId, fixture.title));
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', directChildren(fixture.children));
    await writeHierarchyFixtures(
        fixture.externalId,
        metadata(fixture.externalId, fixture.title),
        fixture.children,
        relatedSubjects,
    );
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', relatedSubjects);
    await mocks.hierarchyPrimary.clearCalls();
}

async function acquisitionTarget(api: ApiClient, fixture: DomainFixture, topId: string): Promise<ContentItem> {
    if (fixture.contentType === 'tv') {
        const season = await childNamed(api, topId, 'Acquisition Journey Season 1');
        return childNamed(api, season.id, 'Acquisition Journey Episode 1');
    }
    if (fixture.contentType === 'music') {
        const album = await childNamed(api, topId, 'Acquisition Journey Album');
        return childNamed(api, album.id, 'Acquisition Journey Track 1');
    }
    return api.get<ContentItem>(`/api/content/${topId}`);
}

async function childNamed(api: ApiClient, parentId: string, name: string): Promise<ContentItem> {
    return until(
        `${name} child`,
        async () => {
            const children = await api.get<Paged<ContentItem>>(`/api/content/${parentId}/children?pageSize=100`);
            return children.items.find((item) => item.name === name);
        },
        60_000,
        500,
    );
}

function metadata(externalId: string, title: string): Record<string, unknown> {
    return {
        ...contribution(externalId, title),
        overview: `${title} provider overview.`,
        publishDate: '2025-04-05T00:00:00Z',
        externalIds: [{ source: 'tmdb', value: externalId, ordinal: 0 }],
        genres: [{ name: 'Journey Acquisition', ordinal: 0 }],
        images: [
            {
                type: 'poster',
                url: `https://assets.journey.invalid/${externalId}.jpg`,
                mimeType: 'image/jpeg',
                source: 'journey',
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
