import type { BadgeVariant } from '@/types/badge';

interface MagpieActivityStatusBadge {
    variant: BadgeVariant;
    label: string;
}

const MAGPIE_ACTIVITY_STATUS_LABEL: Record<string, string> = {
    Queued: 'Queued',
    Pending: 'Pending',
    Running: 'Running',
    Completed: 'Completed',
    Failed: 'Failed',
    Cancelled: 'Cancelled',
    CancellationRequested: 'Cancelling',
    Stale: 'Stale',
};

const MAGPIE_ACTIVITY_STATUS_VARIANT: Record<string, BadgeVariant> = {
    Queued: 'unknown',
    Pending: 'unknown',
    Running: 'info',
    Completed: 'healthy',
    Failed: 'unhealthy',
    Cancelled: 'degraded',
    CancellationRequested: 'degraded',
    Stale: 'unhealthy',
};

export function getMagpieActivityStatusBadge(status: string | null | undefined): MagpieActivityStatusBadge {
    const key = status ?? '';
    return {
        variant: MAGPIE_ACTIVITY_STATUS_VARIANT[key] ?? 'unknown',
        label: MAGPIE_ACTIVITY_STATUS_LABEL[key] ?? key,
    };
}
