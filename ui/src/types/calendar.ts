import type { ContentKind, FulfillmentStatus } from './content';

export interface CalendarEvent {
    id: string;

    contentId: string;

    title: string;

    subtitle?: string;

    date: string;

    posterUrl?: string;

    kind: ContentKind;

    fulfillment: FulfillmentStatus;
}

export type CalendarView = 'month' | 'week' | 'day' | 'list';

export interface CalendarQueryParams {
    startDate?: string;

    endDate?: string;

    contentType?: string;
}
