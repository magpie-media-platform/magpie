import { useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    CalendarBlank,
    CaretLeft,
    CaretRight,
    ListBullets,
    SquaresFour,
    Rows,
    CalendarDots,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { CalendarMainSlot } from '@/components/ui/Slot/CalendarSlots';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useCalendar } from '@/hooks/useCalendar';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import type { CalendarEvent, CalendarView } from '@/types/calendar';

import { DayView } from './DayView';
import { ListView } from './ListView';
import { MonthView } from './MonthView';
import { WeekView } from './WeekView';
import { formatDate, getMonthDays, getWeekStart, isSameDay, WEEKDAY_NAMES } from './calendarUtils';

import './CalendarPage.css';

const ALL_VALUE = '__all__';

const VALID_VIEWS: CalendarView[] = ['month', 'week', 'day', 'list'];

const VALID_KINDS = ['episode', 'movie', 'music', 'book'];

function parseDateParam(value: string | null): Date | null {
    if (!value) {
        return null;
    }
    const parsed = new Date(value + 'T00:00:00');
    if (isNaN(parsed.getTime())) {
        return null;
    }
    return parsed;
}

function formatDateParam(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
}

const kindFilterOptions = [
    { value: ALL_VALUE, label: 'All Types' },
    { value: 'episode', label: 'Episodes' },
    { value: 'movie', label: 'Movies' },
    { value: 'music', label: 'Music' },
    { value: 'book', label: 'Books' },
];

