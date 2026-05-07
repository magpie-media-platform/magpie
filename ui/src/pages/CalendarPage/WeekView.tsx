import { CalendarBlank } from '@phosphor-icons/react';

import { EmptyState } from '@/components/ui/EmptyState';
import type { CalendarEvent } from '@/types/calendar';

import { EventCard } from './EventCard';
import { formatDate, getWeekDays, getWeekStart, isToday, WEEKDAY_NAMES } from './calendarUtils';

interface WeekViewProps {
    currentDate: Date;
    viewEvents: CalendarEvent[];
    getEventsForDate: (date: Date) => CalendarEvent[];
    onDayClick: (date: Date) => void;
    onEventClick: (event: CalendarEvent) => void;
}

export function WeekView({ currentDate, viewEvents, getEventsForDate, onDayClick, onEventClick }: WeekViewProps) {
    return (
        <div className="calendar-page__week-view" data-component="week-view">
            <div className="calendar-page__weekday-header">
                {getWeekDays(getWeekStart(currentDate)).map((date, i) => (
                    <div
                        key={i}
                        className={`calendar-page__weekday-col ${isToday(date) ? 'calendar-page__weekday-col--today' : ''}`}
                    >
                        <span className="calendar-page__weekday-name">{WEEKDAY_NAMES[i]}</span>
                        <span className="calendar-page__weekday-date" data-testid="calendar-page__weekday-date">
                            {date.getDate()}
                        </span>
                    </div>
                ))}
            </div>
            <div className="calendar-page__week-grid">
                {getWeekDays(getWeekStart(currentDate)).map((date, i) => {
                    const dayEvents = getEventsForDate(date);
                    return (
                        <div
                            key={i}
                            role="button"
                            tabIndex={0}
                            className={`calendar-page__week-col ${isToday(date) ? 'calendar-page__week-col--today' : ''}`}
                            onClick={() => onDayClick(date)}
                            onKeyDown={(e) => {
                                if (e.key === 'Enter' || e.key === ' ') {
                                    e.preventDefault();
                                    onDayClick(date);
                                }
                            }}
                            aria-label={formatDate(date)}
                            data-date={date.toISOString().split('T')[0]}
                            {...(isToday(date) ? { 'data-testid': 'calendar-page__week-col--today' } : {})}
                        >
                            {dayEvents.map((evt) => (
                                <EventCard key={evt.id} event={evt} onClick={onEventClick} compact />
                            ))}
                        </div>
                    );
                })}
                {viewEvents.length === 0 && (
                    <div className="calendar-page__week-empty-overlay">
                        <EmptyState
                            icon={<CalendarBlank weight="duotone" />}
                            title="No events this week"
                            description="Try a different week or adjust your filters"
                        />
                    </div>
                )}
            </div>
        </div>
    );
}
