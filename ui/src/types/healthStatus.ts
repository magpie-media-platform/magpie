export type HealthStatusValue = 0 | 1 | 2 | 3;

export const healthStatusLabels: Record<HealthStatusValue, string> = {
    0: 'Unknown',
    1: 'Healthy',
    2: 'Unhealthy',
    3: 'Stale',
};

export const healthStatusVariants: Record<HealthStatusValue, string> = {
    0: 'unknown',
    1: 'healthy',
    2: 'unhealthy',
    3: 'stale',
};
