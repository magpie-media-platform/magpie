import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPatch: vi.fn(),
}));

import { apiGet, apiPatch } from '@/lib/api';
import { useContentReleases, useSetContentReleaseUnmatched, type ContentReleaseRow } from './useContentReleases';

const mockRows: ContentReleaseRow[] = [
    {
        id: 'release-1',
        title: 'Movie.2024.1080p.BluRay.x264',
        protocol: 'torrent',
        size: 5_368_709_120,
        publishDate: '2024-01-15T10:00:00Z',
        isMatch: true,
        properties: '{"seeders":"42","leechers":"3"}',
        acquisitionStatus: 'Downloading',
    },
];

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper };
}

describe('useContentReleases', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('restores a persisted waiting reason from a fresh request and clears it on refetch', async () => {
        // Arrange
        const reason =
            'Waiting for destination capacity: the capacity check is unavailable. Required: unknown. Available: 99 bytes.';
        mockApiGet
            .mockResolvedValueOnce([
                {
                    id: 'release-1',
                    title: 'Film',
                    protocol: 'torrent',
                    publishDate: '2026-09-22T12:00:00Z',
                    acquisitionWaitingReason: reason,
                },
            ])
            .mockResolvedValueOnce(mockRows);
        const { Wrapper } = createWrapper();

        // Act
        const { result } = renderHook(() => useContentReleases('content-1'), { wrapper: Wrapper });

        // Assert
        await waitFor(() => expect(result.current.rows[0]?.acquisitionWaitingReason).toBe(reason));
        expect(result.current.rows[0].acquisitionStatus).toBeUndefined();

        // Act
        await result.current.refetch();

        // Assert
        await waitFor(() => expect(result.current.rows[0]?.acquisitionStatus).toBe('Downloading'));
        expect(result.current.rows[0].acquisitionWaitingReason).toBeUndefined();
    });

    it('fetches scored releases from the content-scoped endpoint', async () => {
        mockApiGet.mockResolvedValue(mockRows);
        const { Wrapper } = createWrapper();

        const { result } = renderHook(() => useContentReleases('content-1'), { wrapper: Wrapper });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(mockApiGet).toHaveBeenCalledWith('/content/content-1/releases');
        expect(result.current.rows).toHaveLength(1);
        expect(result.current.rows[0].acquisitionStatus).toBe('Downloading');
        expect(result.current.rows[0].properties).toContain('"seeders":"42"');
    });

    it('does not fetch when contentId is missing', async () => {
        mockApiGet.mockResolvedValue(mockRows);
        const { Wrapper } = createWrapper();

        renderHook(() => useContentReleases(undefined), { wrapper: Wrapper });

        await new Promise((resolve) => setTimeout(resolve, 10));
        expect(mockApiGet).not.toHaveBeenCalled();
    });

    it('does not fetch when enabled is false', async () => {
        mockApiGet.mockResolvedValue(mockRows);
        const { Wrapper } = createWrapper();

        renderHook(() => useContentReleases('content-1', { enabled: false }), { wrapper: Wrapper });

        await new Promise((resolve) => setTimeout(resolve, 10));
        expect(mockApiGet).not.toHaveBeenCalled();
    });
});

describe('useSetContentReleaseUnmatched', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    it.each(['Unmatched', 'Matched'] as const)('patches status=%s on the content release', async (status) => {
        // Arrange
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useSetContentReleaseUnmatched(), { wrapper: Wrapper });

        // Act
        await act(async () => {
            await result.current.mutateAsync({ contentId: 'content-1', releaseId: 'release-1', status });
        });

        // Assert
        expect(apiPatch).toHaveBeenCalledWith('/content/content-1/releases/release-1', {
            status: { value: status },
        });
    });
});
