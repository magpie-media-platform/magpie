import type { CalendarView } from '@/types/calendar';

import { formatDateKey } from './calendarUtils';

const CALENDAR_URL_PARAMS = {
    view: 'view',
    date: 'date',
} as const;

export const DEFAULT_CALENDAR_VIEW: CalendarView = 'month';

const CALENDAR_VIEWS = ['month', 'week', 'day', 'list'] as const satisfies readonly CalendarView[];

export function normalizeCalendarView(value: string | null): CalendarView | null {
    if (!value) {
        return null;
    }

    const normalized = value.trim().toLowerCase();
    return CALENDAR_VIEWS.includes(normalized as CalendarView) ? (normalized as CalendarView) : null;
}

export function parseCalendarDateParam(value: string | null): Date | null {
    if (!value) {
        return null;
    }

    const trimmed = value.trim();
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
    if (!match) {
        return null;
    }

    const year = Number(match[1]);
    const month = Number(match[2]) - 1;
    const day = Number(match[3]);
    const parsed = new Date(year, month, day);
    parsed.setHours(0, 0, 0, 0);

    return formatDateKey(parsed) === trimmed ? parsed : null;
}

export function canonicalizeCalendarSearchParams(
    searchParams: URLSearchParams,
    today: Date = new Date(),
): URLSearchParams | null {
    const next = new URLSearchParams(searchParams);
    const todayKey = formatDateKey(today);

    const rawView = next.get(CALENDAR_URL_PARAMS.view);
    if (rawView !== null) {
        const normalizedView = normalizeCalendarView(rawView);
        if (!normalizedView || normalizedView === DEFAULT_CALENDAR_VIEW) {
            next.delete(CALENDAR_URL_PARAMS.view);
        } else if (normalizedView !== rawView) {
            next.set(CALENDAR_URL_PARAMS.view, normalizedView);
        }
    }

    const rawDate = next.get(CALENDAR_URL_PARAMS.date);
    if (rawDate !== null) {
        const parsedDate = parseCalendarDateParam(rawDate);
        if (!parsedDate) {
            next.delete(CALENDAR_URL_PARAMS.date);
        } else {
            const normalizedDate = formatDateKey(parsedDate);
            if (normalizedDate === todayKey) {
                next.delete(CALENDAR_URL_PARAMS.date);
            } else if (normalizedDate !== rawDate) {
                next.set(CALENDAR_URL_PARAMS.date, normalizedDate);
            }
        }
    }

    return next.toString() === searchParams.toString() ? null : next;
}
