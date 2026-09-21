import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import { StatusRow as SharedStatusRow } from '../StatusRow';

import './HealthCard.css';

export type HealthStatus = 'healthy' | 'unhealthy' | 'degraded' | 'stabilizing' | 'unknown' | 'stale';

export interface HealthStatusCounts {
    healthy: number;
    unhealthy: number;
    degraded: number;
    stabilizing: number;
    unknown: number;
    stale: number;
}

export interface HealthCardProps extends HTMLAttributes<HTMLDivElement> {
    heading: string;

    icon?: ReactNode;

    counts: HealthStatusCounts;

    loading?: boolean;

    getStatusUrl?: (status: HealthStatus) => string;

    headingLevel?: 2 | 3;
}

const statusLabelMap: Record<HealthStatus, string> = {
    healthy: 'Healthy',
    unhealthy: 'Unhealthy',
    degraded: 'Degraded',
    stabilizing: 'Stabilizing',
    unknown: 'Unknown',
    stale: 'Stale',
};

interface StatusRowProps {
    status: HealthStatus;
    count: number;
    url?: string;
}

const StatusRow = ({ status, count, url }: StatusRowProps) =>
    count === 0 ? null : <SharedStatusRow status={status} label={statusLabelMap[status]} count={count} url={url} />;

export const HealthCard = forwardRef<HTMLDivElement, HealthCardProps>(
    ({ heading, icon, counts, loading = false, getStatusUrl, headingLevel = 2, className, ...props }, ref) => {
        const classes = ['health-card', className].filter(Boolean).join(' ');
        const total =
            counts.healthy + counts.unhealthy + counts.degraded + counts.stabilizing + counts.unknown + counts.stale;
        const HeadingTag = `h${headingLevel}` as const;

        return (
            <div ref={ref} className={classes} data-component="health-card" {...props}>
                <div className="health-card__header">
                    {icon && (
                        <span className="health-card__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <HeadingTag className="health-card__title">{heading}</HeadingTag>
                    <span className="health-card__total">{loading ? '-' : total}</span>
                </div>
                <div className="health-card__body">
                    {loading ? (
                        <>
                            <SharedStatusRow loading />
                            <SharedStatusRow loading />
                        </>
                    ) : total === 0 ? (
                        <div className="health-card__empty">None configured</div>
                    ) : (
                        <>
                            <StatusRow status="healthy" count={counts.healthy} url={getStatusUrl?.('healthy')} />
                            <StatusRow status="unhealthy" count={counts.unhealthy} url={getStatusUrl?.('unhealthy')} />
                            <StatusRow status="degraded" count={counts.degraded} url={getStatusUrl?.('degraded')} />
                            <StatusRow
                                status="stabilizing"
                                count={counts.stabilizing}
                                url={getStatusUrl?.('stabilizing')}
                            />
                            <StatusRow status="stale" count={counts.stale} url={getStatusUrl?.('stale')} />
                            <StatusRow status="unknown" count={counts.unknown} url={getStatusUrl?.('unknown')} />
                        </>
                    )}
                </div>
            </div>
        );
    },
);

HealthCard.displayName = 'HealthCard';

export default HealthCard;
