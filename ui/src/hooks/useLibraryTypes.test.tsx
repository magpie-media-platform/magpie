import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import {
    useLibraryTypes,
    useLibraryCounts,
    libraryTypesQueryKeys,
    libraryCountsQueryKeys,
    type LibraryTypeDefinition,
} from './useLibraryTypes';
import * as api from '@/lib/api';

vi.mock('@/lib/api', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...original,
        apiGet: vi.fn(),
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
    { slug: 'movies', displayName: 'Movies', levelDefinitions: [] },
    { slug: 'shows', displayName: 'TV Shows', levelDefinitions: [] },
];

const mockSubjectTypesResponse = [
    { slug: 'actors', displayName: 'Actors', customMetadataFields: [] },
    { slug: 'studios', displayName: 'Studios', customMetadataFields: [] },
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
            expect(libraryTypesQueryKeys.all).toEqual(['libraryTypes']);
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
                { id: 'movies', name: 'Movies', slug: 'movies' },
                { id: 'shows', name: 'TV Shows', slug: 'shows' },
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
                { id: 'actors', name: 'Actors', slug: 'actors' },
                { id: 'studios', name: 'Studios', slug: 'studios' },
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
                { id: 'custom-kind', name: 'custom-kind', slug: 'custom-kind' },
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
                slug: 'custom',
            });
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
            { id: 'movies', name: 'Movies', slug: 'movies' },
            { id: 'shows', name: 'TV Shows', slug: 'shows' },
        ];

        const subjectTypes: LibraryTypeDefinition[] = [{ id: 'actors', name: 'Actors', slug: 'actors' }];

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
            });
        });

        it('fetches subject type counts in the same batched query', async () => {
            vi.mocked(api.apiGet).mockImplementation((url: string) => {
                if (url.startsWith('/content')) {
                    return Promise.resolve({ totalItems: 0 });
                }
                if (url.startsWith('/subjects') && url.includes('kind=actors')) {
                    return Promise.resolve(Array.from({ length: 5 }, (_, i) => ({ id: String(i) })));
                }
                return Promise.reject(new Error(`Unexpected API call: ${url}`));
            });

            const { result } = renderHook(() => useLibraryCounts(contentTypes, subjectTypes), {
                wrapper: createWrapper(queryClient),
            });

            await waitFor(() => {
                expect(result.current.counts.actors).toBe(5);
            });
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
