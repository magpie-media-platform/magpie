import { describe, expect, it, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
}));

import { apiGet, apiPost, apiPatch, apiDelete } from '@/lib/api';
import { usePackageSources, type PackageSource } from './usePackageSources';

const mockSource: PackageSource = {
    id: 'src-1',
    name: 'nuget.org',
    sourceUrl: 'https://api.nuget.org/v3/index.json',
    enabled: true,
    searchFilterKind: 'Tag',
    searchFilterValue: 'magpie',
    credentialKind: 'None',
    username: null,
    secretReference: null,
    hasSecret: false,
    createdAt: '2024-01-01T00:00:00Z',
    modifiedAt: null,
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });
    return function Wrapper({ children }: { children: React.ReactNode }) {
        return createElement(QueryClientProvider, { client: queryClient }, children);
    };
}

describe('usePackageSources', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockResolvedValue([mockSource]);
        vi.mocked(apiPost).mockResolvedValue(mockSource);
        vi.mocked(apiPatch).mockResolvedValue(undefined);
        vi.mocked(apiDelete).mockResolvedValue(undefined);
    });

    it('fetches package sources from the feed endpoint', async () => {
        const { result } = renderHook(() => usePackageSources(), { wrapper: createWrapper() });

        await waitFor(() => expect(result.current.sources).toHaveLength(1));
        expect(apiGet).toHaveBeenCalledWith('/extensions/package-sources');
        expect(result.current.sources[0].name).toBe('nuget.org');
    });

    it('creates a feed via POST', async () => {
        const { result } = renderHook(() => usePackageSources(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.sources).toHaveLength(1));

        await act(async () => {
            await result.current.createSource({
                name: 'private',
                sourceUrl: 'https://feed.example/v3/index.json',
            });
        });

        expect(apiPost).toHaveBeenCalledWith('/extensions/package-sources', {
            name: 'private',
            sourceUrl: 'https://feed.example/v3/index.json',
        });
    });

    it('patches a feed via PATCH', async () => {
        const { result } = renderHook(() => usePackageSources(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.sources).toHaveLength(1));

        await act(async () => {
            await result.current.patchSource('src-1', { enabled: { value: false } });
        });

        expect(apiPatch).toHaveBeenCalledWith('/extensions/package-sources/src-1', {
            enabled: { value: false },
        });
    });

    it('toggles enabled via PATCH', async () => {
        const { result } = renderHook(() => usePackageSources(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.sources).toHaveLength(1));

        await act(async () => {
            await result.current.setEnabled('src-1', false);
        });
        expect(apiPatch).toHaveBeenCalledWith('/extensions/package-sources/src-1', {
            enabled: { value: false },
        });

        await act(async () => {
            await result.current.setEnabled('src-1', true);
        });
        expect(apiPatch).toHaveBeenCalledWith('/extensions/package-sources/src-1', {
            enabled: { value: true },
        });
    });

    it('deletes a feed via DELETE', async () => {
        const { result } = renderHook(() => usePackageSources(), { wrapper: createWrapper() });
        await waitFor(() => expect(result.current.sources).toHaveLength(1));

        await act(async () => {
            await result.current.deleteSource('src-1');
        });

        expect(apiDelete).toHaveBeenCalledWith('/extensions/package-sources/src-1');
    });
});
