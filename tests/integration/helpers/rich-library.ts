import { acquireContent } from './acquisition';
import { type ApiClient } from './api';
import { drainContentMetadataRefreshItems } from './jobs';
import { mocks } from './mocks';
import { until } from './wait';

interface Paged<T> {
    items: T[];
    totalItems: number;
}

interface Named {
    id: string;
    name: string;
}

interface RootFolder extends Named {
    contentType: string;
}

interface ContentItem extends Named {
    level: string;
}

interface ScheduleTrigger {
    enabled: boolean;
    id: string;
    jobType: string;
}

export interface HierarchyItemFixture {
    alignmentKey: string;
    children?: HierarchyItemFixture[];
    externalIds: Record<string, string>;
    initialContribution: Record<string, unknown>;
    level: string;
    name: string;
    ordinal?: number | null;
    relatedSubjects?: unknown[];
    slug: string;
}

interface HydrateFixture {
    children?: HierarchyItemFixture[];
    contentType: string;
    externalId: string;
    levelSlug: string;
    metadata: Record<string, unknown>;
    relatedSubjects?: unknown[];
    rootFolderId: string;
}

export interface RichLibraryResult {
    contentIds: {
        album: string;
        artist: string;
        book: string;
        episode: string;
        movie: string;
        season: string;
        series: string;
        subject: string;
        track: string;
    };
    fileAnchors: string[];
    patchedTriggers: Array<{
        enabled: boolean;
        endpoint: 'schedule-triggers' | 'event-triggers';
        id: string;
    }>;
}

export async function buildRichLibrary(api: ApiClient): Promise<RichLibraryResult> {
    const scheduleTriggers = await api.get<ScheduleTrigger[]>('/api/schedule-triggers');
    const acquisitionTriggers = ['ReleaseSearchScan', 'ReleaseAutoGrabScan'].map((jobType) => {
        const trigger = scheduleTriggers.find((item) => item.jobType === jobType);
        if (!trigger) {
            throw new Error(`The configured baseline has no ${jobType} schedule trigger.`);
        }
        return trigger;
    });
    const eventTriggers = await api.get<ScheduleTrigger[]>('/api/event-triggers');
    const acquisitionEventTriggers = eventTriggers.filter((item) => item.jobType === 'ReleaseAutoGrabScan');
    if (acquisitionEventTriggers.length === 0) {
        throw new Error('The configured baseline has no ReleaseAutoGrabScan event trigger.');
    }
    const patchedTriggers: RichLibraryResult['patchedTriggers'] = [];

    for (const trigger of acquisitionTriggers) {
        await api.patch(`/api/schedule-triggers/${trigger.id}`, { enabled: { value: false } });
        patchedTriggers.push({ enabled: trigger.enabled, endpoint: 'schedule-triggers', id: trigger.id });
    }
    for (const trigger of acquisitionEventTriggers) {
        await api.patch(`/api/event-triggers/${trigger.id}`, { enabled: { value: false } });
        patchedTriggers.push({ enabled: trigger.enabled, endpoint: 'event-triggers', id: trigger.id });
    }
    return { ...(await populateRichLibrary(api)), patchedTriggers };
}

