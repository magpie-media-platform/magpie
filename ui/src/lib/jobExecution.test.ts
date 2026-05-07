import { describe, expect, it } from 'vitest';

import {
    mapStatusToDisplay,
    statusLabels,
    statusFilterOptions,
    formatJobDateTime,
    calculateDuration,
    formatLogTime,
} from './jobExecution';
import type { JobExecutionStatusValue } from '@/hooks/useJobs';

describe('mapStatusToDisplay', () => {
    it('maps Created (0) to pending', () => {
        expect(mapStatusToDisplay(0 as JobExecutionStatusValue)).toBe('pending');
    });

    it('maps Queued (1) to pending', () => {
        expect(mapStatusToDisplay(1 as JobExecutionStatusValue)).toBe('pending');
    });

    it('maps Running (2) to running', () => {
        expect(mapStatusToDisplay(2 as JobExecutionStatusValue)).toBe('running');
    });

    it('maps Completed (3) to completed', () => {
        expect(mapStatusToDisplay(3 as JobExecutionStatusValue)).toBe('completed');
    });

    it('maps Failed (4) to failed', () => {
        expect(mapStatusToDisplay(4 as JobExecutionStatusValue)).toBe('failed');
    });

    it('maps Cancelled (5) to cancelled', () => {
        expect(mapStatusToDisplay(5 as JobExecutionStatusValue)).toBe('cancelled');
    });

    it('maps CancellationRequested (6) to running', () => {
        expect(mapStatusToDisplay(6 as JobExecutionStatusValue)).toBe('running');
    });

    it('maps unknown values to pending', () => {
        expect(mapStatusToDisplay(99 as JobExecutionStatusValue)).toBe('pending');
    });
});

describe('statusLabels', () => {
    it('has labels for all display statuses', () => {
        expect(statusLabels.pending).toBe('Pending');
        expect(statusLabels.running).toBe('Running');
        expect(statusLabels.completed).toBe('Completed');
        expect(statusLabels.failed).toBe('Failed');
        expect(statusLabels.cancelled).toBe('Cancelled');
    });
});

describe('statusFilterOptions', () => {
    it('includes all option plus each status', () => {
        const values = statusFilterOptions.map((o) => o.value);
        expect(values).toContain('all');
        expect(values).toContain('running');
        expect(values).toContain('pending');
        expect(values).toContain('completed');
        expect(values).toContain('failed');
        expect(values).toContain('cancelled');
    });
});

describe('formatJobDateTime', () => {
    it('formats a valid ISO date', () => {
        const raw = '2024-01-15T10:30:00Z';
        const expected = new Date(raw).toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
        expect(formatJobDateTime(raw)).toBe(expected);
    });

    it('returns raw string for invalid dates', () => {
        expect(formatJobDateTime('not-a-date')).toBe('not-a-date');
    });
});

describe('calculateDuration', () => {
    it('returns -- when startedAt is null', () => {
        expect(calculateDuration(null, '2024-01-15T10:30:00Z')).toBe('--');
    });

    it('returns -- when completedAt is null', () => {
        expect(calculateDuration('2024-01-15T10:30:00Z', null)).toBe('--');
    });

    it('returns seconds for short durations', () => {
        expect(calculateDuration('2024-01-15T10:30:00Z', '2024-01-15T10:30:30Z')).toBe('30s');
    });

    it('returns minutes and seconds for medium durations', () => {
        expect(calculateDuration('2024-01-15T10:30:00Z', '2024-01-15T10:35:30Z')).toBe('5m 30s');
    });

    it('returns hours and minutes for long durations', () => {
        expect(calculateDuration('2024-01-15T10:00:00Z', '2024-01-15T12:30:00Z')).toBe('2h 30m');
    });

    it('returns -- for negative durations', () => {
        expect(calculateDuration('2024-01-15T12:00:00Z', '2024-01-15T10:00:00Z')).toBe('--');
    });
});

describe('formatLogTime', () => {
    it('formats a valid UTC timestamp to locale time', () => {
        const result = formatLogTime('2024-01-15T10:30:45Z');
        expect(result).toMatch(/\d{1,2}:\d{2}:\d{2}/);
    });

    it('returns the raw string for an invalid timestamp', () => {
        expect(formatLogTime('not-a-date')).toBe('not-a-date');
    });
});
