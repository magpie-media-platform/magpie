import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { useLibraryRoute } from '@/pages/LibraryPage/useLibraryRoute';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
    useGlobalEventStreamConnection: vi.fn(),
}));

import {
    useLibraryTypes,
    useLibraryCounts,
    useTypeIconName,
    getRootLevels,
    libraryTypesQueryKeys,
    libraryCountsQueryKeys,
    STANDARD_METADATA_FIELDS,
    type LibraryTypeDefinition,
} from './useLibraryTypes';
import * as api from '@/lib/api';
import { useEventStream } from './useEventStream';

const mockedUseEventStream = vi.mocked(useEventStream);

vi.mock('@/lib/api', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...original,
        apiGet: vi.fn(),
    };
});

vi.mock('@/auth', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/auth')>();
    return {
        ...original,
        useAuth: () => ({ isAuthenticated: true }) as ReturnType<typeof original.useAuth>,
    };
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
                staleTime: 0,
            },
        },
    });
}

function createWrapper(queryClient: QueryClient) {
    return function Wrapper({ children }: { children: ReactNode }) {
        return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
    };
}

const mockContentTypesResponse = [
    { slug: 'movies', displayName: 'Movie', pluralDisplayName: 'Movies', levelDefinitions: [] },
    { slug: 'shows', displayName: 'TV', pluralDisplayName: 'TV Shows', levelDefinitions: [] },
];

const mockSubjectTypesResponse = [
    { slug: 'actors', displayName: 'Person', pluralDisplayName: 'People', customMetadataFields: [] },
    { slug: 'studios', displayName: 'Studio', pluralDisplayName: 'Studios', customMetadataFields: [] },
];

