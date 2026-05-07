import type { JobExecutionStatusValue } from '@/hooks/useJobs';

export type JobExecutionStatusDisplay = 'pending' | 'running' | 'completed' | 'failed' | 'cancelled';

export interface StatusFilterOption {
    value: JobExecutionStatusDisplay | 'all';
    label: string;
}

export interface TemplateOption {
    id: string;
    name: string;
}

export function mapStatusToDisplay(status: JobExecutionStatusValue): JobExecutionStatusDisplay {
    switch (status) {
        case 0:
        case 1:
            return 'pending';
        case 2:
        case 6:
            return 'running';
        case 3:
            return 'completed';
        case 4:
            return 'failed';
        case 5:
            return 'cancelled';
        default:
            return 'pending';
    }
}

export const statusLabels: Record<JobExecutionStatusDisplay, string> = {
    pending: 'Pending',
    running: 'Running',
    completed: 'Completed',
    failed: 'Failed',
    cancelled: 'Cancelled',
};

export const statusFilterOptions: StatusFilterOption[] = [
    { value: 'all', label: 'All Statuses' },
    { value: 'running', label: 'Running' },
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
