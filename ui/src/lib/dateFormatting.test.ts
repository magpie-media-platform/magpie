import { afterEach, describe, expect, it } from 'vitest';

import {
    formatCalendarDate,
    formatDate,
    formatDateTime,
    formatInstantDate,
    formatPublishDate,
    formatShortDate,
    formatUtcDate,
    formatUtcDateTime,
} from './dateFormatting';

const originalTimeZone = process.env.TZ;

describe('formatPublishDate', () => {
    it('shows the year of a year-only date and the full date otherwise', () => {
        expect(formatPublishDate('1970-01-01T00:00:00Z', '--')).toBe('1970');
        expect(formatPublishDate('2009-11-13', '--')).toBe(formatShortDate('2009-11-13'));
        expect(formatPublishDate(null, '--')).toBe('--');
        expect(formatPublishDate('0001-01-01T00:00:00Z', '--')).toBe('--');
    });

    it('shows only the year of a year-precision date', () => {
        expect(formatPublishDate('1975-01-01T00:00:00Z', '--', 'Year')).toBe('1975');
        expect(formatPublishDate('1970-01-01T00:00:00Z', '--', 'Year')).toBe('1970');
    });

    it('shows the month and year of a month-precision date', () => {
        expect(formatPublishDate('1975-03-01T00:00:00Z', '--', 'Month')).toBe('Mar 1975');
    });

    it('shows the full date of a day-precision date or a date without a precision', () => {
        expect(formatPublishDate('1975-01-01T00:00:00Z', '--', 'Day')).toBe('Jan 1, 1975');
        expect(formatPublishDate('1975-01-01T00:00:00Z', '--', null)).toBe('Jan 1, 1975');
        expect(formatPublishDate('1975-01-01T00:00:00Z', '--')).toBe('Jan 1, 1975');
    });
});

describe('formatShortDate', () => {
    afterEach(() => {
        if (originalTimeZone === undefined) {
            delete process.env.TZ;
        } else {
            process.env.TZ = originalTimeZone;
        }
    });

    it('returns "Never" for null', () => {
        expect(formatShortDate(null)).toBe('Never');
    });

    it('returns "Never" for empty string', () => {
        expect(formatShortDate('')).toBe('Never');
    });

    it('returns "Unknown" for invalid date string', () => {
        expect(formatShortDate('not-a-date')).toBe('Unknown');
    });

    it('formats a valid ISO date as a short date', () => {
        expect(formatShortDate('2024-01-15T12:00:00Z')).toBe('Jan 15, 2024');
    });

    it('formats date-only values without shifting them in negative time zones', () => {
        process.env.TZ = 'America/Los_Angeles';
        expect(
            new Date('2024-01-15').toLocaleDateString('en-US', {
                month: 'short',
                day: 'numeric',
                year: 'numeric',
            }),
        ).toBe('Jan 14, 2024');
        expect(formatShortDate('2024-01-15')).toBe('Jan 15, 2024');
        expect(formatShortDate('2024-01-15T00:00:00')).toBe('Jan 15, 2024');
    });

    it('renders the fallback for the Unix epoch sentinel in all its shapes', () => {
        expect(formatShortDate('1970-01-01T00:00:00Z', '--')).toBe('--');
        expect(formatShortDate('1970-01-01T00:00:00.000Z', '--')).toBe('--');
        expect(formatShortDate('1970-01-01T00:00:00', '--')).toBe('--');
        expect(formatShortDate('1970-01-01', '--')).toBe('--');
    });

    it('shows the year of a year-precision 1970 date instead of the sentinel fallback', () => {
        expect(formatShortDate('1970-01-01T00:00:00Z', '--', 'Year')).toBe('1970');
    });

    it('renders the default fallback for the epoch sentinel when none is given', () => {
        expect(formatShortDate('1970-01-01T00:00:00Z')).toBe('Never');
    });

    it('treats the epoch sentinel as unknown regardless of the local time zone', () => {
        process.env.TZ = 'America/Los_Angeles';
        expect(formatShortDate('1970-01-01T00:00:00', '--')).toBe('--');
        expect(formatShortDate('1970-01-01', '--')).toBe('--');
    });

    it('renders the fallback for the .NET DateTime.MinValue sentinel', () => {
        expect(formatShortDate('0001-01-01T00:00:00', '--')).toBe('--');
        expect(formatShortDate('0001-01-01', '--')).toBe('--');
    });

    it('still formats a real date that is near but not the epoch sentinel', () => {
        expect(formatShortDate('1970-01-02T00:00:00Z', '--')).toBe('Jan 2, 1970');
        expect(formatShortDate('1970-01-01T12:00:00Z', '--')).not.toBe('--');
    });
});

