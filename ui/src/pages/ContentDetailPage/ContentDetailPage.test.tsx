import { act, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { useLibraryTypes } from '@/hooks/useLibraryTypes';

import { ContentDetailPage } from './ContentDetailPage';
import { Role } from '@/auth';
import { ApiError } from '@/lib/api';
import { ToastProvider, ToastStateProvider, ToastList, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ContentDetailItem } from '@/types/content';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/components/configuration/FileBrowser', () => ({
    FileBrowser: ({
        open,
        initialPath,
        rootPath,
        endpoint,
        onSelect,
    }: {
        open: boolean;
        initialPath?: string;
        rootPath?: string;
        endpoint?: string;
        onSelect: (path: string) => void;
    }) =>
        open ? (
            <div
                role="dialog"
                aria-label="Import file"
                data-initial-path={initialPath}
                data-root-path={rootPath}
                data-endpoint={endpoint}
            >
                <button type="button" onClick={() => onSelect('/media/library/The Matrix/new.mkv')}>
                    Select host file
                </button>
            </div>
        ) : null,
}));

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const mockUseJobExecution = vi.fn(
    (_id: string, _enabled?: boolean) =>
        ({ job: null, isLoading: false, error: null, refetch: vi.fn() }) as {
            job: unknown;
            isLoading: boolean;
            error: Error | null;
            refetch: () => void;
        },
);
vi.mock('@/hooks/useJobs', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useJobs')>('@/hooks/useJobs');
    return {
        ...actual,
        useJobExecution: (id: string, enabled?: boolean) => mockUseJobExecution(id, enabled),
    };
});

type RawDetailItem = Omit<ContentDetailItem, 'contentType' | 'previews' | 'images'> &
    Partial<Pick<ContentDetailItem, 'contentType' | 'previews' | 'images'>>;

function normalizeDetail(item: RawDetailItem): ContentDetailItem {
    return {
        contentType: item.contentType ?? 'tv',
        previews: [],
        images: [],
        ...item,
    };
}

const rawContentData: Record<string, RawDetailItem> = {
    'content-1': {
        id: 'content-1',
        title: 'Breaking Bad',
        posterUrl: 'https://example.com/poster1.jpg',
        level: 'show',
        levelLabel: 'Show',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled',
        year: 2008,
        publishDate: '2008-01-20',
        overview: 'A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing.',
        path: 'Breaking Bad',
        genres: ['Drama', 'Crime'],
        factValues: { status: 'Ended' },
        externalIds: [
            { source: 'Provider H', value: 'tt0903747', url: 'https://www.provider-h.example/title/tt0903747/' },
            { source: 'internal.magpie', value: 'technical-id' },
        ],
    },
    'content-files': {
        id: 'content-files',
        title: 'The Matrix',
        posterUrl: 'https://example.com/matrix.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled',
        year: 1999,
        overview: 'A hacker learns the truth.',
        path: 'The Matrix',
        mediaFiles: [
            {
                id: 'media-file-1',
                contentItemId: 'content-item-1',
                fileKind: 'Video',
                filePath: '/media/library/The Matrix/the-matrix.mkv',
                fileSize: 1024 * 1024 * 1024,
                createdAt: '2024-01-01T00:00:00Z',
            },
        ],
    },
    'content-3': {
        id: 'content-3',
        title: 'Dune: Part Two',
        posterUrl: 'https://example.com/poster3.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'missing',
        year: 2024,
        overview: 'Paul Atreides unites with Chani and the Fremen.',
    },
    'content-cycle': {
        id: 'content-cycle',
        title: 'Cyclic Content',
        level: 'alpha',
        levelLabel: 'Alpha',
        contentType: 'cycle',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'missing',
    },
    'content-trailers': {
        id: 'content-trailers',
        title: 'Gladiator',
        posterUrl: 'https://example.com/gladiator.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        contentType: 'movie',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'missing',
        year: 2000,
        overview: 'A betrayed general seeks revenge.',
        previews: [
            { url: 'https://www.youtube.com/watch?v=TEASER1', kind: 'teaser', title: 'Teaser' },
            { url: 'https://www.youtube.com/watch?v=TRAILER1', kind: 'trailer', title: 'Official Trailer' },
        ],
        images: [
            { id: '0-poster', url: 'https://example.com/gladiator-poster.jpg', type: 'poster', isPrimary: true },
            { id: '0-backdrop', url: 'https://example.com/gladiator-backdrop.jpg', type: 'backdrop' },
        ],
    },
    'content-5': {
        id: 'content-5',
        title: 'The Gentlemen',
        posterUrl: 'https://example.com/poster5.jpg',
        level: 'show',
        levelLabel: 'Show',
        monitoring: false,
        effectiveMonitoring: false,
        fulfillment: 'missing',
        year: 2024,
        overview: 'A nobleman inherits a weed empire.',
    },
    'content-8': {
        id: 'content-8',
        title: 'Poor Things',
        posterUrl: 'https://example.com/poster8.jpg',
        level: 'movie',
        levelLabel: 'Movie',
        monitoring: false,
        effectiveMonitoring: false,
        fulfillment: 'fulfilledExternally',
        year: 2023,
        overview: 'The story of Bella Baxter.',
    },
    'content-9': {
        id: 'content-9',
        title: 'Fallout',
        level: 'show',
        levelLabel: 'Show',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled',
        year: 2024,
        overview: 'Post-apocalyptic survival.',
    },
    'season-1': {
        id: 'season-1',
        title: 'Season 1',
        posterUrl: 'https://example.com/poster1.jpg',
        level: 'season',
        levelLabel: 'Season',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled',
        year: 2008,
        overview: 'The first season of Breaking Bad.',
        parentId: 'content-1',
    },
    'episode-1': {
        id: 'episode-1',
        title: 'Pilot',
        posterUrl: 'https://example.com/poster1.jpg',
        level: 'episode',
        levelLabel: 'Episode',
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled',
        year: 2008,
        overview: 'Walter White decides to get into the meth-making business.',
        parentId: 'season-1',
    },
};

const mockContentData: Record<string, ContentDetailItem> = Object.fromEntries(
    Object.entries(rawContentData).map(([id, item]) => [id, normalizeDetail(item)]),
);

const mockUseContentHierarchy = vi.fn();
const mockUseContentAncestors = vi.fn();
const mockUseContentList = vi.fn();
const mockUseRefreshContent = vi.fn();
const mockUseSearchContentReleases = vi.fn();
const mockUseRescanContentMediaInfo = vi.fn();
const mockUseMetadataInspector = vi.fn();
const mockMetadataInspectorState = { isLoading: false, error: null as unknown };
const mockMetadataInspectorRefetch = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useContentHierarchy: (activeId: string | undefined, parentId: string | undefined, rootId: string | undefined) =>
        mockUseContentHierarchy(activeId, parentId, rootId),
    useContentAncestors: (leafParentId: string | undefined) => mockUseContentAncestors(leafParentId),
    useContentList: (params: unknown, options: unknown) => mockUseContentList(params, options),
    useRefreshContent: (options?: unknown) => mockUseRefreshContent(options),
    useSearchContentReleases: (options?: unknown) => mockUseSearchContentReleases(options),
    useRescanContentMediaInfo: (options?: unknown) => mockUseRescanContentMediaInfo(options),
    useMediaServerLinks: () => ({ data: [], isLoading: false }),
    useMetadataInspector: (id: string | undefined) => mockUseMetadataInspector(id),
    usePatchMetadataInspector: () => ({ mutate: vi.fn(), isPending: false }),
    useReorderMetadataContributions: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useUpdateContent: () => ({ mutate: vi.fn(), isPending: false }),
    useContentRelationships: () => ({ data: [], isLoading: false }),
    usePendingRefresh: () => ({ data: false, isLoading: false }),
    useEffectiveRootFolder: () => ({
        data: {
            rootFolderId: 'root-1',
            rootFolderName: 'Library',
            rootFolderPath: '/media/library',
            source: 'ContentTypeDefault' as const,
            resolvingHierarchyItemId: null,
            isOverriddenOnItem: false,
        },
        isLoading: false,
    }),
    useSetContentRootFolder: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useApplyContentPath: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useEffectiveQualityTierList: () => ({
        data: {
            qualityTierListId: 'tier-1',
            qualityTierListName: 'HD Tiers',
            source: 'ContentTypeDefaultRootFolder' as const,
            resolvingHierarchyItemId: null,
            resolvingHierarchyItemName: null,
            ownQualityTierListId: null,
        },
        isLoading: false,
    }),
    useSetQualityTierList: () => ({ mutateAsync: vi.fn(), isPending: false }),
    contentQueryKeys: {
        all: ['content'],
        list: () => ['content', 'list'],
        detail: (id: string) => ['content', 'detail', id],
        mediaServerLinks: (id: string) => ['content', 'media-server-links', id],
        effectiveRootFolder: (id: string) => ['content', 'effective-root-folder', id],
    },
}));

vi.mock('@/hooks/useLibraryTypes', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useLibraryTypes')>('@/hooks/useLibraryTypes');
    return {
        ...actual,
        useLibraryTypes: vi.fn(() => ({
            contentTypes: [
                {
                    id: 'movie',
                    name: 'Movie',
                    slug: 'movie',
                    levels: [
                        {
                            slug: 'movie',
                            displayName: 'Movie',
                            parentSlug: null,
                            subfolder: 'movies',
                            standardMetadataFields: [
                                { field: 'OriginalTitle', displayName: 'Original title' },
                                { field: 'SortTitle', displayName: 'Sort title' },
                                { field: 'Language', displayName: 'Language' },
                                { field: 'PublishDate', displayName: 'Publish date' },
                                { field: 'Duration', displayName: 'Duration' },
                                { field: 'SequenceNumber', displayName: 'Sequence number' },
                                { field: 'ProductionStatus', displayName: 'Production status' },
                                { field: 'Established', displayName: 'Established' },
                                { field: 'Dissolved', displayName: 'Dissolved' },
                                { field: 'BirthDate', displayName: 'Birth date' },
                                { field: 'DeathDate', displayName: 'Death date' },
                                { field: 'ContentRatings', displayName: 'Content ratings' },
                                { field: 'Genres', displayName: 'Genres' },
                                { field: 'Previews', displayName: 'Previews' },
                            ],
                            customMetadataFields: [{ key: 'network', displayName: 'Network' }],
                        },
                    ],
                },
                {
                    id: 'tv',
                    name: 'TV',
                    slug: 'tv',
                    levels: [
                        {
                            slug: 'show',
                            displayName: 'Show',
                            parentSlug: null,
                            standardMetadataFields: [
                                { field: 'OriginalTitle', displayName: 'Original title' },
                                { field: 'SortTitle', displayName: 'Sort title' },
                                { field: 'Language', displayName: 'Language' },
                                { field: 'PublishDate', displayName: 'Publish date' },
                                { field: 'Duration', displayName: 'Duration' },
                                { field: 'SequenceNumber', displayName: 'Sequence number' },
                                { field: 'ProductionStatus', displayName: 'Production status' },
                                { field: 'Established', displayName: 'Established' },
                                { field: 'Dissolved', displayName: 'Dissolved' },
                                { field: 'BirthDate', displayName: 'Birth date' },
                                { field: 'DeathDate', displayName: 'Death date' },
                                { field: 'ContentRatings', displayName: 'Content ratings' },
                                { field: 'Genres', displayName: 'Genres' },
                                { field: 'Previews', displayName: 'Previews' },
                            ],
                            customMetadataFields: [{ key: 'network', displayName: 'Network' }],
                            standardFactFields: [{ key: 'status', displayName: 'Status' }],
                        },
                        { slug: 'season', displayName: 'Season', parentSlug: 'show' },
                        { slug: 'episode', displayName: 'Episode', parentSlug: 'season' },
                    ],
                },
                {
                    id: 'cycle',
                    name: 'Cycle',
                    slug: 'cycle',
                    levels: [
                        { slug: 'alpha', displayName: 'Alpha', parentSlug: 'beta', subfolder: 'alpha' },
                        { slug: 'beta', displayName: 'Beta', parentSlug: 'alpha' },
                    ],
                },
            ],
            subjectTypes: [],
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        })),
    };
});

