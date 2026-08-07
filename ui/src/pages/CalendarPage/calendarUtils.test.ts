import { describe, expect, it } from 'vitest';

import {
    getFulfillmentClass,
    formatDate,
    formatTime,
    getDayOfMonth,
    getWeekStart,
    getMonthDays,
    getWeekDays,
    isSameDay,
    isToday,
    parseCalendarDate,
    WEEKDAY_NAMES,
    getFirstDayOfWeek,
} from './calendarUtils';

describe('getFulfillmentClass', () => {
    it('returns class string for each status', () => {
        expect(getFulfillmentClass('missing')).toBe('calendar-page__event--missing');
        expect(getFulfillmentClass('upgradable')).toBe('calendar-page__event--upgradable');
        expect(getFulfillmentClass('fulfilled')).toBe('calendar-page__event--fulfilled');
        expect(getFulfillmentClass('fulfilledExternally')).toBe('calendar-page__event--fulfilledExternally');
        expect(getFulfillmentClass('partial')).toBe('calendar-page__event--partial');
    });
});

describe('formatDate', () => {
    it('formats a date with weekday, month, day, and year', () => {
        const date = new Date(2024, 0, 15);
        const result = formatDate(date);
        expect(result).toContain('2024');
        expect(result).toContain('15');
    });
});

describe('formatTime', () => {
    it('formats a date string to a locale time with hours and minutes', () => {
        const result = formatTime('2024-01-15T14:30:00Z');
        expect(result).toMatch(/\d{1,2}:\d{2}/);
    });
});

describe('getDayOfMonth', () => {
    it('returns the day number as a string', () => {
        expect(getDayOfMonth(new Date(2024, 0, 1))).toBe('1');
        expect(getDayOfMonth(new Date(2024, 0, 15))).toBe('15');
        expect(getDayOfMonth(new Date(2024, 0, 31))).toBe('31');
    });
});

describe('getWeekStart', () => {
    it('returns Sunday of the current week', () => {
        const wed = new Date(2024, 0, 17);
        const start = getWeekStart(wed);
        expect(start.getDay()).toBe(0);
        expect(start.getDate()).toBe(14);
    });

    it('returns the same day when already Sunday', () => {
        const sun = new Date(2024, 0, 14);
        const start = getWeekStart(sun);
        expect(start.getDay()).toBe(0);
        expect(start.getDate()).toBe(14);
    });

    it('sets time to midnight', () => {
        const date = new Date(2024, 0, 17, 15, 30, 45);
        const start = getWeekStart(date);
        expect(start.getHours()).toBe(0);
        expect(start.getMinutes()).toBe(0);
        expect(start.getSeconds()).toBe(0);
    });

    it('does not mutate the input date', () => {
        const original = new Date(2024, 0, 17, 15, 30, 45);
        const originalTime = original.getTime();
        getWeekStart(original);
        expect(original.getTime()).toBe(originalTime);
    });
});

describe('getMonthDays', () => {
    it('returns an array of dates covering the full calendar grid', () => {
        const days = getMonthDays(2024, 0);
        expect(days[0].getDay()).toBe(0);
        expect(days[days.length - 1].getDay()).toBe(6);
        const janDays = days.filter((d) => d.getMonth() === 0 && d.getFullYear() === 2024);
        expect(janDays.length).toBe(31);
    });

    it('includes padding days from previous and next month', () => {
        const days = getMonthDays(2024, 0);
        const prevMonthDays = days.filter((d) => d.getMonth() === 11 && d.getFullYear() === 2023);
        expect(prevMonthDays.length).toBeGreaterThan(0);
    });

    it('handles February in a leap year', () => {
        const days = getMonthDays(2024, 1);
        const febDays = days.filter((d) => d.getMonth() === 1 && d.getFullYear() === 2024);
        expect(febDays.length).toBe(29);
    });

    it('handles February in a non-leap year', () => {
        const days = getMonthDays(2023, 1);
        const febDays = days.filter((d) => d.getMonth() === 1 && d.getFullYear() === 2023);
        expect(febDays.length).toBe(28);
    });
});

