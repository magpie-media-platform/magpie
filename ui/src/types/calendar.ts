import type { FulfillmentStatus } from './content';

export interface CalendarEvent {
    id: string;

    contentId: string;

    parentId?: string;

    title: string;

    subtitle?: string;

    date: string;

    posterUrl?: string;

    contentType: string;

    level: string;

    levelLabel?: string;

    fulfillment: FulfillmentStatus;

    monitoring: boolean | null;

    effectiveMonitoring: boolean;
}

export type CalendarView = 'month' | 'week' | 'day' | 'list';

export interface CalendarQueryParams {
    startDate?: string;

    endDate?: string;

    contentType?: string;
}
