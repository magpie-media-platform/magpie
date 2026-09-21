import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('./useEventStream', () => ({
    useEventStream: vi.fn(),
    useGlobalEventStreamConnection: vi.fn(),
}));

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return { ...actual, apiGet: vi.fn() };
});

import { apiGet } from '@/lib/api';
import {
    useLibraryImportCandidateGroups,
    libraryImportCandidateGroupQueryKeys,
    type PagedLibraryImportCandidateGroups,
} from './useLibraryImportCandidateGroups';

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

const emptyPage: PagedLibraryImportCandidateGroups = {
    items: [],
    pageNumber: 1,
    pageSize: 25,
    totalItems: 0,
};

describe('useLibraryImportCandidateGroups', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('forwards the root folder and paging params to the candidate-groups endpoint', async () => {
        mockApiGet.mockResolvedValue(emptyPage);

        const { result } = renderHook(
            () => useLibraryImportCandidateGroups({ rootFolderId: 'folder-1', pageNumber: 2, pageSize: 25 }),
            { wrapper: createWrapper() },
        );

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(mockApiGet).toHaveBeenCalledWith(
            '/import/candidate-groups?rootFolderId=folder-1&pageNumber=2&pageSize=25',
        );
    });

    it('exposes the group DTO fields returned by the API', async () => {
        mockApiGet.mockResolvedValue({
            items: [
                {
                    groupKey: '/media/tv/Bluey',
                    kind: 'Directory',
                    sourcePath: '/media/tv/Bluey',
                    title: 'Bluey',
                    contentType: 'tv',
                    subtitle: null,
                    match: null,
                    leafCount: 157,
                    topLevelSlug: 'show',
                    leafLevelSlug: 'episode',
                    leafLevelDisplayName: 'Episode',
                    statusCounts: { Pending: 2 },
                },
            ],
            pageNumber: 1,
            pageSize: 25,
            totalItems: 1,
        });

        const { result } = renderHook(() => useLibraryImportCandidateGroups(), { wrapper: createWrapper() });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(result.current.items).toHaveLength(1);
        expect(result.current.items[0]).toMatchObject({
            groupKey: '/media/tv/Bluey',
            kind: 'Directory',
            sourcePath: '/media/tv/Bluey',
            leafCount: 157,
            leafLevelDisplayName: 'Episode',
        });
        expect(result.current.pagination).toEqual({ pageNumber: 1, pageSize: 25, totalItems: 1 });
    });

    it('exposes stable query keys', () => {
        expect(libraryImportCandidateGroupQueryKeys.all).toEqual(['import/candidate-groups']);
        expect(libraryImportCandidateGroupQueryKeys.list({ rootFolderId: 'rf-1' })).toEqual([
            'import/candidate-groups',
            'list',
            { rootFolderId: 'rf-1' },
        ]);
    });
});
