import { forwardRef, memo, type HTMLAttributes, type ReactNode, useState } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwiseIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { useLibraryTypes, useTypeIconName } from '@/hooks/useLibraryTypes';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import type { FulfillmentStatus } from '@/types';

import './UpcomingContentWidget.css';

export type { FulfillmentStatus };

export interface UpcomingContentItem {
    id: string;

    contentId: string;

    parentId?: string;

    title: string;

    subtitle?: string;

    date: string;

    posterUrl?: string;

    contentType?: string;

    level?: string;

    levelLabel?: string;

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

function formatRelativeDate(dateStr: string): string {
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

function formatTime(dateStr: string): string {
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
    const fulfillmentClass = item.fulfillment;
    const { contentTypes } = useLibraryTypes();
    const typeIconName = useTypeIconName();
    const kindLabel =
        resolveContentTypeLabel(
            { level: item.level, levelLabel: item.levelLabel, contentType: item.contentType },
            contentTypes,
        ) || 'Content';
    const [failedPosterUrl, setFailedPosterUrl] = useState<string | null>(null);

    const content = (
        <>
            <div className="upcoming-content-widget__item-poster">
                {item.posterUrl && item.posterUrl !== failedPosterUrl ? (
                    <img
                        src={item.posterUrl}
                        alt=""
                        loading="lazy"
                        onError={() => setFailedPosterUrl(item.posterUrl ?? null)}
                    />
                ) : (
                    <div className="upcoming-content-widget__item-poster-placeholder">
                        <CdnPhosphorIcon name={typeIconName(item.contentType)} weight="duotone" aria-hidden="true" />
                    </div>
                )}
            </div>
            <div className="upcoming-content-widget__item-info">
                <div className="upcoming-content-widget__item-header">
                    <ContentLineageTitle
                        className="upcoming-content-widget__item-title"
                        qualifiedName={{ id: item.contentId, parentId: item.parentId, title: item.title }}
                        fallback={{ contentType: item.contentType, level: item.level }}
                    />
                    <span className="upcoming-content-widget__item-date">{formatRelativeDate(item.date)}</span>
                </div>
                {item.subtitle && <span className="upcoming-content-widget__item-subtitle">{item.subtitle}</span>}
                <div className="upcoming-content-widget__item-meta">
                    <span className="upcoming-content-widget__item-kind" data-content-type={item.contentType}>
                        {kindLabel}
                    </span>
                    <span className="upcoming-content-widget__item-time">{formatTime(item.date)}</span>
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
                data-content-id={item.contentId}
                data-fulfillment={item.fulfillment}
                data-content-type={item.contentType}
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
            data-content-type={item.contentType}
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
            const date = new Date(item.date);
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
                        <Alert variant="error" icon={<WarningCircleIcon weight="duotone" />} title="Failed to load">
                            {onRetry && (
                                <Button variant="ghost" size="sm" onClick={onRetry}>
                                    <ArrowCounterClockwiseIcon weight="bold" aria-hidden="true" />
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
