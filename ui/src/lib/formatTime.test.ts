import { describe, expect, it, vi, afterEach } from 'vitest';

import { formatRelativeTime, formatUptime } from './formatTime';

describe('formatRelativeTime', () => {
    afterEach(() => {
        vi.useRealTimers();
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