export function CalendarPage() {
    useDocumentTitle('Calendar');
    const navigate = useNavigate();
    const [urlState, updateParams] = useUrlFilterState({
        view: 'month',
        date: formatDateParam(new Date()),
        kind: ALL_VALUE,
    });

    const view: CalendarView = VALID_VIEWS.includes(urlState.view as CalendarView)
        ? (urlState.view as CalendarView)
        : 'month';

    const currentDate: Date = useMemo(() => parseDateParam(urlState.date) ?? new Date(), [urlState.date]);

    const kindFilter: string = VALID_KINDS.includes(urlState.kind) ? urlState.kind : ALL_VALUE;

    const dateRange = useMemo(() => {
        const toIso = (d: Date): string => d.toISOString().split('T')[0];
        switch (view) {
            case 'day':
                return { startDate: toIso(currentDate), endDate: toIso(currentDate) };
            case 'week': {
                const weekStart = getWeekStart(currentDate);
                const weekEnd = new Date(weekStart);
                weekEnd.setDate(weekStart.getDate() + 6);
                return { startDate: toIso(weekStart), endDate: toIso(weekEnd) };
            }
            case 'list': {
                const today = new Date();
                today.setHours(0, 0, 0, 0);
                const listEnd = new Date(today);
                listEnd.setDate(today.getDate() + 6);
                return { startDate: toIso(today), endDate: toIso(listEnd) };
            }
            case 'month':
            default: {
                const monthDays = getMonthDays(currentDate.getFullYear(), currentDate.getMonth());
                return { startDate: toIso(monthDays[0]), endDate: toIso(monthDays[monthDays.length - 1]) };
            }
        }
    }, [view, currentDate]);

    const { events, isLoading, refetch } = useCalendar(dateRange);

    const filteredEvents = useMemo(() => {
        if (kindFilter === ALL_VALUE) {
            return events;
        }
        return events.filter((e) => e.kind === kindFilter);
    }, [events, kindFilter]);

    const getEventsForDate = useCallback(
        (date: Date): CalendarEvent[] => {
            return filteredEvents.filter((e) => isSameDay(new Date(e.date), date));
        },
        [filteredEvents],
    );

    const viewEvents = useMemo(() => {
        switch (view) {
            case 'day':
                return getEventsForDate(currentDate);
            case 'week': {
                const weekStart = getWeekStart(currentDate);
                const weekEnd = new Date(weekStart);
                weekEnd.setDate(weekStart.getDate() + 6);
                return filteredEvents.filter((e) => {
                    const eventDate = new Date(e.date);
                    return eventDate >= weekStart && eventDate <= weekEnd;
                });
            }
            case 'list': {
                const today = new Date();
                today.setHours(0, 0, 0, 0);
                const weekEnd = new Date(today);
                weekEnd.setDate(today.getDate() + 6);
                return filteredEvents
                    .filter((e) => {
                        const eventDate = new Date(e.date);
                        return eventDate >= today && eventDate <= weekEnd;
                    })
                    .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
            }
            case 'month': {
                const monthDays = getMonthDays(currentDate.getFullYear(), currentDate.getMonth());
                const monthStart = monthDays[0];
                const monthEnd = monthDays[monthDays.length - 1];
                return filteredEvents.filter((e) => {
                    const eventDate = new Date(e.date);
                    return eventDate >= monthStart && eventDate <= monthEnd;
                });
            }
            default:
                return filteredEvents;
        }
    }, [view, currentDate, filteredEvents, getEventsForDate]);

    const handlePrev = useCallback(() => {
        let newDate: Date;
        switch (view) {
            case 'month':
                newDate = new Date(currentDate.getFullYear(), currentDate.getMonth() - 1, 1);
                break;
            case 'week':
                newDate = new Date(currentDate);
                newDate.setDate(newDate.getDate() - 7);
                break;
            case 'day':
                newDate = new Date(currentDate);
                newDate.setDate(newDate.getDate() - 1);
                break;
            default:
                return;
        }
        updateParams({ date: formatDateParam(newDate) });
    }, [currentDate, view, updateParams]);

    const handleNext = useCallback(() => {
        let newDate: Date;
        switch (view) {
            case 'month':
                newDate = new Date(currentDate.getFullYear(), currentDate.getMonth() + 1, 1);
                break;
            case 'week':
                newDate = new Date(currentDate);
                newDate.setDate(newDate.getDate() + 7);
                break;
            case 'day':
                newDate = new Date(currentDate);
                newDate.setDate(newDate.getDate() + 1);
                break;
            default:
                return;
        }
        updateParams({ date: formatDateParam(newDate) });
    }, [currentDate, view, updateParams]);

    const handleToday = useCallback(() => {
        updateParams({ date: formatDateParam(new Date()) });
    }, [updateParams]);

    const handleEventClick = useCallback(
        (event: CalendarEvent) => {
            navigate(`/library/${event.contentId}`);
        },
        [navigate],
    );

    const handleDayClick = useCallback(
        (date: Date) => {
            updateParams({ view: 'day', date: formatDateParam(date) });
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
                <header className="calendar-page__header">
                    <div className="calendar-page__header-left">
                        <CalendarBlank
                            weight="duotone"
                            className="calendar-page__icon"
                            data-testid="calendar-page__icon"
                            aria-hidden="true"
                        />
                        <h1 className="calendar-page__title">Calendar</h1>
                    </div>
                    <div className="calendar-page__header-actions">
                        <Select value={kindFilter} onValueChange={(val) => updateParams({ kind: val })}>
                            <SelectTrigger className="calendar-page__filter-trigger" data-filter="kind">
                                <SelectValue placeholder="All Types" />
                            </SelectTrigger>
                            <SelectContent>
                                {kindFilterOptions.map((opt) => (
                                    <SelectItem key={opt.value} value={opt.value}>
                                        {opt.label}
                                    </SelectItem>
                                ))}
                            </SelectContent>
                        </Select>
                    </div>
                </header>

                <CalendarMainSlot>
                    <section className="calendar-page__controls" aria-label="Calendar navigation">
                        <div className="calendar-page__nav">
                            <Button
                                variant="ghost"
                                size="sm"
                                onClick={handlePrev}
                                aria-label="Previous"
                                data-action="prev"
                            >
                                <CaretLeft weight="bold" />
                            </Button>
                            <span className="calendar-page__nav-title">{headerTitle}</span>
                            <Button variant="ghost" size="sm" onClick={handleNext} aria-label="Next" data-action="next">
                                <CaretRight weight="bold" />
                            </Button>
                            <Button variant="secondary" size="sm" onClick={handleToday} data-action="today">
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
                                <SquaresFour weight={view === 'month' ? 'fill' : 'regular'} />
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
                                <Rows weight={view === 'week' ? 'fill' : 'regular'} />
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
                                <CalendarDots weight={view === 'day' ? 'fill' : 'regular'} />
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
                                <ListBullets weight={view === 'list' ? 'fill' : 'regular'} />
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

                    {!isLoading && view === 'month' && (
                        <MonthView
                            currentDate={currentDate}
                            viewEvents={viewEvents}
                            getEventsForDate={getEventsForDate}
                            onDayClick={handleDayClick}
                        />
                    )}

                    {!isLoading && view === 'week' && (
                        <WeekView
                            currentDate={currentDate}
                            viewEvents={viewEvents}
                            getEventsForDate={getEventsForDate}
                            onDayClick={handleDayClick}
                            onEventClick={handleEventClick}
                        />
                    )}

                    {!isLoading && view === 'day' && (
                        <DayView viewEvents={viewEvents} onEventClick={handleEventClick} />
                    )}

                    {!isLoading && view === 'list' && (
                        <ListView viewEvents={viewEvents} onEventClick={handleEventClick} />
                    )}
                </CalendarMainSlot>
            </PullToRefresh>
        </div>
    );
}

CalendarPage.displayName = 'CalendarPage';

export default CalendarPage;
