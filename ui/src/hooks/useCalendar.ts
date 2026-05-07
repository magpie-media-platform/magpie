import { useQuery } from '@tanstack/react-query';

import { apiGet, buildUrl } from '@/lib/api';
import { extractContentFields, type ApiContentItemWithMetadata } from '@/lib/contentMapping';
import { POLLING_INTERVALS } from '@/lib/queryClient';
import { queryErrorOrNull } from '@/lib/utils';
import type { CalendarEvent, CalendarQueryParams } from '@/types/calendar';
import type { PagedList } from '@/types/paging';

function mapToCalendarEvent(item: ApiContentItemWithMetadata): CalendarEvent | null {
    const fields = extractContentFields(item);
    if (!fields.date) {
        return null;
    }

    return {
        id: `cal-${item.id}`,
        contentId: item.id,
        title: fields.title,
        subtitle: fields.subtitle,
        date: fields.date,
        posterUrl: fields.posterUrl,
        kind: fields.kind,
        fulfillment: fields.fulfillment,
    };
}

export const calendarQueryKeys = {
    all: ['calendar'] as const,
    events: (params?: CalendarQueryParams) => ['calendar', 'events', params] as const,
};

function buildDateFilter(startDate?: string, endDate?: string): string | undefined {
    const clauses: string[] = [];
    if (startDate) {
        clauses.push(`publishDate >= "${startDate}"`);
    }
    if (endDate) {
        clauses.push(`publishDate <= "${endDate}"`);
    }
    return clauses.length > 0 ? clauses.join(' and ') : undefined;
}

async function fetchCalendarEvents(params?: CalendarQueryParams): Promise<CalendarEvent[]> {
    const filter = buildDateFilter(params?.startDate, params?.endDate);

    const url = buildUrl('/content', {
        contentType: params?.contentType,
        filter,
        pageSize: 200,
    });

    const response = await apiGet<PagedList<ApiContentItemWithMetadata>>(url);

    return response.items.map(mapToCalendarEvent).filter((event): event is CalendarEvent => event !== null);
}

export interface CalendarResult {
    events: CalendarEvent[];
    isLoading: boolean;
    isFetching: boolean;
    error: Error | null;
    refetch: () => void;
}

export function useCalendar(params?: CalendarQueryParams): CalendarResult {
    const query = useQuery({
        queryKey: calendarQueryKeys.events(params),
        queryFn: () => fetchCalendarEvents(params),
        refetchInterval: POLLING_INTERVALS.normal,
        staleTime: POLLING_INTERVALS.normal / 2,
    });

    return {
        events: query.data ?? [],
        isLoading: query.isLoading,
        isFetching: query.isFetching,
        error: queryErrorOrNull(query),
        refetch: query.refetch,
    };
}

export { mapToCalendarEvent, buildDateFilter };
