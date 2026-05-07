import { CalendarBlank } from '@phosphor-icons/react';

import { EmptyState } from '@/components/ui/EmptyState';
import type { CalendarEvent } from '@/types/calendar';

import { formatDate, getDayOfMonth, getFulfillmentClass, getMonthDays, isToday, WEEKDAY_NAMES } from './calendarUtils';

interface MonthViewProps {
    currentDate: Date;
    viewEvents: CalendarEvent[];
    getEventsForDate: (date: Date) => CalendarEvent[];
    onDayClick: (date: Date) => void;
}

export function MonthView({ currentDate, viewEvents, getEventsForDate, onDayClick }: MonthViewProps) {
    return (
        <div className="calendar-page__month-view" data-component="month-view">
            <div className="calendar-page__weekday-header">
                {WEEKDAY_NAMES.map((day) => (
                    <div key={day} className="calendar-page__weekday-name">
                        {day}
                    </div>
                ))}
            </div>
            <div className="calendar-page__month-grid">
                {getMonthDays(currentDate.getFullYear(), currentDate.getMonth()).map((date, i) => {
                    const dayEvents = getEventsForDate(date);
                    const isCurrentMonth = date.getMonth() === currentDate.getMonth();
                    const isTodayDate = isToday(date);
                    return (
                        <button
                            key={i}
                            type="button"
                            className={`calendar-page__day-cell ${!isCurrentMonth ? 'calendar-page__day-cell--other-month' : ''} ${isTodayDate ? 'calendar-page__day-cell--today' : ''}`}
                            onClick={() => onDayClick(date)}
                            aria-label={formatDate(date)}
                            data-date={date.toISOString().split('T')[0]}
                            {...(isTodayDate ? { 'data-testid': 'calendar-page__day-cell--today' } : {})}
                        >
                            <span className="calendar-page__day-number">{getDayOfMonth(date)}</span>
                            {dayEvents.length > 0 && (
                                <div className="calendar-page__day-events">
                                    {dayEvents.slice(0, 3).map((evt) => (
                                        <div
                                            key={evt.id}
                                            className={`calendar-page__day-event ${getFulfillmentClass(evt.fulfillment)}`}
                                            data-testid="calendar-page__day-event"
                                            title={evt.title}
                                        >
                                            <span className="calendar-page__day-event-title">{evt.title}</span>
                                        </div>
                                    ))}
                                    {dayEvents.length > 3 && (
                                        <span className="calendar-page__day-more">+{dayEvents.length - 3} more</span>
                                    )}
                                </div>
                            )}
                        </button>
                    );
                })}
                {viewEvents.length === 0 && (
                    <div className="calendar-page__month-empty-overlay">
                        <EmptyState
                            icon={<CalendarBlank weight="duotone" />}
                            title="No events this month"
                            description="Try a different month or adjust your filters"
                        />
                    </div>
                )}
            </div>
        </div>
    );
}
