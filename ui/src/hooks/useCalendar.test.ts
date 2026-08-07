import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

import { useCalendar, mapToCalendarEvent, buildDateFilter } from './useCalendar';
import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import type { FulfillmentStatusValue } from '@/types/content';

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

vi.mock('@/hooks/useInvalidateQueriesOnEvents', () => ({
    useInvalidateQueriesOnEvents: vi.fn(),
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
                    monitoring: true,
                    effectiveMonitoring: true,
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
                        fulfillment: 'Missing',
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
        expect(apiEvent!.contentType).toBe('episodes');
        expect(apiEvent!.level).toBe('episode');
        expect(apiEvent!.fulfillment).toBe('missing');
    });

    it('subscribes calendar data to content changes', async () => {
        mockApiGet.mockResolvedValueOnce({ items: [], pageNumber: 1, pageSize: 200, totalItems: 0 });

        renderHook(() => useCalendar(), {
            wrapper: createWrapper(),
        });

        expect(vi.mocked(useInvalidateQueriesOnEvents)).toHaveBeenCalledWith(
            expect.arrayContaining([
                'content.hierarchyItem.created',
                'content.hierarchyItem.updated',
                'content.fulfillment.changed',
                'metadata.refreshed',
                'download.imported',
            ]),
            [['calendar', 'events', undefined]],
            expect.objectContaining({
                debounceMs: 300,
                exact: true,
                maxWaitMs: 1200,
                owner: 'calendar-page',
                unprovableMembership: false,
            }),
        );
    });

    it('declares content type and date relevance for a filtered calendar', () => {
        mockApiGet.mockResolvedValueOnce({ items: [], pageNumber: 1, pageSize: 200, totalItems: 0 });
        renderHook(
            () =>
                useCalendar({
                    contentType: 'episodes',
                    endDate: '2026-07-31',
                    startDate: '2026-07-01',
                }),
            { wrapper: createWrapper() },
        );
        const options = vi.mocked(useInvalidateQueriesOnEvents).mock.calls[0]?.[2];

        expect(options?.owner).toBe('calendar-page');
        expect(options?.failOpenEventNames).toEqual(['content.hierarchyItem.created']);
        expect(options?.unprovableMembership).toBe(true);
        expect(
            options?.predicate?.({
                name: 'content.hierarchyItem.updated',
                context: { 'content.impact.contentType': 'movies' },
            }),
        ).toBe(false);
        expect(
            options?.predicate?.({
                name: 'content.hierarchyItem.updated',
                context: { 'content.impact.contentType': 'episodes' },
            }),
        ).toBe(true);
    });

    it('loads every page of matching events', async () => {
        const firstItem = {
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'first',
            name: 'First Movie',
            monitoring: true,
            effectiveMonitoring: true,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [
                    {
                        ordinal: 0,
                        title: 'First Movie',
                        publishDate: '2024-01-01T00:00:00Z',
                    },
                ],
            },
            contentItem: {
                id: 'ci-1',
                contentHierarchyItemId: 'test-1',
                fulfillment: 'Missing',
                createdAt: '2024-01-01T00:00:00Z',
            },
        };
        const secondItem = {
            ...firstItem,
            id: 'test-2',
            metadataContainerId: 'meta-2',
            slug: 'second',
            name: 'Second Movie',
            metadataContainer: {
                id: 'meta-2',
                contributions: [
                    {
                        ordinal: 0,
                        title: 'Second Movie',
                        publishDate: '2024-01-02T00:00:00Z',
                    },
                ],
            },
            contentItem: {
                id: 'ci-2',
                contentHierarchyItemId: 'test-2',
                fulfillment: 'Fulfilled',
                createdAt: '2024-01-01T00:00:00Z',
            },
        };

        const fullFirstPage = Array.from({ length: 200 }, (_, index) => ({
            ...firstItem,
            id: `test-${index}`,
            metadataContainerId: `meta-${index}`,
            slug: `movie-${index}`,
            name: `Movie ${index}`,
            metadataContainer: {
                id: `meta-${index}`,
                contributions: [
                    {
                        ordinal: 0,
                        title: `Movie ${index}`,
                        publishDate: '2024-01-01T00:00:00Z',
                    },
                ],
            },
            contentItem: {
                id: `ci-${index}`,
                contentHierarchyItemId: `test-${index}`,
                fulfillment: 'Missing',
                createdAt: '2024-01-01T00:00:00Z',
            },
        }));

        mockApiGet
            .mockResolvedValueOnce({
                items: fullFirstPage,
                pageNumber: 1,
                pageSize: 200,
                totalItems: 201,
            })
            .mockResolvedValueOnce({
                items: [secondItem],
                pageNumber: 2,
                pageSize: 200,
                totalItems: 201,
            });

        const { result } = renderHook(() => useCalendar(), {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        await waitFor(() => {
            expect(result.current.events).toHaveLength(201);
        });

        expect(result.current.events[200].title).toBe('Second Movie');
        expect(mockApiGet).toHaveBeenCalledTimes(2);
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
                    monitoring: true,
                    effectiveMonitoring: true,
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
                        fulfillment: 'Fulfilled',
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
                    monitoring: true,
                    effectiveMonitoring: true,
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
                        fulfillment: 'Missing',
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
                filter: 'publishDate >= "2024-01-01" and publishDate < "2024-02-01"',
                pageNumber: 1,
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
            parentId: 'season-1',
            metadataContainerId: 'meta-1',
            contentType: 'episodes',
            level: 'episode',
            slug: 'test-show',
            name: 'Test Episode Name',
            monitoring: true,
            effectiveMonitoring: true,
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
                fulfillment: 'Missing' as const,
                createdAt: '2024-01-01T00:00:00Z',
            },
        };

        const event = mapToCalendarEvent(item);

        expect(event).not.toBeNull();
        expect(event!.id).toBe('cal-test-1');
        expect(event!.contentId).toBe('test-1');
        expect(event!.parentId).toBe('season-1');
        expect(event!.title).toBe('Test Show');
        expect(event!.date).toBe('2024-06-15T20:00:00Z');
        expect(event!.contentType).toBe('episodes');
        expect(event!.level).toBe('episode');
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
            monitoring: true,
            effectiveMonitoring: true,
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
        const createItem = (fulfillment: FulfillmentStatusValue) => ({
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'test',
            name: 'Test',
            monitoring: true,
            effectiveMonitoring: true,
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

        expect(mapToCalendarEvent(createItem('Missing'))!.fulfillment).toBe('missing');
        expect(mapToCalendarEvent(createItem('Upgradable'))!.fulfillment).toBe('upgradable');
        expect(mapToCalendarEvent(createItem('Fulfilled'))!.fulfillment).toBe('fulfilled');
        expect(mapToCalendarEvent(createItem('FulfilledExternally'))!.fulfillment).toBe('fulfilledExternally');
    });

    it('carries the content type slug through unchanged', () => {
        const createItem = (contentType: string) => ({
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType,
            level: 'item',
            slug: 'test',
            name: 'Test',
            monitoring: true,
            effectiveMonitoring: true,
            createdAt: '2024-01-01T00:00:00Z',
            metadataContainer: {
                id: 'meta-1',
                contributions: [{ ordinal: 0, title: 'Test', publishDate: '2024-01-01T00:00:00Z' }],
            },
        });

        expect(mapToCalendarEvent(createItem('episodes'))!.contentType).toBe('episodes');
        expect(mapToCalendarEvent(createItem('movies'))!.contentType).toBe('movies');
        expect(mapToCalendarEvent(createItem('music'))!.contentType).toBe('music');
        expect(mapToCalendarEvent(createItem('books'))!.contentType).toBe('books');
        expect(mapToCalendarEvent(createItem('other'))!.contentType).toBe('other');
    });

    it('extracts poster URL from contributions', () => {
        const createItem = (imageUrl: string) => ({
            id: 'test-1',
            metadataContainerId: 'meta-1',
            contentType: 'movies',
            level: 'movie',
            slug: 'test',
            name: 'Test',
            monitoring: true,
            effectiveMonitoring: true,
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
            monitoring: true,
            effectiveMonitoring: true,
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
        expect(buildDateFilter(undefined, '2024-01-31')).toBe('publishDate < "2024-02-01"');
    });

    it('builds filter with both startDate and endDate', () => {
        expect(buildDateFilter('2024-01-01', '2024-01-31')).toBe(
            'publishDate >= "2024-01-01" and publishDate < "2024-02-01"',
        );
    });

    it('builds an exclusive end boundary across a leap day', () => {
        expect(buildDateFilter(undefined, '2024-02-29')).toBe('publishDate < "2024-03-01"');
    });
});
