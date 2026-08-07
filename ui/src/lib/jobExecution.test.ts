import { afterEach, describe, expect, it, vi } from 'vitest';

import {
    canRunJobOnDemand,
    mapStatusToDisplay,
    statusLabels,
    statusFilterOptions,
    formatJobDateTime,
    calculateDuration,
    formatLogTime,
    humanizeJobType,
    requiredRoleForJobScope,
} from './jobExecution';
import type { JobExecutionStatusValue } from '@/hooks/useJobs';
import { Role } from '@/auth/types';

describe('humanizeJobType', () => {
    it('splits a PascalCase job type into spaced words', () => {
        expect(humanizeJobType('DownloadStuckDetection')).toBe('Download Stuck Detection');
        expect(humanizeJobType('ContentMetadataRefreshItem')).toBe('Content Metadata Refresh Item');
    });
});

describe('requiredRoleForJobScope', () => {
    it('matches backend on-demand role requirements', () => {
        expect(requiredRoleForJobScope('User')).toBe(Role.Requester);
        expect(requiredRoleForJobScope('Content')).toBe(Role.Manager);
        expect(requiredRoleForJobScope('Administrative')).toBe(Role.Admin);
        expect(requiredRoleForJobScope('System')).toBe(Role.Admin);
        expect(requiredRoleForJobScope(undefined)).toBe(Role.Admin);
    });
});

describe('canRunJobOnDemand', () => {
    const baseOption = {
        enabled: true,
        onDemand: true,
        supportedTriggerModes: ['Manual' as const],
    };

    it('allows a requester to run user-scoped jobs', () => {
        expect(canRunJobOnDemand({ ...baseOption, scope: 'User' }, Role.Requester)).toBe(true);
    });

    it('rejects a requester for content-scoped jobs', () => {
        expect(canRunJobOnDemand({ ...baseOption, scope: 'Content' }, Role.Requester)).toBe(false);
    });

    it('allows a manager to run content-scoped jobs', () => {
        expect(canRunJobOnDemand({ ...baseOption, scope: 'Content' }, Role.Manager)).toBe(true);
    });

    it('rejects a manager for administrative and system-scoped jobs', () => {
        expect(canRunJobOnDemand({ ...baseOption, scope: 'Administrative' }, Role.Manager)).toBe(false);
        expect(canRunJobOnDemand({ ...baseOption, scope: 'System' }, Role.Manager)).toBe(false);
    });

    it('allows an admin to run administrative and system-scoped jobs', () => {
        expect(canRunJobOnDemand({ ...baseOption, scope: 'Administrative' }, Role.Admin)).toBe(true);
        expect(canRunJobOnDemand({ ...baseOption, scope: 'System' }, Role.Admin)).toBe(true);
    });

    it('requires enabled on-demand jobs that support manual triggers', () => {
        expect(canRunJobOnDemand({ ...baseOption, enabled: false, scope: 'User' }, Role.Admin)).toBe(false);
        expect(canRunJobOnDemand({ ...baseOption, onDemand: false, scope: 'User' }, Role.Admin)).toBe(false);
        expect(
            canRunJobOnDemand({ ...baseOption, supportedTriggerModes: ['Schedule'], scope: 'User' }, Role.Admin),
        ).toBe(false);
    });
});

describe('mapStatusToDisplay', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('maps Queued to queued', () => {
        expect(mapStatusToDisplay('Queued')).toBe('queued');
    });

    it('maps Running to running', () => {
        expect(mapStatusToDisplay('Running')).toBe('running');
    });

    it('maps Completed to completed', () => {
        expect(mapStatusToDisplay('Completed')).toBe('completed');
    });

    it('maps Failed to failed', () => {
        expect(mapStatusToDisplay('Failed')).toBe('failed');
    });

    it('maps Cancelled to cancelled', () => {
        expect(mapStatusToDisplay('Cancelled')).toBe('cancelled');
    });

    it('maps CancellationRequested to running', () => {
        expect(mapStatusToDisplay('CancellationRequested')).toBe('running');
    });

    it('maps unknown values to queued', () => {
        expect(mapStatusToDisplay('Unknown' as JobExecutionStatusValue)).toBe('queued');
    });

    it.each([
        ['future', '2026-08-04T12:00:01Z', 'pending'],
        ['equal', '2026-08-04T12:00:00Z', 'queued'],
        ['past', '2026-08-04T11:59:59Z', 'queued'],
    ])('maps Queued with a $relation notBeforeAt to $expected', (_relation, notBeforeAt, expected) => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));

        expect(mapStatusToDisplay('Queued', notBeforeAt)).toBe(expected);
    });
});

describe('statusLabels', () => {
    it('has labels for all display statuses', () => {
        expect(statusLabels.pending).toBe('Pending');
        expect(statusLabels.queued).toBe('Queued');
        expect(statusLabels.running).toBe('Running');
        expect(statusLabels.completed).toBe('Completed');
        expect(statusLabels.failed).toBe('Failed');
        expect(statusLabels.cancelled).toBe('Cancelled');
    });
});

describe('statusFilterOptions', () => {
    it('includes all option plus each status, with pending and queued as separate options', () => {
        const values = statusFilterOptions.map((o) => o.value);
        expect(values).toContain('all');
        expect(values).toContain('running');
        expect(values).toContain('queued');
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

    it('returns <1s for sub-second durations', () => {
        expect(calculateDuration('2024-01-15T10:30:00.000Z', '2024-01-15T10:30:00.500Z')).toBe('<1s');
    });

    it('returns <1s for a zero-length duration', () => {
        expect(calculateDuration('2024-01-15T10:30:00.000Z', '2024-01-15T10:30:00.000Z')).toBe('<1s');
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
