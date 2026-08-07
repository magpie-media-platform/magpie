import { describe, expect, it, vi, afterEach } from 'vitest';

import {
    formatClockTime,
    formatDurationFromMinutes,
    formatDurationFromSeconds,
    formatElapsedDuration,
    formatPlaybackTime,
    formatRelativeTime,
    formatTimeOfDay,
    formatTimeSpanDuration,
    formatUptime,
} from './formatTime';

const originalTimeZone = process.env.TZ;

describe('formatRelativeTime', () => {
    afterEach(() => {
        vi.useRealTimers();
        if (originalTimeZone === undefined) {
            delete process.env.TZ;
        } else {
            process.env.TZ = originalTimeZone;
        }
    });

    it('returns fallback for null input', () => {
        expect(formatRelativeTime(null)).toBe('Never');
    });

    it('returns fallback for undefined input', () => {
        expect(formatRelativeTime(undefined)).toBe('Never');
    });

    it('returns fallback for empty string input', () => {
        expect(formatRelativeTime('')).toBe('Never');
    });

    it('returns custom fallback when provided', () => {
        expect(formatRelativeTime(null, 'N/A')).toBe('N/A');
    });

    it('returns "Just now" for dates less than 1 minute ago', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T12:00:30Z'));
        expect(formatRelativeTime('2024-01-01T12:00:00Z')).toBe('Just now');
    });

    it('treats offsetless API timestamps as UTC in negative time zones', () => {
        process.env.TZ = 'America/Los_Angeles';
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-02T04:00:30Z'));
        expect(new Date('2024-01-01T20:00:00').toISOString()).toBe('2024-01-02T04:00:00.000Z');
        expect(formatRelativeTime('2024-01-01T20:00:00')).toBe('8h ago');
    });

    it('does not return "Just now" for future timestamps', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T12:00:00Z'));
        expect(formatRelativeTime('2024-01-01T12:01:00Z')).toBe('Jan 1, 2024');
    });

    it('returns minutes ago for dates less than 1 hour ago', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T12:15:00Z'));
        expect(formatRelativeTime('2024-01-01T12:00:00Z')).toBe('15m ago');
    });

    it('returns hours ago for dates less than 1 day ago', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T15:00:00Z'));
        expect(formatRelativeTime('2024-01-01T12:00:00Z')).toBe('3h ago');
    });

    it('returns days ago for dates 1-30 days ago', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-04T12:00:00Z'));
        expect(formatRelativeTime('2024-01-01T12:00:00Z')).toBe('3d ago');
    });

    it('returns days ago for exactly 30 days', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-31T12:00:00Z'));
        expect(formatRelativeTime('2024-01-01T12:00:00Z')).toBe('30d ago');
    });

    it('returns a short date for dates more than 30 days ago', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-03-01T12:00:00Z'));
        expect(formatRelativeTime('2024-01-15T12:00:00Z')).toBe('Jan 15, 2024');
    });

    it('returns a short date for dates many months ago', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-12-01T12:00:00Z'));
        expect(formatRelativeTime('2024-03-10T12:00:00Z')).toBe('Mar 10, 2024');
    });

    it('returns "Unknown" for invalid date strings', () => {
        expect(formatRelativeTime('not-a-date')).toBe('Unknown');
    });
});

describe('formatUptime', () => {
    afterEach(() => {
        vi.useRealTimers();
        if (originalTimeZone === undefined) {
            delete process.env.TZ;
        } else {
            process.env.TZ = originalTimeZone;
        }
    });

    it('returns "-" for null input', () => {
        expect(formatUptime(null)).toBe('-');
    });

    it('returns "-" for undefined input', () => {
        expect(formatUptime(undefined)).toBe('-');
    });

    it('returns "-" for invalid date strings', () => {
        expect(formatUptime('not-a-date')).toBe('-');
    });

    it('returns "Just started" for less than 1 minute', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T12:00:30Z'));
        expect(formatUptime('2024-01-01T12:00:00Z')).toBe('Just started');
    });

    it('returns minutes only for less than 1 hour', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T12:45:00Z'));
        expect(formatUptime('2024-01-01T12:00:00Z')).toBe('45m');
    });

    it('returns hours and minutes for less than 1 day', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T15:30:00Z'));
        expect(formatUptime('2024-01-01T12:00:00Z')).toBe('3h 30m');
    });

    it('returns days, hours, and minutes for multi-day uptime', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-04T14:15:00Z'));
        expect(formatUptime('2024-01-01T12:00:00Z')).toBe('3d 2h 15m');
    });

    it('returns "-" for future dates', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-01T12:00:00Z'));
        expect(formatUptime('2024-01-02T12:00:00Z')).toBe('-');
    });

    it('returns days and hours when minutes are zero', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2024-01-03T12:00:00Z'));
        expect(formatUptime('2024-01-01T12:00:00Z')).toBe('2d');
    });
});

