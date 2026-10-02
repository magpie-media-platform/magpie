import { useState } from 'react';
import { ImageBrokenIcon } from '@phosphor-icons/react';

import { ContentFulfillmentBadge, ContentMonitoringIndicator } from '@/components/content/ContentCardPrimitives';
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
    const [imageError, setImageError] = useState(false);
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
                <div className="calendar-page__event-poster">
                    {event.posterUrl && !imageError ? (
                        <img src={event.posterUrl} alt="" loading="lazy" onError={() => setImageError(true)} />
                    ) : (
                        <div className="calendar-page__event-poster-placeholder">
                            <ImageBrokenIcon weight="duotone" aria-hidden="true" />
                        </div>
                    )}
                </div>
            )}
            <div className="calendar-page__event-info">
                <div className="calendar-page__event-header">
                    <ContentLineageTitle
                        className="calendar-page__event-title"
                        qualifiedName={{ id: event.contentId, parentId: event.parentId, title: event.title }}
                        fallback={{ contentType: event.contentType, level: event.level }}
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
                        monitoring={event.monitoring}
                        effectiveMonitoring={event.effectiveMonitoring}
                        className="calendar-page__event-monitoring"
                        activeClassName="calendar-page__event-monitoring--active"
                    />
                    <ContentFulfillmentBadge fulfillment={event.fulfillment} className="calendar-page__event-badge" />
                </span>
            </div>
        </button>
    );
}
