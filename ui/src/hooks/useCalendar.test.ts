import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

import { useCalendar, mapToCalendarEvent, buildDateFilter } from './useCalendar';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    buildUrl: vi.fn((path, params) => {
        const query = new URLSearchParams();
        if (params) {
            Object.entries(params).forEach(([k, v]) => {
                if (v !== undefined && v !== null) {
                    query.set(k, String(v));
                }
            });
        }
        const qs = query.toString();
        return qs ? `${path}?${qs}` : path;
    }),
}));

import { apiGet } from '@/lib/api';

const mockApiGet = vi.mocked(apiGet);

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
        },
    });

    return function Wrapper({ children }: { children: React.ReactNode }) {
        return createElement(QueryClientProvider, { client: queryClient }, children);
    };
}

describe('useCalendar', () => {
    beforeEach(() => {
        vi.clearAllMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('returns empty events and error when API fails', async () => {
        mockApiGet.mockRejectedValueOnce(new Error('API unavailable'));

        const { result } = renderHook(() => useCalendar(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        expect(result.current.events).toEqual([]);
        expect(result.current.error).toBeInstanceOf(Error);
        expect(result.current.error?.message).toBe('API unavailable');
    });

    it('returns events from API when successful', async () => {
        const apiResponse = {
            items: [
                {
                    id: 'test-1',
                    metadataContainerId: 'meta-1',
                    contentType: 'episodes',
                    level: 'episode',
                    slug: 'test-show',
                    name: 'Test Episode',
                    monitoring: 1,
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'meta-1',
                        contributions: [
                            {
                                ordinal: 0,
                                title: 'Test Show',
                                publishDate: new Date().toISOString(),
                                images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                            },
                        ],
                    },
                    contentItem: {
                        id: 'ci-1',
                        contentHierarchyItemId: 'test-1',
                        fulfillment: 0,
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                },
            ],
            pageNumber: 1,
            pageSize: 200,
            totalItems: 1,
        };

        mockApiGet.mockResolvedValueOnce(apiResponse);

        const { result } = renderHook(() => useCalendar(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await waitFor(() => {
            expect(result.current.events.some((e) => e.title === 'Test Show')).toBe(true);
        });

        const apiEvent = result.current.events.find((e) => e.title === 'Test Show');
        expect(apiEvent).toBeDefined();
        expect(apiEvent!.kind).toBe('episode');
        expect(apiEvent!.fulfillment).toBe('missing');
    });

    it('filters out items without publishDate', async () => {
        const apiResponse = {
            items: [
                {
                    id: 'test-1',
                    metadataContainerId: 'meta-1',
                    contentType: 'episodes',
                    level: 'episode',
                    slug: 'test-show',
                    name: 'With Date',
                    monitoring: 1,
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'meta-1',
                        contributions: [
                            {
                                ordinal: 0,
                                title: 'With Date',
                                publishDate: new Date().toISOString(),
                            },
                        ],
                    },
                    contentItem: {
                        id: 'ci-1',
                        contentHierarchyItemId: 'test-1',
                        fulfillment: 2,
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                },
                {
                    id: 'test-2',
                    metadataContainerId: 'meta-2',
                    contentType: 'movies',
                    level: 'movie',
                    slug: 'test-movie',
                    name: 'No Date',
                    monitoring: 1,
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'meta-2',
                        contributions: [
                            {
                                ordinal: 0,
                                title: 'No Date',
                            },
                        ],
                    },
                    contentItem: {
                        id: 'ci-2',
                        contentHierarchyItemId: 'test-2',
                        fulfillment: 0,
                        createdAt: '2024-01-01T00:00:00Z',
                    },
                },
            ],
            pageNumber: 1,
            pageSize: 200,
            totalItems: 2,
        };

        mockApiGet.mockResolvedValueOnce(apiResponse);

        const { result } = renderHook(() => useCalendar(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await waitFor(() => {
            expect(result.current.events.some((e) => e.title === 'With Date')).toBe(true);
        });

        expect(result.current.events.every((e) => e.title !== 'No Date')).toBe(true);
    });

    it('passes date filter params to API', async () => {
        mockApiGet.mockResolvedValueOnce({
            items: [],
            pageNumber: 1,
            pageSize: 200,
            totalItems: 0,
        });

        const params = { startDate: '2024-01-01', endDate: '2024-01-31' };
        const { result } = renderHook(() => useCalendar(params), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        const { buildUrl } = await import('@/lib/api');
        expect(buildUrl).toHaveBeenCalledWith(
            '/content',
            expect.objectContaining({
                filter: 'publishDate >= "2024-01-01" and publishDate <= "2024-01-31"',
                pageSize: 200,
            }),
        );
    });

    it('provides refetch function', async () => {
        mockApiGet.mockResolvedValue({
            items: [],
            pageNumber: 1,
            pageSize: 200,
            totalItems: 0,
        });

        const { result } = renderHook(() => useCalendar(), {
            wrapper: createWrapper(),
        });

        expect(typeof result.current.refetch).toBe('function');
    });
});

describe('mapToCalendarEvent', () => {
    it('maps API content item to calendar event', () => {
        const item = {
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'episodes',
            level: 'episode',
            slug: 'test-show',
            name: 'Test Episode Name',
            monitoring: 1,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [
                    {
                        ordinal: 0,
                        title: 'Test Show',
                        publishDate: '2024-06-15T20:00:00Z',
                        images: [{ type: 'poster', url: 'https://example.com/poster.jpg' }],
                    },
                ],
            },
            contentItem: {
                id: 'ci-1',
                contentHierarchyItemId: 'test-1',
                fulfillment: 0 as const,
                createdAt: '2024-01-01T00:00:00Z',
            },
        };

        const event = mapToCalendarEvent(item);

        expect(event).not.toBeNull();
        expect(event!.id).toBe('cal-test-1');
        expect(event!.contentId).toBe('test-1');
        expect(event!.title).toBe('Test Show');
        expect(event!.date).toBe('2024-06-15T20:00:00Z');
        expect(event!.kind).toBe('episode');
        expect(event!.fulfillment).toBe('missing');
        expect(event!.posterUrl).toBe('https://example.com/poster.jpg');
    });

    it('returns null for items without publishDate', () => {
        const item = {
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'test-movie',
            name: 'Test Movie',
            monitoring: 1,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [{ ordinal: 0, title: 'Test Movie' }],
            },
        };

        const event = mapToCalendarEvent(item);
        expect(event).toBeNull();
    });

    it('maps fulfillment status correctly', () => {
        const createItem = (fulfillment: 0 | 1 | 2 | 3) => ({
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'test',
            name: 'Test',
            monitoring: 1,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [{ ordinal: 0, title: 'Test', publishDate: '2024-01-01T00:00:00Z' }],
            },
            contentItem: {
                id: 'ci-1',
                contentHierarchyItemId: 'test-1',
                fulfillment,
                createdAt: '2024-01-01T00:00:00Z',
            },
        });

        expect(mapToCalendarEvent(createItem(0))!.fulfillment).toBe('missing');
        expect(mapToCalendarEvent(createItem(1))!.fulfillment).toBe('upgradable');
        expect(mapToCalendarEvent(createItem(2))!.fulfillment).toBe('fulfilled');
        expect(mapToCalendarEvent(createItem(3))!.fulfillment).toBe('fulfilledExternally');
    });

    it('maps content type correctly', () => {
        const createItem = (contentType: string) => ({
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType,
            level: 'item',
            slug: 'test',
            name: 'Test',
            monitoring: 1,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [{ ordinal: 0, title: 'Test', publishDate: '2024-01-01T00:00:00Z' }],
            },
        });

        expect(mapToCalendarEvent(createItem('episodes'))!.kind).toBe('episode');
        expect(mapToCalendarEvent(createItem('movies'))!.kind).toBe('movie');
        expect(mapToCalendarEvent(createItem('music'))!.kind).toBe('music');
        expect(mapToCalendarEvent(createItem('books'))!.kind).toBe('book');
        expect(mapToCalendarEvent(createItem('other'))!.kind).toBe('unknown');
    });

    it('extracts poster URL from contributions', () => {
        const createItem = (imageUrl: string) => ({
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'test',
            name: 'Test',
            monitoring: 1,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [
                    {
                        ordinal: 0,
                        title: 'Test',
                        publishDate: '2024-01-01T00:00:00Z',
                        images: [{ type: 'poster', url: imageUrl }],
                    },
                ],
            },
        });

        expect(mapToCalendarEvent(createItem('https://example.com/poster.jpg'))!.posterUrl).toBe(
            'https://example.com/poster.jpg',
        );

        expect(mapToCalendarEvent(createItem('http://cdn.example.com/img.jpg'))!.posterUrl).toBe(
            'http://cdn.example.com/img.jpg',
        );
    });

    it('returns undefined posterUrl when no contributions exist', () => {
        const item = {
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'test',
            name: 'Test',
            monitoring: 1,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [{ ordinal: 0, title: 'Test', publishDate: '2024-01-01T00:00:00Z' }],
            },
        };

        expect(mapToCalendarEvent(item)!.posterUrl).toBeUndefined();
    });
});

describe('buildDateFilter', () => {
    it('returns undefined when no dates are provided', () => {
        expect(buildDateFilter()).toBeUndefined();
        expect(buildDateFilter(undefined, undefined)).toBeUndefined();
    });

    it('builds filter with only startDate', () => {
        expect(buildDateFilter('2024-01-01')).toBe('publishDate >= "2024-01-01"');
    });

    it('builds filter with only endDate', () => {
        expect(buildDateFilter(undefined, '2024-01-31')).toBe('publishDate <= "2024-01-31"');
    });

    it('builds filter with both startDate and endDate', () => {
        expect(buildDateFilter('2024-01-01', '2024-01-31')).toBe(
            'publishDate >= "2024-01-01" and publishDate <= "2024-01-31"',
        );
    });
});