describe('formatPlaybackTime', () => {
    it('formats negative and invalid values as zero', () => {
        expect(formatPlaybackTime(-1)).toBe('0:00');
        expect(formatPlaybackTime(Number.NaN)).toBe('0:00');
    });

    it('formats minute and hour playback values', () => {
        expect(formatPlaybackTime(65.9)).toBe('1:05');
        expect(formatPlaybackTime(3665)).toBe('1:01:05');
    });
});

describe('formatDurationFromSeconds', () => {
    it('rounds seconds to minutes for short durations', () => {
        expect(formatDurationFromSeconds(89)).toBe('1m');
        expect(formatDurationFromSeconds(90)).toBe('2m');
    });

    it('formats hour durations', () => {
        expect(formatDurationFromSeconds(3600)).toBe('1h');
        expect(formatDurationFromSeconds(5400)).toBe('1h 30m');
    });
});

describe('formatDurationFromMinutes', () => {
    it('formats minute and hour durations', () => {
        expect(formatDurationFromMinutes(45)).toBe('45m');
        expect(formatDurationFromMinutes(60)).toBe('1h');
        expect(formatDurationFromMinutes(90)).toBe('1h 30m');
    });
});

describe('formatTimeSpanDuration', () => {
    it('returns undefined for empty, invalid, and zero-minute durations', () => {
        expect(formatTimeSpanDuration(null)).toBeUndefined();
        expect(formatTimeSpanDuration('not-a-duration')).toBeUndefined();
        expect(formatTimeSpanDuration('00:00:30')).toBeUndefined();
    });

    it('formats time span strings as runtime', () => {
        expect(formatTimeSpanDuration('00:45:00')).toBe('45m');
        expect(formatTimeSpanDuration('01:30:00')).toBe('1h 30m');
        expect(formatTimeSpanDuration('1.02:00:00')).toBe('26h');
    });
});

describe('formatElapsedDuration', () => {
    it('returns undefined when either timestamp is missing', () => {
        expect(formatElapsedDuration(null, '2025-01-01T00:01:00Z')).toBeUndefined();
        expect(formatElapsedDuration('2025-01-01T00:00:00Z', null)).toBeUndefined();
    });

    it('formats elapsed timestamp ranges', () => {
        expect(formatElapsedDuration('2025-01-01T00:00:00.000Z', '2025-01-01T00:00:00.500Z')).toBe('500ms');
        expect(formatElapsedDuration('2025-01-01T00:00:00Z', '2025-01-01T00:00:45Z')).toBe('45s');
        expect(formatElapsedDuration('2025-01-01T00:00:00Z', '2025-01-01T00:02:30Z')).toBe('2m 30s');
        expect(formatElapsedDuration('2025-01-01T00:00:00Z', '2025-01-01T00:05:00Z')).toBe('5m');
    });
});

describe('formatTimeOfDay', () => {
    it('formats a date string to a locale time with hours and minutes', () => {
        const result = formatTimeOfDay('2024-01-15T14:30:00Z');
        expect(result).toMatch(/\d{1,2}:\d{2}/);
    });
});

describe('formatClockTime', () => {
    it('formats hour and minute values with two digits', () => {
        expect(formatClockTime(3, 5)).toBe('03:05');
        expect(formatClockTime(14, 30)).toBe('14:30');
    });
});
