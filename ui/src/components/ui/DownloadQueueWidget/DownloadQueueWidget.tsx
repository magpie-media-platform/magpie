import { LocalContentLink } from '@/components/content/LocalContentPreview';
import { forwardRef, memo, type HTMLAttributes, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwiseIcon, TrayIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ContentLineageTitle } from '@/components/content/ContentLineageTitle';
import { EmptyState } from '@/components/ui/EmptyState';
import type { DownloadState, DownloadStateCounts, ActiveDownloadItem } from '@/types/system';

import { StatusRow as SharedStatusRow } from '../StatusRow';
import { Skeleton } from '../Skeleton';

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

const stateLabelMap: Record<keyof DownloadStateCounts, string> = {
    downloading: 'Downloading',
    queued: 'Queued',
    paused: 'Paused',
    stalled: 'Stalled',
};

interface StatusRowProps {
    state: keyof DownloadStateCounts;
    count: number;
    url?: string;
}

const StatusRow = ({ state, count, url }: StatusRowProps) => (
    <SharedStatusRow
        pulse={state === 'downloading'}
        status={state}
        label={stateLabelMap[state]}
        count={count}
        url={url}
    />
);

interface DownloadItemProps {
    download: ActiveDownloadItem;
    url?: string;
}

const DownloadItem = ({ download, url }: DownloadItemProps) => {
    const isClickable = url !== undefined;
    const progressPercent = Math.min(100, Math.max(0, download.progress));
    const displayPercent = Number.isInteger(progressPercent) ? progressPercent : Math.round(progressPercent);
    const stateVariant =
        download.state === 'downloading'
            ? 'downloading'
            : download.state === 'paused'
              ? 'paused'
              : download.state === 'stalled'
                ? 'stalled'
                : 'queued';

    const content = (
        <>
            <div className="download-queue-widget__item-header">
                <span className="download-queue-widget__item-title">
                    {download.contentId ? (
                        <LocalContentLink contentId={download.contentId} className="download-queue-widget__item-link">
                            <ContentLineageTitle
                                qualifiedName={{
                                    id: download.contentId,
                                    parentId: download.parentId,
                                    title: download.title,
                                }}
                            />
                        </LocalContentLink>
                    ) : (
                        download.title
                    )}
                </span>
                <span className="download-queue-widget__item-meta">
                    {download.eta && <span className="download-queue-widget__item-eta">{download.eta}</span>}
                    {download.size && <span className="download-queue-widget__item-size">{download.size}</span>}
                </span>
            </div>
            <div
                className="download-queue-widget__progress-bar"
                role="progressbar"
                aria-valuenow={displayPercent}
                aria-valuemin={0}
                aria-valuemax={100}
            >
                <div className="download-queue-widget__progress-fill" style={{ width: `${progressPercent}%` }} />
            </div>
            <span className="download-queue-widget__item-percent">{displayPercent}%</span>
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={`download-queue-widget__item download-queue-widget__item--${stateVariant} download-queue-widget__item--clickable`}
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
        const total = counts.downloading + counts.queued + counts.paused + counts.stalled;
        const displayedDownloads = activeDownloads.slice(0, maxItems);
        const HeadingTag = `h${headingLevel}` as const;
        const showEmptyState = total === 0 && displayedDownloads.length === 0 && !loading && !error;

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
                    ) : showEmptyState ? null : (
                        <>
                            <StatusRow
                                state="downloading"
                                count={counts.downloading}
                                url={getStateUrl?.('downloading')}
                            />
                            <StatusRow state="queued" count={counts.queued} url={getStateUrl?.('queued')} />
                            <StatusRow state="paused" count={counts.paused} url={getStateUrl?.('paused')} />
                            <StatusRow state="stalled" count={counts.stalled} url={getStateUrl?.('stalled')} />
                        </>
                    )}
                </div>

                {showEmptyState && (
                    <div className="download-queue-widget__empty">
                        <EmptyState icon={<TrayIcon weight="duotone" />} title="No active downloads" size="sm" />
                    </div>
                )}

                {(displayedDownloads.length > 0 || loading) && !error && (
                    <div className="download-queue-widget__items">
                        {loading ? (
                            <>
                                <Skeleton className="download-queue-widget__item" height={48} />
                                <Skeleton className="download-queue-widget__item" height={48} />
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
