import { forwardRef, useRef, useState, type ComponentPropsWithoutRef, type ReactNode } from 'react';
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
    type Icon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Checkbox } from '@/components/ui/Checkbox';
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/DropdownMenu';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';

import { formatInstantDate } from '@/lib/dateFormatting';
import { formatDurationFromSeconds } from '@/lib/formatTime';
import { formatFileSize } from '@/lib/formatSize';

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
}

export interface FilesTabProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    files: MediaFile[];

    onDeleteFile?: (file: MediaFile) => void;

    onUnlinkFile?: (file: MediaFile) => void;

    onReassignFile?: (file: MediaFile) => void;

    onImportFile?: () => void;

    onRenameFile?: (file: MediaFile) => void;

    onRenameFiles?: (ids: string[]) => void;

    /** Applies the rename mask to every file of this item and its descendants. */
    onRenameAllFiles?: () => void;

    deletingFileId?: string | null;

    unlinkingFileId?: string | null;

    importing?: boolean;

    emptyMessage?: string;
}

function ImportFileButton({ onImport, importing }: { onImport: () => void; importing: boolean }) {
    return (
        <Button
            variant="secondary"
            size="sm"
            onClick={onImport}
            loading={importing}
            disabled={importing}
            data-action="import-file"
        >
            <PlusIcon weight="bold" aria-hidden="true" />
            Import file
        </Button>
    );
}

function RenameAllFilesButton({ onRenameAll }: { onRenameAll: () => void }) {
    return (
        <Button variant="secondary" size="sm" onClick={onRenameAll} data-action="rename-all-files">
            Apply rename mask to descendant files
        </Button>
    );
}

/**
 * Runs a menu action after the menu closes and focus is back on its trigger. An action that opens a dialog records the
 * focused element to return to, and the menu item is gone by the time the dialog closes.
 */
function useMenuAction() {
    const triggerRef = useRef<HTMLButtonElement>(null);
    const pendingRef = useRef<(() => void) | null>(null);
    return {
        triggerRef,
        run: (action: () => void) => () => {
            pendingRef.current = action;
        },
        onCloseAutoFocus: (event: Event) => {
            const action = pendingRef.current;
            pendingRef.current = null;
            if (action) {
                event.preventDefault();
                triggerRef.current?.focus();
                action();
            }
        },
    };
}

function FileActionsMenu({
    file,
    busy,
    onReassign,
    onRename,
    onUnlink,
    onDelete,
}: {
    file: MediaFile;
    busy: boolean;
    onReassign?: (file: MediaFile) => void;
    onRename?: (file: MediaFile) => void;
    onUnlink?: (file: MediaFile) => void;
    onDelete?: (file: MediaFile) => void;
}) {
    const { triggerRef, run, onCloseAutoFocus } = useMenuAction();
    return (
        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <Button
                    ref={triggerRef}
                    variant="ghost"
                    size="sm"
                    className="files-tab__menu-trigger"
                    aria-label={`Actions for ${file.fileName}`}
                    loading={busy}
                    data-action="file-menu"
                    data-file-id={file.id}
                >
                    <DotsThreeVerticalIcon weight="bold" aria-hidden="true" />
                </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onCloseAutoFocus={onCloseAutoFocus}>
                {onReassign && (
                    <DropdownMenuItem onSelect={run(() => onReassign(file))} data-action="reassign-file">
                        <ArrowsLeftRightIcon weight="bold" aria-hidden="true" />
                        Correct episode association
                    </DropdownMenuItem>
                )}
                {onRename && (
                    <DropdownMenuItem onSelect={run(() => onRename(file))} data-action="rename-file">
                        <PencilSimpleLineIcon weight="bold" aria-hidden="true" />
                        Apply rename mask
                    </DropdownMenuItem>
                )}
                {onUnlink && (
                    <DropdownMenuItem
                        onSelect={run(() => onUnlink(file))}
                        aria-label={`Unlink ${file.fileName}`}
                        data-action="unlink-file"
                        data-file-id={file.id}
                    >
                        <LinkBreakIcon weight="bold" aria-hidden="true" />
                        Unlink
                    </DropdownMenuItem>
                )}
                {onDelete && (
                    <>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                            className="files-tab__menu-item--danger"
                            onSelect={run(() => onDelete(file))}
                            aria-label={`Delete ${file.fileName}`}
                            data-action="delete-file"
                            data-file-id={file.id}
                        >
                            <TrashIcon weight="bold" aria-hidden="true" />
                            Delete
                        </DropdownMenuItem>
                    </>
                )}
            </DropdownMenuContent>
        </DropdownMenu>
    );
}