vi.mock('@/hooks/useTags', () => ({
    useTagsByEntity: () => ({ tags: [], isLoading: false, error: null }),
    tagQueryKeys: {
        all: ['tags'],
        list: () => ['tags', 'list'],
        byPrefix: (prefix: string) => ['tags', 'prefix', prefix],
        byEntity: (entityId: string) => ['tags', 'entity', entityId],
    },
}));

const { mockGrabMutate, mockContentReleaseRows, mockLatestDownload, mockLatestDownloadLoading } = vi.hoisted(() => ({
    mockGrabMutate: vi.fn(),
    mockContentReleaseRows: { current: [] as unknown[] },
    mockLatestDownload: { current: null as unknown },
    mockLatestDownloadLoading: { current: false },
}));

vi.mock('@/hooks/useDownloads', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useDownloads')>('@/hooks/useDownloads');
    return {
        ...actual,
        useLatestDownloadForRelease: () => ({
            download: mockLatestDownload.current,
            isLoading: mockLatestDownloadLoading.current,
            error: null,
        }),
    };
});

vi.mock('@/hooks/useReleases', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useReleases')>('@/hooks/useReleases');
    return {
        ...actual,
        useGrabRelease: () => ({ mutate: mockGrabMutate, mutateAsync: vi.fn(), isPending: false }),
    };
});

vi.mock('@/hooks/useContentReleases', async () => {
    const actual = await vi.importActual<typeof import('@/hooks/useContentReleases')>('@/hooks/useContentReleases');
    return {
        ...actual,
        useContentReleases: () => ({
            rows: mockContentReleaseRows.current,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        }),
    };
});

const {
    mockHasRole,
    mockDeleteFileMutate,
    mockDeleteFilePending,
    mockAttachFileMutate,
    mockAttachFilePending,
    mockUnlinkFileMutate,
    mockUnlinkFilePending,
    mockFileBrowserLocation,
    mockFileBrowserLocationRefetch,
    mockRemoveContentMutate,
} = vi.hoisted(() => ({
    mockHasRole: { current: (_minRole: number): boolean => true },
    mockDeleteFileMutate: vi.fn(),
    mockDeleteFilePending: { current: false as boolean },
    mockAttachFileMutate: vi.fn(),
    mockAttachFilePending: { current: false as boolean },
    mockUnlinkFileMutate: vi.fn(),
    mockUnlinkFilePending: { current: false as boolean },
    mockFileBrowserLocation: {
        current: {
            rootPath: '/media/library',
            initialPath: '/media/library/The Matrix',
        } as { rootPath: string; initialPath: string } | undefined,
    },
    mockFileBrowserLocationRefetch: vi.fn(),
    mockRemoveContentMutate: vi.fn(),
}));

vi.mock('@/auth', async () => {
    const actual = await vi.importActual<typeof import('@/auth')>('@/auth');
    const { TEST_PERMISSION_CATALOG } = await import('@/test/permissions');
    return {
        ...actual,
        useAuth: () => ({
            hasRole: (minRole: number) => mockHasRole.current(minRole),
            hasPermission: (permission: import('@/auth').Permission) => {
                const required = TEST_PERMISSION_CATALOG[permission];
                return required !== undefined && mockHasRole.current(required);
            },
            getRequiredRole: (permission: import('@/auth').Permission) => TEST_PERMISSION_CATALOG[permission],
        }),
    };
});

vi.mock('@/hooks/useDeleteContentFile', () => ({
    useDeleteContentFile: () => ({
        mutate: mockDeleteFileMutate,
        isPending: mockDeleteFilePending.current,
    }),
}));

vi.mock('@/hooks/useContentFileMutations', () => ({
    useAttachContentFile: () => ({ mutate: mockAttachFileMutate, isPending: mockAttachFilePending.current }),
    useUnlinkContentFile: () => ({ mutate: mockUnlinkFileMutate, isPending: mockUnlinkFilePending.current }),
    useContentFileBrowserLocation: () => ({
        data: mockFileBrowserLocation.current,
        refetch: mockFileBrowserLocationRefetch,
    }),
}));

vi.mock('@/hooks/useRemoveContent', () => ({
    useRemoveContent: () => ({
        mutate: mockRemoveContentMutate,
        isPending: false,
    }),
}));

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });
}

function ContentDetailRoute() {
    const location = useLocation();

    return (
        <>
            <ContentDetailPage />
            <output data-testid="content-detail-page__location-search">{location.search}</output>
        </>
    );
}