async function populateRichLibrary(api: ApiClient): Promise<Omit<RichLibraryResult, 'patchedTriggers'>> {
    const roots = await api.get<RootFolder[]>('/api/root-folders');
    const root = (contentType: string): RootFolder => {
        const match = roots.find((item) => item.contentType === contentType);
        if (!match) {
            throw new Error(`The configured baseline has no ${contentType} root folder.`);
        }
        return match;
    };

    const episodes = Array.from({ length: 30 }, (_, index) => {
        const number = index + 1;
        const { publishDate, sequenceNumber } = episodeSchedule(number);
        return {
            level: 'episode',
            name: `Journey Episode ${String(number).padStart(2, '0')}`,
            slug: `journey-episode-${String(number).padStart(2, '0')}`,
            ordinal: sequenceNumber,
            alignmentKey: `episode:1:${number}`,
            externalIds: { tmdb: `rich-tv-episode-${number}` },
            initialContribution: {
                providerType: mocks.hierarchyPrimary.slug,
                source: `tmdb:rich-tv-episode-${number}`,
                title: `Journey Episode ${String(number).padStart(2, '0')}`,
                overview: `Episode ${number} in the rich-library sequence.`,
                publishDate,
                sequenceNumber,
                externalIds: [{ source: 'tmdb', value: `rich-tv-episode-${number}`, ordinal: 0 }],
            },
        };
    });

    const series = await hydrate(api, {
        contentType: 'tv',
        externalId: 'rich-tv',
        levelSlug: 'show',
        rootFolderId: root('tv').id,
        metadata: richMetadata('rich-tv', 'Journey Series', 'A series with a complete, paged episode hierarchy.'),
        children: [
            {
                level: 'season',
                name: 'Journey Season 1',
                slug: 'journey-season-1',
                ordinal: 1,
                alignmentKey: 'season:1',
                externalIds: { tmdb: 'rich-tv-season-1' },
                initialContribution: {
                    providerType: mocks.hierarchyPrimary.slug,
                    source: 'tmdb:rich-tv-season-1',
                    title: 'Journey Season 1',
                    sequenceNumber: 1,
                    publishDate: '2025-01-01T00:00:00Z',
                },
                children: episodes,
            },
        ],
    });
    const season = await waitForChild(api, series, 'Journey Season 1');
    const episodeRows = await until(
        'the rich series to contain all 30 episodes',
        async () => {
            const page = await api.get<Paged<ContentItem>>(`/api/content/${season.id}/children?pageSize=100`);
            return page.totalItems === 30 ? page.items : undefined;
        },
        60_000,
        500,
    );
    const episode = episodeRows.find((item) => item.name === 'Journey Episode 01');
    if (!episode) {
        throw new Error('The first rich-library episode did not hydrate.');
    }
    for (const item of episodeRows) {
        const { publishDate, sequenceNumber } = episodeSchedule(Number(item.name.slice(-2)));
        await api.patch(`/api/content/${item.id}/metadata`, {
            sequenceNumber: { value: sequenceNumber },
            publishDate: { value: publishDate },
        });
    }

    const movie = await hydrate(api, {
        contentType: 'movie',
        externalId: 'rich-movie',
        levelSlug: 'movie',
        rootFolderId: root('movie').id,
        metadata: richMetadata('rich-movie', 'Journey Feature', 'A feature with metadata, a main file, and a sidecar.'),
        relatedSubjects: [
            {
                relationshipKind: 'performer',
                relationshipQualifier: 'lead',
                relationshipOrdinal: 0,
                subjectKind: 'person',
                subjectName: 'Journey Performer',
                subjectExternalIds: { imdb: 'nm-rich-performer' },
                initialSubjectContribution: {
                    ...richMetadata(
                        'rich-performer',
                        'Journey Performer',
                        'The performer related to the rich-library feature.',
                    ),
                    source: 'imdb:nm-rich-performer',
                    birthDate: '1984-05-06T00:00:00Z',
                },
            },
        ],
    });
    await api.patch(`/api/content/${movie}`, { tags: { add: ['journey-rich', 'featured'] } });

    const artist = await hydrate(api, {
        contentType: 'music',
        externalId: 'rich-artist',
        levelSlug: 'artist',
        rootFolderId: root('music').id,
        metadata: richMetadata('rich-artist', 'Journey Artist', 'An artist with one ordered album.'),
        children: [
            {
                level: 'album',
                name: 'Journey Album',
                slug: 'journey-album',
                alignmentKey: 'release-group:rich-album',
                externalIds: { musicbrainz: 'rich-album' },
                initialContribution: {
                    providerType: mocks.hierarchyPrimary.slug,
                    source: 'musicbrainz:rich-album',
                    title: 'Journey Album',
                    publishDate: '2024-06-01T00:00:00Z',
                },
                children: [1, 2, 3].map((number) => ({
                    level: 'track',
                    name: `Journey Track ${number}`,
                    slug: `journey-track-${number}`,
                    ordinal: number,
                    alignmentKey: `track:rich-track-${number}`,
                    externalIds: { musicbrainz: `rich-track-${number}` },
                    initialContribution: {
                        providerType: mocks.hierarchyPrimary.slug,
                        source: `musicbrainz:rich-track-${number}`,
                        title: `Journey Track ${number}`,
                        sequenceNumber: number,
                    },
                })),
            },
        ],
    });
    const album = await waitForChild(api, artist, 'Journey Album');
    const track = await waitForChild(api, album.id, 'Journey Track 1');

    const book = await hydrate(api, {
        contentType: 'book',
        externalId: 'rich-book',
        levelSlug: 'ebook',
        rootFolderId: root('book').id,
        metadata: richMetadata('rich-book', 'Journey Book', 'A book fulfilled by the current document-file contract.'),
    });
    for (let index = 1; index <= 48; index += 1) {
        const label = String(index).padStart(2, '0');
        const metadata = richMetadata(
            `rich-catalog-${label}`,
            `Journey Catalog ${label}`,
            `Paged rich-library catalog fixture ${label}.`,
        );
        if (index === 26) {
            metadata['publishDate'] = new Date(Date.now() + 2 * 24 * 60 * 60 * 1_000).toISOString();
        }
        await hydrate(api, {
            contentType: 'movie',
            externalId: `rich-catalog-${label}`,
            levelSlug: 'movie',
            rootFolderId: root('movie').id,
            metadata,
        });
    }

    const acquisitions = [];
    acquisitions.push(
        await acquireContent(api, {
            scenario: 'rich-movie-acquisition',
            contentId: movie,
            searchTitle: 'Journey Feature',
            releaseTitle: 'Journey Feature 2024 720p WEB-DL',
            releaseExternalId: 'rich-movie-release',
            downloadExternalId: 'rich-movie-download',
            categories: ['movies'],
            landedFiles: [
                { source: 'movie-720p.mkv', target: 'Journey Feature/Journey Feature.mkv' },
                { source: 'movie.en.srt', target: 'Journey Feature/Journey Feature.en.srt' },
            ],
        }),
    );
    await until(
        'rich-library movie imported release',
        async () => {
            const releases = await api.get<Array<{ title: string; acquisitionStatus: string }>>(
                `/api/content/${movie}/releases`,
            );
            return releases.find(
                (release) =>
                    release.title === 'Journey Feature 2024 720p WEB-DL' &&
                    release.acquisitionStatus === 'Imported',
            );
        },
        30_000,
        250,
    );
    acquisitions.push(
        await acquireContent(api, {
            scenario: 'rich-tv-acquisition',
            contentId: episode.id,
            searchTitle: episode.name,
            releaseTitle: 'Journey Series S01E01 720p WEB-DL',
            releaseExternalId: 'rich-episode-release',
            downloadExternalId: 'rich-episode-download',
            categories: ['tv'],
            landedFiles: [{ source: 'episode-720p.mkv', target: 'Journey Series S01E01.mkv' }],
        }),
    );
    acquisitions.push(
        await acquireContent(api, {
            scenario: 'rich-music-acquisition',
            contentId: track.id,
            searchTitle: track.name,
            releaseTitle: 'Journey Artist Journey Album Track 01 FLAC',
            releaseExternalId: 'rich-track-release',
            downloadExternalId: 'rich-track-download',
            categories: ['music'],
            landedFiles: [{ source: 'track.flac', target: 'Journey Artist - Journey Track 1.flac' }],
        }),
    );
    acquisitions.push(
        await acquireContent(api, {
            scenario: 'rich-book-acquisition',
            contentId: book,
            searchTitle: 'Journey Book',
            releaseTitle: 'Journey Book PDF',
            releaseExternalId: 'rich-book-release',
            downloadExternalId: 'rich-book-download',
            categories: ['books'],
            landedFiles: [{ source: 'book.pdf', target: 'Journey Book.pdf' }],
        }),
    );

    const subjects = await until(
        'the rich-library subject relationship',
        async () => {
            const page = await api.get<Paged<{ id: string; metadataContainer?: { contributions?: Array<{ title?: string }> } }>>(
                `/api/subjects?filter=${encodeURIComponent('title = "Journey Performer"')}&pageSize=10`,
            );
            return page.items.find((item) =>
                item.metadataContainer?.contributions?.some((contribution) => contribution.title === 'Journey Performer'),
            );
        },
        60_000,
        500,
    );

    const fileAnchors = acquisitions.flatMap((result) =>
        result.content.contentItem!.mediaFiles.map((file) => hostAnchor(file.filePath)),
    );
    return {
        contentIds: {
            album: album.id,
            artist,
            book,
            episode: episode.id,
            movie,
            season: season.id,
            series,
            subject: subjects.id,
            track: track.id,
        },
        fileAnchors,
    };
}

