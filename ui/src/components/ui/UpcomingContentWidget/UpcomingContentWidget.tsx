import { forwardRef, memo, type HTMLAttributes, type ReactNode, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwise, WarningCircle } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';

import './UpcomingContentWidget.css';

export type ContentKind = 'episode' | 'movie' | 'music' | 'book' | 'unknown';

export type FulfillmentStatus = 'missing' | 'upgradable' | 'fulfilled' | 'fulfilledExternally';

export interface UpcomingContentItem {
    id: string;

    contentId: string;

    title: string;

    subtitle?: string;

    airDate: string;

    posterUrl?: string;

    kind: ContentKind;

    fulfillment: FulfillmentStatus;
}

export interface UpcomingContentWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    items: UpcomingContentItem[];

    maxItems?: number;

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    getItemUrl?: (item: UpcomingContentItem) => string;

    viewCalendarUrl?: string;

    headingLevel?: 2 | 3;
}

const kindClassMap: Record<ContentKind, string> = {
    episode: 'episode',
    movie: 'movie',
    music: 'music',
    book: 'book',
    unknown: 'unknown',
};

const fulfillmentClassMap: Record<FulfillmentStatus, string> = {
    missing: 'missing',
    upgradable: 'upgradable',
    fulfilled: 'fulfilled',
    fulfilledExternally: 'fulfilledExternally',
};

const kindLabelMap: Record<ContentKind, string> = {
    episode: 'Episode',
    movie: 'Movie',
    music: 'Music',
    book: 'Book',
    unknown: 'Unknown',
};

function formatAirDate(dateStr: string): string {
    const date = new Date(dateStr);
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const tomorrow = new Date(today);
    tomorrow.setDate(tomorrow.getDate() + 1);

    const eventDate = new Date(date);
    eventDate.setHours(0, 0, 0, 0);

    const diffDays = Math.round((eventDate.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));

    if (diffDays === 0) {
        return 'Today';
    } else if (diffDays === 1) {
        return 'Tomorrow';
    } else if (diffDays > 1 && diffDays <= 7) {
        return date.toLocaleDateString(undefined, { weekday: 'long' });
    } else {
        return date.toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
        });
    }
}

function formatAirTime(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleTimeString(undefined, {
        hour: 'numeric',
        minute: '2-digit',
    });
}

interface ContentItemProps {
    item: UpcomingContentItem;
    url?: string;
}

const ContentItem = ({ item, url }: ContentItemProps) => {
    const isClickable = url !== undefined;
    const fulfillmentClass = fulfillmentClassMap[item.fulfillment];
    const kindClass = kindClassMap[item.kind];
    const kindLabel = kindLabelMap[item.kind];
    const [imageError, setImageError] = useState(false);

    const content = (
        <>
            <div className="upcoming-content-widget__item-poster">
                {item.posterUrl && !imageError ? (
                    <img src={item.posterUrl} alt="" loading="lazy" onError={() => setImageError(true)} />
                ) : (
                    <div
                        className={`upcoming-content-widget__item-poster-placeholder upcoming-content-widget__item-poster-placeholder--${kindClass}`}
                    >
                        <span className="upcoming-content-widget__item-kind-letter" aria-hidden="true">
                            {kindLabel.charAt(0)}
                        </span>
                    </div>
                )}
            </div>
            <div className="upcoming-content-widget__item-info">
                <div className="upcoming-content-widget__item-header">
                    <span className="upcoming-content-widget__item-title">{item.title}</span>
                    <span className="upcoming-content-widget__item-date">{formatAirDate(item.airDate)}</span>
                </div>
                {item.subtitle && <span className="upcoming-content-widget__item-subtitle">{item.subtitle}</span>}
                <div className="upcoming-content-widget__item-meta">
                    <span
                        className={`upcoming-content-widget__item-kind upcoming-content-widget__item-kind--${kindClass}`}
                        data-kind={kindClass}
                    >
                        {kindLabel}
                    </span>
                    <span className="upcoming-content-widget__item-time">{formatAirTime(item.airDate)}</span>
                </div>
            </div>
            <span className="upcoming-content-widget__item-indicator" aria-hidden="true" />
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={`upcoming-content-widget__item upcoming-content-widget__item--${fulfillmentClass} upcoming-content-widget__item--clickable`}
                aria-label={`View ${item.title}`}
                data-content-id={item.contentId}
                data-fulfillment={item.fulfillment}
                data-kind={item.kind}
                data-variant={fulfillmentClass}
                data-clickable
            >
                {content}
            </Link>
        );
    }

    return (
        <div
            className={`upcoming-content-widget__item upcoming-content-widget__item--${fulfillmentClass}`}
            data-content-id={item.contentId}
            data-fulfillment={item.fulfillment}
            data-kind={item.kind}
            data-variant={fulfillmentClass}
        >
            {content}
        </div>
    );
};

