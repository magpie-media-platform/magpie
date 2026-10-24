import { forwardRef, memo, type HTMLAttributes, type ReactNode } from 'react';
import { Link } from 'react-router-dom';

import { EmptyState } from '@/components/ui/EmptyState';
import { LoadErrorAlert } from '@/components/ui/LoadErrorAlert';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { ContentMonitoringIndicator } from '@/components/content/ContentCardPrimitives';
import { getFulfillmentBadge } from '@/types/content';
import type { TemporalPrecision } from '@/types/contentMetadata';
import type { FulfillmentStatus, MonitoringMode } from '@/types';

import { StatusRow as SharedStatusRow } from '../StatusRow';
import { Skeleton } from '../Skeleton';

import './ContentStatsWidget.css';

export interface ContentStatsRow {
    status: FulfillmentStatus;

    label?: string;

    count: number;

    url?: string;
}

export interface RecentContentItem {
    id: string;
    parentId?: string;
    title: string;

    timestamp: string;
    fulfillment: FulfillmentStatus;
    publishDate?: string;
    publishDatePrecision?: TemporalPrecision;

    effectiveMonitoring?: MonitoringMode;
}

export interface ContentStatsWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    total?: number;

    rows: ContentStatsRow[];

    recentHeading?: string;

    recentEmptyMessage?: string;

    recentItems?: RecentContentItem[];

    maxItems?: number;

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    viewAllUrl?: string;

    viewAllLabel?: string;

    getItemUrl?: (item: RecentContentItem) => string;

    showItemStatus?: boolean;
}

interface ContentItemProps {
    item: RecentContentItem;
    url?: string;
    showStatus: boolean;
}

const ContentItem = ({ item, url, showStatus }: ContentItemProps) => {
    const isClickable = url !== undefined;
    const { variant, label: statusLabel } = getFulfillmentBadge(item.fulfillment, {
        publishDate: item.publishDate,
        publishDatePrecision: item.publishDatePrecision,
    });

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
                        effectiveMonitoring={item.effectiveMonitoring}
                        className="content-stats-widget__item-monitoring"
                        activeClassName="content-stats-widget__item-monitoring--active"
                    />
                )}
                {showStatus && (
                    <span className="content-stats-widget__item-status" aria-label={`Status: ${statusLabel}`}>
                        {statusLabel}
                    </span>
                )}
                <span className="content-stats-widget__item-timestamp">{item.timestamp}</span>
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
            total,
            rows,
            recentHeading = 'Recent Additions',
            recentEmptyMessage = 'No recent additions',
            recentItems = [],
            maxItems = 3,
            loading = false,
            error = null,
            onRetry,
            viewAllUrl,
            viewAllLabel = 'View all content',
            getItemUrl,
            showItemStatus = true,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['content-stats-widget', className].filter(Boolean).join(' ');
        const displayedItems = recentItems.slice(0, maxItems);
        const hasContent = recentItems.length > 0 || (total ?? 0) > 0;

        return (
            <div ref={ref} className={classes} data-component="content-stats-widget" {...props}>
                <div className="content-stats-widget__header">
                    {icon && (
                        <span className="content-stats-widget__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <h2 className="content-stats-widget__title">{heading}</h2>
                    {total !== undefined && (
                        <span className="content-stats-widget__total">{loading ? '-' : total}</span>
                    )}
                </div>

                <div className="content-stats-widget__summary">
                    {loading ? (
                        rows.map((row) => <SharedStatusRow key={row.label ?? row.status} loading />)
                    ) : error ? (
                        <LoadErrorAlert onRetry={onRetry} />
                    ) : (
                        rows.map((row) => (
                            <SharedStatusRow
                                key={row.label ?? row.status}
                                status={row.status}
                                label={row.label ?? getFulfillmentBadge(row.status).label}
                                count={row.count}
                                url={row.url}
                            />
                        ))
                    )}
                </div>

                {!error && (
                    <>
                        <div className="content-stats-widget__recent-header">{recentHeading}</div>
                        <div className="content-stats-widget__items">
                            {loading ? (
                                <>
                                    <Skeleton className="content-stats-widget__item" height={48} />
                                    <Skeleton className="content-stats-widget__item" height={48} />
                                </>
                            ) : displayedItems.length === 0 ? (
                                <EmptyState title={recentEmptyMessage} size="sm" />
                            ) : (
                                displayedItems.map((item) => (
                                    <ContentItem
                                        key={item.id}
                                        item={item}
                                        url={getItemUrl?.(item)}
                                        showStatus={showItemStatus}
                                    />
                                ))
                            )}
                        </div>
                    </>
                )}

                {hasContent && !loading && viewAllUrl && (
                    <div className="content-stats-widget__footer">
                        <Link to={viewAllUrl} className="content-stats-widget__view-all" data-action="view-all">
                            {viewAllLabel}
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