function episodeSchedule(number: number): { publishDate: string; sequenceNumber: number | null } {
    return number <= 28
        ? { publishDate: `2025-03-${String(number).padStart(2, '0')}T00:00:00Z`, sequenceNumber: number }
        : { publishDate: `2025-04-${String(number === 30 ? 20 : 10).padStart(2, '0')}T00:00:00Z`, sequenceNumber: null };
}

async function hydrate(api: ApiClient, fixture: HydrateFixture): Promise<string> {
    await mocks.hierarchyPrimary.useScenario(`rich-${fixture.externalId}`);
    await mocks.hierarchyPrimary.write('tryGetMetadataAsync.json', fixture.metadata);
    await mocks.hierarchyPrimary.write('expandChildrenAsync.json', directChildren(fixture.children ?? []));
    await writeHierarchyFixtures(
        fixture.externalId,
        fixture.metadata,
        fixture.children ?? [],
        fixture.relatedSubjects ?? [],
    );
    await mocks.hierarchyPrimary.write('expandRelatedSubjectsAsync.json', fixture.relatedSubjects ?? []);
    await mocks.hierarchyPrimary.write('searchAsync.json', []);

    const id = await api.post<string>(
        `/api/content/from-external-id?rootFolderId=${fixture.rootFolderId}&monitoring=true`,
        {
            providerType: mocks.hierarchyPrimary.slug,
            externalIdKey: 'tmdb',
            externalId: fixture.externalId,
            contentType: fixture.contentType,
            levelSlug: fixture.levelSlug,
        },
    );
    const title = String(fixture.metadata['title']);
    await until(
        `${title} metadata`,
        async () => ((await api.get<ContentItem>(`/api/content/${id}`)).name === title ? true : undefined),
        60_000,
        500,
    );
    await Promise.all([
        mocks.hierarchyPrimary.waitForCalls<{ parent: { id: string } }>(1, {
            method: 'expandChildrenAsync',
            predicate: (record) => record.request.parent.id === id,
        }),
        mocks.hierarchyPrimary.waitForCalls<{ item: { id: string } }>(1, {
            method: 'expandRelatedSubjectsAsync',
            predicate: (record) => record.request.item.id === id,
        }),
    ]);
    await drainContentMetadataRefreshItems(api);
    return id;
}

