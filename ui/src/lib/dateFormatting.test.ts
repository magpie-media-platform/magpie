import { describe, expect, it } from 'vitest';

import { formatShortDate, formatDateTime } from './dateFormatting';

describe('formatShortDate', () => {
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
});

describe('formatDateTime', () => {
    it('returns the original string for an invalid date', () => {
        expect(formatDateTime('not-a-date')).toBe('not-a-date');
    });

    it('formats a valid ISO date string including time components', () => {
        const result = formatDateTime('2024-06-01T12:00:00Z');
        expect(result).toMatch(/2024/);
        expect(result).toMatch(/Jun/);
    });
});
