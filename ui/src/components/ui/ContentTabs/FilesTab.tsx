import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import { File, Folder, VideoCamera, HardDrive } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { DataTable } from '../DataTable';
import { Badge } from '../Badge';

import { formatFileSize } from './formatFileSize';

import './ContentTabs.css';

export interface FileQuality {
    resolution?: string;

    videoCodec?: string;

    audioCodec?: string;

    source?: string;
}

export interface MediaFile {
    id: string;

    relativePath: string;

    fileName: string;

    size: number;

    quality?: FileQuality;

    dateAdded?: string;

    languages?: string[];
}

export interface FilesTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    files: MediaFile[];

    loading?: boolean;

    onFileClick?: (file: MediaFile) => void;

    emptyMessage?: string;
}

function formatDate(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        return date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
        });
    } catch {
        return dateStr;
    }
}

function getFileIcon() {
    return VideoCamera;
}

function extractFolder(relativePath: string): string {
    const parts = relativePath.split(/[/\\]/);
    if (parts.length > 1) {
        return parts.slice(0, -1).join('/');
    }
    return '';
}

function FileNameCell({ file }: { file: MediaFile }) {
    const Icon = getFileIcon();
    const folder = extractFolder(file.relativePath);

    return (
        <div className="files-tab__name-cell">
            <Icon weight="bold" className="files-tab__file-icon" aria-hidden="true" />
            <div className="files-tab__name-info">
                <span className="files-tab__file-name">{file.fileName}</span>
                {folder && (
                    <span className="files-tab__folder">
                        <Folder weight="regular" aria-hidden="true" />
                        {folder}
                    </span>
                )}
            </div>
        </div>
    );
}

function QualityCell({ quality }: { quality?: FileQuality }) {
    if (!quality) {
        return <span className="files-tab__empty-cell">-</span>;
    }

    return (
        <div className="files-tab__quality-cell">
            {quality.resolution && (
                <Badge variant="default" size="sm">
                    {quality.resolution}
                </Badge>
            )}
            {quality.videoCodec && <span className="files-tab__codec">{quality.videoCodec}</span>}
        </div>
    );
}

function SizeCell({ size }: { size: number }) {
    return (
        <div className="files-tab__size-cell">
            <HardDrive weight="regular" aria-hidden="true" />
            <span>{formatFileSize(size)}</span>
        </div>
    );
}

export const FilesTab = forwardRef<HTMLDivElement, FilesTabProps>(
    ({ files, loading = false, onFileClick, emptyMessage = 'No files found', className, ...props }, ref) => {
        const columns: ColumnDef<MediaFile>[] = useMemo(
            () => [
                {
                    id: 'fileName',
                    accessorKey: 'fileName',
                    header: 'File',
                    size: 350,
                    cell: ({ row }) => <FileNameCell file={row.original} />,
                },
                {
                    id: 'quality',
                    header: 'Quality',
                    size: 150,
                    cell: ({ row }) => <QualityCell quality={row.original.quality} />,
                },
                {
                    id: 'size',
                    accessorKey: 'size',
                    header: 'Size',
                    size: 100,
                    cell: ({ row }) => <SizeCell size={row.original.size} />,
                },
                {
                    id: 'dateAdded',
                    accessorKey: 'dateAdded',
                    header: 'Added',
                    size: 120,
                    cell: ({ row }) => (row.original.dateAdded ? formatDate(row.original.dateAdded) : '-'),
                },
            ],
            [],
        );

        const handleRowClick = useCallback(
            (row: MediaFile) => {
                if (onFileClick) {
                    onFileClick(row);
                }
            },
            [onFileClick],
        );

        const getRowId = useCallback((row: MediaFile) => row.id, []);

        const classes = ['files-tab', className].filter(Boolean).join(' ');

        const totalSize = files.reduce((sum, file) => sum + file.size, 0);

        return (
            <div ref={ref} className={classes} data-component="files-tab" {...props}>
                {files.length > 0 && (
                    <div className="files-tab__summary">
                        <span className="files-tab__count">
                            <File weight="bold" aria-hidden="true" />
                            {files.length} {files.length === 1 ? 'file' : 'files'}
                        </span>
                        <span className="files-tab__total-size">
                            <HardDrive weight="bold" aria-hidden="true" />
                            {formatFileSize(totalSize)} total
                        </span>
                    </div>
                )}

                <div className="files-tab__table">
                    <DataTable<MediaFile>
                        columns={columns}
                        data={files}
                        onRowClick={onFileClick ? handleRowClick : undefined}
                        loading={loading}
                        enableSorting={true}
                        enablePagination={false}
                        getRowId={getRowId}
                        emptyState={
                            <div className="files-tab__empty">
                                <File weight="thin" aria-hidden="true" />
                                <p>{emptyMessage}</p>
                            </div>
                        }
                    />
                </div>

                <div className="files-tab__mobile-list">
                    {files.length === 0 ? (
                        <div className="files-tab__empty">
                            <File weight="thin" aria-hidden="true" />
                            <p>{emptyMessage}</p>
                        </div>
                    ) : (
                        files.map((file) => (
                            <div
                                key={file.id}
                                className="files-tab__mobile-card"
                                onClick={onFileClick ? () => onFileClick(file) : undefined}
                                onKeyDown={
                                    onFileClick
                                        ? (e) => {
                                              if (e.key === 'Enter' || e.key === ' ') {
                                                  e.preventDefault();
                                                  onFileClick(file);
                                              }
                                          }
                                        : undefined
                                }
                                data-file-id={file.id}
                                role={onFileClick ? 'button' : undefined}
                                tabIndex={onFileClick ? 0 : undefined}
                            >
                                <FileNameCell file={file} />
                                <div className="files-tab__mobile-meta">
                                    <QualityCell quality={file.quality} />
                                    <SizeCell size={file.size} />
                                </div>
                                {file.dateAdded && (
                                    <span className="files-tab__mobile-date">Added {formatDate(file.dateAdded)}</span>
                                )}
                            </div>
                        ))
                    )}
                </div>
            </div>
        );
    },
);

FilesTab.displayName = 'FilesTab';

export default FilesTab;
