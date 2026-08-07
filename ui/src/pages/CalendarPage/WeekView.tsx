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
                    <div
                        key={i}
                        className={`calendar-page__weekday-col ${isToday(date) ? 'calendar-page__weekday-col--today' : ''}`}
                    >
                        <span className="calendar-page__weekday-name">{WEEKDAY_NAMES[date.getDay()]}</span>
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
                            data-date={formatDateKey(date)}
                            {...(isToday(date) ? { 'data-testid': 'calendar-page__week-col--today' } : {})}
                        >
                            <div className="calendar-page__week-day-heading" aria-hidden="true">
                                <span>{WEEKDAY_NAMES[date.getDay()]}</span>
                                <span>{date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</span>
                            </div>
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
