import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import { useContentReleases, type ContentReleaseRow } from './useContentReleases';

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
