import { forwardRef, memo, type HTMLAttributes, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwiseIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { ContentMonitoringIndicator } from '@/components/content/ContentCardPrimitives';
import { getFulfillmentBadge } from '@/types/content';
import type { FulfillmentStatus, MonitoringMode } from '@/types';

import { StatusRow as SharedStatusRow } from '../StatusRow';
import { Skeleton } from '../Skeleton';

import './ContentStatsWidget.css';

export type { FulfillmentStatus };

export interface FulfillmentCounts {
    total: number;
    missing: number;
    upgradable: number;
    fulfilled: number;
    fulfilledExternally: number;
    partial: number;
}

export interface RecentContentItem {
    id: string;
    parentId?: string;
    title: string;

    addedAt: string;
    fulfillment: FulfillmentStatus;

    monitoring?: MonitoringMode | null;

    effectiveMonitoring?: MonitoringMode;
}

export interface ContentStatsWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    counts: FulfillmentCounts;

    recentItems?: RecentContentItem[];

    maxItems?: number;

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    getStatusUrl?: (status: FulfillmentStatus) => string;

    viewAllUrl?: string;

    getItemUrl?: (item: RecentContentItem) => string;

    headingLevel?: 2 | 3;
}

interface StatusRowProps {
    status: FulfillmentStatus;
    count: number;
    url?: string;
}

const StatusRow = ({ status, count, url }: StatusRowProps) => {
    const { label } = getFulfillmentBadge(status);
    return <SharedStatusRow status={status} label={label} count={count} url={url} />;
};

interface ContentItemProps {
    item: RecentContentItem;
    url?: string;
}

const ContentItem = ({ item, url }: ContentItemProps) => {
    const isClickable = url !== undefined;
    const { variant, label: statusLabel } = getFulfillmentBadge(item.fulfillment);

    const content = (
        <>
            <span className="content-stats-widget__item-indicator" aria-hidden="true" />
            <ContentLineageTitle
                className="content-stats-widget__item-title"
                qualifiedName={{ id: item.id, parentId: item.parentId, title: item.title }}
            />
            <span className="content-stats-widget__item-meta">
                {item.effectiveMonitoring !== undefined && (
                    <ContentMonitoringIndicator
                        monitoring={item.monitoring === undefined ? item.effectiveMonitoring : item.monitoring}
                        effectiveMonitoring={item.effectiveMonitoring}
                        className="content-stats-widget__item-monitoring"
                        activeClassName="content-stats-widget__item-monitoring--active"
                    />
                )}
                <span className="content-stats-widget__item-status" aria-label={`Status: ${statusLabel}`}>
                    {statusLabel}
                </span>
                <span className="content-stats-widget__item-timestamp">{item.addedAt}</span>
            </span>
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={[
                    'content-stats-widget__item',
                    `content-stats-widget__item--${variant}`,
                    'content-stats-widget__item--clickable',
                ]
                    .filter(Boolean)
                    .join(' ')}
                data-content-id={item.id}
                data-status={item.fulfillment}
                data-variant={variant}
                data-clickable
            >
                {content}
            </Link>
        );
    }

    return (
        <div
            className={['content-stats-widget__item', `content-stats-widget__item--${variant}`].join(' ')}
            data-content-id={item.id}
            data-status={item.fulfillment}
            data-variant={variant}
        >
            {content}
        </div>
    );
};

const ContentStatsWidgetBase = forwardRef<HTMLDivElement, ContentStatsWidgetProps>(
    (
        {
            heading = 'Content',
            icon,
            counts,
            recentItems = [],
            maxItems = 3,
            loading = false,
            error = null,
            onRetry,
            getStatusUrl,
            viewAllUrl,
            getItemUrl,
            headingLevel = 2,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['content-stats-widget', className].filter(Boolean).join(' ');
        const total = counts.total;
        const displayedItems = recentItems.slice(0, maxItems);
        const hasMore = recentItems.length > maxItems || total > 0;
        const HeadingTag = `h${headingLevel}` as const;

        return (
            <div ref={ref} className={classes} data-component="content-stats-widget" {...props}>
                <div className="content-stats-widget__header">
                    {icon && (
                        <span className="content-stats-widget__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <HeadingTag className="content-stats-widget__title">{heading}</HeadingTag>
                    <span className="content-stats-widget__total">{loading ? '-' : total}</span>
                </div>

                <div className="content-stats-widget__summary">
                    {loading ? (
                        <>
                            <SharedStatusRow loading />
                            <SharedStatusRow loading />
                            <SharedStatusRow loading />
                            <SharedStatusRow loading />
                            <SharedStatusRow loading />
                        </>
                    ) : error ? (
                        <Alert variant="error" icon={<WarningCircleIcon weight="duotone" />} title="Failed to load">
                            {onRetry && (
                                <Button variant="ghost" size="sm" onClick={onRetry}>
                                    <ArrowCounterClockwiseIcon weight="bold" aria-hidden="true" />
                                    Try again
                                </Button>
                            )}
                        </Alert>
                    ) : (
                        <>
                            <StatusRow status="fulfilled" count={counts.fulfilled} url={getStatusUrl?.('fulfilled')} />
                            <StatusRow
                                status="upgradable"
                                count={counts.upgradable}
                                url={getStatusUrl?.('upgradable')}
                            />
                            <StatusRow status="partial" count={counts.partial} url={getStatusUrl?.('partial')} />
                            <StatusRow status="missing" count={counts.missing} url={getStatusUrl?.('missing')} />
                            <StatusRow
                                status="fulfilledExternally"
                                count={counts.fulfilledExternally}
                                url={getStatusUrl?.('fulfilledExternally')}
                            />
                        </>
                    )}
                </div>

                {(displayedItems.length > 0 || loading) && !error && (
                    <div className="content-stats-widget__recent">
                        <div className="content-stats-widget__recent-header">Recent Additions</div>
                        <div className="content-stats-widget__items">
                            {loading ? (
                                <>
                                    <Skeleton className="content-stats-widget__item" height={48} />
                                    <Skeleton className="content-stats-widget__item" height={48} />
                                </>
                            ) : (
                                displayedItems.map((item) => (
                                    <ContentItem key={item.id} item={item} url={getItemUrl?.(item)} />
                                ))
                            )}
                        </div>
                    </div>
                )}

                {hasMore && !loading && viewAllUrl && (
                    <div className="content-stats-widget__footer">
                        <Link to={viewAllUrl} className="content-stats-widget__view-all" data-action="view-all">
                            View all content
                        </Link>
                    </div>
                )}
            </div>
        );
    },
);

ContentStatsWidgetBase.displayName = 'ContentStatsWidget';

export const ContentStatsWidget = memo(ContentStatsWidgetBase);

export default ContentStatsWidget;