describe('getWeekDays', () => {
    it('returns exactly 7 days', () => {
        const start = new Date(2024, 0, 14);
        const days = getWeekDays(start);
        expect(days.length).toBe(7);
    });

    it('returns consecutive days starting from the given date', () => {
        const start = new Date(2024, 0, 14);
        const days = getWeekDays(start);
        expect(days[0].getDate()).toBe(14);
        expect(days[1].getDate()).toBe(15);
        expect(days[6].getDate()).toBe(20);
    });

    it('does not mutate the start date', () => {
        const start = new Date(2024, 0, 14);
        const originalTime = start.getTime();
        getWeekDays(start);
        expect(start.getTime()).toBe(originalTime);
    });
});

describe('isSameDay', () => {
    it('returns true for the same date', () => {
        const a = new Date(2024, 0, 15, 10, 0);
        const b = new Date(2024, 0, 15, 22, 30);
        expect(isSameDay(a, b)).toBe(true);
    });

    it('returns false for different dates', () => {
        const a = new Date(2024, 0, 15);
        const b = new Date(2024, 0, 16);
        expect(isSameDay(a, b)).toBe(false);
    });

    it('returns false for same day in different months', () => {
        const a = new Date(2024, 0, 15);
        const b = new Date(2024, 1, 15);
        expect(isSameDay(a, b)).toBe(false);
    });

    it('returns false for same day in different years', () => {
        const a = new Date(2024, 0, 15);
        const b = new Date(2025, 0, 15);
        expect(isSameDay(a, b)).toBe(false);
    });
});

describe('parseCalendarDate', () => {
    it('keeps date-only publication values on their calendar date in a negative time zone', () => {
        const originalTimeZone = process.env.TZ;
        process.env.TZ = 'America/Los_Angeles';
        try {
            expect(parseCalendarDate('2026-07-20')).toEqual(new Date(2026, 6, 20));
            expect(parseCalendarDate('2026-07-20T00:00:00Z')).toEqual(new Date(2026, 6, 20));
        } finally {
            if (originalTimeZone === undefined) {
                delete process.env.TZ;
            } else {
                process.env.TZ = originalTimeZone;
            }
        }
    });

    it('preserves the instant for publication values with a time', () => {
        expect(parseCalendarDate('2026-07-20T20:00:00Z')).toEqual(new Date('2026-07-20T20:00:00Z'));
    });
});

describe('isToday', () => {
    it('returns true for today', () => {
        expect(isToday(new Date())).toBe(true);
    });

    it('returns false for yesterday', () => {
        const yesterday = new Date();
        yesterday.setDate(yesterday.getDate() - 1);
        expect(isToday(yesterday)).toBe(false);
    });
});

describe('WEEKDAY_NAMES', () => {
    it('contains 7 entries starting with Sun', () => {
        expect(WEEKDAY_NAMES).toHaveLength(7);
        expect(WEEKDAY_NAMES[0]).toBe('Sun');
        expect(WEEKDAY_NAMES[6]).toBe('Sat');
    });
});

describe('getFirstDayOfWeek', () => {
    it('returns Monday (1) for a Monday-first locale when weekInfo is supported', () => {
        const day = getFirstDayOfWeek('fr-FR');
        expect([0, 1]).toContain(day);
    });

    it('returns Sunday (0) for en-US', () => {
        expect(getFirstDayOfWeek('en-US')).toBe(0);
    });
});

describe('getWeekStart with explicit locale', () => {
    it('returns Monday of the current week for a Monday-first locale (when weekInfo is supported)', () => {
        const wed = new Date(2024, 0, 17);
        const start = getWeekStart(wed, 'fr-FR');
        const firstDay = getFirstDayOfWeek('fr-FR');
        if (firstDay === 1) {
            expect(start.getDay()).toBe(1);
            expect(start.getDate()).toBe(15);
        } else {
            expect(start.getDay()).toBe(0);
            expect(start.getDate()).toBe(14);
        }
    });

    it('returns Sunday of the current week for en-US', () => {
        const wed = new Date(2024, 0, 17);
        const start = getWeekStart(wed, 'en-US');
        expect(start.getDay()).toBe(0);
        expect(start.getDate()).toBe(14);
    });
});
