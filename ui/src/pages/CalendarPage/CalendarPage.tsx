import { useEffect, useMemo, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useOpenLocalContentPreview } from '@/components/content/LocalContentPreview';
import {
    CalendarBlankIcon,
    CaretLeftIcon,
    CaretRightIcon,
    ListBulletsIcon,
    SquaresFourIcon,
    RowsIcon,
    CalendarDotsIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Alert } from '@/components/ui/Alert';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { Slot } from '@/components/ui/Slot';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useCalendar } from '@/hooks/useCalendar';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import type { CalendarEvent, CalendarView } from '@/types/calendar';

import { CalendarEventList } from './CalendarEventList';
import { MonthView } from './MonthView';
import { WeekView } from './WeekView';
import {
    formatDate,
    formatDateKey,
    getMonthDays,
    getWeekStart,
    isSameDay,
    parseCalendarDate,
    WEEKDAY_NAMES,
} from './calendarUtils';
import {
    canonicalizeCalendarSearchParams,
    DEFAULT_CALENDAR_VIEW,
    normalizeCalendarView,
    parseCalendarDateParam,
} from './calendarUrlState';

import './CalendarPage.css';

export function CalendarPage() {
    useDocumentTitle('Calendar');
    const openPreview = useOpenLocalContentPreview();
    const [searchParams, setSearchParams] = useSearchParams();
    const [urlState, updateParams] = useUrlFilterState({
        view: DEFAULT_CALENDAR_VIEW,
        date: formatDateKey(new Date()),
    });

    useEffect(() => {
        const canonicalParams = canonicalizeCalendarSearchParams(searchParams);
        if (canonicalParams) {
            setSearchParams(canonicalParams, { replace: true });
        }
    }, [searchParams, setSearchParams]);

    const view: CalendarView = normalizeCalendarView(urlState.view) ?? DEFAULT_CALENDAR_VIEW;

    const currentDate: Date = useMemo(() => parseCalendarDateParam(urlState.date) ?? new Date(), [urlState.date]);

    const dateRange = useMemo(() => {
        switch (view) {
            case 'day':
                return { startDate: formatDateKey(currentDate), endDate: formatDateKey(currentDate) };
            case 'week': {
                const weekStart = getWeekStart(currentDate);
                const weekEnd = new Date(weekStart);
                weekEnd.setDate(weekStart.getDate() + 6);
                return { startDate: formatDateKey(weekStart), endDate: formatDateKey(weekEnd) };
            }
            case 'list': {
                const today = new Date();
                today.setHours(0, 0, 0, 0);
                const listEnd = new Date(today);
                listEnd.setDate(today.getDate() + 6);
                return { startDate: formatDateKey(today), endDate: formatDateKey(listEnd) };
            }
            case 'month':
            default: {
                const monthDays = getMonthDays(currentDate.getFullYear(), currentDate.getMonth());
                return {
                    startDate: formatDateKey(monthDays[0]),
                    endDate: formatDateKey(monthDays[monthDays.length - 1]),
                };
            }
        }
    }, [view, currentDate]);

    const { events, isLoading, isTruncated, error, refetch } = useCalendar(dateRange);
    const loadFailed = Boolean(error) && events.length === 0;

    const getEventsForDate = useCallback(
        (date: Date): CalendarEvent[] => {
            return events.filter((event) => isSameDay(parseCalendarDate(event.date), date));
        },
        [events],
    );

    const viewEvents = useMemo(() => {
        if (view === 'day') {
            return getEventsForDate(currentDate);
        }
        if (view !== 'list') {
            return [];
        }
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        const endExclusive = new Date(today);
        endExclusive.setDate(today.getDate() + 7);
        return events
            .filter((event) => {
                const eventDate = parseCalendarDate(event.date);
                return eventDate >= today && eventDate < endExclusive;
            })
            .sort((a, b) => parseCalendarDate(a.date).getTime() - parseCalendarDate(b.date).getTime());
    }, [view, currentDate, events, getEventsForDate]);

    const shiftDate = useCallback(
        (step: 1 | -1) => {
            let newDate: Date;
            switch (view) {
                case 'month':
                    newDate = new Date(currentDate.getFullYear(), currentDate.getMonth() + step, 1);
                    break;
                case 'week':
                    newDate = new Date(currentDate);
                    newDate.setDate(newDate.getDate() + step * 7);
                    break;
                case 'day':
                    newDate = new Date(currentDate);
                    newDate.setDate(newDate.getDate() + step);
                    break;
                case 'list':
                    return;
            }
            updateParams({ date: formatDateKey(newDate) });
        },
        [currentDate, view, updateParams],
    );

    const handleToday = useCallback(() => {
        updateParams({ date: formatDateKey(new Date()) });
    }, [updateParams]);

    const handleEventClick = useCallback(
        (event: CalendarEvent) => {
            openPreview(event.contentId);
        },
        [openPreview],
    );

    const handleDayClick = useCallback(
        (date: Date) => {
            updateParams({ view: 'day', date: formatDateKey(date) });
        },
        [updateParams],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const headerTitle = useMemo(() => {
        switch (view) {
            case 'month':
                return currentDate.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
            case 'week': {
                const weekStart = getWeekStart(currentDate);
                const weekEnd = new Date(weekStart);
                weekEnd.setDate(weekStart.getDate() + 6);
                return `${weekStart.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} - ${weekEnd.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}`;
            }
            case 'day':
                return formatDate(currentDate);
            case 'list':
                return 'Upcoming';
            default:
                return '';
        }
    }, [view, currentDate]);

    return (
        <div className="calendar-page" data-page="calendar">
            <PullToRefresh onRefresh={handleRefresh} className="calendar-page__pull-to-refresh">
                <PageHeader
                    className="calendar-page__header"
                    title="Calendar"
                    description="Upcoming release calendar for content in your library."
                    icon={<CalendarBlankIcon weight="duotone" data-testid="calendar-page__icon" aria-hidden="true" />}
                />

                <Slot slot="calendar.main">
                    {isTruncated && (
                        <Alert variant="warning" title="Not every calendar event is listed">
                            Only part of this date range was loaded. Select a shorter date range.
                        </Alert>
                    )}
                    <section className="calendar-page__controls" aria-label="Calendar navigation">
                        <div className="calendar-page__nav">
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => shiftDate(-1)}
                                disabled={view === 'list'}
                                aria-label="Previous"
                                data-action="prev"
                            >
                                <CaretLeftIcon weight="bold" />
                            </Button>
                            <span className="calendar-page__nav-title">{headerTitle}</span>
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => shiftDate(1)}
                                disabled={view === 'list'}
                                aria-label="Next"
                                data-action="next"
                            >
                                <CaretRightIcon weight="bold" />
                            </Button>
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={handleToday}
                                disabled={view === 'list'}
                                data-action="today"
                            >
                                Today
                            </Button>
                        </div>
                        <div className="calendar-page__view-switcher">
                            <button
                                type="button"
                                className={`calendar-page__view-btn ${view === 'month' ? 'calendar-page__view-btn--active' : ''}`}
                                onClick={() => updateParams({ view: 'month' })}
                                aria-label="Month view"
                                aria-pressed={view === 'month'}
                                data-view="month"
                                data-active={view === 'month' || undefined}
                            >
                                <SquaresFourIcon weight={view === 'month' ? 'fill' : 'regular'} />
                                <span className="calendar-page__view-btn-label">Month</span>
                            </button>
                            <button
                                type="button"
                                className={`calendar-page__view-btn ${view === 'week' ? 'calendar-page__view-btn--active' : ''}`}
                                onClick={() => updateParams({ view: 'week' })}
                                aria-label="Week view"
                                aria-pressed={view === 'week'}
                                data-view="week"
                                data-active={view === 'week' || undefined}
                            >
                                <RowsIcon weight={view === 'week' ? 'fill' : 'regular'} />
                                <span className="calendar-page__view-btn-label">Week</span>
                            </button>
                            <button
                                type="button"
                                className={`calendar-page__view-btn ${view === 'day' ? 'calendar-page__view-btn--active' : ''}`}
                                onClick={() => updateParams({ view: 'day' })}
                                aria-label="Day view"
                                aria-pressed={view === 'day'}
                                data-view="day"
                                data-active={view === 'day' || undefined}
                            >
                                <CalendarDotsIcon weight={view === 'day' ? 'fill' : 'regular'} />
                                <span className="calendar-page__view-btn-label">Day</span>
                            </button>
                            <button
                                type="button"
                                className={`calendar-page__view-btn calendar-page__view-btn--list ${view === 'list' ? 'calendar-page__view-btn--active' : ''}`}
                                onClick={() => updateParams({ view: 'list' })}
                                aria-label="List view"
                                aria-pressed={view === 'list'}
                                data-view="list"
                                data-active={view === 'list' || undefined}
                            >
                                <ListBulletsIcon weight={view === 'list' ? 'fill' : 'regular'} />
                                <span className="calendar-page__view-btn-label">List</span>
                            </button>
                        </div>
                    </section>

                    {isLoading && (
                        <div className="calendar-page__loading" data-testid="calendar-loading">
                            <div className="calendar-page__skeleton-header">
                                {WEEKDAY_NAMES.map((day) => (
                                    <Skeleton key={day} variant="text" width="2rem" height="1rem" />
                                ))}
                            </div>
                            <div className="calendar-page__skeleton-grid">
                                {Array.from({ length: 35 }, (_, i) => (
                                    <Skeleton key={i} variant="rect" height="4rem" />
                                ))}
                            </div>
                        </div>
                    )}

                    {!isLoading && loadFailed && (
                        <EmptyState
                            icon={<WarningCircleIcon weight="duotone" />}
                            title="Failed to load calendar events"
                            onRetry={refetch}
                        />
                    )}

                    {!isLoading && !loadFailed && view === 'month' && (
                        <MonthView
                            currentDate={currentDate}
                            getEventsForDate={getEventsForDate}
                            onDayClick={handleDayClick}
                        />
                    )}

                    {!isLoading && !loadFailed && view === 'week' && (
                        <WeekView
                            currentDate={currentDate}
                            getEventsForDate={getEventsForDate}
                            onDayClick={handleDayClick}
                            onEventClick={handleEventClick}
                        />
                    )}

                    {!isLoading && !loadFailed && view === 'day' && (
                        <CalendarEventList
                            viewEvents={viewEvents}
                            onEventClick={handleEventClick}
                            containerClassName="calendar-page__day-view"
                            dataComponent="day-view"
                            listClassName="calendar-page__day-events-list"
                            emptyTitle="No events on this day"
                            emptyDescription="Try a different date or check other views"
                        />
                    )}

                    {!isLoading && !loadFailed && view === 'list' && (
                        <CalendarEventList
                            viewEvents={viewEvents}
                            onEventClick={handleEventClick}
                            containerClassName="calendar-page__list-view"
                            dataComponent="list-view"
                            listClassName="calendar-page__list-items"
                            listTestId="calendar-page__list-items"
                            emptyTitle="No upcoming events"
                            emptyDescription="Content will appear here as premiere dates approach"
                            showDate
                        />
                    )}
                </Slot>
            </PullToRefresh>
        </div>
    );
}

CalendarPage.displayName = 'CalendarPage';

export default CalendarPage;
