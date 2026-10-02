import type { BadgeVariant } from '@/types/badge';

interface MagpieActivityStatusBadge {
    variant: BadgeVariant;
    label: string;
}

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
        label: key === 'CancellationRequested' ? 'Cancelling' : key,
    };
}
