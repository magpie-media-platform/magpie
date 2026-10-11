import { useQuery, useQueryClient } from '@tanstack/react-query';

import { useInvalidateQueriesOnEvents } from '@/hooks/useInvalidateQueriesOnEvents';
import { apiGet, buildUrl } from '@/lib/api';
import { extractContentFields, type ApiContentItemWithMetadata } from '@/lib/contentMapping';
import { MAX_PAGE_FETCHES } from '@/lib/paging';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { escapeQueryString } from '@/lib/simpleFilterQuery';
import { queryErrorOrNull } from '@/lib/utils';
import type { CalendarEvent, CalendarQueryParams } from '@/types/calendar';
import type { PagedList } from '@/types/paging';

const CALENDAR_PAGE_SIZE = 200;

function mapToCalendarEvent(item: ApiContentItemWithMetadata): CalendarEvent | null {
    const fields = extractContentFields(item);
    if (!fields.date) {
        return null;
    }

    return {
        id: `cal-${item.id}`,
        contentId: item.id,
        parentId: item.parentId ?? undefined,
        title: fields.title,
        subtitle: fields.subtitle,
        date: fields.date,
        datePrecision: fields.datePrecision,
        posterUrl: fields.posterUrl,
        contentType: fields.contentType,
        level: fields.level,
        levelLabel: fields.levelLabel,
        fulfillment: fields.fulfillment,
        effectiveMonitoring: item.effectiveMonitoring,
    };
}

const calendarQueryKeys = {
    events: (params?: CalendarQueryParams) => ['calendar', 'events', params] as const,
};

function buildDateFilter(startDate?: string, endDate?: string): string | undefined {
    const clauses: string[] = [];
    if (startDate) {
        clauses.push(`publishDate >= "${escapeQueryString(startDate)}"`);
    }
    if (endDate) {
        const exclusiveEnd = new Date(`${endDate}T00:00:00.000Z`);
        exclusiveEnd.setUTCDate(exclusiveEnd.getUTCDate() + 1);
        clauses.push(`publishDate < "${escapeQueryString(exclusiveEnd.toISOString().slice(0, 10))}"`);
    }
    return clauses.length > 0 ? clauses.join(' and ') : undefined;
}

async function fetchCalendarEvents(params?: CalendarQueryParams) {
    const filter = buildDateFilter(params?.startDate, params?.endDate);
    const events: CalendarEvent[] = [];
    let loadedItems = 0;
    let totalItems = 0;

    for (let pageNumber = 1; pageNumber <= MAX_PAGE_FETCHES; pageNumber += 1) {
        const url = buildUrl('/content', {
            contentType: params?.contentType,
            filter,
            pageNumber,
            pageSize: CALENDAR_PAGE_SIZE,
        });

        const response = await apiGet<PagedList<ApiContentItemWithMetadata>>(url);
        loadedItems += response.items.length;
        totalItems = response.totalItems;
        events.push(
            ...response.items.map(mapToCalendarEvent).filter((event): event is CalendarEvent => event !== null),
        );

        if (
            response.items.length === 0 ||
            response.items.length < CALENDAR_PAGE_SIZE ||
            loadedItems >= response.totalItems
        ) {
            break;
        }
    }
    return { events, truncated: loadedItems < totalItems };
}

interface CalendarResult {
    events: CalendarEvent[];
    isLoading: boolean;
    isFetching: boolean;
    isTruncated: boolean;
    error: Error | null;
    refetch: () => Promise<void>;
}

export function useCalendar(params?: CalendarQueryParams): CalendarResult {
    const queryClient = useQueryClient();
    const hasFilters = Boolean(params?.contentType || params?.endDate || params?.startDate);
    useInvalidateQueriesOnEvents(
        [
            'content.hierarchyItem.created',
            'content.hierarchyItem.deleted',
            'content.hierarchyItem.updated',
            'content.fulfillment.changed',
            'metadata.refreshed',
            'download.imported',
        ],
        [calendarQueryKeys.events(params)],
        {
            debounceMs: 300,
            exact: true,
            failOpenEventNames: hasFilters ? ['content.hierarchyItem.created'] : undefined,
            maxWaitMs: 1200,
            predicate: (event) => {
                const current = queryClient.getQueryData<Awaited<ReturnType<typeof fetchCalendarEvents>>>(
                    calendarQueryKeys.events(params),
                );
                if (
                    current?.events.some(
                        (calendarEvent) => calendarEvent.contentId === event.context['content.hierarchyItemId'],
                    )
                ) {
                    return true;
                }
                if (!hasFilters) {
                    return true;
                }
                const contentType = event.context['content.impact.contentType'];
                return !params?.contentType || !contentType || contentType === params.contentType;
            },
            unprovableMembership: hasFilters,
        },
    );

    const query = useQuery({
        queryKey: calendarQueryKeys.events(params),
        queryFn: () => fetchCalendarEvents(params),
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    const refetch = async (): Promise<void> => {
        await query.refetch();
    };

    return {
        events: query.data?.events ?? [],
        isTruncated: query.data?.truncated ?? false,
        isLoading: query.isLoading,
        isFetching: query.isFetching,
        error: queryErrorOrNull(query),
        refetch,
    };
}

export { mapToCalendarEvent, buildDateFilter };
