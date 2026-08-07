import type { CalendarEvent } from '@/types/calendar';

import { CalendarEventList } from './CalendarEventList';

interface ListViewProps {
    viewEvents: CalendarEvent[];
    onEventClick: (event: CalendarEvent) => void;
}

export function ListView({ viewEvents, onEventClick }: ListViewProps) {
    return (
        <CalendarEventList
            viewEvents={viewEvents}
            onEventClick={onEventClick}
            containerClassName="calendar-page__list-view"
            dataComponent="list-view"
            listClassName="calendar-page__list-items"
            listTestId="calendar-page__list-items"
            emptyTitle="No upcoming events"
            emptyDescription="Content will appear here as premiere dates approach"
            showDate
        />
    );
}
