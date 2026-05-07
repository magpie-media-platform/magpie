import { CalendarBlank } from '@phosphor-icons/react';

import { EmptyState } from '@/components/ui/EmptyState';
import type { CalendarEvent } from '@/types/calendar';

import { EventCard } from './EventCard';

interface ListViewProps {
    viewEvents: CalendarEvent[];
    onEventClick: (event: CalendarEvent) => void;
}

export function ListView({ viewEvents, onEventClick }: ListViewProps) {
    return (
        <div className="calendar-page__list-view" data-component="list-view">
            {viewEvents.length === 0 ? (
                <EmptyState
                    icon={<CalendarBlank weight="duotone" />}
                    title="No upcoming events"
                    description="Content will appear here as premiere dates approach"
                />
            ) : (
                <div className="calendar-page__list-items" data-testid="calendar-page__list-items">
                    {viewEvents.map((evt) => (
                        <EventCard key={evt.id} event={evt} onClick={onEventClick} showDate />
                    ))}
                </div>
            )}
        </div>
    );
}