describe('formatDateTime', () => {
    afterEach(() => {
        if (originalTimeZone === undefined) {
            delete process.env.TZ;
        } else {
            process.env.TZ = originalTimeZone;
        }
    });

    it('returns the original string for an invalid date', () => {
        expect(formatDateTime('not-a-date')).toBe('not-a-date');
    });

    it('formats a valid ISO date string including time components', () => {
        const result = formatDateTime('2024-06-01T12:00:00Z');
        expect(result).toMatch(/2024/);
        expect(result).toMatch(/Jun/);
    });

    it('renders the placeholder for the .NET DateTime.MinValue sentinel', () => {
        expect(formatDateTime('0001-01-01T00:00:00Z')).toBe('-');
        expect(formatDateTime('0001-01-01T00:00:00')).toBe('-');
        expect(formatDateTime('0001-01-01')).toBe('-');
    });

    it('renders the placeholder for the Unix epoch sentinel', () => {
        expect(formatDateTime('1970-01-01T00:00:00Z')).toBe('-');
    });

    it('still formats a real date near the epoch sentinel', () => {
        expect(formatDateTime('1970-01-02T00:00:00Z')).not.toBe('-');
    });

    it('formats offsetless API timestamps as UTC in negative time zones', () => {
        process.env.TZ = 'America/Los_Angeles';
        const expected = new Date('2024-01-01T20:00:00Z').toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });

        expect(new Date('2024-01-01T20:00:00').toISOString()).toBe('2024-01-02T04:00:00.000Z');
        expect(formatDateTime('2024-01-01T20:00:00')).toBe(expected);
    });
});

describe('formatDate', () => {
    afterEach(() => {
        if (originalTimeZone === undefined) {
            delete process.env.TZ;
        } else {
            process.env.TZ = originalTimeZone;
        }
    });

    it('returns the original string for an invalid date', () => {
        expect(formatDate('not-a-date')).toBe('not-a-date');
    });

    it('formats a date string with short month, day, and year', () => {
        const expected = new Date('2024-06-01T12:00:00Z').toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });

        expect(formatDate('2024-06-01T12:00:00Z')).toBe(expected);
    });

    it('formats date-only values without shifting them in negative time zones', () => {
        process.env.TZ = 'America/Los_Angeles';

        expect(formatDate('2026-07-20')).toBe('Jul 20, 2026');
        expect(formatDate('2026-07-20T00:00:00Z')).toBe('Jul 20, 2026');
    });

    it('renders the placeholder for the .NET DateTime.MinValue sentinel', () => {
        expect(formatDate('0001-01-01T00:00:00Z')).toBe('-');
        expect(formatDate('0001-01-01T00:00:00')).toBe('-');
        expect(formatDate('0001-01-01')).toBe('-');
    });

    it('renders the placeholder for the Unix epoch sentinel', () => {
        expect(formatDate('1970-01-01T00:00:00Z')).toBe('-');
    });

    it('renders a custom fallback for a min/default date', () => {
        expect(formatDate('0001-01-01T00:00:00Z', 'Unknown')).toBe('Unknown');
        expect(formatDate('', 'Unknown')).toBe('Unknown');
    });

    it('still formats a real date near the epoch sentinel', () => {
        expect(formatDate('1970-01-02T00:00:00Z')).not.toBe('-');
    });
});

describe('formatInstantDate', () => {
    afterEach(() => {
        if (originalTimeZone === undefined) {
            delete process.env.TZ;
        } else {
            process.env.TZ = originalTimeZone;
        }
    });

    it('keeps midnight UTC timestamps as instants in negative time zones', () => {
        process.env.TZ = 'America/Los_Angeles';

        expect(formatInstantDate('2024-06-01T00:00:00Z')).toBe('May 31, 2024');
    });
});

describe('formatUtcDate', () => {
    it('formats a date string in UTC', () => {
        expect(formatUtcDate('2024-01-01T20:00:00Z')).toBe('Jan 1, 2024');
    });
});

describe('formatUtcDateTime', () => {
    it('formats a date-time string in UTC', () => {
        const expected = new Date('2024-01-01T20:00:00Z').toLocaleString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            timeZone: 'UTC',
        });

        expect(formatUtcDateTime('2024-01-01T20:00:00Z')).toBe(expected);
    });
});

describe('formatCalendarDate', () => {
    it('formats a Date with weekday, month, day, and year', () => {
        const date = new Date(2024, 0, 15);
        const expected = date.toLocaleDateString(undefined, {
            weekday: 'long',
            year: 'numeric',
            month: 'long',
            day: 'numeric',
        });

        expect(formatCalendarDate(date)).toBe(expected);
    });
});
