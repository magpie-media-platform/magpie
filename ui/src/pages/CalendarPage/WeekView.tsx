import type { CalendarEvent } from '@/types/calendar';

import { EventCard } from './EventCard';
import { formatDate, formatDateKey, getWeekDays, getWeekStart, isToday, WEEKDAY_NAMES } from './calendarUtils';

interface WeekViewProps {
    currentDate: Date;
    getEventsForDate: (date: Date) => CalendarEvent[];
    onDayClick: (date: Date) => void;
    onEventClick: (event: CalendarEvent) => void;
}

export function WeekView({ currentDate, getEventsForDate, onDayClick, onEventClick }: WeekViewProps) {
    return (
        <div className="calendar-page__week-view" data-component="week-view">
            <div className="calendar-page__weekday-header">
                {getWeekDays(getWeekStart(currentDate)).map((date, i) => (
                    <button
                        key={i}
                        type="button"
                        className={`calendar-page__weekday-col ${isToday(date) ? 'calendar-page__weekday-col--today' : ''}`}
                        onClick={() => onDayClick(date)}
                        aria-label={formatDate(date)}
                    >
                        <span className="calendar-page__weekday-name">{WEEKDAY_NAMES[date.getDay()]}</span>
                        <span className="calendar-page__weekday-date" data-testid="calendar-page__weekday-date">
                            {date.getDate()}
                        </span>
                    </button>
                ))}
            </div>
            <div className="calendar-page__week-grid">
                {getWeekDays(getWeekStart(currentDate)).map((date, i) => {
                    const dayEvents = getEventsForDate(date);
                    return (
                        <div
                            key={i}
                            className={`calendar-page__week-col ${isToday(date) ? 'calendar-page__week-col--today' : ''}`}
                            data-date={formatDateKey(date)}
                            {...(isToday(date) ? { 'data-testid': 'calendar-page__week-col--today' } : {})}
                        >
                            <button
                                type="button"
                                className="calendar-page__week-day-heading"
                                onClick={() => onDayClick(date)}
                                aria-label={formatDate(date)}
                            >
                                <span>{WEEKDAY_NAMES[date.getDay()]}</span>
                                <span>{date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
                            </button>
                            {dayEvents.map((evt) => (
                                <EventCard key={evt.id} event={evt} onClick={onEventClick} compact />
                            ))}
                            {dayEvents.length === 0 && (
                                <span className="calendar-page__week-day-empty" aria-hidden="true">
                                    No events
                                </span>
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
}
