import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import type { DiskSpaceInfo } from '@/types/system';

import './DiskSpaceWidget.css';

export type { DiskSpaceInfo };

export interface DiskSpaceWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    folders: DiskSpaceInfo[];

    loading?: boolean;

    onFolderClick?: (folder: DiskSpaceInfo) => void;

    onViewAllClick?: () => void;

    headingLevel?: 2 | 3;
}

function formatBytes(bytes: number): string {
    if (bytes === 0) {
        return '0 B';
    }

    const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const k = 1024;
    const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), units.length - 1);
    const value = bytes / Math.pow(k, i);

    const decimals = i >= 3 ? 2 : i >= 2 ? 1 : 0;

    return `${value.toFixed(decimals)} ${units[i]}`;
}

function calculateUsagePercent(usedBytes: number, totalBytes: number): number {
    if (totalBytes === 0) {
        return 0;
    }
    return Math.round((usedBytes / totalBytes) * 100);
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
    const usedPercent = calculateUsagePercent(folder.usedBytes, folder.totalBytes);
    const status = getUsageStatus(usedPercent);
    const isClickable = onClick !== undefined;

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
            data-usage-status={status}
        >
            <div className="disk-space-widget__row-header">
                <span className="disk-space-widget__folder-name">{folder.name}</span>
                <span className={`disk-space-widget__usage-percent disk-space-widget__usage-percent--${status}`}>
                    {usedPercent}%
                </span>
            </div>
            <div className="disk-space-widget__progress-track">
                <div
                    className={`disk-space-widget__progress-bar disk-space-widget__progress-bar--${status}`}
                    style={{ width: `${usedPercent}%` }}
                    role="progressbar"
                    aria-valuenow={usedPercent}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`${usedPercent}% used`}
                />
            </div>
            <div className="disk-space-widget__row-details">
                <span className="disk-space-widget__used">{formatBytes(folder.usedBytes)} used</span>
                <span className="disk-space-widget__free">{formatBytes(folder.freeBytes)} free</span>
                <span className="disk-space-widget__total">{formatBytes(folder.totalBytes)} total</span>
            </div>
            <div className="disk-space-widget__row-compact">
                {formatBytes(folder.usedBytes)} / {formatBytes(folder.totalBytes)}
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
            <span className="disk-space-widget__used disk-space-widget__used--skeleton" />
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

        const totalUsed = folders.reduce((sum, f) => sum + f.usedBytes, 0);
        const totalSpace = folders.reduce((sum, f) => sum + f.totalBytes, 0);
        const totalFree = folders.reduce((sum, f) => sum + f.freeBytes, 0);

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
                        {loading ? '-' : folders.length} {folders.length === 1 ? 'folder' : 'folders'}
                    </span>
                </div>

                {!loading && folders.length > 0 && (
                    <>
                        <div className="disk-space-widget__summary">
                            <div className="disk-space-widget__summary-item">
                                <span className="disk-space-widget__summary-label">Total Used</span>
                                <span className="disk-space-widget__summary-value">{formatBytes(totalUsed)}</span>
                            </div>
                            <div className="disk-space-widget__summary-item">
                                <span className="disk-space-widget__summary-label">Total Free</span>
                                <span className="disk-space-widget__summary-value">{formatBytes(totalFree)}</span>
                            </div>
                            <div className="disk-space-widget__summary-item">
                                <span className="disk-space-widget__summary-label">Total Capacity</span>
                                <span className="disk-space-widget__summary-value">{formatBytes(totalSpace)}</span>
                            </div>
                        </div>
                        <div className="disk-space-widget__summary-compact">
                            {formatBytes(totalUsed)} / {formatBytes(totalSpace)}
                        </div>
                    </>
                )}

                <div className="disk-space-widget__folders">
                    {loading ? (
                        <>
                            <SkeletonRow />
                            <SkeletonRow />
                        </>
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

                {!loading && folders.length > 0 && onViewAllClick && (
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
