import { ImageBrokenIcon } from '@phosphor-icons/react';

import {
    ContentFulfillmentBadge,
    ContentMonitoringIndicator,
    ContentPoster,
} from '@/components/content/ContentCardPrimitives';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { useTypeIconName } from '@/hooks/useLibraryTypes';
import type { CalendarEvent } from '@/types/calendar';

import { formatTime, parseCalendarDate, parseCalendarDateParts } from './calendarUtils';

interface EventCardProps {
    event: CalendarEvent;
    onClick: (event: CalendarEvent) => void;
    compact?: boolean;
    showDate?: boolean;
}

export function EventCard({ event, onClick, compact = false, showDate = false }: EventCardProps) {
    const typeIconName = useTypeIconName();
    const posterPlaceholder = <ImageBrokenIcon weight="duotone" aria-hidden="true" />;
    const eventDate = parseCalendarDate(event.date);
    const hasTimeOfDay = parseCalendarDateParts(event.date) === null;

    return (
        <button
            type="button"
            className={`calendar-page__event-card ${compact ? 'calendar-page__event-card--compact' : ''}`}
            onClick={(clickEvent) => {
                clickEvent.stopPropagation();
                onClick(event);
            }}
            data-testid="calendar-page__event-card"
            data-event-id={event.id}
            data-content-id={event.contentId}
            data-fulfillment={event.fulfillment}
        >
            {!compact && (
                <ContentPoster
                    posterUrl={event.posterUrl}
                    alt=""
                    className="calendar-page__event-poster"
                    placeholderClassName="calendar-page__event-poster-placeholder"
                    placeholder={posterPlaceholder}
                    failurePlaceholder={posterPlaceholder}
                />
            )}
            <div className="calendar-page__event-info">
                <div className="calendar-page__event-header">
                    <ContentLineageTitle
                        className="calendar-page__event-title"
                        qualifiedName={{ id: event.contentId, parentId: event.parentId, title: event.title }}
                        fallback={{ contentType: event.contentType, level: event.level }}
                        stacked
                    />
                    <CdnPhosphorIcon
                        name={typeIconName(event.contentType)}
                        weight="duotone"
                        className="calendar-page__event-kind-icon"
                        aria-hidden="true"
                    />
                </div>
                {event.subtitle && <span className="calendar-page__event-subtitle">{event.subtitle}</span>}
                {showDate && (
                    <span className="calendar-page__event-date" data-testid="calendar-page__event-date">
                        {eventDate.toLocaleDateString(undefined, {
                            weekday: 'short',
                            month: 'short',
                            day: 'numeric',
                        })}{' '}
                        {hasTimeOfDay && <span className="calendar-page__event-time">{formatTime(event.date)}</span>}
                    </span>
                )}
                <span className="calendar-page__event-statuses">
                    <ContentMonitoringIndicator
                        effectiveMonitoring={event.effectiveMonitoring}
                        className="calendar-page__event-monitoring"
                        activeClassName="calendar-page__event-monitoring--active"
                    />
                    <ContentFulfillmentBadge
                        fulfillment={event.fulfillment}
                        publishDate={event.date}
                        publishDatePrecision={event.datePrecision}
                        className="calendar-page__event-badge"
                    />
                </span>
            </div>
        </button>
    );
}
