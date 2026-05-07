import { type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwise, WarningCircle } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import type { HealthStatusCounts, HealthStatus } from '@/components/ui/HealthCard';

import './ServiceHealthBar.css';

export interface ServiceHealthEntry {
    name: string;

    icon?: ReactNode;

    counts: HealthStatusCounts;

    getStatusUrl?: (status: HealthStatus) => string;

    addUrl?: string;
}

export interface ServiceHealthBarProps {
    services: ServiceHealthEntry[];

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    headingLevel?: 2 | 3;
}

function overallStatus(counts: HealthStatusCounts): HealthStatus {
    if (counts.unhealthy > 0) {
        return 'unhealthy';
    }
    if (counts.stale > 0) {
        return 'stale';
    }
    if (counts.unknown > 0) {
        return 'unknown';
    }
    return 'healthy';
}

function primaryUrl(counts: HealthStatusCounts, getStatusUrl?: (status: HealthStatus) => string): string | undefined {
    if (!getStatusUrl) {
        return undefined;
    }
    const status = overallStatus(counts);
    return getStatusUrl(status);
}

function totalCount(counts: HealthStatusCounts): number {
    return counts.healthy + counts.unhealthy + counts.unknown + counts.stale;
}

function summaryLabel(counts: HealthStatusCounts): string {
    const total = totalCount(counts);
    if (counts.unhealthy > 0) {
        return `${counts.unhealthy} unhealthy`;
    }
    if (counts.stale > 0) {
        return `${counts.stale} stale`;
    }
    if (counts.unknown > 0) {
        return `${counts.unknown} unknown`;
    }
    return `${total} OK`;
}

const variantMap: Record<HealthStatus, string> = {
    healthy: 'healthy',
    unhealthy: 'unhealthy',
    stale: 'degraded',
    unknown: 'unknown',
};

const SkeletonSegment = () => (
    <div
        className="service-health-bar__segment service-health-bar__segment--skeleton"
        data-testid="service-health-bar__segment--skeleton"
    >
        <span className="service-health-bar__icon service-health-bar__icon--skeleton" aria-hidden="true" />
        <span className="service-health-bar__name service-health-bar__name--skeleton" />
        <span className="service-health-bar__dot service-health-bar__dot--skeleton" />
        <span className="service-health-bar__summary service-health-bar__summary--skeleton" />
    </div>
);

export function ServiceHealthBar({
    services,
    loading = false,
    error = null,
    onRetry,
    headingLevel = 2,
}: ServiceHealthBarProps) {
    const HeadingTag = `h${headingLevel}` as const;

    return (
        <div className="service-health-bar" data-component="service-health-bar">
            <HeadingTag className="sr-only">Service Health</HeadingTag>

            <div className="service-health-bar__services">
                {loading ? (
                    <>
                        <SkeletonSegment />
                        <SkeletonSegment />
                        <SkeletonSegment />
                    </>
                ) : error ? (
                    <Alert variant="error" icon={<WarningCircle weight="duotone" />} title="Failed to load">
                        {onRetry && (
                            <Button variant="ghost" size="sm" onClick={onRetry}>
                                <ArrowCounterClockwise weight="bold" aria-hidden="true" />
                                Try again
                            </Button>
                        )}
                    </Alert>
                ) : (
                    services.map((service) => {
                        const isEmpty = totalCount(service.counts) === 0;
                        const status = overallStatus(service.counts);
                        const variant = variantMap[status];
                        const url =
                            isEmpty && service.addUrl
                                ? service.addUrl
                                : primaryUrl(service.counts, service.getStatusUrl);
                        const label = isEmpty && service.addUrl ? 'Add first' : summaryLabel(service.counts);

                        const content = (
                            <>
                                {service.icon && (
                                    <span className="service-health-bar__icon" aria-hidden="true">
                                        {service.icon}
                                    </span>
                                )}
                                <span className="service-health-bar__name">{service.name}</span>
                                <span
                                    className={`service-health-bar__dot service-health-bar__dot--${variant}`}
                                    aria-hidden="true"
                                />
                                <span className="service-health-bar__summary">{label}</span>
                            </>
                        );

                        if (url) {
                            return (
                                <Link
                                    key={service.name}
                                    to={url}
                                    className={`service-health-bar__segment service-health-bar__segment--${variant}`}
                                    aria-label={`${service.name}: ${label}`}
                                >
                                    {content}
                                </Link>
                            );
                        }

                        return (
                            <div
                                key={service.name}
                                className={`service-health-bar__segment service-health-bar__segment--${variant}`}
                                aria-label={`${service.name}: ${label}`}
                            >
                                {content}
                            </div>
                        );
                    })
                )}
            </div>
        </div>
    );
}

ServiceHealthBar.displayName = 'ServiceHealthBar';

export default ServiceHealthBar;
