const dateOnlyPattern = /^(\d{4})-(\d{2})-(\d{2})$/;
const midnightIsoDateTimePattern = /^(\d{4})-(\d{2})-(\d{2})T00:00:00(?:\.0+)?(?:Z)?$/;
const offsetlessIsoDateTimePattern = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/;

interface CalendarDateParts {
    year: number;
    month: number;
    day: number;
}

export function parseUtcInstant(value: string): Date {
    const trimmed = value.trim();
    const normalized = offsetlessIsoDateTimePattern.test(trimmed) ? `${trimmed}Z` : trimmed;

    return new Date(normalized);
}

export function parseCalendarDateParts(value: string): CalendarDateParts | null {
    const trimmed = value.trim();
    const match = dateOnlyPattern.exec(trimmed) ?? midnightIsoDateTimePattern.exec(trimmed);
    if (!match) {
        return null;
    }

    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    const date = new Date(year, month - 1, day);
    if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) {
        return null;
    }

    return { year, month, day };
}

export function parseCalendarDate(value: string): Date {
    const calendarDate = parseCalendarDateParts(value);
    return calendarDate
        ? new Date(calendarDate.year, calendarDate.month - 1, calendarDate.day)
        : parseUtcInstant(value);
}
