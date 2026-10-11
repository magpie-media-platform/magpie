import { forwardRef, useId, type HTMLAttributes, type ReactNode } from 'react';

import { LoadErrorAlert } from '@/components/ui/LoadErrorAlert';

import type { DiskSpaceInfo } from '@/types/system';

import { formatDiskSpaceSize } from '@/lib/formatSize';

import './DiskSpaceWidget.css';

export type { DiskSpaceInfo };

export interface DiskSpaceWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    folders: DiskSpaceInfo[];

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    onFolderClick?: (folder: DiskSpaceInfo) => void;

    onViewAllClick?: () => void;

    headingLevel?: 2 | 3;
}

function calculateUsagePercent(usedBytes: number, totalBytes: number): number {
    if (totalBytes === 0) {
        return 0;
    }
    return Math.round((usedBytes / totalBytes) * 100);
}

function calculateContentPercent(contentBytes: number, totalBytes: number, usedPercent: number): number {
    if (contentBytes <= 0 || totalBytes === 0) {
        return 0;
    }
    return Math.min(Math.max(Math.round((contentBytes / totalBytes) * 100), 1), usedPercent);
}

function getUsageStatus(percent: number): 'healthy' | 'warning' | 'critical' {
    if (percent >= 85) {
        return 'critical';
    }
    if (percent >= 70) {
        return 'warning';
    }
    return 'healthy';
}

interface DiskSpaceRowProps {
    folder: DiskSpaceInfo;
    onClick?: () => void;
}

const DiskSpaceRow = ({ folder, onClick }: DiskSpaceRowProps) => {
    const isUnavailable = folder.totalBytes === 0;
    const usedPercent = calculateUsagePercent(folder.usedBytes, folder.totalBytes);
    const contentPercent = calculateContentPercent(folder.contentBytes, folder.totalBytes, usedPercent);
    const otherPercent = usedPercent - contentPercent;
    const status = getUsageStatus(usedPercent);
    const isClickable = onClick !== undefined;
    const figuresId = useId();

    return (
        <div
            className={`disk-space-widget__row${isClickable ? ' disk-space-widget__row--clickable' : ''}`}
            onClick={isClickable ? onClick : undefined}
            onKeyDown={
                isClickable
                    ? (e) => {
                          if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault();
                              onClick();
                          }
                      }
                    : undefined
            }
            role={isClickable ? 'button' : undefined}
            tabIndex={isClickable ? 0 : undefined}
            aria-label={isClickable ? `View root folder ${folder.name}` : undefined}
            data-folder-id={folder.id}
            data-clickable={isClickable || undefined}
            data-usage-status={isUnavailable ? 'unavailable' : status}
        >
            <div className="disk-space-widget__row-header">
                <span className="disk-space-widget__folder-name">{folder.name}</span>
                {isUnavailable ? (
                    <span className="disk-space-widget__usage-percent disk-space-widget__usage-percent--unavailable">
                        Unavailable
                    </span>
                ) : (
                    <span className={`disk-space-widget__usage-percent disk-space-widget__usage-percent--${status}`}>
                        {usedPercent}%
                    </span>
                )}
            </div>
            {!isUnavailable && (
                <div
                    className="disk-space-widget__progress-track"
                    role="progressbar"
                    aria-valuenow={usedPercent}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`${usedPercent}% used`}
                    aria-describedby={`${figuresId}-content ${figuresId}-total`}
                >
                    {contentPercent > 0 && (
                        <div className="disk-space-widget__progress-content" style={{ width: `${contentPercent}%` }} />
                    )}
                    {otherPercent > 0 && (
                        <div
                            className={`disk-space-widget__progress-bar disk-space-widget__progress-bar--${status}`}
                            style={{ width: `${otherPercent}%` }}
                        />
                    )}
                </div>
            )}
            <div className="disk-space-widget__row-details">
                <span id={`${figuresId}-content`} className="disk-space-widget__content">
                    {!isUnavailable && <span className="disk-space-widget__content-marker" aria-hidden="true" />}
                    {formatDiskSpaceSize(folder.contentBytes)} content
                </span>
                {!isUnavailable && (
                    <>
                        <span className="disk-space-widget__free">{formatDiskSpaceSize(folder.freeBytes)} free</span>
                        <span id={`${figuresId}-total`} className="disk-space-widget__total">
                            {formatDiskSpaceSize(folder.totalBytes)} total
                        </span>
                    </>
                )}
            </div>
            <div className="disk-space-widget__path" title={folder.path}>
                {folder.path}
            </div>
        </div>
    );
};

const SkeletonRow = () => (
    <div className="disk-space-widget__row disk-space-widget__row--skeleton">
        <div className="disk-space-widget__row-header">
            <span className="disk-space-widget__folder-name disk-space-widget__folder-name--skeleton" />
            <span className="disk-space-widget__usage-percent disk-space-widget__usage-percent--skeleton" />
        </div>
        <div className="disk-space-widget__progress-track disk-space-widget__progress-track--skeleton" />
        <div className="disk-space-widget__row-details">
            <span className="disk-space-widget__content disk-space-widget__content--skeleton" />
            <span className="disk-space-widget__free disk-space-widget__free--skeleton" />
            <span className="disk-space-widget__total disk-space-widget__total--skeleton" />
        </div>
        <div className="disk-space-widget__path disk-space-widget__path--skeleton" />
    </div>
);

export const DiskSpaceWidget = forwardRef<HTMLDivElement, DiskSpaceWidgetProps>(
    (
        {
            heading = 'Disk Space',
            icon,
            folders,
            loading = false,
            error = null,
            onRetry,
            onFolderClick,
            onViewAllClick,
            headingLevel = 2,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['disk-space-widget', className].filter(Boolean).join(' ');
        const HeadingTag = `h${headingLevel}` as const;

        return (
            <div ref={ref} className={classes} data-component="disk-space-widget" {...props}>
                <div className="disk-space-widget__header">
                    {icon && (
                        <span className="disk-space-widget__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <HeadingTag className="disk-space-widget__title">{heading}</HeadingTag>
                    <span className="disk-space-widget__folder-count">
                        {loading || error ? '-' : folders.length} {folders.length === 1 ? 'folder' : 'folders'}
                    </span>
                </div>

                <div className="disk-space-widget__folders">
                    {loading ? (
                        <>
                            <SkeletonRow />
                            <SkeletonRow />
                        </>
                    ) : error ? (
                        <LoadErrorAlert onRetry={onRetry} />
                    ) : folders.length === 0 ? (
                        <div className="disk-space-widget__empty">No root folders configured</div>
                    ) : (
                        folders.map((folder) => (
                            <DiskSpaceRow
                                key={folder.id}
                                folder={folder}
                                onClick={onFolderClick ? () => onFolderClick(folder) : undefined}
                            />
                        ))
                    )}
                </div>

                {!loading && !error && folders.length > 0 && onViewAllClick && (
                    <div className="disk-space-widget__footer">
                        <button
                            type="button"
                            className="disk-space-widget__view-all"
                            onClick={onViewAllClick}
                            data-action="view-all"
                        >
                            Manage root folders
                        </button>
                    </div>
                )}
            </div>
        );
    },
);

DiskSpaceWidget.displayName = 'DiskSpaceWidget';

export default DiskSpaceWidget;
