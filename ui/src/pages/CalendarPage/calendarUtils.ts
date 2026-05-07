import { FilmSlate, Television, MusicNote, Book } from '@phosphor-icons/react';

import type { ContentKind, FulfillmentStatus } from '@/types/content';

export function getKindIcon(kind: ContentKind) {
    switch (kind) {
        case 'episode':
            return Television;
        case 'movie':
            return FilmSlate;
        case 'music':
            return MusicNote;
        case 'book':
            return Book;
        default:
            return FilmSlate;
    }
}

export function getFulfillmentClass(status: FulfillmentStatus): string {
    return `calendar-page__event--${status}`;
}

export function getFulfillmentVariant(
    status: FulfillmentStatus,
): 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally' {
    return status;
}

export function formatDate(date: Date): string {
    return date.toLocaleDateString(undefined, {
        weekday: 'long',
        year: 'numeric',
        month: 'long',
        day: 'numeric',
    });
}

export function formatTime(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleTimeString(undefined, {
        hour: '2-digit',
        minute: '2-digit',
    });
}

export function getDayOfMonth(date: Date): string {
    return date.getDate().toString();
}

/**
 * Returns the locale's first day of week as a JS day index (0 = Sunday, 1 = Monday, ...).
 *
 * Uses `Intl.Locale.weekInfo` (modern Chromium / Firefox / recent Safari) when
 * available. The Intl spec uses 1 = Monday ... 7 = Sunday, which is converted
 * to JS day index. Falls back to Sunday (0) when unavailable or on errors.
 */
export function getFirstDayOfWeek(locale?: string): number {
    try {
        const tag = locale ?? (typeof navigator !== 'undefined' ? navigator.language : undefined);
        if (!tag) {
            return 0;
        }
        // weekInfo is non-standard but widely supported. Cast through unknown to avoid
        // depending on the @types/node-or-browser variant that ships it.
        const info = new Intl.Locale(tag) as unknown as {
            weekInfo?: { firstDay?: number };
            getWeekInfo?: () => { firstDay: number };
        };
        const firstDay = info.getWeekInfo?.().firstDay ?? info.weekInfo?.firstDay;
        if (typeof firstDay === 'number') {
            // Intl: 1 = Monday ... 7 = Sunday. JS: 0 = Sunday ... 6 = Saturday.
            return firstDay === 7 ? 0 : firstDay;
        }
    } catch {
        // fall through to default
    }
    return 0;
}

export function getWeekStart(date: Date, locale?: string): Date {
    const d = new Date(date);
    const day = d.getDay();
    const firstDay = getFirstDayOfWeek(locale);
    const offset = (day - firstDay + 7) % 7;
    d.setDate(d.getDate() - offset);
    d.setHours(0, 0, 0, 0);
    return d;
}

export function getMonthDays(year: number, month: number): Date[] {
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);

    const startPadding = firstDay.getDay();
    const endPadding = 6 - lastDay.getDay();

    const days: Date[] = [];

    for (let i = startPadding - 1; i >= 0; i--) {
        const d = new Date(year, month, -i);
        days.push(d);
    }

    for (let i = 1; i <= lastDay.getDate(); i++) {
        days.push(new Date(year, month, i));
    }

    for (let i = 1; i <= endPadding; i++) {
        days.push(new Date(year, month + 1, i));
    }

    return days;
}

export function getWeekDays(startDate: Date): Date[] {
    const days: Date[] = [];
    for (let i = 0; i < 7; i++) {
        const d = new Date(startDate);
        d.setDate(startDate.getDate() + i);
        days.push(d);
    }
    return days;
}

export function isSameDay(date1: Date, date2: Date): boolean {
    return (
        date1.getFullYear() === date2.getFullYear() &&
        date1.getMonth() === date2.getMonth() &&
        date1.getDate() === date2.getDate()
    );
}

export function isToday(date: Date): boolean {
    return isSameDay(date, new Date());
}

/**
 * Legacy English weekday-name array starting on Sunday. Kept for backwards
 * compatibility; new code should prefer `getWeekdayNames(locale)`, which
 * returns names in the locale's first-day-of-week order.
 */
export const WEEKDAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/**
 * Returns 7 short weekday names for the given locale, ordered starting from
 * the locale's first day of week. Falls back to navigator.language and then to
 * the legacy English Sunday-first list.
 */
export function getWeekdayNames(locale?: string): string[] {
    try {
        const tag = locale ?? (typeof navigator !== 'undefined' ? navigator.language : undefined);
        if (!tag) {
            return [...WEEKDAY_NAMES];
        }
        const formatter = new Intl.DateTimeFormat(tag, { weekday: 'short' });
        const firstDay = getFirstDayOfWeek(tag);
        // Pick a known Sunday as the anchor (2024-01-07 is a Sunday).
        const sunday = new Date(2024, 0, 7);
        const names: string[] = [];
        for (let i = 0; i < 7; i++) {
            const d = new Date(sunday);
            d.setDate(sunday.getDate() + ((firstDay + i) % 7));
            names.push(formatter.format(d));
        }
        return names;
    } catch {
        return [...WEEKDAY_NAMES];
    }
}
