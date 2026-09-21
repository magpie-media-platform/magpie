import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: vi.fn((path: string, params?: Record<string, unknown>) => {
        const searchParams = new URLSearchParams();
        for (const [key, value] of Object.entries(params ?? {})) {
            if (value !== undefined && value !== null) {
                searchParams.set(key, String(value));
            }
        }
        const query = searchParams.toString();
        return query ? `${path}?${query}` : path;
    }),
}));

vi.mock('@/hooks/useInvalidateQueriesOnEvents', () => ({
    useInvalidateQueriesOnEvents: vi.fn(),
}));

import { apiGet } from '@/lib/api';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { ApiContentHierarchyItem, PagedContentHierarchyItems } from '@/types/content';

import { fetchWantedChildren, useWantedContent, WANTED_CONTAINER_FILTER, WANTED_PAGE_SIZE } from './useWantedContent';

const missingItem: ApiContentHierarchyItem = {
    id: 'content-missing-1',
    metadataContainerId: 'mc-1',
    contentType: 'movies',
    level: 'movie',
    slug: 'missing-movie',
    name: 'Missing Movie',
    monitoring: true,
    effectiveMonitoring: true,
    createdAt: '2024-01-01T00:00:00Z',
    contentItem: {
        id: 'ci-1',
        contentHierarchyItemId: 'content-missing-1',
        fulfillment: 'Missing',
        dateLastSearched: null,
        createdAt: '2024-01-01T00:00:00Z',
    },
};

const upgradableItem: ApiContentHierarchyItem = {
    ...missingItem,
    id: 'content-upgradable-1',
    name: 'Upgradable Movie',
    contentItem: {
        ...missingItem.contentItem!,
        id: 'ci-2',
        contentHierarchyItemId: 'content-upgradable-1',
        fulfillment: 'Upgradable',
    },
};

function pageOf(items: ApiContentHierarchyItem[]): PagedContentHierarchyItems {
    return { items, pageNumber: 1, pageSize: WANTED_PAGE_SIZE, totalItems: items.length };
}

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 }, mutations: { retry: false } },
    });
    const Wrapper = ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
    return { Wrapper };
}

describe('useWantedContent', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        vi.mocked(apiGet).mockImplementation(async (url: string) => {
            if (url.includes('fulfillment=Upgradable')) {
                return pageOf([upgradableItem]);
            }
            return pageOf([missingItem]);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('queries root wanted containers through the public content route', async () => {
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useWantedContent(), { wrapper: Wrapper });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        const urls = vi.mocked(apiGet).mock.calls.map(([url]) => String(url));
        const containerUrl = urls.find((url) => url.includes('filter='));
        const upgradeUrl = urls.find((url) => url.includes('fulfillment=Upgradable'));
        expect(containerUrl).toBeDefined();
        expect(upgradeUrl).toBeDefined();
        const containerParams = new URL(containerUrl!, 'http://magpie.local').searchParams;
        const upgradeParams = new URL(upgradeUrl!, 'http://magpie.local').searchParams;
        expect(containerParams.get('topLevelOnly')).toBe('true');
        expect(containerParams.get('monitoring')).toBe('true');
        expect(containerParams.get('filter')).toBe(WANTED_CONTAINER_FILTER);
        expect(upgradeParams.get('topLevelOnly')).toBe('true');
        expect(upgradeParams.get('monitoring')).toBe('true');
        expect(upgradeParams.get('fulfillment')).toBe('Upgradable');
        expect(urls.every((url) => url.startsWith('/content?'))).toBe(true);
    });

    it('fails open on new content and downloads for the filtered wanted lists', async () => {
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useWantedContent(), { wrapper: Wrapper });

        await waitFor(() => expect(result.current.isLoading).toBe(false));
        const listOptions = vi
            .mocked(useInvalidateQueriesOnEvents)
            .mock.calls.filter(([events]) => events.includes('download.created'))
            .at(-1)?.[2];

        expect(listOptions?.failOpenEventNames).toEqual(['content.hierarchyItem.created', 'download.created']);
        expect(listOptions?.entityIds).toEqual(['content-missing-1', 'content-upgradable-1']);
    });

    it('queries a direct wanted scope through parentId without a root-only constraint', async () => {
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useWantedContent('show-1'), { wrapper: Wrapper });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        const urls = vi.mocked(apiGet).mock.calls.map(([url]) => String(url));
        expect(urls).toContainEqual(expect.stringContaining('parentId=show-1&monitoring=true'));
        expect(urls.some((url) => url.includes('topLevelOnly='))).toBe(false);
    });

    it('keeps upgrade leaves available for the page to include without promoting parent upgrades', async () => {
        const { Wrapper } = createWrapper();
        const { result } = renderHook(() => useWantedContent(), { wrapper: Wrapper });

        await waitFor(() => expect(result.current.isLoading).toBe(false));

        expect(result.current.items.map((item) => item.id)).toEqual(['content-missing-1']);
        expect(result.current.upgradeItems.map((item) => item.id)).toEqual(['content-upgradable-1']);
    });

    it('stops the shared child loader at fifty pages', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue({
            items: Array(200).fill(missingItem),
            pageNumber: 1,
            pageSize: 200,
            totalItems: 10200,
        });
        // Act
        const children = await fetchWantedChildren('parent-1');
        // Assert
        expect(apiGet).toHaveBeenCalledTimes(50);
        expect(apiGet).toHaveBeenLastCalledWith(expect.stringContaining('pageNumber=50&pageSize=200'));
        expect(children.map((item) => item.id)).toEqual(['content-missing-1']);
    });

    it('reports the list as truncated when the loaded rows fall short of the server total', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue({
            items: Array(200).fill(missingItem),
            pageNumber: 1,
            pageSize: 200,
            totalItems: 10200,
        });
        const { Wrapper } = createWrapper();
        // Act
        const { result } = renderHook(() => useWantedContent(), { wrapper: Wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        // Assert
        expect(result.current.isTruncated).toBe(true);
    });

    it('reports the list as complete when every row is loaded', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue(pageOf([missingItem]));
        const { Wrapper } = createWrapper();
        // Act
        const { result } = renderHook(() => useWantedContent(), { wrapper: Wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        // Assert
        expect(result.current.isTruncated).toBe(false);
    });

    it('counts overlapping wanted and upgrade rows once', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValue(pageOf([missingItem]));
        const { Wrapper } = createWrapper();
        // Act
        const { result } = renderHook(() => useWantedContent(), { wrapper: Wrapper });
        await waitFor(() => expect(result.current.isLoading).toBe(false));
        // Assert
        expect(result.current.totalItems).toBe(1);
    });

    it('loads monitored descendants through the same content route', async () => {
        await fetchWantedChildren('parent-1');

        expect(apiGet).toHaveBeenCalledWith(
            expect.stringContaining('parentId=parent-1&monitoring=true&pageNumber=1&pageSize=200'),
        );
    });
});
