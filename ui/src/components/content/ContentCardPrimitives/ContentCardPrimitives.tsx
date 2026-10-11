import { useEffect, useState, type MouseEvent, type ReactNode } from 'react';
import { EyeIcon, EyeSlashIcon, FlagCheckeredIcon, ImageBrokenIcon } from '@phosphor-icons/react';

import { Badge, type BadgeSize } from '@/components/ui/Badge';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { Tooltip } from '@/components/ui/Tooltip';
import { useLongPress } from '@/hooks/ui/useLongPress';
import { useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { isUnrecognizedContentLevel, resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { getFulfillmentBadge } from '@/types/content';
import type { TemporalPrecision } from '@/types/contentMetadata';
import type { FulfillmentStatus, MonitoringMode } from '@/types';

const brokenImageGlyph = <ImageBrokenIcon weight="thin" />;

const CROPPED_ARTWORK_MAX_RATIO = 0.8;

export interface ContentPosterProps {
    posterUrl?: string | null;
    title?: string;
    alt?: string;
    className: string;
    placeholderClassName: string;
    placeholder?: ReactNode;
    'data-testid'?: string;
    loading?: 'eager' | 'lazy';
}

export function ContentPoster({
    posterUrl,
    title,
    alt,
    className,
    placeholderClassName,
    placeholder = brokenImageGlyph,
    loading = 'lazy',
    ...imageProps
}: ContentPosterProps) {
    const [posterFailed, setPosterFailed] = useState(false);
    const [posterUncropped, setPosterUncropped] = useState(false);

    useEffect(() => {
        setPosterFailed(false);
        setPosterUncropped(false);
    }, [posterUrl]);

    if (posterUrl && !posterFailed) {
        return (
            <img
                {...imageProps}
                src={posterUrl}
                alt={alt ?? `${title ?? 'Content'} artwork`}
                className={className}
                loading={loading}
                data-artwork={posterUncropped ? 'uncropped' : undefined}
                onLoad={({ currentTarget }) =>
                    setPosterUncropped(
                        currentTarget.naturalHeight > 0 &&
                            currentTarget.naturalWidth / currentTarget.naturalHeight > CROPPED_ARTWORK_MAX_RATIO,
                    )
                }
                onError={() => setPosterFailed(true)}
            />
        );
    }

    return (
        <div className={`${className} ${placeholderClassName}`} aria-hidden="true">
            {posterUrl ? brokenImageGlyph : placeholder}
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
    effectiveMonitoring: MonitoringMode;
    className: string;
    activeClassName?: string;
    inactiveClassName?: string;
    /** Shows the state as text next to the icon. Compact lists leave it out and show the icon only. */
    labelClassName?: string;
    dataMonitoring?: string;
}

/**
 * Shows the monitoring state as an icon: an eye, a struck-through eye, or a checkered flag for Shotgun. A tooltip and
 * the accessible name give the monitoring state. A long press opens the tooltip on touch devices, which have no hover.
 */
export function ContentMonitoringIndicator({
    effectiveMonitoring,
    className,
    activeClassName,
    inactiveClassName,
    labelClassName,
    dataMonitoring,
}: ContentMonitoringIndicatorProps) {
    const [open, setOpen] = useState(false);
    const longPress = useLongPress({ onLongPress: () => setOpen(true) });
    const isMonitored = effectiveMonitoring !== 'Unmonitored';
    const isShotgun = effectiveMonitoring === 'Shotgun';
    const Icon = !isMonitored ? EyeSlashIcon : isShotgun ? FlagCheckeredIcon : EyeIcon;
    const classes = [className, isMonitored ? activeClassName : inactiveClassName].filter(Boolean).join(' ');

    return (
        <Tooltip content={effectiveMonitoring} open={open} onOpenChange={setOpen}>
            <span
                className={classes}
                role="img"
                aria-label={effectiveMonitoring}
                data-monitoring={dataMonitoring}
                data-shotgun={isShotgun ? 'true' : undefined}
                {...longPress.handlers}
                onPointerDown={(event) => {
                    event.stopPropagation();
                    longPress.handlers.onPointerDown(event);
                }}
                onClick={(event: MouseEvent) => {
                    longPress.consumeClick(event);
                }}
                onContextMenu={(event) => event.preventDefault()}
                style={{ WebkitTouchCallout: 'none', userSelect: 'none' }}
            >
                <Icon weight={isMonitored ? 'fill' : 'regular'} aria-hidden="true" />
                {labelClassName && (
                    <span className={labelClassName} aria-hidden="true">
                        {effectiveMonitoring}
                    </span>
                )}
            </span>
        </Tooltip>
    );
}

interface ContentFulfillmentBadgeProps {
    fulfillment: FulfillmentStatus;
    importPending?: boolean;
    publishDate?: string;
    publishDatePrecision?: TemporalPrecision;
    size?: BadgeSize;
    className?: string;
}

export function ContentFulfillmentBadge({
    fulfillment,
    importPending,
    publishDate,
    publishDatePrecision,
    size = 'sm',
    className,
}: ContentFulfillmentBadgeProps) {
    const badge = getFulfillmentBadge(fulfillment, { importPending, publishDate, publishDatePrecision });

    return (
        <Badge variant={badge.variant} size={size} className={className} dot>
            {badge.label}
        </Badge>
    );
}
