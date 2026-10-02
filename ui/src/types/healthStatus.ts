export type HealthStatusValue = 'Unknown' | 'Healthy' | 'Unhealthy' | 'Stale' | 'Degraded' | 'Stabilizing';

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
