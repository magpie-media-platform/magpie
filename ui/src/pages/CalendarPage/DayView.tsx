import type { CalendarEvent } from '@/types/calendar';

import { CalendarEventList } from './CalendarEventList';

interface DayViewProps {
    viewEvents: CalendarEvent[];
    onEventClick: (event: CalendarEvent) => void;
}

export function DayView({ viewEvents, onEventClick }: DayViewProps) {
    return (
        <CalendarEventList
            viewEvents={viewEvents}
            onEventClick={onEventClick}
            containerClassName="calendar-page__day-view"
            dataComponent="day-view"
            listClassName="calendar-page__day-events-list"
            emptyTitle="No events on this day"
            emptyDescription="Try a different date or check other views"
        />
    );
}
