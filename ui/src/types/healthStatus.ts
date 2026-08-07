export type HealthStatusValue = 'Unknown' | 'Healthy' | 'Unhealthy' | 'Stale' | 'Degraded' | 'Stabilizing';

export const healthStatusLabels: Record<HealthStatusValue, string> = {
    Unknown: 'Unknown',
    Healthy: 'Healthy',
    Unhealthy: 'Unhealthy',
    Stale: 'Stale',
    Degraded: 'Degraded',
    Stabilizing: 'Stabilizing',
};

export const healthStatusVariants: Record<HealthStatusValue, string> = {
    Unknown: 'unknown',
    Healthy: 'healthy',
    Unhealthy: 'unhealthy',
    Stale: 'stale',
    Degraded: 'degraded',
    Stabilizing: 'stabilizing',
};

export const normalizedHealthStatuses: Record<string, HealthStatusValue> = {
    unknown: 'Unknown',
    healthy: 'Healthy',
    unhealthy: 'Unhealthy',
    stale: 'Stale',
    degraded: 'Degraded',
    stabilizing: 'Stabilizing',
};

export function normalizeHealthStatus(status: string | null | undefined): HealthStatusValue {
    if (!status) {
        return 'Unknown';
    }

    return normalizedHealthStatuses[status.trim().toLowerCase()] ?? 'Unknown';
}
