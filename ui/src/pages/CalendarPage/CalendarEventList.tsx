import { CalendarBlankIcon } from '@phosphor-icons/react';

import { EmptyState } from '@/components/ui/EmptyState';
import type { CalendarEvent } from '@/types/calendar';

import { EventCard } from './EventCard';

interface CalendarEventListProps {
    viewEvents: CalendarEvent[];
    onEventClick: (event: CalendarEvent) => void;
    containerClassName: string;
    dataComponent: string;
    listClassName: string;
    listTestId?: string;
    emptyTitle: string;
    emptyDescription: string;
    showDate?: boolean;
}

export function CalendarEventList({
    viewEvents,
    onEventClick,
    containerClassName,
    dataComponent,
    listClassName,
    listTestId,
    emptyTitle,
    emptyDescription,
    showDate = false,
}: CalendarEventListProps) {
    return (
        <div className={containerClassName} data-component={dataComponent}>
            {viewEvents.length === 0 ? (
                <EmptyState
                    icon={<CalendarBlankIcon weight="duotone" />}
                    title={emptyTitle}
                    description={emptyDescription}
                />
            ) : (
                <div className={listClassName} data-testid={listTestId}>
                    {viewEvents.map((evt) => (
                        <EventCard key={evt.id} event={evt} onClick={onEventClick} showDate={showDate} />
                    ))}
                </div>
            )}
        </div>
    );
}
