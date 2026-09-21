import type { FulfillmentStatus } from '@/types/content';

export { formatCalendarDate as formatDate } from '@/lib/dateFormatting';
export { formatTimeOfDay as formatTime } from '@/lib/formatTime';
export { parseCalendarDate, parseCalendarDateParts } from '@/lib/dateParsing';

export function getFulfillmentClass(status: FulfillmentStatus): string {
    return `calendar-page__event--${status}`;
}

export function getDayOfMonth(date: Date): string {
    return date.getDate().toString();
}

export function formatDateKey(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
}

export function getFirstDayOfWeek(locale?: string): number {
    try {
        const tag = locale ?? (typeof navigator !== 'undefined' ? navigator.language : undefined);
        if (!tag) {
            return 0;
        }
        const info = new Intl.Locale(tag) as unknown as {
            weekInfo?: { firstDay?: number };
            getWeekInfo?: () => { firstDay: number };
        };
        const firstDay = info.getWeekInfo?.().firstDay ?? info.weekInfo?.firstDay;
        if (typeof firstDay === 'number') {
            return firstDay === 7 ? 0 : firstDay;
        }
    } catch {
        // Not every engine supplies Intl.Locale week info, so an unsupported engine falls back to Sunday.
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

    const weekStart = getFirstDayOfWeek();
    const startPadding = (firstDay.getDay() - weekStart + 7) % 7;
    const endPadding = 6 - ((lastDay.getDay() - weekStart + 7) % 7);

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

export const WEEKDAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
