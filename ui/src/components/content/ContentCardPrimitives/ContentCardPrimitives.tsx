import { useEffect, useState } from 'react';
import { EyeIcon, EyeSlashIcon, ImageBrokenIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { isUnrecognizedContentLevel, resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { resolveEffectiveMonitoring } from '@/lib/monitoring';
import { getFulfillmentBadge } from '@/types/content';
import type { FulfillmentStatus } from '@/types';

export interface ContentPosterProps {
    posterUrl?: string | null;
    title?: string;
    alt?: string;
    className: string;
    placeholderClassName: string;
}

export function ContentPoster({ posterUrl, title, alt, className, placeholderClassName }: ContentPosterProps) {
    const [posterFailed, setPosterFailed] = useState(false);

    useEffect(() => {
        setPosterFailed(false);
    }, [posterUrl]);

    if (posterUrl && !posterFailed) {
        return (
            <img
                src={posterUrl}
                alt={alt ?? `${title ?? 'Content'} artwork`}
                className={className}
                loading="lazy"
                onError={() => setPosterFailed(true)}
            />
        );
    }

    return (
        <div className={`${className} ${placeholderClassName}`} aria-hidden="true">
            <ImageBrokenIcon weight="thin" />
        </div>
    );
}

export interface ContentTypeBadgeProps {
    level?: string;
    levelLabel?: string;
    contentType?: string;
}

export function ContentTypeBadge({ level, levelLabel, contentType }: ContentTypeBadgeProps) {
    const { contentTypes } = useLibraryTypes();
    const typeIconName = useTypeIconName();
    const label = resolveContentTypeLabel({ level, levelLabel, contentType }, contentTypes) || 'Content';
    const needsClassification = isUnrecognizedContentLevel({ level, contentType }, contentTypes);

    return (
        <Badge variant={needsClassification ? 'warning' : 'default'} size="sm">
            <CdnPhosphorIcon name={typeIconName(contentType)} weight="duotone" aria-hidden="true" />
            {label}
        </Badge>
    );
}

export interface ContentMonitoringIndicatorProps {
    monitoring: boolean | null;
    effectiveMonitoring: boolean;
    className: string;
    activeClassName?: string;
    inactiveClassName?: string;
    labelClassName?: string;
    inactiveLabel?: string;
    dataMonitoring?: string;
}

export function ContentMonitoringIndicator({
    monitoring,
    effectiveMonitoring,
    className,
    activeClassName,
    inactiveClassName,
    labelClassName,
    inactiveLabel = 'Not monitored',
    dataMonitoring,
}: ContentMonitoringIndicatorProps) {
    const { monitored: isMonitored, inherited } = resolveEffectiveMonitoring(monitoring, effectiveMonitoring);
    const baseLabel = isMonitored ? 'Monitored' : inactiveLabel;
    const label = inherited ? `${baseLabel} (inherited)` : baseLabel;
    const Icon = isMonitored ? EyeIcon : EyeSlashIcon;
    const classes = [className, isMonitored ? activeClassName : inactiveClassName].filter(Boolean).join(' ');
    const textLabel = isMonitored ? 'Monitored' : 'Not monitored';

    return (
        <span className={classes} title={label} data-monitoring={dataMonitoring}>
            <Icon weight={isMonitored ? 'fill' : 'regular'} aria-label={label} />
            {labelClassName && <span className={labelClassName}>{textLabel}</span>}
        </span>
    );
}

interface ContentFulfillmentBadgeProps {
    fulfillment: FulfillmentStatus;
    effectiveMonitoring: boolean;
}

export function ContentFulfillmentBadge({ fulfillment, effectiveMonitoring }: ContentFulfillmentBadgeProps) {
    if (!effectiveMonitoring) {
        return null;
    }

    const badge = getFulfillmentBadge(fulfillment);

    return (
        <Badge variant={badge.variant} size="sm" dot>
            {badge.label}
        </Badge>
    );
}