const SkeletonItem = () => (
    <div className="upcoming-content-widget__item upcoming-content-widget__item--skeleton">
        <div className="upcoming-content-widget__item-poster upcoming-content-widget__item-poster--skeleton" />
        <div className="upcoming-content-widget__item-info">
            <div className="upcoming-content-widget__item-header">
                <span className="upcoming-content-widget__item-title upcoming-content-widget__item-title--skeleton" />
                <span className="upcoming-content-widget__item-date upcoming-content-widget__item-date--skeleton" />
            </div>
            <span className="upcoming-content-widget__item-subtitle upcoming-content-widget__item-subtitle--skeleton" />
            <div className="upcoming-content-widget__item-meta">
                <span className="upcoming-content-widget__item-kind upcoming-content-widget__item-kind--skeleton" />
                <span className="upcoming-content-widget__item-time upcoming-content-widget__item-time--skeleton" />
            </div>
        </div>
    </div>
);

const UpcomingContentWidgetBase = forwardRef<HTMLDivElement, UpcomingContentWidgetProps>(
    (
        {
            heading = 'Upcoming',
            icon,
            items,
            maxItems = 5,
            loading = false,
            error = null,
            onRetry,
            getItemUrl,
            viewCalendarUrl,
            headingLevel = 2,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['upcoming-content-widget', className].filter(Boolean).join(' ');
        const displayedItems = items.slice(0, maxItems);
        const hasMore = items.length > maxItems;
        const HeadingTag = `h${headingLevel}` as const;

        const todayCount = items.filter((item) => {
            const date = new Date(item.airDate);
            const today = new Date();
            return date.toDateString() === today.toDateString();
        }).length;

        return (
            <div ref={ref} className={classes} data-component="upcoming-content-widget" {...props}>
                <div className="upcoming-content-widget__header">
                    {icon && (
                        <span className="upcoming-content-widget__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <HeadingTag className="upcoming-content-widget__title">{heading}</HeadingTag>
                    {todayCount > 0 && !loading && (
                        <span
                            className="upcoming-content-widget__today-badge"
                            aria-label={`${todayCount} airing today`}
                        >
                            {todayCount} today
                        </span>
                    )}
                </div>

                <div className="upcoming-content-widget__body">
                    {loading ? (
                        <>
                            <SkeletonItem />
                            <SkeletonItem />
                            <SkeletonItem />
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
                    ) : displayedItems.length > 0 ? (
                        displayedItems.map((item) => <ContentItem key={item.id} item={item} url={getItemUrl?.(item)} />)
                    ) : (
                        <div className="upcoming-content-widget__empty">No upcoming content</div>
                    )}
                </div>

                {(hasMore || items.length > 0) && !loading && viewCalendarUrl && (
                    <div className="upcoming-content-widget__footer">
                        <Link
                            to={viewCalendarUrl}
                            className="upcoming-content-widget__view-all"
                            data-action="view-calendar"
                        >
                            View calendar
                        </Link>
                    </div>
                )}
            </div>
        );
    },
);

UpcomingContentWidgetBase.displayName = 'UpcomingContentWidget';

export const UpcomingContentWidget = memo(UpcomingContentWidgetBase);

export default UpcomingContentWidget;
