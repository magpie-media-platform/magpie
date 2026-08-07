import { describe, expect, it } from 'vitest';

import { canonicalizeCalendarSearchParams, normalizeCalendarView, parseCalendarDateParam } from './calendarUrlState';

describe('normalizeCalendarView', () => {
    it('normalizes supported views', () => {
        expect(normalizeCalendarView(' WEEK ')).toBe('week');
    });

    it('rejects unsupported views', () => {
        expect(normalizeCalendarView('agenda')).toBeNull();
    });
});

describe('parseCalendarDateParam', () => {
    it('parses canonical date params as local dates', () => {
        const date = parseCalendarDateParam('2025-06-15');

        expect(date?.getFullYear()).toBe(2025);
        expect(date?.getMonth()).toBe(5);
        expect(date?.getDate()).toBe(15);
    });

    it('rejects calendar-overflow dates', () => {
        expect(parseCalendarDateParam('2025-02-31')).toBeNull();
    });
});

describe('canonicalizeCalendarSearchParams', () => {
    it('removes invalid view and date params while preserving unrelated params', () => {
        const params = new URLSearchParams('view=bogus&date=not-a-date&source=dashboard');

        const canonical = canonicalizeCalendarSearchParams(params, new Date(2025, 5, 15));

        expect(canonical?.toString()).toBe('source=dashboard');
    });

    it('removes default values from the URL', () => {
        const params = new URLSearchParams('view=month&date=2025-06-15');

        const canonical = canonicalizeCalendarSearchParams(params, new Date(2025, 5, 15));

        expect(canonical?.toString()).toBe('');
    });
});
