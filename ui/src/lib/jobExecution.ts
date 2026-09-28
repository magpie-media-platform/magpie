import { Role, meetsRoleRequirement, type Role as RoleValue } from '@/auth/types';
import type { JobExecutionStatusValue, JobParameterDefinition, JobScopeValue, JobTriggerModeValue } from '@/types/jobs';

export type JobExecutionStatusDisplay = 'pending' | 'queued' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface StatusFilterOption {
    value: JobExecutionStatusDisplay | 'all';
    label: string;
}

export interface TemplateOption {
    id: string;
    name: string;
    onDemand?: boolean;
    supportsImmediate?: boolean;
    enabled?: boolean;
    scope?: JobScopeValue;
    supportedTriggerModes?: JobTriggerModeValue[];
    requesterTriggerModes?: JobTriggerModeValue[];
    defaultSchedule?: string | null;
    parameters?: JobParameterDefinition[];
}

export function requiredRoleForJobScope(scope: JobScopeValue | undefined): RoleValue {
    switch (scope) {
        case undefined:
            return Role.Admin;
        case 'User':
            return Role.Requester;
        case 'Content':
            return Role.Manager;
        case 'Administrative':
        case 'System':
            return Role.Admin;
    }
}

export function canRunJobOnDemand(
    option: Pick<TemplateOption, 'enabled' | 'onDemand' | 'scope' | 'supportedTriggerModes'>,
    userRole: RoleValue | null | undefined,
): boolean {
    if (userRole === null || userRole === undefined) {
        return false;
    }

    if (
        option.onDemand !== true ||
        option.enabled === false ||
        option.supportedTriggerModes?.includes('Manual') !== true
    ) {
        return false;
    }

    return meetsRoleRequirement(userRole, requiredRoleForJobScope(option.scope));
}

export function humanizeJobType(jobType: string): string {
    return jobType.replace(/([A-Z])/g, ' $1').trim();
}

export function mapStatusToDisplay(
    status: JobExecutionStatusValue,
    notBeforeAt?: string | null,
): JobExecutionStatusDisplay {
    if (status === 'Queued' && notBeforeAt && Date.parse(notBeforeAt) > Date.now()) {
        return 'pending';
    }

    switch (status) {
        case 'Queued':
            return 'queued';
        case 'Running':
        case 'CancellationRequested':
            return 'running';
        case 'Completed':
            return 'completed';
        case 'Failed':
            return 'failed';
        case 'Cancelled':
            return 'cancelled';
        default:
            return 'queued';
    }
}

export const statusLabels: Record<JobExecutionStatusDisplay, string> = {
    pending: 'Pending',
    queued: 'Queued',
    running: 'Running',
    completed: 'Completed',
    failed: 'Failed',
    cancelled: 'Cancelled',
};

export const statusFilterOptions: StatusFilterOption[] = [
    { value: 'all', label: 'All Statuses' },
    { value: 'running', label: 'Running' },
    { value: 'queued', label: 'Queued' },
    { value: 'pending', label: 'Pending' },
    { value: 'completed', label: 'Completed' },
    { value: 'failed', label: 'Failed' },
    { value: 'cancelled', label: 'Cancelled' },
];

export function formatJobDateTime(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        if (isNaN(date.getTime())) {
            return dateStr;
        }
        return date.toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
    } catch {
        return dateStr;
    }
}

export function calculateDuration(
    startedAt: string | null | undefined,
    completedAt: string | null | undefined,
): string {
    if (!startedAt || !completedAt) {
        return '--';
    }
    try {
        const start = new Date(startedAt).getTime();
        const end = new Date(completedAt).getTime();
        const diffMs = end - start;
        if (diffMs < 0) {
            return '--';
        }
        const seconds = Math.floor(diffMs / 1000);
        const minutes = Math.floor(seconds / 60);
        const hours = Math.floor(minutes / 60);
        if (hours > 0) {
            return `${hours}h ${minutes % 60}m`;
        }
        if (minutes > 0) {
            return `${minutes}m ${seconds % 60}s`;
        }
        if (seconds === 0) {
            return '<1s';
        }
        return `${seconds}s`;
    } catch {
        return '--';
    }
}

export function formatLogTime(timestamp: string): string {
    try {
        const date = new Date(timestamp);
        if (isNaN(date.getTime())) {
            return timestamp;
        }
        return date.toLocaleTimeString(undefined, {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
        });
    } catch {
        return timestamp;
    }
}