describe('useLibraryTypes', () => {
    let queryClient: QueryClient;

    beforeEach(() => {
        queryClient = createTestQueryClient();
        vi.clearAllMocks();
    });

    afterEach(() => {
        queryClient.clear();
    });

    describe('libraryTypesQueryKeys', () => {
        it('generates correct query keys', () => {
            expect(libraryTypesQueryKeys.contentTypes()).toEqual(['libraryTypes', 'content-types']);
            expect(libraryTypesQueryKeys.subjectTypes()).toEqual(['libraryTypes', 'subject-types']);
        });
    });

    describe('useLibraryTypes', () => {
        it('fetches from both endpoints', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve(mockContentTypesResponse);
                }
                if (url === '/subject-types') {
                    return Promise.resolve(mockSubjectTypesResponse);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(api.apiGet).toHaveBeenCalledWith('/content-types');
            expect(api.apiGet).toHaveBeenCalledWith('/subject-types');
            expect(result.current.contentTypes).toHaveLength(2);
            expect(result.current.subjectTypes).toHaveLength(2);
        });

        it('maps content types to LibraryTypeDefinition', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve(mockContentTypesResponse);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.contentTypes).toEqual([
                {
                    id: 'movies',
                    name: 'Movie',
                    pluralName: 'Movies',
                    slug: 'movies',
                    iconName: null,
                    levels: [],
                    supportedMetadataFields: 0x3fffff,
                    customMetadataFields: [],
                    standardMetadataFields: STANDARD_METADATA_FIELDS,
                },
                {
                    id: 'shows',
                    name: 'TV',
                    pluralName: 'TV Shows',
                    slug: 'shows',
                    iconName: null,
                    levels: [],
                    supportedMetadataFields: 0x3fffff,
                    customMetadataFields: [],
                    standardMetadataFields: STANDARD_METADATA_FIELDS,
                },
            ]);
        });

        it('maps subject types to LibraryTypeDefinition', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve(mockSubjectTypesResponse);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.subjectTypes).toEqual([
                {
                    id: 'actors',
                    name: 'Person',
                    pluralName: 'People',
                    slug: 'actors',
                    iconName: null,
                    levels: [],
                    supportedMetadataFields: 0x3fffff,
                    customMetadataFields: [],
                    standardMetadataFields: STANDARD_METADATA_FIELDS,
                },
                {
                    id: 'studios',
                    name: 'Studio',
                    pluralName: 'Studios',
                    slug: 'studios',
                    iconName: null,
                    levels: [],
                    supportedMetadataFields: 0x3fffff,
                    customMetadataFields: [],
                    standardMetadataFields: STANDARD_METADATA_FIELDS,
                },
            ]);
        });

        it('falls back to slug when displayName is null for subject types', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([{ slug: 'custom-kind', displayName: null, customMetadataFields: [] }]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.subjectTypes).toEqual([
                {
                    id: 'custom-kind',
                    name: 'custom-kind',
                    pluralName: 'custom-kind',
                    slug: 'custom-kind',
                    iconName: null,
                    levels: [],
                    supportedMetadataFields: 0x3fffff,
                    customMetadataFields: [],
                    standardMetadataFields: STANDARD_METADATA_FIELDS,
                },
            ]);
        });

        it('returns empty arrays when loading', () => {
            vi.mocked(api.apiGet).mockImplementation(() => new Promise(() => {}));

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            expect(result.current.isLoading).toBe(true);
            expect(result.current.contentTypes).toEqual([]);
            expect(result.current.subjectTypes).toEqual([]);
        });

        it('handles API errors from content-types endpoint', async () => {
            const error = new Error('Network error');
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.reject(error);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.error).toBe(error);
            expect(result.current.contentTypes).toEqual([]);
        });

        it('handles API errors from subject-types endpoint', async () => {
            const error = new Error('Network error');
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([]);
                }
                if (url === '/subject-types') {
                    return Promise.reject(error);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.error).toBe(error);
            expect(result.current.subjectTypes).toEqual([]);
        });

        it('handles minimal type definition', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([{ slug: 'custom', displayName: 'Custom', levelDefinitions: [] }]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            expect(result.current.contentTypes[0]).toEqual({
                id: 'custom',
                name: 'Custom',
                pluralName: 'Custom',
                slug: 'custom',
                iconName: null,
                levels: [],
                supportedMetadataFields: 0x3fffff,
                customMetadataFields: [],
                standardMetadataFields: STANDARD_METADATA_FIELDS,
            });
        });

        it('carries level definitions instead of discarding them', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([
                        {
                            slug: 'foo',
                            displayName: 'Foo',
                            levelDefinitions: [
                                { slug: 'foo-a', displayName: 'A', parentSlug: null, subfolder: '' },
                                { slug: 'foo-b', displayName: 'B', parentSlug: null },
                                { slug: 'foo-c', displayName: 'C', parentSlug: null },
                            ],
                        },
                    ]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            const foo = result.current.contentTypes[0];
            expect(foo.levels).toHaveLength(3);
            expect(getRootLevels(foo).map((l) => l.slug)).toEqual(['foo-a', 'foo-b', 'foo-c']);
            expect(foo.levels.map((level) => level.subfolder)).toEqual(['', null, null]);
        });

        it('carries level file contracts and subfolders', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([
                        {
                            slug: 'tv',
                            displayName: 'TV',
                            levelDefinitions: [
                                { slug: 'show', displayName: 'Show', parentSlug: null, subfolder: 'shows' },
                                { slug: 'season', displayName: 'Season', parentSlug: 'show' },
                                {
                                    slug: 'episode',
                                    displayName: 'Episode',
                                    parentSlug: 'season',
                                    acceptedFileTypes: ['video/*'],
                                    acceptedSidecarTypes: ['.srt'],
                                },
                                {
                                    slug: 'audiobook',
                                    displayName: 'Audiobook',
                                    parentSlug: null,
                                    acceptedFileTypes: ['audio/*'],
                                    subfolder: 'audiobooks',
                                },
                            ],
                        },
                    ]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            const levels = result.current.contentTypes[0].levels;
            expect(levels.map((level) => level.acceptedFileTypes)).toEqual([[], [], ['video/*'], ['audio/*']]);
            expect(levels.map((level) => level.acceptedSidecarTypes)).toEqual([[], [], ['.srt'], []]);
            expect(levels.map((level) => level.subfolder)).toEqual(['shows', null, null, 'audiobooks']);
        });

        it('carries supported metadata fields from content levels and subject kinds', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([
                        {
                            slug: 'books',
                            displayName: 'Book',
                            levelDefinitions: [
                                {
                                    slug: 'book',
                                    displayName: 'Book',
                                    parentSlug: null,
                                    supportedMetadataFields: 'Title, PublishDate',
                                    customMetadataFields: [{ key: 'isbn', displayName: 'ISBN' }],
                                },
                            ],
                        },
                    ]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([
                        {
                            slug: 'publisher',
                            displayName: 'Publisher',
                            customMetadataFields: [{ key: 'country', displayName: 'Country' }],
                            supportedMetadataFields: 'Title',
                        },
                    ]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            const contentType = result.current.contentTypes[0];
            const subjectType = result.current.subjectTypes[0];
            expect(contentType.standardMetadataFields?.map((field) => field.field)).toEqual(['Title', 'PublishDate']);
            expect(contentType.customMetadataFields).toEqual([
                { key: 'isbn', displayName: 'ISBN', dataType: null, format: null },
            ]);
            expect(contentType.levels[0].customMetadataFields).toEqual([
                { key: 'isbn', displayName: 'ISBN', dataType: null, format: null },
            ]);
            expect(subjectType.standardMetadataFields?.map((field) => field.field)).toEqual(['Title']);
            expect(subjectType.customMetadataFields).toEqual([
                { key: 'country', displayName: 'Country', dataType: null, format: null },
            ]);
        });

        it('provides refetch function that calls both endpoints', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve(mockContentTypesResponse);
                }
                if (url === '/subject-types') {
                    return Promise.resolve(mockSubjectTypesResponse);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryTypes(), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => expect(result.current.isLoading).toBe(false));

            vi.mocked(api.apiGet).mockClear();
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve(mockContentTypesResponse);
                }
                if (url === '/subject-types') {
                    return Promise.resolve(mockSubjectTypesResponse);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            await result.current.refetch();

            expect(api.apiGet).toHaveBeenCalledWith('/content-types');
            expect(api.apiGet).toHaveBeenCalledWith('/subject-types');
        });
    });

    it.each([
        ['/types/alpha/shared', 'camera'],
        ['/types/beta/shared', 'book'],
        ['/types/shared', 'folder'],
    ])('resolves the route icon within its owning type: %s', async (path, icon) => {
        // Arrange
        vi.mocked(api.apiGet)
            .mockResolvedValueOnce([
                {
                    slug: 'alpha',
                    displayName: 'Alpha',
                    iconName: 'star',
                    levelDefinitions: [{ slug: 'shared', displayName: 'Shared', iconName: 'camera' }],
                },
                {
                    slug: 'beta',
                    displayName: 'Beta',
                    iconName: 'heart',
                    levelDefinitions: [{ slug: 'shared', displayName: 'Shared', iconName: 'book' }],
                },
                { slug: 'shared', displayName: 'Shared', iconName: 'folder', levelDefinitions: [] },
            ])
            .mockResolvedValueOnce([]);
        // Act
        const { result } = renderHook(() => useLibraryRoute(), {
            wrapper: ({ children }) => (
                <QueryClientProvider client={queryClient}>
                    <MemoryRouter initialEntries={[path]}>
                        <Routes>
                            <Route path="/types/:contentType/:levelSlug?" element={children} />
                        </Routes>
                    </MemoryRouter>
                </QueryClientProvider>
            ),
        });
        // Assert
        await waitFor(() => expect(result.current.pageIconName).toBe(icon));
    });

    describe('useTypeIconName', () => {
        it('keeps type icons separate from colliding level slugs', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url === '/content-types') {
                    return Promise.resolve([
                        {
                            slug: 'foo',
                            displayName: 'Foo',
                            iconName: 'film-strip',
                            levelDefinitions: [
                                {
                                    slug: 'foo-a',
                                    displayName: 'A',
                                    parentSlug: null,
                                    iconName: 'video-camera',
                                },
                                { slug: 'foo', displayName: 'B', parentSlug: null, iconName: 'film-reel' },
                            ],
                        },
                    ]);
                }
                if (url === '/subject-types') {
                    return Promise.resolve([
                        { slug: 'person', displayName: 'Person', iconName: 'person', customMetadataFields: [] },
                    ]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useTypeIconName(), { wrapper: createWrapper(queryClient) });

            await waitFor(() => {
                expect(result.current('foo')).toBe('film-strip');
            });

            const resolve = result.current;
            expect(resolve('foo')).toBe('film-strip');
            expect(resolve('foo-a')).toBeUndefined();
            expect(resolve('FOO')).toBe('film-strip');
            expect(resolve('person', { subject: true })).toBe('person');
            expect(resolve('unmapped')).toBeUndefined();
            expect(resolve('unmapped', { subject: true })).toBeUndefined();
        });
    });

    describe('libraryCountsQueryKeys', () => {
        it('generates correct query keys', () => {
            expect(libraryCountsQueryKeys.counts(['movies', 'shows'], ['actors'])).toEqual([
                'libraryTypes',
                'counts',
                ['movies', 'shows'],
                ['actors'],
            ]);
        });
    });

    describe('useLibraryCounts', () => {
        const contentTypes: LibraryTypeDefinition[] = [
            { id: 'movies', name: 'Movie', pluralName: 'Movies', slug: 'movies', levels: [] },
            { id: 'shows', name: 'TV', pluralName: 'TV Shows', slug: 'shows', levels: [] },
        ];

        const subjectTypes: LibraryTypeDefinition[] = [
            { id: 'actors', name: 'Person', pluralName: 'People', slug: 'actors', levels: [] },
        ];

        it('fetches all content type counts in a single batched query', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url.startsWith('/content') && url.includes('contentType=movies')) {
                    return Promise.resolve({ totalItems: 42 });
                }
                if (url.startsWith('/content') && url.includes('contentType=shows')) {
                    return Promise.resolve({ totalItems: 150 });
                }
                if (url.startsWith('/subjects') && url.includes('kind=actors')) {
                    return Promise.resolve({ totalItems: 5 });
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryCounts(contentTypes, subjectTypes), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.counts.movies).toBe(42);
                expect(result.current.counts.shows).toBe(150);
                expect(result.current.counts.actors).toBe(5);
            });
        });

        it('coalesces a burst of content events into a single refetch cycle', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url.startsWith('/content') && url.includes('contentType=movies')) {
                    return Promise.resolve({ totalItems: 1 });
                }
                if (url.startsWith('/content') && url.includes('contentType=shows')) {
                    return Promise.resolve({ totalItems: 2 });
                }
                if (url.startsWith('/subjects') && url.includes('kind=actors')) {
                    return Promise.resolve({ totalItems: 3 });
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryCounts(contentTypes, subjectTypes), {
                wrapper: createWrapper(queryClient),
            });
            await waitFor(() => expect(result.current.counts.movies).toBe(1));

            vi.useFakeTimers();
            vi.mocked(api.apiGet).mockClear();
            const handler = mockedUseEventStream.mock.calls[0]?.[0];
            for (let index = 0; index < 100; index += 1) {
                handler?.({
                    name: 'content.hierarchyItem.created',
                    context: { 'content.hierarchyItemId': `content-${index}` },
                });
            }
            await act(async () => {
                await vi.advanceTimersByTimeAsync(300);
            });

            expect(vi.mocked(api.apiGet)).toHaveBeenCalledTimes(3);
            vi.useRealTimers();
        });

        it('counts only top-level content so the total matches the linked list', async () => {
            const seen: string[] = [];
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url.startsWith('/content')) {
                    seen.push(url);
                    return Promise.resolve({ totalItems: 3 });
                }
                if (url.startsWith('/subjects')) {
                    return Promise.resolve([]);
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryCounts(contentTypes, subjectTypes), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.counts.shows).toBe(3);
            });

            expect(seen.length).toBeGreaterThan(0);
            for (const url of seen) {
                expect(url).toContain('topLevelOnly=true');
            }
        });

        it('fetches subject type counts in the same batched query', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url.startsWith('/content')) {
                    return Promise.resolve({ totalItems: 0 });
                }
                if (url.startsWith('/subjects') && url.includes('kind=actors')) {
                    return Promise.resolve({ totalItems: 5 });
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryCounts(contentTypes, subjectTypes), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.counts.actors).toBe(5);
            });

            expect(api.apiGet).toHaveBeenCalledWith(expect.stringContaining('pageSize=1'));
        });

        it('returns empty counts while loading', () => {
            vi.mocked(api.apiGet).mockImplementation(() => new Promise(() => {}));

            const { result } = renderHook(() => useLibraryCounts(contentTypes, subjectTypes), {
                wrapper: createWrapper(queryClient),
            });

            expect(result.current.counts.movies).toBeUndefined();
            expect(result.current.counts.shows).toBeUndefined();
            expect(result.current.counts.actors).toBeUndefined();
        });

        it('returns empty counts for empty type arrays', () => {
            const { result } = renderHook(() => useLibraryCounts([], []), { wrapper: createWrapper(queryClient) });

            expect(result.current.counts).toEqual({});
        });
    });
});
