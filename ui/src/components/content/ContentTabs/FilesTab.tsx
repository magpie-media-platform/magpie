import { forwardRef, useMemo, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import {
    ArrowsLeftRightIcon,
    DotsThreeVerticalIcon,
    FileIcon,
    FolderIcon,
    PencilSimpleLineIcon,
    VideoCameraIcon,
    HardDriveIcon,
    LinkBreakIcon,
    TrashIcon,
    FilmStripIcon,
    SpeakerHighIcon,
    ClosedCaptioningIcon,
    PlusIcon,
    ScanIcon,
    type Icon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Checkbox } from '@/components/ui/Checkbox';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/DropdownMenu';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';
import { useMenuAction } from '@/hooks/ui/useMenuAction';
import { useBulkSelection, type BulkSelection } from '@/hooks/useBulkSelection';

import { formatInstantDate } from '@/lib/dateFormatting';
import { formatDurationFromSeconds } from '@/lib/formatTime';
import { formatFileSize, formatPrimaryMediaSummary } from '@/lib/formatSize';

import './ContentTabs.css';

interface MediaInfoTrack {
    type: string;
    name: string;
    codec?: string | null;
    bitRate?: number | null;
    durationSeconds?: number | null;
    width?: number | null;
    height?: number | null;
    frameRate?: number | null;
    videoRange?: string | null;
    bitDepth?: number | null;
    channels?: number | null;
    sampleRate?: number | null;
    language?: string | null;
    forced?: boolean | null;
    default?: boolean | null;
    sdh?: boolean | null;
    properties?: Record<string, string> | null;
}

export interface MediaInfo {
    container?: string | null;
    durationSeconds?: number | null;
    bitRate?: number | null;
    tracks?: MediaInfoTrack[] | null;
}

export interface MediaFile {
    id: string;

    relativePath: string;

    fileName: string;

    size: number;

    dateAdded?: string;

    mediaInfo?: MediaInfo | null;

    /** The number of items that the file covers. */
    coveredItemCount?: number;

    /** Another item owns the file, and the file also covers this item. */
    covering?: boolean;
}

export interface FilesTabProps extends ComponentPropsWithoutRef<'div'> {
    files: MediaFile[];

    onDeleteFiles?: (files: MediaFile[], selection?: BulkSelection) => void;

    onUnlinkFiles?: (files: MediaFile[], selection?: BulkSelection) => void;

    onReassignFile?: (file: MediaFile) => void;

    onImportFile?: () => void;

    /** Starts the scan for files of this item. */
    onScanFiles?: () => void;

    onRenameFiles?: (ids: string[]) => void;

    /** Applies the rename mask to every file of this item and its descendants. */
    onRenameAllFiles?: () => void;

    /** The label of the action that renames all files. */
    renameAllLabel?: string;

    /** Whether the file import is in progress. */
    importing?: boolean;

    /** Whether the scan for files is starting. */
    scanning?: boolean;

    emptyMessage?: string;

    /** Content between the toolbar and the files. */
    children?: ReactNode;
}

function ContentFileActions({
    renameAll,
    renameAllLabel,
    scan,
    scanning,
    importFile,
    importing,
}: {
    renameAll?: () => void;
    renameAllLabel: string;
    scan?: () => void;
    scanning: boolean;
    importFile?: () => void;
    importing: boolean;
}) {
    return (
        <span className="files-tab__content-actions">
            {renameAll && (
                <Button variant="secondary" size="sm" onClick={renameAll} data-action="rename-all-files">
                    <PencilSimpleLineIcon weight="bold" aria-hidden="true" />
                    {renameAllLabel}
                </Button>
            )}
            {scan && (
                <Button variant="secondary" size="sm" onClick={scan} loading={scanning} data-action="scan-files">
                    <ScanIcon weight="bold" aria-hidden="true" />
                    Scan for files
                </Button>
            )}
            {importFile && (
                <Button
                    variant="secondary"
                    size="sm"
                    onClick={importFile}
                    loading={importing}
                    data-action="import-file"
                >
                    <PlusIcon weight="bold" aria-hidden="true" />
                    Import file
                </Button>
            )}
        </span>
    );
}

function FileActionsMenu({
    file,
    onReassign,
    onRename,
    onUnlink,
    onDelete,
}: {
    file: MediaFile;
    onReassign?: (file: MediaFile) => void;
    onRename?: () => void;
    onUnlink?: () => void;
    onDelete?: () => void;
}) {
    const { triggerRef, schedule, onCloseAutoFocus } = useMenuAction();
    return (
        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <Button
                    ref={triggerRef}
                    variant="ghost"
                    size="sm"
                    className="files-tab__menu-trigger"
                    aria-label={`Actions for ${file.fileName}`}
                    data-action="file-menu"
                    data-file-id={file.id}
                >
                    <DotsThreeVerticalIcon weight="bold" aria-hidden="true" />
                </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onCloseAutoFocus={onCloseAutoFocus}>
                {onReassign && (
                    <DropdownMenuItem onSelect={schedule(() => onReassign(file))} data-action="reassign-file">
                        <ArrowsLeftRightIcon weight="bold" aria-hidden="true" />
                        Correct episode association
                    </DropdownMenuItem>
                )}
                {onRename && (
                    <DropdownMenuItem onSelect={schedule(onRename)} data-action="rename-file">
                        <PencilSimpleLineIcon weight="bold" aria-hidden="true" />
                        Rename using mask…
                    </DropdownMenuItem>
                )}
                {onUnlink && (
                    <DropdownMenuItem onSelect={schedule(onUnlink)} data-action="unlink-file" data-file-id={file.id}>
                        <LinkBreakIcon weight="bold" aria-hidden="true" />
                        Unlink
                    </DropdownMenuItem>
                )}
                {onDelete && (
                    <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                            className="files-tab__menu-item--danger"
                            onSelect={schedule(onDelete)}
                            data-action="delete-file"
                            data-file-id={file.id}
                        >
                            <TrashIcon weight="bold" aria-hidden="true" />
                            Delete from disk
                        </DropdownMenuItem>
                    </>
                )}
            </DropdownMenuContent>
        </DropdownMenu>
    );
}

function SelectedFileActions({
    files,
    selection,
    onRenameFiles,
    onUnlinkFiles,
    onDeleteFiles,
}: {
    files: MediaFile[];
    selection: BulkSelection;
    onRenameFiles?: (ids: string[]) => void;
    onUnlinkFiles?: (files: MediaFile[], selection?: BulkSelection) => void;
    onDeleteFiles?: (files: MediaFile[], selection?: BulkSelection) => void;
}) {
    const renameIds = files.filter((file) => !file.covering).map((file) => file.id);
    return (
        <>
            {onRenameFiles && (
                <Button
                    variant="default"
                    size="sm"
                    onClick={() => onRenameFiles(renameIds)}
                    disabled={renameIds.length === 0}
                    aria-label="Rename using mask…"
                    data-action="rename-selected-files"
                >
                    <PencilSimpleLineIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Rename using mask…</span>
                </Button>
            )}
            {onUnlinkFiles && (
                <Button
                    variant="default"
                    size="sm"
                    onClick={() => onUnlinkFiles(files, selection)}
                    disabled={files.length === 0}
                    aria-label="Unlink"
                    data-action="unlink-selected-files"
                >
                    <LinkBreakIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Unlink</span>
                </Button>
            )}
            {onDeleteFiles && (
                <Button
                    variant="destructive"
                    size="sm"
                    onClick={() => onDeleteFiles(files, selection)}
                    disabled={files.length === 0}
                    aria-label="Delete from disk"
                    data-action="delete-selected-files"
                >
                    <TrashIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Delete from disk</span>
                </Button>
            )}
        </>
    );
}

function getFileIcon(file: MediaFile): Icon {
    const trackTypes = file.mediaInfo?.tracks?.map((track) => track.type.toLowerCase()) ?? [];
    if (trackTypes.includes('video')) {
        return VideoCameraIcon;
    }
    if (trackTypes.includes('audio')) {
        return SpeakerHighIcon;
    }
    if (trackTypes.includes('text') || trackTypes.includes('subtitle')) {
        return ClosedCaptioningIcon;
    }
    return FileIcon;
}

function extractFolder(relativePath: string): string {
    const parts = relativePath.split(/[/\\]/);
    if (parts.length > 1) {
        return parts.slice(0, -1).join('/');
    }
    return '';
}

function FileNameCell({ file }: { file: MediaFile }) {
    const Icon = getFileIcon(file);
    const folder = extractFolder(file.relativePath);

    return (
        <div className="files-tab__name-cell">
            <Icon weight="bold" className="files-tab__file-icon" aria-hidden="true" />
            <div className="files-tab__name-info">
                <span className="files-tab__file-name">{file.fileName}</span>
                {folder && (
                    <span className="files-tab__folder" title={folder}>
                        <FolderIcon weight="regular" aria-hidden="true" />
                        <span className="files-tab__folder-path">{folder}</span>
                    </span>
                )}
            </div>
        </div>
    );
}

function QualityCell({ mediaInfo }: { mediaInfo?: MediaInfo | null }) {
    if (!mediaInfo?.container) {
        return null;
    }

    return (
        <div className="files-tab__quality-cell">
            <Badge variant="default" size="sm">
                {formatContainer(mediaInfo.container)}
            </Badge>
        </div>
    );
}

function SizeCell({ size }: { size: number }) {
    return (
        <div className="files-tab__size-cell">
            <HardDriveIcon weight="regular" aria-hidden="true" />
            <span>{formatFileSize(size)}</span>
        </div>
    );
}

function pickProperty(track: MediaInfoTrack, ...keys: string[]): string | undefined {
    if (!track.properties) {
        return undefined;
    }
    for (const key of keys) {
        const direct = track.properties[key];
        if (direct) {
            return direct;
        }
        const lower = track.properties[key.toLowerCase()];
        if (lower) {
            return lower;
        }
    }
    return undefined;
}

function formatBitrateBps(bps: number): string {
    const mbps = bps / 1_000_000;
    if (mbps >= 1) {
        return `${mbps.toFixed(2)} Mbps`;
    }
    return `${Math.round(bps / 1_000)} kbps`;
}

function trackBitrate(track: MediaInfoTrack): string | undefined {
    if (track.bitRate != null) {
        return formatBitrateBps(track.bitRate);
    }
    const raw = pickProperty(track, 'BitRate', 'Bitrate');
    const numeric = raw != null ? Number(raw) : Number.NaN;
    return Number.isFinite(numeric) && numeric > 0 ? formatBitrateBps(numeric) : undefined;
}

function resolutionLabel(height?: number | null): string | undefined {
    if (height == null || height <= 0) {
        return undefined;
    }
    return height === 2160 ? '4K' : `${height}p`;
}

function formatFrameRate(frameRate: number): string {
    return `${Math.round(frameRate * 1000) / 1000} fps`;
}

function formatSampleRate(hz: number): string {
    const khz = hz / 1000;
    return `${Number.isInteger(khz) ? khz : khz.toFixed(1)} kHz`;
}

function channelLabel(channels?: number | null): string | undefined {
    if (channels == null || channels <= 0) {
        return undefined;
    }
    switch (channels) {
        case 1:
            return 'Mono';
        case 2:
            return 'Stereo';
        case 6:
            return '5.1';
        case 8:
            return '7.1';
        default:
            return `${channels}ch`;
    }
}

const SUBTITLE_FORMATS: Record<string, string> = {
    subrip: 'SRT',
    srt: 'SRT',
    ass: 'ASS',
    ssa: 'ASS',
    hdmv_pgs_subtitle: 'PGS',
    pgssub: 'PGS',
    dvd_subtitle: 'VobSub',
    dvdsub: 'VobSub',
    mov_text: 'Text',
    webvtt: 'VTT',
    vtt: 'VTT',
};

function subtitleFormat(codec?: string | null): string | undefined {
    if (!codec) {
        return undefined;
    }
    return SUBTITLE_FORMATS[codec.toLowerCase()] ?? codec.toUpperCase();
}

const CONTAINER_NAMES: Record<string, string> = {
    matroska: 'MKV',
    webm: 'WebM',
    mp4: 'MP4',
    mov: 'MP4',
    m4v: 'MP4',
    avi: 'AVI',
    mpegts: 'TS',
    flv: 'FLV',
    ogg: 'OGG',
    wav: 'WAV',
    flac: 'FLAC',
};

function formatContainer(name: string): string {
    const first = name.split(',')[0]?.trim().toLowerCase() ?? '';
    return CONTAINER_NAMES[first] ?? first.toUpperCase();
}

const FALLBACK_TRACK_NAME = /^(?:video|audio|subtitle):\d+$/;

function trackTitle(track: MediaInfoTrack): string | undefined {
    const name = track.name?.trim();
    if (name && !FALLBACK_TRACK_NAME.test(name)) {
        return name;
    }
    return pickProperty(track, 'title', 'Title');
}

function VideoRow({ track }: { track: MediaInfoTrack }) {
    const codec = track.codec ?? pickProperty(track, 'Codec', 'CodecID', 'Format');
    const resolution = track.width && track.height ? `${track.width}x${track.height}` : undefined;
    const tier = resolutionLabel(track.height);
    const bitDepth = track.bitDepth ? `${track.bitDepth}-bit` : undefined;
    const frameRate = track.frameRate ? formatFrameRate(track.frameRate) : undefined;
    const bitrate = trackBitrate(track);
    const isHdr = track.videoRange === 'HDR';

    return (
        <div className="files-tab__track-row" data-testid="files-tab-track-video">
            {codec && <span className="files-tab__track-primary">{codec}</span>}
            {resolution && <span className="files-tab__track-field">{resolution}</span>}
            {tier && (
                <Badge variant="default" size="sm">
                    {tier}
                </Badge>
            )}
            {isHdr && (
                <Badge variant="info" size="sm">
                    HDR
                </Badge>
            )}
            {!isHdr && track.videoRange && <span className="files-tab__track-field">{track.videoRange}</span>}
            {bitDepth && <span className="files-tab__track-field">{bitDepth}</span>}
            {frameRate && <span className="files-tab__track-field">{frameRate}</span>}
            {bitrate && <span className="files-tab__track-field">{bitrate}</span>}
        </div>
    );
}

function AudioRow({ track }: { track: MediaInfoTrack }) {
    const codec = track.codec ?? pickProperty(track, 'Codec', 'Format');
    const channels = channelLabel(track.channels);
    const language = track.language ?? pickProperty(track, 'Language');
    const bitrate = trackBitrate(track);
    const sampleRate = track.sampleRate ? formatSampleRate(track.sampleRate) : undefined;

    return (
        <div className="files-tab__track-row" data-testid="files-tab-track-audio">
            {codec && <span className="files-tab__track-primary">{codec}</span>}
            {channels && <span className="files-tab__track-field">{channels}</span>}
            {language && <span className="files-tab__track-field">{language}</span>}
            {bitrate && <span className="files-tab__track-field">{bitrate}</span>}
            {sampleRate && <span className="files-tab__track-field">{sampleRate}</span>}
        </div>
    );
}

function TextRow({ track }: { track: MediaInfoTrack }) {
    const language = track.language ?? pickProperty(track, 'Language');
    const format = subtitleFormat(track.codec ?? pickProperty(track, 'Codec', 'Format'));
    const title = trackTitle(track);

    return (
        <div className="files-tab__track-row" data-testid="files-tab-track-text">
            {language && <span className="files-tab__track-primary">{language}</span>}
            {format && <span className="files-tab__track-field">{format}</span>}
            {title && <span className="files-tab__track-field">{title}</span>}
            {track.default === true && (
                <Badge variant="default" size="sm">
                    Default
                </Badge>
            )}
            {track.forced === true && (
                <Badge variant="warning" size="sm">
                    Forced
                </Badge>
            )}
            {track.sdh === true && (
                <Badge variant="info" size="sm">
                    SDH
                </Badge>
            )}
        </div>
    );
}

function TrackSection({ label, icon: SectionIcon, children }: { label: string; icon: Icon; children: ReactNode }) {
    return (
        <div className="files-tab__track-section">
            <div className="files-tab__track-section-label">
                <SectionIcon weight="bold" aria-hidden="true" />
                <span>{label}</span>
            </div>
            {children}
        </div>
    );
}

function trackType(track: MediaInfoTrack): string {
    return track.type?.toLowerCase() ?? '';
}

function MediaInfoPanel({ mediaInfo, size }: { mediaInfo: MediaInfo | null | undefined; size: number }) {
    if (!mediaInfo?.tracks?.length) {
        return (
            <div
                className="files-tab__media-info files-tab__media-info--empty"
                data-testid="files-tab-media-info-empty"
            >
                <span>MediaInfo not extracted</span>
            </div>
        );
    }

    const tracks = mediaInfo.tracks;
    const videoTracks = tracks.filter((track) => trackType(track) === 'video');
    const audioTracks = tracks.filter((track) => trackType(track) === 'audio');
    const textTracks = tracks.filter((track) => trackType(track) === 'subtitle' || trackType(track) === 'text');

    const headerParts = [
        mediaInfo.container ? formatContainer(mediaInfo.container) : undefined,
        mediaInfo.durationSeconds ? formatDurationFromSeconds(mediaInfo.durationSeconds) : undefined,
        formatFileSize(size),
        mediaInfo.bitRate ? formatBitrateBps(mediaInfo.bitRate) : undefined,
    ].filter((part): part is string => Boolean(part));

    return (
        <div className="files-tab__media-info" data-testid="files-tab-media-info">
            <div className="files-tab__media-file-header">{headerParts.join(' · ')}</div>
            {videoTracks.length > 0 && (
                <TrackSection label="Video" icon={FilmStripIcon}>
                    {videoTracks.map((track, index) => (
                        <VideoRow key={`video:${track.name}:${index}`} track={track} />
                    ))}
                </TrackSection>
            )}
            {audioTracks.length > 0 && (
                <TrackSection label="Audio" icon={SpeakerHighIcon}>
                    {audioTracks.map((track, index) => (
                        <AudioRow key={`audio:${track.name}:${index}`} track={track} />
                    ))}
                </TrackSection>
            )}
            {textTracks.length > 0 && (
                <TrackSection label="Text" icon={ClosedCaptioningIcon}>
                    {textTracks.map((track, index) => (
                        <TextRow key={`text:${track.name}:${index}`} track={track} />
                    ))}
                </TrackSection>
            )}
        </div>
    );
}

export const FilesTab = forwardRef<HTMLDivElement, FilesTabProps>(
    (
        {
            files,
            onDeleteFiles,
            onUnlinkFiles,
            onReassignFile,
            onImportFile,
            onScanFiles,
            onRenameFiles,
            onRenameAllFiles,
            renameAllLabel = 'Rename all files…',
            importing = false,
            scanning = false,
            emptyMessage = 'No files found',
            className,
            children,
            ...props
        },
        ref,
    ) => {
        const classes = ['files-tab', className].filter(Boolean).join(' ');
        const fileIds = useMemo(() => files.map((file) => file.id), [files]);
        const selection = useBulkSelection({
            pageRowIds: fileIds,
            totalCount: fileIds.length,
            allowSelectAllAcrossPages: false,
        });
        const phone = useMediaQuery('(max-width: 639px)');
        const contentActions =
            phone || !(onRenameAllFiles || onScanFiles || onImportFile) ? undefined : (
                <ContentFileActions
                    renameAll={onRenameAllFiles}
                    renameAllLabel={renameAllLabel}
                    scan={onScanFiles}
                    scanning={scanning}
                    importFile={onImportFile}
                    importing={importing}
                />
            );

        if (files.length === 0) {
            return (
                <div ref={ref} className={classes} data-component="files-tab" {...props}>
                    <EmptyState
                        icon={<FileIcon weight="thin" aria-hidden="true" />}
                        title={emptyMessage}
                        action={contentActions}
                        size="sm"
                    />
                    {children}
                </div>
            );
        }

        const totalSize = files.reduce((sum, file) => sum + file.size, 0);
        const selectedFiles = files.filter((file) => selection.isRowChecked(file.id));
        const hasBulkActions = !!(onRenameFiles || onUnlinkFiles || onDeleteFiles);
        const hasFileActions = hasBulkActions || !!onReassignFile;
        const selectedFileActions = (
            <SelectedFileActions
                files={selectedFiles}
                selection={selection}
                onRenameFiles={onRenameFiles}
                onUnlinkFiles={onUnlinkFiles}
                onDeleteFiles={onDeleteFiles}
            />
        );

        return (
            <div ref={ref} className={classes} data-component="files-tab" {...props}>
                <div className="files-tab__header" data-testid="files-tab-header">
                    <span className="files-tab__summary">
                        <FileIcon weight="bold" aria-hidden="true" />
                        <span>{formatPrimaryMediaSummary(files.length, totalSize)}</span>
                    </span>
                    {hasBulkActions && (
                        <SelectModeToggle
                            active={selection.selectionMode}
                            onToggle={selection.toggleSelectionMode}
                            mobileOnly={false}
                            label="Select files"
                        />
                    )}
                    {contentActions}
                </div>

                {children}

                {hasBulkActions && (
                    <BulkActionsToolbar
                        selection={selection}
                        label="File selection actions"
                        itemNounPlural="files"
                        displaySelectedCount={selectedFiles.length}
                        visible={selection.selectionMode}
                        selectAll
                    >
                        {selectedFileActions}
                    </BulkActionsToolbar>
                )}

                <div className="files-tab__panels">
                    {files.map((file) => {
                        const renameFile = onRenameFiles && !file.covering ? () => onRenameFiles([file.id]) : undefined;
                        const unlinkFile = onUnlinkFiles ? () => onUnlinkFiles([file]) : undefined;
                        const deleteFile = onDeleteFiles ? () => onDeleteFiles([file]) : undefined;

                        return (
                            <div key={file.id} className="files-tab__panel" data-file-id={file.id}>
                                <div className="files-tab__panel-header">
                                    {selection.selectionMode && (
                                        <Checkbox
                                            aria-label={`Select ${file.fileName}`}
                                            checked={selection.isRowChecked(file.id)}
                                            onCheckedChange={(checked) =>
                                                selection.toggleRow(file.id, checked === true)
                                            }
                                        />
                                    )}
                                    <FileNameCell file={file} />
                                    {hasFileActions && (
                                        <FileActionsMenu
                                            file={file}
                                            onReassign={onReassignFile}
                                            onRename={renameFile}
                                            onUnlink={unlinkFile}
                                            onDelete={deleteFile}
                                        />
                                    )}
                                </div>
                                <div className="files-tab__panel-meta">
                                    <QualityCell mediaInfo={file.mediaInfo} />
                                    <SizeCell size={file.size} />
                                    {file.coveredItemCount !== undefined && (
                                        <Badge variant="info" size="sm">
                                            Covers {file.coveredItemCount} items
                                        </Badge>
                                    )}
                                    {file.dateAdded && (
                                        <span className="files-tab__panel-date">
                                            Added {formatInstantDate(file.dateAdded)}
                                        </span>
                                    )}
                                </div>
                                <MediaInfoPanel mediaInfo={file.mediaInfo} size={file.size} />
                            </div>
                        );
                    })}
                </div>

                {hasBulkActions && selection.selectionMode && (
                    <MobileSelectionBar
                        selection={selection}
                        displaySelectedCount={selectedFiles.length}
                        itemNounPlural="files"
                        actionsClassName="files-tab__selection-actions"
                    >
                        {selectedFileActions}
                    </MobileSelectionBar>
                )}
            </div>
        );
    },
);

FilesTab.displayName = 'FilesTab';

export default FilesTab;
