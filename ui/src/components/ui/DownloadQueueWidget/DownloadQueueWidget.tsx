import { forwardRef, memo, type HTMLAttributes, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwise, WarningCircle } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import type { DownloadState, DownloadStateCounts, ActiveDownloadItem } from '@/types/system';

import './DownloadQueueWidget.css';

export type { DownloadState, DownloadStateCounts, ActiveDownloadItem };

export interface DownloadQueueWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    counts: DownloadStateCounts;

    activeDownloads?: ActiveDownloadItem[];

    maxItems?: number;

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    getStateUrl?: (state: keyof DownloadStateCounts) => string;

    viewAllUrl?: string;

    getDownloadUrl?: (download: ActiveDownloadItem) => string;

    headingLevel?: 2 | 3;
}

const stateVariantMap: Record<keyof DownloadStateCounts, string> = {
    downloading: 'downloading',
    queued: 'queued',
    stalled: 'stalled',
};

const stateLabelMap: Record<keyof DownloadStateCounts, string> = {
    downloading: 'Downloading',
    queued: 'Queued',
    stalled: 'Stalled',
};

interface StatusRowProps {
    state: keyof DownloadStateCounts;
    count: number;
    url?: string;
}

const StatusRow = ({ state, count, url }: StatusRowProps) => {
    const variant = stateVariantMap[state];
    const label = stateLabelMap[state];
    const isClickable = url !== undefined && count > 0;

    const content = (
        <>
            <span className="download-queue-widget__indicator" aria-hidden="true" />
            <span className="download-queue-widget__label">{label}</span>
            <span className="download-queue-widget__count">{count}</span>
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={`download-queue-widget__row download-queue-widget__row--${variant} download-queue-widget__row--clickable`}
                aria-label={`View ${count} ${label.toLowerCase()} items`}
                data-status={state}
                data-variant={variant}
                data-clickable
            >
                {content}
            </Link>
        );
    }

    return (
        <div className={`download-queue-widget__row download-queue-widget__row--${variant}`} data-status={state}>
            {content}
        </div>
    );
};

interface DownloadItemProps {
    download: ActiveDownloadItem;
    url?: string;
}

const DownloadItem = ({ download, url }: DownloadItemProps) => {
    const isClickable = url !== undefined;
    const progressPercent = Math.min(100, Math.max(0, download.progress));
    const stateVariant =
        download.state === 'downloading' ? 'downloading' : download.state === 'stalled' ? 'stalled' : 'queued';

    const content = (
        <>
            <div className="download-queue-widget__item-header">
                <span className="download-queue-widget__item-title">{download.title}</span>
                <span className="download-queue-widget__item-meta">
                    {download.eta && <span className="download-queue-widget__item-eta">{download.eta}</span>}
                    {download.size && <span className="download-queue-widget__item-size">{download.size}</span>}
                </span>
            </div>
            <div
                className="download-queue-widget__progress-bar"
                role="progressbar"
                aria-valuenow={progressPercent}
                aria-valuemin={0}
                aria-valuemax={100}
            >
                <div className="download-queue-widget__progress-fill" style={{ width: `${progressPercent}%` }} />
            </div>
            <span className="download-queue-widget__item-percent">{progressPercent}%</span>
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={`download-queue-widget__item download-queue-widget__item--${stateVariant} download-queue-widget__item--clickable`}
                aria-label={`View ${download.title}`}
                data-download-id={download.id}
                data-status={download.state}
                data-progress={progressPercent}
                data-variant={stateVariant}
                data-clickable
            >
                {content}
            </Link>
        );
    }

    return (
        <div
            className={`download-queue-widget__item download-queue-widget__item--${stateVariant}`}
            data-download-id={download.id}
            data-status={download.state}
            data-progress={progressPercent}
        >
            {content}
        </div>
    );
};

const SkeletonRow = () => (
    <div
        className="download-queue-widget__row download-queue-widget__row--skeleton"
        data-testid="download-queue-widget__row--skeleton"
    >
        <span className="download-queue-widget__indicator download-queue-widget__indicator--skeleton" />
        <span className="download-queue-widget__label download-queue-widget__label--skeleton" />
        <span className="download-queue-widget__count download-queue-widget__count--skeleton" />
    </div>
);

const SkeletonItem = () => (
    <div className="download-queue-widget__item download-queue-widget__item--skeleton">
        <div className="download-queue-widget__item-header">
            <span className="download-queue-widget__item-title download-queue-widget__item-title--skeleton" />
        </div>
        <div className="download-queue-widget__progress-bar download-queue-widget__progress-bar--skeleton">
            <div className="download-queue-widget__progress-fill download-queue-widget__progress-fill--skeleton" />
        </div>
    </div>
);

const DownloadQueueWidgetBase = forwardRef<HTMLDivElement, DownloadQueueWidgetProps>(
    (
        {
            heading = 'Downloads',
            icon,
            counts,
            activeDownloads = [],
            maxItems = 3,
            loading = false,
            error = null,
            onRetry,
            getStateUrl,
            viewAllUrl,
            getDownloadUrl,
            headingLevel = 2,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['download-queue-widget', className].filter(Boolean).join(' ');
        const total = counts.downloading + counts.queued + counts.stalled;
        const displayedDownloads = activeDownloads.slice(0, maxItems);
        const HeadingTag = `h${headingLevel}` as const;

        return (
            <div ref={ref} className={classes} data-component="download-queue-widget" {...props}>
                <div className="download-queue-widget__header">
                    {icon && (
                        <span className="download-queue-widget__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <HeadingTag className="download-queue-widget__title">{heading}</HeadingTag>
                    <span className="download-queue-widget__total">{loading ? '-' : total}</span>
                </div>

                <div className="download-queue-widget__summary">
                    {loading ? (
                        <>
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
                            <StatusRow
                                state="downloading"
                                count={counts.downloading}
                                url={getStateUrl?.('downloading')}
                            />
                            <StatusRow state="queued" count={counts.queued} url={getStateUrl?.('queued')} />
                            <StatusRow state="stalled" count={counts.stalled} url={getStateUrl?.('stalled')} />
                        </>
                    )}
                </div>

                {(displayedDownloads.length > 0 || loading) && !error && (
                    <div className="download-queue-widget__items">
                        {loading ? (
                            <>
                                <SkeletonItem />
                                <SkeletonItem />
                            </>
                        ) : (
                            displayedDownloads.map((download) => (
                                <DownloadItem key={download.id} download={download} url={getDownloadUrl?.(download)} />
                            ))
                        )}
                    </div>
                )}

                {viewAllUrl && !loading && (
                    <div className="download-queue-widget__footer">
                        <Link to={viewAllUrl} className="download-queue-widget__view-all" data-action="view-all">
                            View all downloads
                        </Link>
                    </div>
                )}
            </div>
        );
    },
);

DownloadQueueWidgetBase.displayName = 'DownloadQueueWidget';

export const DownloadQueueWidget = memo(DownloadQueueWidgetBase);

export default DownloadQueueWidget;