export async function writeHierarchyFixtures(
    externalId: string,
    metadata: Record<string, unknown>,
    children: HierarchyItemFixture[],
    relatedSubjects: unknown[] = [],
): Promise<void> {
    await mocks.hierarchyPrimary.write(`tryGetMetadataAsync-${externalId}.json`, metadata);
    await mocks.hierarchyPrimary.write(`expandChildrenAsync-${externalId}.json`, directChildren(children));
    await mocks.hierarchyPrimary.write(`expandRelatedSubjectsAsync-${externalId}.json`, relatedSubjects);

    for (const child of children) {
        const childExternalId = Object.values(child.externalIds)[0];
        if (!childExternalId) {
            throw new Error(`Hierarchy fixture ${child.name} has no external id.`);
        }
        const initialExternalIds = child.initialContribution['externalIds'];
        const initialExternalId = Array.isArray(initialExternalIds)
            ? (initialExternalIds[0] as { value?: unknown } | undefined)?.value
            : undefined;
        if (initialExternalId !== undefined && initialExternalId !== childExternalId) {
            throw new Error(`Hierarchy fixture ${child.name} has mismatched external ids.`);
        }
        const externalIds = Object.entries(child.externalIds).map(([source, value], ordinal) => ({
            source,
            value,
            ordinal,
        }));
        await writeHierarchyFixtures(
            childExternalId,
            { ...child.initialContribution, externalIds },
            child.children ?? [],
            child.relatedSubjects ?? [],
        );
    }
}

export function directChildren(children: HierarchyItemFixture[]): HierarchyItemFixture[] {
    return children.map((child) => ({ ...child, children: [] }));
}

async function waitForChild(api: ApiClient, parentId: string, name: string): Promise<ContentItem> {
    return until(
        `${name} child`,
        async () => {
            const page = await api.get<Paged<ContentItem>>(`/api/content/${parentId}/children?pageSize=100`);
            return page.items.find((item) => item.name === name);
        },
        60_000,
        500,
    );
}

function richMetadata(externalId: string, title: string, overview: string): Record<string, unknown> {
    return {
        providerType: mocks.hierarchyPrimary.slug,
        source: `tmdb:${externalId}`,
        title,
        originalTitle: `${title} Original`,
        sortTitle: title.replace(/^Journey /, ''),
        overview,
        language: 'en',
        publishDate: '2024-05-06T00:00:00Z',
        productionStatus: 'released',
        customMetadata: [{ key: 'journey-code', value: externalId }],
        externalIds: [{ source: 'tmdb', value: externalId, ordinal: 0 }],
        genres: [{ name: 'Journey Genre', ordinal: 0 }],
        images: [
            {
                type: 'poster',
                mimeType: 'image/jpeg',
                url: `https://assets.journey.invalid/${externalId}.jpg`,
                source: 'journey-fixture',
                width: 600,
                height: 900,
                isPrimary: true,
                ordinal: 0,
            },
        ],
        references: [
            {
                kind: 'homepage',
                source: 'journey-fixture',
                url: `https://catalog.journey.invalid/${externalId}`,
                displayName: `${title} catalog`,
                language: 'en',
                ordinal: 0,
            },
        ],
    };
}

function hostAnchor(filePath: string): string {
    if (!filePath.startsWith('/library/')) {
        throw new Error(`Imported file ${filePath} is outside the fixture library.`);
    }
    return `library/${filePath.slice('/library/'.length)}`;
}
