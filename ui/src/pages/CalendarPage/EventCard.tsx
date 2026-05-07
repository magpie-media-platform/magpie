import { useState } from 'react';
import { ImageBroken } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import type { CalendarEvent } from '@/types/calendar';

import { getKindIcon, getFulfillmentClass, getFulfillmentVariant, formatTime } from './calendarUtils';

interface EventCardProps {
    event: CalendarEvent;
    onClick: (event: CalendarEvent) => void;
    compact?: boolean;
    showDate?: boolean;
}

export function EventCard({ event, onClick, compact = false, showDate = false }: EventCardProps) {
    const KindIcon = getKindIcon(event.kind);
    const [imageError, setImageError] = useState(false);

    return (
        <button
            type="button"
            className={`calendar-page__event-card ${getFulfillmentClass(event.fulfillment)} ${compact ? 'calendar-page__event-card--compact' : ''}`}
            onClick={() => onClick(event)}
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
                            <ImageBroken weight="duotone" aria-hidden="true" />
                        </div>
                    )}
                </div>
            )}
            <div className="calendar-page__event-info">
                <div className="calendar-page__event-header">
                    <span className="calendar-page__event-title">{event.title}</span>
                    <KindIcon weight="duotone" className="calendar-page__event-kind-icon" aria-hidden="true" />
                </div>
                {event.subtitle && <span className="calendar-page__event-subtitle">{event.subtitle}</span>}
                {showDate && (
                    <span className="calendar-page__event-date" data-testid="calendar-page__event-date">
                        {new Date(event.date).toLocaleDateString(undefined, {
                            weekday: 'short',
                            month: 'short',
                            day: 'numeric',
                        })}{' '}
                        <span className="calendar-page__event-time">{formatTime(event.date)}</span>
                    </span>
                )}
                {!compact && (
                    <Badge
                        variant={getFulfillmentVariant(event.fulfillment)}
                        size="sm"
                        className="calendar-page__event-badge"
                    >
                        {event.fulfillment}
                    </Badge>
                )}
            </div>
        </button>
    );
}
