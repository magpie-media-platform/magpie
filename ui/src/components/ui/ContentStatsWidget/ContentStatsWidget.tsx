import { forwardRef, memo, type HTMLAttributes, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwise, WarningCircle } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';

import './ContentStatsWidget.css';

export type FulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally';

export interface FulfillmentCounts {
    total: number;
    missing: number;
    upgradable: number;
    fulfilled: number;
    fulfilledExternally: number;
}

export interface RecentContentItem {
    id: string;
    title: string;

    addedAt: string;
    fulfillment: FulfillmentStatus;
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

const statusVariantMap: Record<FulfillmentStatus, string> = {
    missing: 'missing',
    upgradable: 'upgradable',
    fulfilled: 'fulfilled',
    fulfilledExternally: 'fulfilledExternally',
};

const statusLabelMap: Record<FulfillmentStatus, string> = {
    missing: 'Missing',
    upgradable: 'Upgradable',
    fulfilled: 'Fulfilled',
    fulfilledExternally: 'External',
};

interface StatusRowProps {
    status: FulfillmentStatus;
    count: number;
    url?: string;
}

const StatusRow = ({ status, count, url }: StatusRowProps) => {
    const variant = statusVariantMap[status];
    const label = statusLabelMap[status];
    const isClickable = url !== undefined && count > 0;

    const content = (
        <>
            <span className="content-stats-widget__indicator" aria-hidden="true" />
            <span className="content-stats-widget__label">{label}</span>
            <span className="content-stats-widget__count">{count}</span>
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={`content-stats-widget__row content-stats-widget__row--${variant} content-stats-widget__row--clickable`}
                aria-label={`View ${count} ${label.toLowerCase()} items`}
                data-status={status}
                data-variant={variant}
                data-clickable
            >
                {content}
            </Link>
        );
    }

    return (
        <div
            className={`content-stats-widget__row content-stats-widget__row--${variant}`}
            data-status={status}
            data-variant={variant}
        >
            {content}
        </div>
    );
};

interface ContentItemProps {
    item: RecentContentItem;
    url?: string;
}

const ContentItem = ({ item, url }: ContentItemProps) => {
    const isClickable = url !== undefined;
    const variant = statusVariantMap[item.fulfillment];
    const statusLabel = statusLabelMap[item.fulfillment];

    const content = (
        <>
            <span className="content-stats-widget__item-indicator" aria-hidden="true" />
            <span className="content-stats-widget__item-title">{item.title}</span>
            <span className="content-stats-widget__item-meta">
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
                className={`content-stats-widget__item content-stats-widget__item--${variant} content-stats-widget__item--clickable`}
                aria-label={`View ${item.title}`}
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
            className={`content-stats-widget__item content-stats-widget__item--${variant}`}
            data-content-id={item.id}
            data-status={item.fulfillment}
            data-variant={variant}
        >
            {content}
        </div>
    );
};

const SkeletonRow = () => (
    <div className="content-stats-widget__row content-stats-widget__row--skeleton">
        <span className="content-stats-widget__indicator content-stats-widget__indicator--skeleton" />
        <span className="content-stats-widget__label content-stats-widget__label--skeleton" />
        <span className="content-stats-widget__count content-stats-widget__count--skeleton" />
    </div>
);

const SkeletonItem = () => (
    <div className="content-stats-widget__item content-stats-widget__item--skeleton">
        <span className="content-stats-widget__item-indicator content-stats-widget__item-indicator--skeleton" />
        <span className="content-stats-widget__item-title content-stats-widget__item-title--skeleton" />
        <span className="content-stats-widget__item-meta">
            <span className="content-stats-widget__item-status content-stats-widget__item-status--skeleton" />
            <span className="content-stats-widget__item-timestamp content-stats-widget__item-timestamp--skeleton" />
        </span>
    </div>
);

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
                            <SkeletonRow />
                            <SkeletonRow />
                            <SkeletonRow />
                            <SkeletonRow />
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
                        <>
                            <StatusRow status="fulfilled" count={counts.fulfilled} url={getStatusUrl?.('fulfilled')} />
                            <StatusRow
                                status="upgradable"
                                count={counts.upgradable}
                                url={getStatusUrl?.('upgradable')}
                            />
                            <StatusRow status="missing" count={counts.missing} url={getStatusUrl?.('missing')} />
                            <StatusRow status="fulfilledExternally" count={counts.fulfilledExternally} url={getStatusUrl?.('fulfilledExternally')} />
                        </>
                    )}
                </div>

                {(displayedItems.length > 0 || loading) && !error && (
                    <div className="content-stats-widget__recent">
                        <div className="content-stats-widget__recent-header">Recent Additions</div>
                        <div className="content-stats-widget__items">
                            {loading ? (
                                <>
                                    <SkeletonItem />
                                    <SkeletonItem />
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
