import { CalendarBlank } from '@phosphor-icons/react';

import { EmptyState } from '@/components/ui/EmptyState';
import type { CalendarEvent } from '@/types/calendar';

import { EventCard } from './EventCard';

interface DayViewProps {
    viewEvents: CalendarEvent[];
    onEventClick: (event: CalendarEvent) => void;
}

export function DayView({ viewEvents, onEventClick }: DayViewProps) {
    return (
        <div className="calendar-page__day-view" data-component="day-view">
            {viewEvents.length === 0 ? (
                <EmptyState
                    icon={<CalendarBlank weight="duotone" />}
                    title="No events on this day"
                    description="Try a different date or check other views"
                />
            ) : (
                <div className="calendar-page__day-events-list">
                    {viewEvents.map((evt) => (
                        <EventCard key={evt.id} event={evt} onClick={onEventClick} />
                    ))}
                </div>
            )}
        </div>
    );
}
