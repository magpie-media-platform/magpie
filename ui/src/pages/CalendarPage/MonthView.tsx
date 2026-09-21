import type { CalendarEvent } from '@/types/calendar';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';

import {
    formatDate,
    formatDateKey,
    getDayOfMonth,
    getFirstDayOfWeek,
    getFulfillmentClass,
    getMonthDays,
    isToday,
    WEEKDAY_NAMES,
} from './calendarUtils';

interface MonthViewProps {
    currentDate: Date;
    getEventsForDate: (date: Date) => CalendarEvent[];
    onDayClick: (date: Date) => void;
}

export function MonthView({ currentDate, getEventsForDate, onDayClick }: MonthViewProps) {
    const firstDayOfWeek = getFirstDayOfWeek();

    return (
        <div className="calendar-page__month-view" data-component="month-view">
            <div className="calendar-page__weekday-header">
                {WEEKDAY_NAMES.map((_, offset) => {
                    const name = WEEKDAY_NAMES[(firstDayOfWeek + offset) % 7];
                    return (
                        <div key={name} className="calendar-page__weekday-name">
                            {name}
                        </div>
                    );
                })}
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
                            data-date={formatDateKey(date)}
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
                                        >
                                            <ContentLineageTitle
                                                className="calendar-page__day-event-title"
                                                qualifiedName={{
                                                    id: evt.contentId,
                                                    parentId: evt.parentId,
                                                    title: evt.title,
                                                }}
                                                fallback={{ contentType: evt.contentType, level: evt.level }}
                                            />
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
            </div>
        </div>
    );
}