function DeleteFileButton({
    file,
    isDeleting,
    disabled,
    onDelete,
}: {
    file: MediaFile;
    isDeleting: boolean;
    disabled: boolean;
    onDelete: (file: MediaFile) => void;
}) {
    return (
        <Button
            variant="destructive"
            size="sm"
            onClick={(event) => {
                event.stopPropagation();
                onDelete(file);
            }}
            loading={isDeleting}
            disabled={disabled}
            aria-label={`Delete ${file.fileName}`}
            title="Delete file from disk"
            data-action="delete-file"
            data-file-id={file.id}
        >
            <TrashIcon weight="bold" aria-hidden="true" />
            Delete
        </Button>
    );
}

function UnlinkFileButton({
    file,
    isUnlinking,
    disabled,
    onUnlink,
}: {
    file: MediaFile;
    isUnlinking: boolean;
    disabled: boolean;
    onUnlink: (file: MediaFile) => void;
}) {
    return (
        <Button
            variant="outline"
            size="sm"
            onClick={(event) => {
                event.stopPropagation();
                onUnlink(file);
            }}
            loading={isUnlinking}
            disabled={disabled}
            aria-label={`Unlink ${file.fileName}`}
            title="Unlink file and leave it on disk"
            data-action="unlink-file"
            data-file-id={file.id}
        >
            <LinkBreakIcon weight="bold" aria-hidden="true" />
            Unlink
        </Button>
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
            onDeleteFile,
            onUnlinkFile,
            onReassignFile,
            onImportFile,
            onRenameFile,
            onRenameFiles,
            onRenameAllFiles,
            deletingFileId = null,
            unlinkingFileId = null,
            importing = false,
            emptyMessage = 'No files found',
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['files-tab', className].filter(Boolean).join(' ');
        const [selectedIds, setSelectedIds] = useState<string[]>([]);
        // In the card layout each file card's actions go into a menu, so the card keeps one line of controls.
        const compact = useCardLayout();
        // Below 640 px the content actions button carries rename-all and import, so the tab keeps only per-file work.
        const phone = useMediaQuery('(max-width: 639px)');
        const renameAll = phone ? undefined : onRenameAllFiles;
        const importFile = phone ? undefined : onImportFile;

        if (files.length === 0) {
            return (
                <div ref={ref} className={classes} data-component="files-tab" {...props}>
                    <EmptyState
                        icon={<FileIcon weight="thin" aria-hidden="true" />}
                        title={emptyMessage}
                        action={
                            importFile || renameAll ? (
                                <span className="files-tab__empty-actions">
                                    {renameAll && <RenameAllFilesButton onRenameAll={renameAll} />}
                                    {importFile && <ImportFileButton onImport={importFile} importing={importing} />}
                                </span>
                            ) : undefined
                        }
                        size="sm"
                    />
                </div>
            );
        }

        const totalSize = files.reduce((sum, file) => sum + file.size, 0);
        const hasSelection = files.some((file) => selectedIds.includes(file.id));
        const renameSelected = () =>
            onRenameFiles?.(files.filter((file) => selectedIds.includes(file.id)).map((file) => file.id));
        const hasFileActions = !!(onUnlinkFile || onDeleteFile || onRenameFile || onReassignFile);

        return (
            <div ref={ref} className={classes} data-component="files-tab" {...props}>
                <div className="files-tab__header" data-testid="files-tab-header">
                    <span className="files-tab__summary">
                        <FileIcon weight="bold" aria-hidden="true" />
                        <span>
                            {files.length} {files.length === 1 ? 'file' : 'files'}
                            {' · '}
                            {formatFileSize(totalSize)}
                        </span>
                    </span>
                    {onRenameFiles && (
                        <Button
                            variant="secondary"
                            size="sm"
                            disabled={!hasSelection}
                            onClick={renameSelected}
                            data-action="rename-selected-files"
                        >
                            Apply rename mask to selected files
                        </Button>
                    )}
                    {renameAll && <RenameAllFilesButton onRenameAll={renameAll} />}
                    {importFile && (
                        <span className="files-tab__import-action">
                            <ImportFileButton onImport={importFile} importing={importing} />
                        </span>
                    )}
                </div>

                <div className="files-tab__panels">
                    {files.map((file) => {
                        const isDeleting = deletingFileId === file.id;
                        const isUnlinking = unlinkingFileId === file.id;
                        const isMutating = isDeleting || isUnlinking;

                        return (
                            <div key={file.id} className="files-tab__panel" data-file-id={file.id}>
                                <div className="files-tab__panel-header">
                                    {onRenameFiles && (
                                        <Checkbox
                                            aria-label={`Select ${file.fileName} for rename`}
                                            checked={selectedIds.includes(file.id)}
                                            onCheckedChange={(checked) =>
                                                setSelectedIds((current) =>
                                                    checked === true
                                                        ? [...current.filter((id) => id !== file.id), file.id]
                                                        : current.filter((id) => id !== file.id),
                                                )
                                            }
                                        />
                                    )}
                                    <FileNameCell file={file} />
                                    {compact && hasFileActions && (
                                        <FileActionsMenu
                                            file={file}
                                            busy={isMutating}
                                            onReassign={onReassignFile}
                                            onRename={onRenameFile}
                                            onUnlink={onUnlinkFile}
                                            onDelete={onDeleteFile}
                                        />
                                    )}
                                </div>
                                <div className="files-tab__panel-meta">
                                    <QualityCell mediaInfo={file.mediaInfo} />
                                    <SizeCell size={file.size} />
                                    {file.dateAdded && (
                                        <span className="files-tab__panel-date">
                                            Added {formatInstantDate(file.dateAdded)}
                                        </span>
                                    )}
                                </div>
                                <MediaInfoPanel mediaInfo={file.mediaInfo} size={file.size} />
                                {!compact && hasFileActions && (
                                    <div className="files-tab__file-actions">
                                        {onReassignFile && (
                                            <Button
                                                variant="outline"
                                                size="sm"
                                                onClick={() => onReassignFile(file)}
                                                disabled={isMutating}
                                            >
                                                Correct episode association
                                            </Button>
                                        )}
                                        {onRenameFile && (
                                            <Button
                                                variant="outline"
                                                size="sm"
                                                onClick={() => onRenameFile(file)}
                                                disabled={isMutating}
                                            >
                                                Apply rename mask
                                            </Button>
                                        )}
                                        {onUnlinkFile && (
                                            <UnlinkFileButton
                                                file={file}
                                                isUnlinking={isUnlinking}
                                                disabled={isMutating}
                                                onUnlink={onUnlinkFile}
                                            />
                                        )}
                                        {onDeleteFile && (
                                            <DeleteFileButton
                                                file={file}
                                                isDeleting={isDeleting}
                                                disabled={isMutating}
                                                onDelete={onDeleteFile}
                                            />
                                        )}
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            </div>
        );
    },
);

FilesTab.displayName = 'FilesTab';

export default FilesTab;
