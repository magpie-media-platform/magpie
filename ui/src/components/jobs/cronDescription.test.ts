import { describe, expect, it } from 'vitest';

import { describeCron, describeCronUtc } from './cronDescription';

describe('describeCron', () => {
    it('uses friendly labels for the form presets', () => {
        expect(describeCron('0 * * * *')).toBe('Every hour');
        expect(describeCron('0 */4 * * *')).toBe('Every 4 hours');
        expect(describeCron('0 0 * * *')).toBe('Daily at midnight');
        expect(describeCron('0 6 * * *')).toBe('Daily at 6 AM');
        expect(describeCron('0 0 * * 0')).toBe('Weekly on Sunday');
    });

    it('humanizes step-minute expressions', () => {
        expect(describeCron('* * * * *')).toBe('Every minute');
        expect(describeCron('*/5 * * * *')).toBe('Every 5 minutes');
        expect(describeCron('*/10 * * * *')).toBe('Every 10 minutes');
        expect(describeCron('*/15 * * * *')).toBe('Every 15 minutes');
        expect(describeCron('*/30 * * * *')).toBe('Every 30 minutes');
    });

    it('humanizes hourly-at-minute expressions', () => {
        expect(describeCron('30 * * * *')).toBe('Hourly at :30');
    });

    it('humanizes step-hour expressions', () => {
        expect(describeCron('0 */3 * * *')).toBe('Every 3 hours');
        expect(describeCron('15 */2 * * *')).toBe('Every 2 hours at :15');
    });

    it('humanizes fixed daily times not covered by a preset', () => {
        expect(describeCron('0 3 * * *')).toBe('Daily at 03:00');
        expect(describeCron('30 14 * * *')).toBe('Daily at 14:30');
    });

    it('humanizes weekly runs at a fixed time', () => {
        expect(describeCron('0 9 * * 1')).toBe('Weekly on Monday at 09:00');
        expect(describeCron('0 0 * * 7')).toBe('Weekly on Sunday at 00:00');
    });

    it('falls back to the raw expression for shapes outside its vocabulary', () => {
        expect(describeCron('0 0 1 * *')).toBe('0 0 1 * *');
        expect(describeCron('0 0 * 6 *')).toBe('0 0 * 6 *');
        expect(describeCron('0 9-17 * * *')).toBe('0 9-17 * * *');
        expect(describeCron('0 0 * * 1,3,5')).toBe('0 0 * * 1,3,5');
    });

    it('falls back when the field count is not five', () => {
        expect(describeCron('0 0 * *')).toBe('0 0 * *');
        expect(describeCron('0 0 * * * *')).toBe('0 0 * * * *');
    });

    it('trims surrounding whitespace before matching', () => {
        expect(describeCron('  */5 * * * *  ')).toBe('Every 5 minutes');
    });
});

describe('describeCronUtc', () => {
    it('adds the UTC qualifier to humanized cron labels', () => {
        expect(describeCronUtc('0 6 * * *')).toBe('Daily at 6 AM (UTC)');
        expect(describeCronUtc('*/15 * * * *')).toBe('Every 15 minutes (UTC)');
    });

    it('adds the UTC qualifier to raw fallback expressions', () => {
        expect(describeCronUtc('0 0 1 * *')).toBe('0 0 1 * * (UTC)');
    });
});
