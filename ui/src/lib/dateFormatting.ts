import { parseCalendarDate, parseUtcInstant } from './dateParsing';

const unknownDateSentinels = new Set<number>([Date.parse('1970-01-01T00:00:00Z'), Date.parse('0001-01-01T00:00:00Z')]);

function isUnknownDateSentinel(dateStr: string): boolean {
    return unknownDateSentinels.has(parseUtcInstant(dateStr).getTime());
}

export function formatShortDate(dateStr: string | null | undefined, fallback = 'Never'): string {
    if (!dateStr) {
        return fallback;
    }
    if (isUnknownDateSentinel(dateStr)) {
        return fallback;
    }
    try {
        const date = parseCalendarDate(dateStr);
        if (isNaN(date.getTime())) {
            return 'Unknown';
        }
        return date.toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
            year: 'numeric',
        });
    } catch {
        return 'Unknown';
    }
}

export function formatDate(dateStr: string, fallback = '-'): string {
    return formatDateWithParser(dateStr, fallback, parseCalendarDate);
}

export function formatInstantDate(dateStr: string, fallback = '-'): string {
    return formatDateWithParser(dateStr, fallback, parseUtcInstant);
}

function formatDateWithParser(dateStr: string, fallback: string, parser: (value: string) => Date): string {
    if (!dateStr) {
        return fallback;
    }
    if (isUnknownDateSentinel(dateStr)) {
        return fallback;
    }
    try {
        const date = parser(dateStr);
        if (isNaN(date.getTime())) {
            return dateStr;
        }
        if (date.getUTCFullYear() <= 1) {
            return fallback;
        }
        return date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });
    } catch {
        return dateStr;
    }
}

export function formatDateTime(dateStr: string): string {
    if (isUnknownDateSentinel(dateStr)) {
        return '-';
    }
    try {
        const date = parseUtcInstant(dateStr);
        if (isNaN(date.getTime())) {
            return dateStr;
        }
        return date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
    } catch {
        return dateStr;
    }
}

export function formatUtcDate(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        timeZone: 'UTC',
    });
}

export function formatUtcDateTime(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        timeZone: 'UTC',
    });
}

export function toLocalDateKey(date: Date): string {
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${date.getFullYear()}-${month}-${day}`;
}

export function formatCalendarDate(date: Date): string {
    return date.toLocaleDateString(undefined, {
        weekday: 'long',
        year: 'numeric',
        month: 'long',
        day: 'numeric',
    });
}
