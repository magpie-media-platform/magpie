import type { TemporalPrecision } from '@/types/contentMetadata';
import { parseCalendarDate, parseUtcInstant } from './dateParsing';

const unknownDateSentinels = new Set<number>([Date.parse('1970-01-01T00:00:00Z'), Date.parse('0001-01-01T00:00:00Z')]);

function isUnknownDateSentinel(dateStr: string): boolean {
    return unknownDateSentinels.has(parseUtcInstant(dateStr).getTime());
}

export function formatShortDate(
    dateStr: string | null | undefined,
    fallback = 'Never',
    precision?: TemporalPrecision | null,
): string {
    if (!dateStr) {
        return fallback;
    }
    const coarse = precision === 'Year' || precision === 'Month';
    if (!coarse && isUnknownDateSentinel(dateStr)) {
        return fallback;
    }
    const date = parseCalendarDate(dateStr);
    if (isNaN(date.getTime())) {
        return 'Unknown';
    }
    return date.toLocaleDateString(undefined, {
        month: precision === 'Year' ? undefined : 'short',
        day: coarse ? undefined : 'numeric',
        year: 'numeric',
    });
}

/**
 * Formats a content publication date at its precision. A 1970-01-01 date, which can be a year-only 1970 date,
 * shows as 1970.
 */
export function formatPublishDate(
    dateStr: string | null | undefined,
    fallback: string,
    precision?: TemporalPrecision | null,
): string {
    if (!dateStr) {
        return fallback;
    }
    const year = parseUtcInstant(dateStr).getUTCFullYear();
    return formatShortDate(dateStr, year > 1 ? String(year) : fallback, precision);
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
}

export function formatDateTime(dateStr: string, { seconds = false }: { seconds?: boolean } = {}): string {
    if (isUnknownDateSentinel(dateStr)) {
        return '-';
    }
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
        second: seconds ? '2-digit' : undefined,
    });
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
