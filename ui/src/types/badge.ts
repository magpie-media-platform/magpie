export type BadgeVariant =
    | 'default'
    | 'healthy'
    | 'unhealthy'
    | 'degraded'
    | 'unknown'
    | 'stabilizing'
    | 'missing'
    | 'upgradable'
    | 'fulfilled'
    | 'fulfilledExternally'
    | 'partial'
    | 'primary'
    | 'warning'
    | 'info'
    | 'success'
    | 'downloading'
    | 'queued'
    | 'grabbed'
    | 'paused'
    | 'stalled'
    | 'downloaded'
    | 'importing'
    | 'imported'
    | 'failed'
    | 'running'
    | 'pending'
    | 'completed'
    | 'cancelled'
    | 'stale';

export type BadgeSize = 'sm' | 'md';

export type BadgeStyle = 'filled' | 'outline';
