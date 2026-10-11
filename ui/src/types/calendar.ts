import type { FulfillmentStatus, MonitoringMode } from './content';
import type { TemporalPrecision } from './contentMetadata';

export interface CalendarEvent {
    id: string;

    contentId: string;

    parentId?: string;

    title: string;

    subtitle?: string;

    date: string;

    datePrecision?: TemporalPrecision;

    posterUrl?: string;

    contentType: string;

    level: string;

    levelLabel?: string;

    fulfillment: FulfillmentStatus;

    effectiveMonitoring: MonitoringMode;
}

export type CalendarView = 'month' | 'week' | 'day' | 'list';

export interface CalendarQueryParams {
    startDate?: string;

    endDate?: string;

    contentType?: string;
}