function renderWithRouter(contentId: string, childId?: string, grandchildId?: string, search?: string) {
    const queryClient = createTestQueryClient();
    const basePath = grandchildId
        ? `/library/${contentId}/${childId}/${grandchildId}`
        : childId
          ? `/library/${contentId}/${childId}`
          : `/library/${contentId}`;
    const path = search ? `${basePath}${search}` : basePath;

    return render(
        <QueryClientProvider client={queryClient}>
            <MemoryRouter initialEntries={[path]}>
                <TooltipProvider>
                    <ToastProvider>
                        <ToastStateProvider>
                            <Routes>
                                <Route path="/library" element={<div data-testid="library-route" />} />
                                <Route
                                    path="/interactive-search/:contentId"
                                    element={<div data-testid="interactive-search-route" />}
                                />
                                <Route path="/library/:contentId" element={<ContentDetailRoute />} />
                                <Route path="/library/:contentId/:childId" element={<ContentDetailRoute />} />
                                <Route
                                    path="/library/:contentId/:childId/:grandchildId"
                                    element={<ContentDetailRoute />}
                                />
                            </Routes>
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

describe('ContentDetailPage', () => {
    const defaultContext = {
        apiBaseUrl: '/api',
        userId: 'user-123',
        username: 'testuser',
        role: 'Admin',
    };

    const noExtensions = {
        extensions: [],
        isReady: true,
        isLoading: false,
    };

    const mockMutationResult = {
        mutate: vi.fn(),
        isPending: false,
    };

    beforeEach(() => {
        window.innerWidth = 800;
        vi.clearAllMocks();
        mockContentReleaseRows.current = [];
        mockLatestDownload.current = null;
        mockLatestDownloadLoading.current = false;
        mockHasRole.current = () => true;
        mockDeleteFilePending.current = false;
        mockAttachFilePending.current = false;
        mockUnlinkFilePending.current = false;
        mockFileBrowserLocation.current = {
            rootPath: '/media/library',
            initialPath: '/media/library/The Matrix',
        };
        mockFileBrowserLocationRefetch.mockResolvedValue({
            data: mockFileBrowserLocation.current,
            error: null,
            isError: false,
        });
        mockMetadataInspectorState.isLoading = false;
        mockMetadataInspectorState.error = null;
        mockUseExtensions.mockReturnValue(defaultContext);
        mockUseSlotExtensions.mockReturnValue(noExtensions);

        mockUseContentHierarchy.mockImplementation(
            (activeId: string | undefined, parentId: string | undefined, rootId: string | undefined) => {
                const makeResult = (id: string | undefined) => {
                    const data = id ? mockContentData[id] : undefined;
                    return {
                        data,
                        isLoading: false,
                        error: data ? null : new ApiError(404, 'Not found'),
                        refetch: vi.fn(),
                    };
                };
                return {
                    content: makeResult(activeId),
                    parent: makeResult(parentId),
                    root: makeResult(rootId),
                };
            },
        );

        mockUseContentAncestors.mockImplementation((leafParentId: string | undefined) => {
            const chain: ContentDetailItem[] = [];
            let currentId = leafParentId;
            const seen = new Set<string>();
            while (currentId && mockContentData[currentId] && !seen.has(currentId)) {
                seen.add(currentId);
                chain.push(mockContentData[currentId]);
                currentId = mockContentData[currentId].parentId;
            }
            chain.reverse();
            return { ancestors: chain, isLoading: false };
        });

        mockUseContentList.mockReturnValue({
            data: { items: [], pageNumber: 1, pageSize: 25, totalItems: 0 },
            isLoading: false,
            error: null,
        });

        mockUseRefreshContent.mockReturnValue(mockMutationResult);
        mockUseSearchContentReleases.mockReturnValue(mockMutationResult);
        mockUseRescanContentMediaInfo.mockReturnValue(mockMutationResult);
        mockUseJobExecution.mockReturnValue({ job: null, isLoading: false, error: null, refetch: vi.fn() });
        mockUseMetadataInspector.mockImplementation((id: string | undefined) => {
            const item = id ? mockContentData[id] : undefined;
            return {
                data:
                    item && !mockMetadataInspectorState.isLoading && !mockMetadataInspectorState.error
                        ? {
                              materialized: {
                                  containerId: `metadata-${item.id}`,
                                  contentHierarchyItemId: item.id,
                                  title: item.title,
                                  overview: item.overview,
                                  publishDate: item.publishDate,
                                  productionStatus: item.factValues?.status as string | undefined,
                                  originalTitle: item.id === 'content-1' ? 'BrBa' : undefined,
                                  sortTitle: item.id === 'content-1' ? 'Breaking Bad, The' : undefined,
                                  language: item.id === 'content-1' ? 'en' : undefined,
                                  duration: item.id === 'content-1' ? '01:42:30' : undefined,
                                  sequenceNumber: item.id === 'content-1' ? 7 : undefined,
                                  established: item.id === 'content-1' ? '2000-01-02T15:30:00-08:00' : undefined,
                                  dissolved: item.id === 'content-1' ? '2001-02-03T00:00:00Z' : undefined,
                                  birthDate: item.id === 'content-1' ? '1956-03-07T00:00:00Z' : undefined,
                                  deathDate: item.id === 'content-1' ? '2020-12-01T00:00:00Z' : undefined,
                                  alternateTexts:
                                      item.id === 'content-1'
                                          ? [
                                                {
                                                    id: 'alternate-2',
                                                    ordinal: 2,
                                                    kind: 'Title',
                                                    text: 'Los Pollos',
                                                    language: 'es',
                                                },
                                                {
                                                    id: 'alternate-1',
                                                    ordinal: 1,
                                                    kind: 'Description',
                                                    text: 'Chimie',
                                                    language: 'fr',
                                                },
                                            ]
                                          : [],
                                  contentRatings:
                                      item.id === 'content-1'
                                          ? [
                                                { id: 'rating-2', ordinal: 2, body: 'BBFC', rating: '15' },
                                                { id: 'rating-1', ordinal: 1, body: 'MPAA', rating: 'TV-MA' },
                                            ]
                                          : [],
                                  awards:
                                      item.id === 'content-1'
                                          ? [
                                                {
                                                    id: 'award-1',
                                                    ordinal: 1,
                                                    body: 'Emmy Awards',
                                                    name: 'Outstanding Drama Series',
                                                    status: 'Won',
                                                    year: 2013,
                                                },
                                                {
                                                    id: 'award-2',
                                                    ordinal: 2,
                                                    body: 'Emmy Awards',
                                                    name: 'Outstanding Drama Series',
                                                    status: 'nominated',
                                                    year: 2014,
                                                },
                                                {
                                                    id: 'award-3',
                                                    ordinal: 3,
                                                    body: 'Emmy Awards',
                                                    name: 'Ensemble Prize',
                                                    category: 'Repertory',
                                                },
                                                {
                                                    id: 'award-4',
                                                    ordinal: 4,
                                                    body: 'Critics Circle',
                                                    name: 'Breakthrough Performance',
                                                    status: 'finalist',
                                                    year: 2009,
                                                },
                                            ]
                                          : [],
                                  relationships: [
                                      {
                                          id: `relationship-${item.id}`,
                                          ordinal: 0,
                                          kind: 'Related',
                                          relatedMetadataContainerId: `subject-${item.id}`,
                                      },
                                  ],
                                  customMetadata:
                                      item.id === 'content-1'
                                          ? [
                                                { id: 'custom-1', key: 'network', value: 'AMC' },
                                                { id: 'custom-2', key: 'provider-secret', value: 'hidden' },
                                            ]
                                          : [],
                                  externalIds: item.externalIds?.map((externalId, ordinal) => ({
                                      id: `external-${ordinal}`,
                                      ordinal,
                                      value: externalId.value,
                                      source: externalId.source,
                                  })),
                                  genres: item.genres?.map((name, ordinal) => ({
                                      id: `genre-${ordinal}`,
                                      ordinal,
                                      name,
                                  })),
                                  previews: item.previews.map((preview, ordinal) => ({
                                      id: `preview-${ordinal}`,
                                      ordinal,
                                      ...preview,
                                  })),
                                  images: item.images.map((image, ordinal) => ({ ...image, ordinal })),
                              },
                              contributions: [],
                              subjects: {},
                          }
                        : undefined,
                isLoading: mockMetadataInspectorState.isLoading,
                error:
                    mockMetadataInspectorState.error ??
                    (item || mockMetadataInspectorState.isLoading ? null : new ApiError(404, 'Not found')),
                refetch: mockMetadataInspectorRefetch,
            };
        });
    });

    describe('page structure', () => {
        it('renders the page with correct data-page attribute', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument();
            });
        });

        it('renders library link in breadcrumbs', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const libraryLinks = screen.getAllByRole('link', { name: /library/i });
                expect(libraryLinks.length).toBeGreaterThan(0);
                expect(libraryLinks[0]).toHaveAttribute('href', '/library');
            });
        });
    });

    describe('remove from library action', () => {
        const openPage = async () => {
            renderWithRouter('content-files');
            await waitFor(() => {
                expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument();
            });
        };

        it('shows the Remove action for a manager', async () => {
            await openPage();
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /^remove$/i })).toBeInTheDocument();
            });
        });

        it('hides the Remove action when the user lacks the Manager role', async () => {
            mockHasRole.current = (minRole: number) => minRole <= Role.Requester;
            await openPage();
            await waitFor(() => {
                expect(screen.getAllByRole('button', { name: /search now/i }).length).toBeGreaterThan(0);
            });
            expect(screen.queryByRole('button', { name: /^remove$/i })).not.toBeInTheDocument();
        });

        it('opens a confirmation dialog with the delete-files toggle off by default', async () => {
            const user = userEvent.setup();
            await openPage();
            await user.click(await screen.findByRole('button', { name: /^remove$/i }));

            const dialog = await screen.findByRole('dialog');
            expect(within(dialog).getByText(/Everything nested under it/i)).toBeInTheDocument();
            expect(within(dialog).getByText(/Download history is kept/i)).toBeInTheDocument();
            expect(within(dialog).getByText(/this cannot be undone/i)).toBeInTheDocument();
            const toggle = within(dialog).getByRole('switch', { name: /also delete files from disk/i });
            expect(toggle).toHaveAttribute('aria-checked', 'false');
            expect(mockRemoveContentMutate).not.toHaveBeenCalled();
        });

        it('removes with files kept when the toggle is left off', async () => {
            const user = userEvent.setup();
            await openPage();
            await user.click(await screen.findByRole('button', { name: /^remove$/i }));
            const dialog = await screen.findByRole('dialog');

            await user.click(within(dialog).getByRole('button', { name: /^delete$/i }));
            expect(mockRemoveContentMutate).toHaveBeenCalledTimes(1);
            expect(mockRemoveContentMutate.mock.calls[0][0]).toEqual({
                contentHierarchyItemId: 'content-files',
                deleteFiles: false,
            });
        });

        it('removes with files deleted when the toggle is on', async () => {
            const user = userEvent.setup();
            await openPage();
            await user.click(await screen.findByRole('button', { name: /^remove$/i }));
            const dialog = await screen.findByRole('dialog');

            await user.click(within(dialog).getByRole('switch', { name: /also delete files from disk/i }));
            await user.click(within(dialog).getByRole('button', { name: /^delete$/i }));
            expect(mockRemoveContentMutate).toHaveBeenCalledTimes(1);
            expect(mockRemoveContentMutate.mock.calls[0][0]).toEqual({
                contentHierarchyItemId: 'content-files',
                deleteFiles: true,
            });
        });

        it('surfaces a success toast and navigates back to the library on success', async () => {
            mockRemoveContentMutate.mockImplementation((_vars, opts) => {
                opts?.onSuccess?.();
            });
            const user = userEvent.setup();
            await openPage();
            await user.click(await screen.findByRole('button', { name: /^remove$/i }));
            const dialog = await screen.findByRole('dialog');
            await user.click(within(dialog).getByRole('button', { name: /^delete$/i }));

            await waitFor(() => {
                expect(screen.getAllByText(/Removed from library/i).length).toBeGreaterThan(0);
            });
            expect(
                screen.getAllByText('Magpie removed the item from the library. The files remain on disk.').length,
            ).toBeGreaterThan(0);
        });

        it('does not call the mutation when the dialog is cancelled', async () => {
            const user = userEvent.setup();
            await openPage();
            await user.click(await screen.findByRole('button', { name: /^remove$/i }));
            const dialog = await screen.findByRole('dialog');
            await user.click(within(dialog).getByRole('button', { name: /cancel/i }));
            expect(mockRemoveContentMutate).not.toHaveBeenCalled();
        });
    });

    describe('content-type breadcrumbs', () => {
        const breadcrumbList = () => {
            const nav = screen.getByRole('navigation', { name: /breadcrumb/i });
            return within(nav).getByRole('list');
        };

        it('shows a content-type crumb linking to the type landing for a movie', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                const typeLink = within(breadcrumbList()).getByRole('link', { name: /movie/i });
                expect(typeLink).toHaveAttribute('href', '/library/content/movie');
            });
        });

        it('omits a level crumb for a single-root content type', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(within(breadcrumbList()).getByRole('link', { name: /movie/i })).toBeInTheDocument();
            });
            expect(within(breadcrumbList()).queryByRole('link', { name: /^scenes?$/i })).not.toBeInTheDocument();
        });
    });

    describe('content display', () => {
        it('renders the content title', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Breaking Bad/i, level: 1 })).toBeInTheDocument();
            });
        });

        it('renders the content year', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('(2008)')).toBeInTheDocument();
            });
        });

        it('renders the publish date from the detail response in the aligned supporting rows', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByTestId('content-publish-date-row')).toHaveTextContent('Publish DateJan 20, 2008');
            });
            expect(document.querySelector('.content-details-header__publish-date')).not.toBeInTheDocument();
        });

        it('renders the content artwork', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByAltText('Breaking Bad artwork')).toBeInTheDocument();
            });
        });

        it('renders monitoring status in the unified settings block', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByTestId('content-policy-settings')).toBeInTheDocument();
                expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Monitored');
            });
            expect(document.querySelector('.content-details-header__monitoring')).not.toBeInTheDocument();
        });

        it('orders every supporting row on one shared label and value grid', async () => {
            renderWithRouter('content-1');

            const grid = await screen.findByRole('region', { name: 'Content supporting information' });
            await waitFor(() => {
                expect(within(grid).getByTestId('content-links-row')).toBeInTheDocument();
            });

            expect(
                Array.from(grid.querySelectorAll('.detail-info-row__label')).map((label) => label.textContent),
            ).toEqual(['Monitoring', 'Path', 'Root Folder', 'Quality', 'Tags', 'Publish Date', 'Links', 'Genres']);
        });

        it('omits the publish date row when no date is materialized', async () => {
            renderWithRouter('content-3');

            await screen.findByRole('heading', { name: /Dune: Part Two/i, level: 1 });
            expect(screen.queryByTestId('content-publish-date-row')).not.toBeInTheDocument();
        });

        it('renders the fulfillment status badge', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('Fulfilled')).toBeInTheDocument();
            });
        });

        it('renders the level badge by hierarchy level (show, not episode)', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByText('Show')).toBeInTheDocument();
            });
        });
    });

    describe('loading state', () => {
        it('shows loading skeleton while fetching', () => {
            mockUseContentHierarchy.mockReturnValue({
                content: { data: undefined, isLoading: true, error: null },
                parent: { data: undefined, isLoading: false, error: null },
                root: { data: undefined, isLoading: false, error: null },
            });

            renderWithRouter('content-1');
            expect(screen.getByTestId('content-detail-page__loading')).toBeInTheDocument();
        });

        it('preserves a conditional-tab deep link while metadata is loading', async () => {
            mockMetadataInspectorState.isLoading = true;

            renderWithRouter('content-trailers', undefined, undefined, '?tab=previews&view=grid');

            expect(screen.getByTestId('content-detail-page__loading')).toBeInTheDocument();
            await waitFor(() => {
                expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent(
                    '?tab=previews&view=grid',
                );
            });
        });
    });

    describe('action buttons', () => {
        it('renders the refresh button', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /refresh/i })).toBeInTheDocument();
            });
        });

        it('renders the search now button', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /search now/i })).toBeInTheDocument();
            });
        });

        it('opens interactive search from the content actions sheet', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await user.click(await screen.findByRole('button', { name: 'Content Actions' }));
            await user.click(await screen.findByRole('button', { name: 'Interactive Search' }));

            expect(await screen.findByTestId('interactive-search-route')).toBeInTheDocument();
        });

        it('renders the rescan media info button for an editor', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /rescan media info/i })).toBeInTheDocument();
            });
        });

        it('hides the rescan media info button when the user lacks the Manager role', async () => {
            mockHasRole.current = (minRole: number) => minRole <= Role.Requester;
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getAllByRole('button', { name: /search now/i }).length).toBeGreaterThan(0);
            });
            expect(screen.queryByRole('button', { name: /rescan media info/i })).not.toBeInTheDocument();
        });

        it('rescan media info triggers the per-item job', async () => {
            const mutate = vi.fn();
            mockUseRescanContentMediaInfo.mockReturnValue({ mutate, isPending: false });

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /rescan media info/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /rescan media info/i }));

            expect(mutate).toHaveBeenCalledTimes(1);
            expect(mutate.mock.calls[0][0]).toBe('content-1');
        });

        it('search now triggers a search-and-grab search', async () => {
            const mutate = vi.fn();
            mockUseSearchContentReleases.mockReturnValue({ mutate, isPending: false });

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /search now/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /search now/i }));

            expect(mutate).toHaveBeenCalledTimes(1);
            expect(mutate.mock.calls[0][0]).toEqual({ id: 'content-1' });
        });

        it('shows a "Search complete" toast when the immediate search finishes', async () => {
            const mutate = vi.fn((_vars: unknown, opts?: { onSuccess?: (disposition: unknown) => void }) =>
                opts?.onSuccess?.({ executionId: 'execution-1', status: 'Completed', statusMessage: null }),
            );
            mockUseSearchContentReleases.mockReturnValue({ mutate, isPending: false });

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /search now/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /search now/i }));

            await waitFor(() => {
                expect(screen.getByText('Search complete')).toBeInTheDocument();
            });
            expect(screen.getByRole('tab', { name: /seasons/i })).toHaveAttribute('aria-selected', 'true');
            expect(screen.getByRole('tab', { name: /releases/i })).toHaveAttribute('aria-selected', 'false');
        });

        it('shows the queued child search count reported by the search job', async () => {
            const mutate = vi.fn((_vars: unknown, opts?: { onSuccess?: (disposition: unknown) => void }) =>
                opts?.onSuccess?.({
                    executionId: 'execution-1',
                    status: 'Completed',
                    statusMessage: 'The search queued 18 child searches.',
                }),
            );
            mockUseSearchContentReleases.mockReturnValue({ mutate, isPending: false });

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /search now/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /search now/i }));

            await waitFor(() => {
                expect(screen.getByText('The search queued 18 child searches.')).toBeInTheDocument();
            });
        });

        it('does not register a queued search completion handler for immediate search', async () => {
            const mutate = vi.fn((_vars: unknown, opts?: { onSuccess?: (disposition: unknown) => void }) =>
                opts?.onSuccess?.({ executionId: 'execution-1', status: 'Completed', statusMessage: null }),
            );
            mockUseSearchContentReleases.mockReturnValue({ mutate, isPending: false });

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /search now/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /search now/i }));

            await waitFor(() => {
                expect(mockUseSearchContentReleases).toHaveBeenCalledWith(undefined);
            });
        });
    });

    describe('inline edit controls (content.edit gating)', () => {
        const waitForPage = async () => {
            await waitFor(() => {
                expect(document.querySelector('[data-page="content-detail"]')).toBeInTheDocument();
            });
        };

        it('renders the monitoring, root-folder, quality, and add-tag controls for a manager', async () => {
            mockHasRole.current = () => true;
            renderWithRouter('content-1');
            await waitForPage();

            expect(screen.getByTestId('monitoring-control__toggle')).toBeInTheDocument();
            expect(screen.getByTestId('root-folder-control__trigger')).toBeInTheDocument();
            expect(screen.getByTestId('quality-tier-control__trigger')).toBeInTheDocument();
            expect(screen.getByTestId('tags-chip-strip-add')).toBeInTheDocument();
        });

        it('hides every inline edit control from a viewer', async () => {
            mockHasRole.current = () => false;
            renderWithRouter('content-1');
            await waitForPage();

            expect(screen.getByTestId('monitoring-control__effective')).toBeInTheDocument();
            expect(screen.queryByTestId('monitoring-control__toggle')).not.toBeInTheDocument();
            expect(screen.queryByTestId('root-folder-control__trigger')).not.toBeInTheDocument();
            expect(screen.queryByTestId('quality-tier-control__trigger')).not.toBeInTheDocument();
            expect(screen.queryByTestId('tags-chip-strip-add')).not.toBeInTheDocument();
        });

        it('hides every inline edit control from a requester (content.edit is Manager)', async () => {
            mockHasRole.current = (minRole: number) => minRole <= Role.Requester;
            renderWithRouter('content-1');
            await waitForPage();

            expect(screen.getByTestId('monitoring-control__effective')).toBeInTheDocument();
            expect(screen.queryByTestId('monitoring-control__toggle')).not.toBeInTheDocument();
            expect(screen.queryByTestId('root-folder-control__trigger')).not.toBeInTheDocument();
            expect(screen.queryByTestId('quality-tier-control__trigger')).not.toBeInTheDocument();
            expect(screen.queryByTestId('tags-chip-strip-add')).not.toBeInTheDocument();
        });
    });

    describe('grab release', () => {
        const releaseRow = (overrides: Record<string, unknown> = {}) => ({
            id: 'release-1',
            title: 'Breaking.Bad.S01.1080p.BluRay.x264',
            protocol: 'torrent',
            publishDate: '2024-01-15T10:00:00Z',
            size: 5_368_709_120,
            isMatch: true,
            rejectionReason: null,
            ...overrides,
        });

        async function openReleasesTab(user: ReturnType<typeof userEvent.setup>) {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /releases/i })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: /releases/i }));
            await waitFor(() => {
                expect(screen.getAllByRole('button', { name: /grab/i }).length).toBeGreaterThan(0);
            });
        }

        it('grabs a matching release without prompting via the confirm dialog', async () => {
            const confirmSpy = vi.spyOn(window, 'confirm');
            mockContentReleaseRows.current = [releaseRow({ isMatch: true })];

            const user = userEvent.setup();
            await openReleasesTab(user);

            await user.click(screen.getAllByRole('button', { name: /grab/i })[0]);

            expect(confirmSpy).not.toHaveBeenCalled();
            expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
            expect(mockGrabMutate).toHaveBeenCalledWith(
                { id: 'release-1', contentHierarchyItemId: 'content-1' },
                expect.anything(),
            );
            confirmSpy.mockRestore();
        });

        it('submits only one mutation for rapid repeated activation of the same release', async () => {
            mockContentReleaseRows.current = [
                releaseRow({ id: 'release-1', isMatch: true }),
                releaseRow({ id: 'release-2', title: 'Breaking.Bad.S01.720p.WEB.x264', isMatch: true }),
            ];

            const user = userEvent.setup();
            await openReleasesTab(user);

            const releaseOneButton = screen
                .getAllByRole('button', { name: /grab/i })
                .find((button) => button.getAttribute('data-release-id') === 'release-1');
            expect(releaseOneButton).toBeDefined();

            act(() => {
                releaseOneButton!.click();
                releaseOneButton!.click();
            });

            expect(mockGrabMutate).toHaveBeenCalledTimes(1);
            expect(releaseOneButton).toBeDisabled();
            expect(releaseOneButton).toHaveAttribute('aria-busy', 'true');
            expect(releaseOneButton).toHaveTextContent('Grabbing...');

            const releaseTwoButton = screen
                .getAllByRole('button', { name: /grab/i })
                .find((button) => button.getAttribute('data-release-id') === 'release-2');
            expect(releaseTwoButton).toBeEnabled();
        });

        it('shows the release as queued after a successful grab submission', async () => {
            mockContentReleaseRows.current = [releaseRow({ isMatch: true })];
            mockGrabMutate.mockImplementation((_variables, options) => {
                options.onSuccess();
            });

            const user = userEvent.setup();
            await openReleasesTab(user);
            await user.click(screen.getAllByRole('button', { name: /grab/i })[0]);

            expect(screen.getAllByText('Queued').length).toBeGreaterThan(0);
            expect(document.querySelector('button[data-action="grab"][data-release-id="release-1"]')).toBeNull();
        });

        it('keeps a successfully retried failed release queued while its previous status is stale', async () => {
            mockContentReleaseRows.current = [
                releaseRow({ isMatch: true, acquisitionStatus: 'Failed', failureReason: 'DownloadStuck' }),
            ];
            mockGrabMutate.mockImplementation((_variables, options) => {
                options.onSuccess();
            });

            const user = userEvent.setup();
            await openReleasesTab(user);
            await user.click(screen.getAllByRole('button', { name: /grab/i })[0]);

            expect(screen.getAllByText('Queued').length).toBeGreaterThan(0);
            expect(document.querySelector('button[data-action="grab"][data-release-id="release-1"]')).toBeNull();
        });

        it('restores Grab and shows the existing feedback after a failed submission', async () => {
            mockContentReleaseRows.current = [releaseRow({ isMatch: true })];
            mockGrabMutate.mockImplementation((_variables, options) => {
                options.onError(new Error('The download client timed out'));
            });

            const user = userEvent.setup();
            await openReleasesTab(user);
            await user.click(screen.getAllByRole('button', { name: /grab/i })[0]);

            expect(screen.getAllByRole('button', { name: /^grab$/i })[0]).toBeEnabled();
            expect(await screen.findByText('Grab failed')).toBeInTheDocument();
            expect(screen.getByText('The download client timed out')).toBeInTheDocument();
            expect(screen.queryByText('No download client configured')).not.toBeInTheDocument();
        });

        it('shows the app confirm dialog for a non-match and proceeds when confirmed', async () => {
            const confirmSpy = vi.spyOn(window, 'confirm');
            mockContentReleaseRows.current = [
                releaseRow({ isMatch: false, rejectionReason: 'Wrong year (2019 vs 2008)' }),
            ];

            const user = userEvent.setup();
            await openReleasesTab(user);

            await user.click(screen.getAllByRole('button', { name: /grab/i })[0]);

            expect(confirmSpy).not.toHaveBeenCalled();
            const dialog = await screen.findByRole('dialog');
            expect(within(dialog).getByText(/Wrong year \(2019 vs 2008\)/i)).toBeInTheDocument();
            expect(mockGrabMutate).not.toHaveBeenCalled();

            await user.click(within(dialog).getByRole('button', { name: /grab anyway/i }));
            expect(mockGrabMutate).toHaveBeenCalledWith(
                { id: 'release-1', contentHierarchyItemId: 'content-1' },
                expect.anything(),
            );
            confirmSpy.mockRestore();
        });

        it('aborts the grab when the non-match confirm dialog is cancelled', async () => {
            const confirmSpy = vi.spyOn(window, 'confirm');
            mockContentReleaseRows.current = [releaseRow({ isMatch: false, rejectionReason: 'Wrong edition' })];

            const user = userEvent.setup();
            await openReleasesTab(user);

            await user.click(screen.getAllByRole('button', { name: /grab/i })[0]);

            const dialog = await screen.findByRole('dialog');
            await user.click(within(dialog).getByRole('button', { name: /cancel/i }));

            expect(confirmSpy).not.toHaveBeenCalled();
            expect(mockGrabMutate).not.toHaveBeenCalled();
            confirmSpy.mockRestore();
        });
    });

    describe('release download details', () => {
        const grabbedRelease = {
            id: 'release-1',
            title: 'Breaking.Bad.S01.1080p.BluRay.x264',
            indexerName: 'Court Ordered Smoothie',
            protocol: 'torrent',
            externalId: 'torrent-42',
            categories: ['TV/HD'],
            languages: ['English'],
            publishDate: '2024-01-15T10:00:00Z',
            size: 5_368_709_120,
            properties: '{"seeders":"10","leechers":"1"}',
            isMatch: true,
            rejectionReason: null,
            acquisitionStatus: 'Downloading',
            failureReason: null,
        };

        const latestDownload = {
            id: 'dl-1',
            releaseId: 'release-1',
            downloadClientId: 'client-1',
            downloadClientName: 'SABnzbd',
            externalId: 'sab_123',
            title: 'Breaking.Bad.S01.1080p.BluRay.x264',
            status: 'Downloading',
            progress: 42,
            createdAt: '2024-01-16T10:00:00Z',
        };

        async function openReleases(user: ReturnType<typeof userEvent.setup>) {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /releases/i })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: /releases/i }));
        }

        it('shows the release summary, download and indexer properties as sections in one dialog', async () => {
            mockContentReleaseRows.current = [grabbedRelease];
            mockLatestDownload.current = latestDownload;

            const user = userEvent.setup();
            await openReleases(user);

            const titleButtons = await screen.findAllByRole('button', { name: /Breaking\.Bad\.S01/i });
            await user.click(titleButtons[0]);

            const releaseDialog = await screen.findByTestId('release-details-dialog');
            expect(within(releaseDialog).getByText('Court Ordered Smoothie')).toBeInTheDocument();
            expect(within(releaseDialog).getByText('torrent-42')).toBeInTheDocument();
            expect(within(releaseDialog).getByText('TV/HD')).toBeInTheDocument();
            expect(within(releaseDialog).getByText('seeders')).toBeInTheDocument();
            expect(within(releaseDialog).getByText('10')).toBeInTheDocument();
            expect(within(releaseDialog).getByText('Downloading')).toBeInTheDocument();
            expect(within(releaseDialog).getByText('sab_123')).toBeInTheDocument();
            expect(releaseDialog.querySelector('[data-component="release-download"]')).not.toBeNull();
            expect(screen.queryByTestId('download-details-dialog')).not.toBeInTheDocument();
        });

        it('shows a loading download section while the latest download is still resolving', async () => {
            mockContentReleaseRows.current = [grabbedRelease];
            mockLatestDownload.current = null;
            mockLatestDownloadLoading.current = true;

            const user = userEvent.setup();
            await openReleases(user);

            const titleButtons = await screen.findAllByRole('button', { name: /Breaking\.Bad\.S01/i });
            await user.click(titleButtons[0]);

            const releaseDialog = await screen.findByTestId('release-details-dialog');
            expect(releaseDialog.querySelector('[data-component="release-download-loading"]')).not.toBeNull();
            expect(releaseDialog.querySelector('[data-component="release-download"]')).toBeNull();
        });

        it('omits the download section when the release has no backing download (retention orphan)', async () => {
            mockContentReleaseRows.current = [grabbedRelease];
            mockLatestDownload.current = null;
            mockLatestDownloadLoading.current = false;

            const user = userEvent.setup();
            await openReleases(user);

            const titleButtons = await screen.findAllByRole('button', { name: /Breaking\.Bad\.S01/i });
            await user.click(titleButtons[0]);

            const releaseDialog = await screen.findByTestId('release-details-dialog');
            expect(releaseDialog.querySelector('[data-component="release-download"]')).toBeNull();
            expect(releaseDialog.querySelector('[data-component="release-download-loading"]')).toBeNull();
            expect(within(releaseDialog).getByText('Court Ordered Smoothie')).toBeInTheDocument();
        });

        it('opens release details with no download section for an ungrabbed release', async () => {
            mockContentReleaseRows.current = [{ ...grabbedRelease, acquisitionStatus: null }];

            const user = userEvent.setup();
            await openReleases(user);

            const titleButtons = await screen.findAllByRole('button', { name: /Breaking\.Bad\.S01/i });
            await user.click(titleButtons[0]);

            const dialog = await screen.findByTestId('release-details-dialog');
            expect(within(dialog).getByText('leechers')).toBeInTheDocument();
            expect(dialog.querySelector('[data-component="release-download"]')).toBeNull();
            expect(screen.queryByTestId('download-details-dialog')).not.toBeInTheDocument();
        });

        it('does not render a content-scoped Downloads tab (latest state lives on the release row)', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /releases/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /^downloads$/i })).not.toBeInTheDocument();
        });
    });

    describe('tabs', () => {
        it('renders the Related tab first for a leaf item', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /related/i })).toBeInTheDocument();
            });
            const tabs = screen.getAllByRole('tab');
            expect(tabs[0]).toHaveAccessibleName(/related/i);
            expect(tabs[1]).toHaveAccessibleName(/details/i);
            expect(screen.queryByRole('tab', { name: /cast & crew/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: /overview/i })).not.toBeInTheDocument();
        });

        it('keeps Details reachable via ?tab=details', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');
            await waitFor(() => {
                const detailsTab = screen.getByRole('tab', { name: /details/i });
                expect(detailsTab).toHaveAttribute('aria-selected', 'true');
            });
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="content-facts"]')).toBeInTheDocument();
        });

        it('renders the Files tab', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /files/i })).toBeInTheDocument();
            });
        });

        it('renders the Activity tab', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /activity/i })).toBeInTheDocument();
            });
        });

        it('does not render a redundant History tab', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /activity/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /^history$/i })).not.toBeInTheDocument();
        });

        it('drops the Release History tab (folded into Activity)', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /activity/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /release history/i })).not.toBeInTheDocument();
        });

        it('gives every tab an accessible name so the icon-only tier keeps a label', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /activity/i })).toBeInTheDocument();
            });
            for (const tab of screen.getAllByRole('tab')) {
                expect(tab).toHaveAttribute('aria-label', expect.stringMatching(/\S/));
            }
        });

        it('selects the Related tab by default for a leaf item (no ?tab= param)', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: /related/i });
                expect(relatedTab).toHaveAttribute('aria-selected', 'true');
            });
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="related-subjects-tab"]')).toBeInTheDocument();
        });

        it('switches to Files tab when clicked', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /files/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /files/i }));

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText(/no files attached/i).length).toBeGreaterThan(0);
        });

        it('opens the host-file picker at the effective path within the configured root', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Import file' }));

            const dialog = screen.getByRole('dialog', { name: 'Import file' });
            expect(dialog).toHaveAttribute('data-initial-path', '/media/library/The Matrix');
            expect(dialog).toHaveAttribute('data-root-path', '/media/library');
            expect(dialog).toHaveAttribute('data-endpoint', '/system/files?scope=content-files');
            expect(mockFileBrowserLocationRefetch).toHaveBeenCalledTimes(1);
        });

        it('refetches the browser location every time the host-file picker opens', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Import file' }));
            await user.click(screen.getByRole('button', { name: 'Select host file' }));
            await user.click(screen.getByRole('button', { name: 'Import file' }));

            expect(mockFileBrowserLocationRefetch).toHaveBeenCalledTimes(2);
        });

        it('fetches the browser location before opening the host-file picker when it is not cached', async () => {
            const user = userEvent.setup();
            mockFileBrowserLocation.current = undefined;
            mockFileBrowserLocationRefetch.mockImplementation(async () => {
                mockFileBrowserLocation.current = {
                    rootPath: '/media/library',
                    initialPath: '/media/library/The Matrix',
                };
                return { data: mockFileBrowserLocation.current, error: null, isError: false };
            });
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Import file' }));

            expect(mockFileBrowserLocationRefetch).toHaveBeenCalledTimes(1);
            expect(await screen.findByRole('dialog', { name: 'Import file' })).toBeInTheDocument();
        });

        it('shows an error when the browser location cannot be fetched', async () => {
            const user = userEvent.setup();
            mockFileBrowserLocation.current = undefined;
            mockFileBrowserLocationRefetch.mockResolvedValue({
                data: undefined,
                error: new ApiError(503, 'Browser offline'),
                isError: true,
            });
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Import file' }));

            expect(await screen.findByText('File browser unavailable')).toBeInTheDocument();
            expect(screen.getByText('Browser offline')).toBeInTheDocument();
        });

        it('does not open the host-file picker with stale data after a failed refresh', async () => {
            const user = userEvent.setup();
            mockFileBrowserLocationRefetch.mockResolvedValue({
                data: mockFileBrowserLocation.current,
                error: new ApiError(503, 'Browser offline'),
                isError: true,
            });
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Import file' }));

            expect(await screen.findByText('File browser unavailable')).toBeInTheDocument();
            expect(screen.queryByRole('dialog', { name: 'Import file' })).not.toBeInTheDocument();
        });

        it('attaches the selected host file and identifies primary content', async () => {
            const user = userEvent.setup();
            mockAttachFileMutate.mockImplementation((_variables, options) => {
                options.onSuccess({
                    mediaFile: { id: 'new-file', filePath: '/media/library/The Matrix/new.mkv' },
                    isPrimary: true,
                });
            });
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Import file' }));
            await user.click(screen.getByRole('button', { name: 'Select host file' }));

            expect(mockAttachFileMutate).toHaveBeenCalledWith(
                {
                    contentHierarchyItemId: 'content-files',
                    filePath: '/media/library/The Matrix/new.mkv',
                },
                expect.any(Object),
            );
            expect(await screen.findByText('The file is attached as primary content.')).toBeInTheDocument();
        });

        it('confirms that unlink keeps the host file before removing the association', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Unlink the-matrix.mkv' }));

            const dialog = screen.getByRole('dialog');
            expect(within(dialog).getByText(/removes only the association/i)).toBeInTheDocument();
            expect(within(dialog).getByText(/host file stays on disk/i)).toBeInTheDocument();
            await user.click(within(dialog).getByRole('button', { name: 'Unlink' }));
            expect(mockUnlinkFileMutate).toHaveBeenCalledWith(
                { contentHierarchyItemId: 'content-files', mediaFileId: 'media-file-1' },
                expect.any(Object),
            );
        });

        it('keeps the unlink dialog open and shows the server error after failure', async () => {
            const user = userEvent.setup();
            mockUnlinkFileMutate.mockImplementation((_variables, options) => {
                options.onError(new Error('The association could not be removed.'));
            });
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Unlink the-matrix.mkv' }));
            await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Unlink' }));

            expect(screen.getByRole('dialog')).toBeInTheDocument();
            expect(await screen.findByText('The association could not be removed.')).toBeInTheDocument();
        });

        it('blocks repeated unlink submissions while the request is pending', async () => {
            const user = userEvent.setup();
            mockUnlinkFilePending.current = true;
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Unlink the-matrix.mkv' }));

            expect(within(screen.getByRole('dialog')).getByRole('button', { name: 'Unlinking...' })).toBeDisabled();
            expect(document.querySelector('[data-action="unlink-file"]')).toBeDisabled();
            expect(document.querySelector('[data-action="delete-file"]')).toBeDisabled();
            expect(mockUnlinkFileMutate).not.toHaveBeenCalled();
        });

        it('hides file mutation actions from users below Manager', async () => {
            mockHasRole.current = () => false;
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await screen.findByText('the-matrix.mkv');
            expect(screen.queryByRole('button', { name: 'Import file' })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Unlink the-matrix.mkv' })).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: 'Delete the-matrix.mkv' })).not.toBeInTheDocument();
        });

        it('does not claim a disk outcome the 204 response cannot confirm after a file delete', async () => {
            mockDeleteFileMutate.mockImplementation((_vars, opts) => {
                opts?.onSuccess?.();
            });
            const user = userEvent.setup();
            renderWithRouter('content-files', undefined, undefined, '?tab=files');

            await user.click(await screen.findByRole('button', { name: 'Delete the-matrix.mkv' }));
            const dialog = screen.getByRole('dialog');
            await user.click(within(dialog).getByRole('button', { name: /^delete$/i }));

            expect(await screen.findByText('Deleted "the-matrix.mkv".')).toBeInTheDocument();
        });

        it('switches to Activity tab when clicked', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /activity/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('tab', { name: /activity/i }));

            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="activity-tab"]')).toBeInTheDocument();
        });

        it('restores active tab from ?tab= search param', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=files');

            await waitFor(() => {
                const filesTab = screen.getByRole('tab', { name: /files/i });
                expect(filesTab).toHaveAttribute('aria-selected', 'true');
            });

            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText(/no files attached/i).length).toBeGreaterThan(0);
        });

        it('retains a valid deep link and unrelated query parameters', async () => {
            renderWithRouter('content-files', undefined, undefined, '?tab=files&view=grid');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /files/i })).toHaveAttribute('aria-selected', 'true');
            });

            expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent(
                '?tab=files&view=grid',
            );
        });

        it('opens a metadata-backed Previews deep link without rewriting it', async () => {
            renderWithRouter('content-trailers', undefined, undefined, '?tab=previews&view=grid');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /previews/i })).toHaveAttribute('aria-selected', 'true');
            });
            expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent(
                '?tab=previews&view=grid',
            );
        });

        it('falls back to the default tab for ?tab=children on a leaf item', async () => {
            renderWithRouter('content-3', undefined, undefined, '?tab=children&view=grid');

            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: /related/i });
                expect(relatedTab).toHaveAttribute('aria-selected', 'true');
                expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent('?view=grid');
            });

            expect(screen.queryByRole('tab', { name: /season/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: /episode/i })).not.toBeInTheDocument();
            const tabPanel = screen.getByRole('tabpanel');
            expect(tabPanel.querySelector('[data-component="related-subjects-tab"]')).toBeInTheDocument();
        });

        it('removes an explicit empty tab while preserving unrelated query parameters', async () => {
            renderWithRouter('content-3', undefined, undefined, '?tab=&view=grid');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /related/i })).toHaveAttribute('aria-selected', 'true');
                expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent('?view=grid');
            });
        });

        it('does not canonicalize an invalid tab until extension tabs are ready', async () => {
            mockUseSlotExtensions.mockReturnValue({ extensions: [], isReady: false, isLoading: true });

            renderWithRouter('content-3', undefined, undefined, '?tab=diagnostics&view=grid');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /related/i })).toHaveAttribute('aria-selected', 'true');
            });

            expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent(
                '?tab=diagnostics&view=grid',
            );
        });

        it('renders the strip as the single tab picker with no dropdown mirror', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tablist')).toBeInTheDocument();
            });
            expect(screen.queryByTestId('content-detail-page__tab-select')).not.toBeInTheDocument();
            expect(screen.queryByRole('combobox', { name: /select tab/i })).not.toBeInTheDocument();
            const tablist = screen.getByRole('tablist');
            expect(within(tablist).getAllByRole('tab').length).toBeGreaterThan(1);
        });

        it('defaults to the Related tab for a leaf item when ?tab= param is absent', async () => {
            renderWithRouter('content-3');

            await waitFor(() => {
                const relatedTab = screen.getByRole('tab', { name: /related/i });
                expect(relatedTab).toHaveAttribute('aria-selected', 'true');
            });
        });

        it('renders the Details tab content (facts) inside a tabpanel, not above the tabs', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');
            await waitFor(() => {
                expect(document.querySelector('[data-component="content-facts"]')).toBeInTheDocument();
            });
            const facts = document.querySelector('[data-component="content-facts"]') as HTMLElement;
            expect(facts.closest('[role="tabpanel"]')).not.toBeNull();
            expect(within(facts).getByText('Ended')).toBeInTheDocument();
            expect(
                within(screen.getByRole('region', { name: 'Content supporting information' })).queryByText('Ended'),
            ).not.toBeInTheDocument();
            expect(within(facts).getByText('tt0903747')).toBeInTheDocument();
            expect(within(facts).queryByText('Drama')).not.toBeInTheDocument();
        });

        it('renders genres as a header-level strip above the tabs', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(document.querySelector('[data-component="content-genres"]')).toBeInTheDocument();
            });
            const strip = document.querySelector('[data-component="content-genres"]') as HTMLElement;
            expect(strip.closest('[role="tabpanel"]')).toBeNull();
            expect(within(strip).getByText('Drama')).toBeInTheDocument();
            expect(within(strip).getByText('Crime')).toBeInTheDocument();
        });
    });

    describe('children tab visibility', () => {
        it.each([
            ['Season', 'Seasons'],
            ['Category', 'Categories'],
            ['Items', 'Items'],
            ['  ', 'Items'],
        ])('renders plural collection labels for %s', async (displayName, expectedLabel) => {
            // Arrange
            const mockLibraryTypes = vi.mocked(useLibraryTypes);
            const libraryTypes = mockLibraryTypes();
            const definition = libraryTypes.contentTypes[1];
            const levels = [
                { ...definition.levels[0], displayName: 'Collection' },
                { ...definition.levels[1], displayName },
                { ...definition.levels[0], slug: 'other-root', displayName: 'Other root', parentSlug: null },
            ];
            await vi.mocked(useLibraryTypes).withImplementation(
                () => ({ ...libraryTypes, contentTypes: [{ ...definition, levels }] }),
                async () => {
                    // Act
                    renderWithRouter('content-1');
                    // Assert
                    expect(await screen.findByRole('tab', { name: `${expectedLabel} (0)` })).toBeInTheDocument();
                    const breadcrumbs = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole(
                        'list',
                    );
                    expect(within(breadcrumbs).getByRole('link', { name: 'Collections' })).toHaveAttribute(
                        'href',
                        '/library/content/tv/show',
                    );
                },
            );
        });

        it('discloses a truncated child list while keeping the true total', async () => {
            // Arrange
            mockUseContentList.mockReturnValue({
                data: { items: [], pageNumber: 1, pageSize: 1000, totalItems: 1500 },
                isLoading: false,
                error: null,
            });
            // Act
            renderWithRouter('content-1');
            // Assert
            expect(await screen.findByText(/This tab shows 0 of 1500 items/)).toBeInTheDocument();
            expect(screen.getByRole('tab', { name: /seasons/i }).querySelector('.tab-count')).toHaveTextContent('1500');
        });

        it('shows Seasons tab when content has children', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
        });

        it('shows the api total on the sub-items tab when that total is zero', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });

            const seasonsTab = screen.getByRole('tab', { name: /seasons/i });
            expect(seasonsTab.querySelector('.tab-count')).toHaveTextContent('0');
        });

        it('loads children through the shared content query sorted by sequence number ascending', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });

            expect(mockUseContentList).toHaveBeenCalledWith(
                expect.objectContaining({
                    parentId: 'content-1',
                    sortBy: 'SequenceNumber',
                    sortDirection: 'Ascending',
                }),
                expect.anything(),
            );
        });

        it('places the sub-items tab first then Related, defaulting to the sub-items tab, when children exist', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
            const tabs = screen.getAllByRole('tab');
            expect(tabs[0]).toHaveAccessibleName(/seasons/i);
            expect(tabs[1]).toHaveAccessibleName(/related/i);
            expect(tabs[2]).toHaveAccessibleName(/details/i);
            expect(screen.getByRole('tab', { name: /seasons/i })).toHaveAttribute('aria-selected', 'true');
            expect(screen.getByRole('tab', { name: /related/i })).toHaveAttribute('aria-selected', 'false');
        });

        it('lands on the sub-items panel by default when children exist', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toHaveAttribute('aria-selected', 'true');
            });
            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText('Season 1').length).toBeGreaterThan(0);
        });

        it('lets ?tab=related select Related over the sub-items default when children exist', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1', undefined, undefined, '?tab=related');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /related/i })).toHaveAttribute('aria-selected', 'true');
            });
            expect(screen.getByRole('tab', { name: /seasons/i })).toHaveAttribute('aria-selected', 'false');
        });

        it('keeps Details reachable via ?tab=details when children exist', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1', undefined, undefined, '?tab=details');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /details/i })).toHaveAttribute('aria-selected', 'true');
            });
        });

        it('labels the sub-items tab from the child level in the registry, ignoring materialized child labels', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            level: 'season',
                            levelLabel: 'Wrong Label',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /wrong label/i })).not.toBeInTheDocument();
        });

        it('carries the child posterUrl into the children tab so artwork renders', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            posterUrl: 'https://example.com/season1.jpg',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            const user = userEvent.setup();
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: /seasons/i }));

            const artwork = await screen.findAllByAltText('Season 1 artwork');
            expect(artwork.length).toBeGreaterThan(0);
            artwork.forEach((image) => {
                expect(image).toHaveAttribute('src', 'https://example.com/season1.jpg');
            });
        });

        it('shows each child publish date in the date column', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'season-1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                            publishDate: '2020-03-15T00:00:00Z',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            const user = userEvent.setup();
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: /seasons/i }));

            const matches = await screen.findAllByText('Mar 15, 2020');
            expect(matches.length).toBeGreaterThan(0);
            expect(screen.queryByText('-')).not.toBeInTheDocument();
        });

        it('shows the children tab for a non-leaf item even when no children are materialized', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
            expect(screen.getByRole('tab', { name: /seasons/i })).toHaveAttribute('aria-selected', 'true');
            const tabPanel = screen.getByRole('tabpanel');
            expect(within(tabPanel).getAllByText(/no seasons found/i).length).toBeGreaterThan(0);
        });

        it('hides children tab for movies even if children data is empty', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /related/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /season/i })).not.toBeInTheDocument();
            expect(screen.queryByRole('tab', { name: /episode/i })).not.toBeInTheDocument();
        });

        it('shows Episodes tab when viewing a season with children', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 'episode-1',
                            title: 'Pilot',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 1,
                },
                isLoading: false,
                error: null,
            });

            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /episode/i })).toBeInTheDocument();
            });
        });

        it('numbers children by their real sequence number and preserves the API order', async () => {
            mockUseContentList.mockReturnValue({
                data: {
                    items: [
                        {
                            id: 's1',
                            title: 'Season 1',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                            sequenceNumber: 1,
                        },
                        {
                            id: 's2',
                            title: 'Season 2',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                            sequenceNumber: 2,
                        },
                        {
                            id: 's10',
                            title: 'Season 10',
                            monitoring: true,
                            effectiveMonitoring: true,
                            fulfillment: 'fulfilled',
                            sequenceNumber: 10,
                        },
                    ],
                    pageNumber: 1,
                    pageSize: 25,
                    totalItems: 3,
                },
                isLoading: false,
                error: null,
            });

            const user = userEvent.setup();
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /seasons/i })).toBeInTheDocument();
            });
            await user.click(screen.getByRole('tab', { name: /seasons/i }));

            const mobileList = await waitFor(() => {
                const list = document.querySelector('.children-tab__mobile-list');
                expect(list).toBeInTheDocument();
                return list as HTMLElement;
            });

            const titles = Array.from(mobileList.querySelectorAll('.children-tab__title')).map((el) =>
                el.textContent?.replace(/\s+/g, ' ').trim(),
            );
            expect(titles).toEqual(['1.Season 1', '2.Season 2', '10.Season 10']);
        });
    });

    describe('facts in the Details tab', () => {
        it('renders the synopsis once, in the title card (not behind a tab)', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Breaking Bad/i })).toBeInTheDocument();
            });
            const synopsis = screen.getAllByText(/chemistry teacher/i);
            expect(synopsis).toHaveLength(1);
            const header = screen.getByRole('banner');
            expect(within(header).getByText(/chemistry teacher/i)).toBeInTheDocument();
        });

        it('renders details and external ids inside the Details tab (not genres)', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');
            await waitFor(() => {
                expect(document.querySelector('[data-component="content-facts"]')).toBeInTheDocument();
            });
            const facts = document.querySelector('[data-component="content-facts"]') as HTMLElement;

            expect(facts.closest('[role="tabpanel"]')).not.toBeNull();
            expect(within(facts).getByText('Ended')).toBeInTheDocument();
            expect(
                within(screen.getByRole('region', { name: 'Content supporting information' })).queryByText('Ended'),
            ).not.toBeInTheDocument();
            expect(within(facts).getByText('tt0903747')).toBeInTheDocument();
            expect(within(facts).queryByText('Drama')).not.toBeInTheDocument();
            expect(within(facts).queryByText('Crime')).not.toBeInTheDocument();
        });

        it('renders the exact title, alternate-text, and declared custom rows', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');
            const details = await waitFor(() => screen.getByRole('region', { name: 'Details' }));

            expect(
                within(details)
                    .getAllByRole('term')
                    .map((node) => node.textContent),
            ).toEqual([
                'Original title',
                'Sort title',
                'Language',
                'Duration',
                'Sequence number',
                'Production status',
                'Established',
                'Dissolved',
                'Birth date',
                'Death date',
                'Network',
            ]);
            expect(
                within(screen.getByRole('heading', { name: 'Alternate texts' }).closest('section') as HTMLElement)
                    .getAllByRole('term')
                    .map((node) => node.textContent),
            ).toEqual(['Descriptionfr', 'Titlees']);
            expect(
                within(details)
                    .getAllByRole('definition')
                    .map((node) => node.textContent),
            ).toEqual([
                'BrBa',
                'Breaking Bad, The',
                'en',
                '1 h 42 m',
                '7',
                'Ended',
                'Jan 2, 2000',
                'Feb 3, 2001',
                'Mar 7, 1956',
                'Dec 1, 2020',
                'AMC',
            ]);
            expect(
                within(screen.getByRole('heading', { name: 'Alternate texts' }).closest('section') as HTMLElement)
                    .getAllByRole('definition')
                    .map((node) => node.textContent),
            ).toEqual(['Chimie', 'Los Pollos']);
            expect(within(details).queryByText('hidden')).not.toBeInTheDocument();
        });

        it('renders grouped awards under the overview in materialized order', async () => {
            renderWithRouter('content-1');
            const awards = await waitFor(() => {
                const node = document.querySelector('[data-component="content-awards-panel"]');
                expect(node).toBeInTheDocument();
                return node as HTMLElement;
            });

            expect(awards.closest('header')).toBe(screen.getByRole('banner'));
            expect(
                within(awards)
                    .getAllByRole('term')
                    .map((node) => node.textContent),
            ).toEqual(['Emmy Awards', 'Critics Circle']);
            expect(Array.from(awards.querySelectorAll('.awards-panel__entry')).map((node) => node.textContent)).toEqual(
                [
                    'Outstanding Drama Series 2013, 2014 (nominated)',
                    'Ensemble Prize · Repertory',
                    'Breakthrough Performance 2009 (finalist)',
                ],
            );
        });

        it('appends every content rating to the header badge row', async () => {
            renderWithRouter('content-1');

            const header = await screen.findByRole('banner');
            expect(
                Array.from(header.querySelectorAll('.content-details-header__badges > .badge')).map(
                    (badge) => badge.textContent,
                ),
            ).toEqual(['Show', 'Fulfilled']);
            expect(
                Array.from(header.querySelectorAll('.content-details-header__rating')).map((chip) => chip.textContent),
            ).toEqual(['MPAATV-MA', 'BBFC15']);
        });

        it('formats the materialized duration for reading', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');

            expect(await screen.findByText('1 h 42 m')).toBeInTheDocument();
        });

        it('keeps internal external IDs out of Links and Details', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');

            await screen.findByText('tt0903747');
            expect(screen.queryByText('technical-id')).not.toBeInTheDocument();
        });
    });

    describe('previews', () => {
        it('plays the first provider-ordered preview in the header when previews exist', async () => {
            renderWithRouter('content-trailers');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Gladiator/i })).toBeInTheDocument();
            });
            const header = screen.getByRole('banner');
            expect(within(header).getByTestId('content-details-header__poster--preview')).toBeInTheDocument();
            expect(within(header).getByRole('button', { name: 'Play Teaser' })).toBeInTheDocument();
        });

        it('shows the Previews (N) tab when previews exist', async () => {
            renderWithRouter('content-trailers');
            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /previews/i })).toBeInTheDocument();
            });
            const previewsTab = screen.getByRole('tab', { name: /previews/i });
            expect(previewsTab).toHaveTextContent('2');
        });

        it('omits the Previews tab and poster preview when no previews exist', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Dune: Part Two/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /previews/i })).not.toBeInTheDocument();
            expect(screen.queryByTestId('content-details-header__poster--preview')).not.toBeInTheDocument();
            expect(screen.getByAltText('Dune: Part Two artwork')).toBeInTheDocument();
        });
    });

    describe('images', () => {
        it('shows the Images (N) tab and renders the gallery when images exist', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-trailers');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Gladiator/i })).toBeInTheDocument();
            });

            const imagesTab = screen.getByRole('tab', { name: /images/i });
            expect(imagesTab).toBeInTheDocument();
            expect(imagesTab).toHaveTextContent('2');

            await user.click(imagesTab);
            const panel = await screen.findByRole('tabpanel', { name: /images/i });
            expect(within(panel).getByAltText('Gladiator poster')).toBeInTheDocument();
            expect(within(panel).getByAltText('Gladiator backdrop')).toBeInTheDocument();
        });

        it('omits the Images tab when the item carries no images', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Dune: Part Two/i })).toBeInTheDocument();
            });
            expect(screen.queryByRole('tab', { name: /images/i })).not.toBeInTheDocument();
        });
    });

    describe('different content items', () => {
        it('renders movie content correctly', async () => {
            renderWithRouter('content-3');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Dune: Part Two/i })).toBeInTheDocument();
                const header = screen.getByRole('banner');
                expect(within(header).getByText('Movie')).toBeInTheDocument();
                expect(within(header).getByText('Missing')).toBeInTheDocument();
            });
        });

        it('renders unmonitored content correctly', async () => {
            renderWithRouter('content-5');
            await waitFor(() => {
                expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Not monitored');
            });
            expect(within(screen.getByRole('banner')).queryByText('Not monitored')).not.toBeInTheDocument();
        });

        it('renders no fulfillment badge for an unmonitored item', async () => {
            renderWithRouter('content-8');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Poor Things/i })).toBeInTheDocument();
            });
            const header = screen.getByRole('banner');
            expect(screen.getByTestId('monitoring-control__effective')).toHaveTextContent('Not monitored');
            expect(within(header).queryByText('External')).not.toBeInTheDocument();
        });

        it('renders content without poster', async () => {
            renderWithRouter('content-9');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Fallout/i })).toBeInTheDocument();
                expect(document.querySelector('.content-details-header__poster--placeholder')).toBeInTheDocument();
            });
        });
    });

    describe('not found state', () => {
        it('shows not found message for unknown content ID', async () => {
            renderWithRouter('nonexistent-id', undefined, undefined, '?tab=diagnostics&view=grid');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Content Not Found/i })).toBeInTheDocument();
                expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
            });
            expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent(
                '?tab=diagnostics&view=grid',
            );
        });

        it('shows back to library link on not found page', async () => {
            renderWithRouter('nonexistent-id');
            await waitFor(() => {
                const backLink = screen.getByRole('link', { name: /library/i });
                expect(backLink).toBeInTheDocument();
                expect(backLink).toHaveAttribute('href', '/library');
            });
        });
    });

    describe('error state (non-404)', () => {
        const errorHierarchy = (contentOverride: Record<string, unknown>) => ({
            content: contentOverride,
            parent: { data: undefined, isLoading: false, error: null, refetch: vi.fn() },
            root: { data: undefined, isLoading: false, error: null, refetch: vi.fn() },
        });

        it('shows failed to load message for server errors', async () => {
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: new ApiError(500, 'Internal Server Error'),
                    refetch: vi.fn(),
                }),
            );

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Failed to Load Content/i })).toBeInTheDocument();
                expect(screen.getByText(/error occurred while loading/i)).toBeInTheDocument();
            });
        });

        it('shows a retryable failure for a non-JSON response', async () => {
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: new ApiError(200, 'Expected JSON response but received text/html'),
                    refetch: vi.fn(),
                }),
            );

            renderWithRouter('movie');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Failed to Load Content/i })).toBeInTheDocument();
                expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
            });
        });

        it('shows retry button instead of back link for non-404 errors', async () => {
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: new ApiError(500, 'Internal Server Error'),
                    refetch: vi.fn(),
                }),
            );

            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
                expect(screen.queryByRole('link', { name: /library/i })).not.toBeInTheDocument();
            });
        });

        it('calls refetch when retry button is clicked', async () => {
            const mockRefetch = vi.fn();
            mockUseContentHierarchy.mockReturnValue(
                errorHierarchy({
                    data: undefined,
                    isLoading: false,
                    error: new ApiError(502, 'Bad Gateway'),
                    refetch: mockRefetch,
                }),
            );

            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
            });

            await user.click(screen.getByRole('button', { name: /retry/i }));
            expect(mockRefetch).toHaveBeenCalled();
        });

        it('shows a scoped metadata error beside the surviving page and retries the inspector query', async () => {
            mockMetadataInspectorState.error = new ApiError(500, 'Inspector failed');

            const user = userEvent.setup();
            renderWithRouter('content-1');

            const alert = await screen.findByTestId('content-metadata-error');
            expect(within(alert).getByText('Failed to Load Content Metadata')).toBeInTheDocument();
            expect(within(alert).getByText('Inspector failed')).toBeInTheDocument();

            expect(screen.getByRole('banner')).toBeInTheDocument();
            expect(screen.getByRole('heading', { name: /Breaking Bad/i })).toBeInTheDocument();
            expect(screen.getByTestId('content-policy-settings')).toBeInTheDocument();
            expect(screen.queryByRole('heading', { name: 'Content Not Found' })).not.toBeInTheDocument();

            await user.click(within(alert).getByRole('button', { name: /retry/i }));
            expect(mockMetadataInspectorRefetch).toHaveBeenCalled();
        });

        it('shows not found rather than a metadata error when both queries 404', async () => {
            renderWithRouter('nonexistent-id');

            expect(await screen.findByRole('heading', { name: 'Content Not Found' })).toBeInTheDocument();
            expect(screen.queryByTestId('content-metadata-error')).not.toBeInTheDocument();
            expect(screen.getByRole('link', { name: /library/i })).toHaveAttribute('href', '/library');
        });
    });

    describe('extension slot', () => {
        it('renders the content details header slot', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const slotWrapper = document.querySelector('[data-slot="content.details.header"]');
                expect(slotWrapper).toBeInTheDocument();
            });
        });

        it('retains a valid content tab extension deep link', async () => {
            mockUseSlotExtensions.mockImplementation((slot: string) => {
                if (slot === 'content.details.tabs') {
                    return {
                        extensions: [
                            {
                                id: 'content-diagnostics',
                                extensionId: 'diagnostics-extension',
                                name: 'Content Diagnostics',
                                slot: 'content.details.tabs',
                                modifier: 'After',
                                tagName: 'magpie-content-diagnostics',
                                priority: 100,
                                attributes: { 'tab-label': 'Diagnostics' },
                            },
                        ],
                        isReady: true,
                        isLoading: false,
                    };
                }

                return noExtensions;
            });

            renderWithRouter('content-1', undefined, undefined, '?tab=ext-content-diagnostics&view=grid');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /diagnostics/i })).toHaveAttribute('aria-selected', 'true');
            });

            expect(document.querySelectorAll('magpie-content-diagnostics')).toHaveLength(1);
            expect(screen.getByTestId('content-detail-page__location-search')).toHaveTextContent(
                '?tab=ext-content-diagnostics&view=grid',
            );
        });

        it('renders the content details action, metadata, and overview slots', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=details');
            await waitFor(() => {
                expect(document.querySelector('[data-slot="content.details.actions"]')).toBeInTheDocument();
                expect(document.querySelector('[data-slot="content.details.metadata"]')).toBeInTheDocument();
                expect(document.querySelector('[data-slot="content.details.overview"]')).toBeInTheDocument();
            });
        });

        it('renders the content details files slot when the files tab is active', async () => {
            renderWithRouter('content-1', undefined, undefined, '?tab=files');
            await waitFor(() => {
                expect(document.querySelector('[data-slot="content.details.files"]')).toBeInTheDocument();
            });
        });
    });

    describe('accessibility', () => {
        it('has navigation regions for breadcrumbs', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const navRegions = screen.getAllByRole('navigation');
                expect(navRegions.length).toBeGreaterThan(0);
                expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();
            });
        });

        it('has header element for content header', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('banner')).toBeInTheDocument();
            });
        });

        it('includes data attribute for content ID', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                const header = document.querySelector('[data-content-id="content-1"]');
                expect(header).toBeInTheDocument();
            });
        });

        it('has accessible tab list', async () => {
            renderWithRouter('content-1');
            await waitFor(() => {
                expect(screen.getByRole('tablist')).toBeInTheDocument();
            });
        });

        it('tabs are keyboard navigable', async () => {
            const user = userEvent.setup();
            renderWithRouter('content-1');

            await waitFor(() => {
                expect(screen.getByRole('tab', { name: /related/i })).toBeInTheDocument();
            });

            await user.tab();
            expect(document.activeElement).toBeInTheDocument();
        });
    });

    describe('nested routes', () => {
        it('renders season content when childId is provided', async () => {
            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Season 1/i })).toBeInTheDocument();
            });
        });

        it('renders breadcrumbs showing full hierarchy', async () => {
            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                const breadcrumbNav = screen.getByRole('navigation', { name: /breadcrumb/i });
                expect(breadcrumbNav).toBeInTheDocument();
            });
        });

        it('includes the parent show as a link when a season is opened directly', async () => {
            renderWithRouter('season-1');
            await waitFor(() => {
                const list = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole('list');
                const showLink = within(list).getByRole('link', { name: /breaking bad/i });
                expect(showLink).toHaveAttribute('href', '/library/content-1');
            });
            const list = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole('list');
            expect(within(list).getByText('Season 1')).toBeInTheDocument();
        });

        it('includes both the show and season as links when an episode is opened directly', async () => {
            renderWithRouter('episode-1');
            await waitFor(() => {
                const list = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole('list');
                expect(within(list).getByRole('link', { name: /breaking bad/i })).toHaveAttribute(
                    'href',
                    '/library/content-1',
                );
            });
            const list = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole('list');
            expect(within(list).getByRole('link', { name: /season 1/i })).toHaveAttribute('href', '/library/season-1');
            expect(within(list).getByText('Pilot')).toBeInTheDocument();
        });

        it('includes the full show and season chain for a nested episode route', async () => {
            renderWithRouter('content-1', 'season-1', 'episode-1');
            await waitFor(() => {
                const list = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole('list');
                expect(within(list).getByRole('link', { name: /breaking bad/i })).toHaveAttribute(
                    'href',
                    '/library/content-1',
                );
            });
            const list = within(screen.getByRole('navigation', { name: /breadcrumb/i })).getByRole('list');
            expect(within(list).getByRole('link', { name: /season 1/i })).toHaveAttribute('href', '/library/season-1');
            expect(within(list).getByText('Pilot')).toBeInTheDocument();
        });

        it('renders episode content when grandchildId is provided', async () => {
            renderWithRouter('content-1', 'season-1', 'episode-1');
            await waitFor(() => {
                expect(screen.getByRole('heading', { name: /Pilot/i })).toBeInTheDocument();
            });
        });

        it('parent link shows parent name', async () => {
            renderWithRouter('content-1', 'season-1');
            await waitFor(() => {
                const parentLink = screen.getByTestId('content-details-header__parent-link');
                expect(parentLink).toBeInTheDocument();
                expect(parentLink?.textContent).toContain('Breaking Bad');
            });
        });
    });
});
