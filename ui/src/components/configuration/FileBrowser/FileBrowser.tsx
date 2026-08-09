import { useState, useCallback, useEffect, type ReactNode } from 'react';
import {
    FolderIcon,
    FolderOpenIcon,
    FileIcon,
    HouseIcon,
    ArrowUpIcon,
    CaretRightIcon,
    SpinnerGapIcon,
    WarningIcon,
} from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
    DialogClose,
} from '@/components/ui/Dialog';
import { useFileBrowser, type FileSystemEntry } from '@/hooks/useFileBrowser';

import type { PathInputMode } from '@/components/ui/PathInput';

import './FileBrowser.css';

export type { FileSystemEntry };

function normalizeInitialPath(path: string | undefined): string {
    return path?.trim() || '/';
}

function getPathDetails(path: string) {
    const uncMatch = /^([\\/]{2})([^\\/]+)([\\/])([^\\/]+)(?:[\\/]+)?/.exec(path);
    if (uncMatch) {
        const rootPath = `${uncMatch[1]}${uncMatch[2]}${uncMatch[3]}${uncMatch[4]}`;
        return {
            rootPath,
            separator: uncMatch[3],
            segments: path
                .slice(uncMatch[0].length)
                .split(/[\\/]+/)
                .filter(Boolean),
            rootHasTrailingSeparator: false,
            relative: false,
        };
    }

    const driveMatch = /^([A-Za-z]:)([\\/]*)/.exec(path);
    if (driveMatch) {
        const separator = driveMatch[2][0] ?? '\\';
        return {
            rootPath: `${driveMatch[1]}${separator}`,
            separator,
            segments: path
                .slice(driveMatch[0].length)
                .split(/[\\/]+/)
                .filter(Boolean),
            rootHasTrailingSeparator: true,
            relative: false,
        };
    }

    if (path.startsWith('/')) {
        return {
            rootPath: '/',
            separator: '/',
            segments: path.slice(1).split(/\/+/).filter(Boolean),
            rootHasTrailingSeparator: true,
            relative: false,
        };
    }

    const separator = path.includes('\\') ? '\\' : '/';
    return {
        rootPath: '/',
        separator,
        segments: path.split(/[\\/]+/).filter(Boolean),
        rootHasTrailingSeparator: false,
        relative: true,
    };
}

function buildPath(path: string, segmentCount: number): string {
    const details = getPathDetails(path);
    const segments = details.segments.slice(0, segmentCount);
    if (segments.length === 0) {
        return details.rootPath;
    }

    if (details.relative) {
        return segments.join(details.separator);
    }

    const separator = details.rootHasTrailingSeparator ? '' : details.separator;
    return `${details.rootPath}${separator}${segments.join(details.separator)}`;
}

export interface FileBrowserProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    onSelect: (path: string) => void;

    mode?: PathInputMode;

    initialPath?: string;

    title?: string;

    description?: string;

    endpoint?: string;
}

export function FileBrowser({
    open,
    onOpenChange,
    onSelect,
    mode = 'folder',
    initialPath,
    title,
    description,
    endpoint,
}: FileBrowserProps): ReactNode {
    const defaultInitialPath = normalizeInitialPath(initialPath);

    const [currentPath, setCurrentPath] = useState(defaultInitialPath);
    const [manualPath, setManualPath] = useState(defaultInitialPath);
    const [selectedEntry, setSelectedEntry] = useState<FileSystemEntry | null>(null);

    const { navigate: browseNavigate, entries, isLoading: loading, error: apiError, reset } = useFileBrowser(endpoint);

    const error = apiError?.message ?? null;

    const pathDetails = getPathDetails(currentPath);
    const isAtRoot = pathDetails.segments.length === 0;

    const navigateTo = useCallback(
        (path: string) => {
            setSelectedEntry(null);
            setCurrentPath(path);
            setManualPath(path);
            browseNavigate(path);
        },
        [browseNavigate],
    );

    useEffect(() => {
        if (open) {
            navigateTo(defaultInitialPath);
        } else {
            reset();
        }
    }, [open, defaultInitialPath, navigateTo, reset]);

    const navigateUp = useCallback(() => {
        if (isAtRoot) return;
        const parentPath = buildPath(currentPath, pathDetails.segments.length - 1);
        navigateTo(parentPath);
    }, [currentPath, isAtRoot, navigateTo, pathDetails.segments.length]);

    const navigateHome = useCallback(() => {
        navigateTo(pathDetails.rootPath);
    }, [navigateTo, pathDetails.rootPath]);

    const handleEntryClick = useCallback((entry: FileSystemEntry) => {
        setSelectedEntry(entry);
    }, []);

    const handleEntryDoubleClick = useCallback(
        (entry: FileSystemEntry) => {
            if (entry.type === 'folder') {
                navigateTo(entry.path);
            } else if (mode === 'file' || mode === 'both') {
                onSelect(entry.path);
            }
        },
        [mode, navigateTo, onSelect],
    );

    const handleEntryOpen = useCallback(
        (entry: FileSystemEntry) => {
            navigateTo(entry.path);
        },
        [navigateTo],
    );

    const handleManualPathSubmit = useCallback(
        (e: React.SubmitEvent) => {
            e.preventDefault();
            e.stopPropagation();
            if (manualPath.trim()) {
                navigateTo(manualPath.trim());
            }
        },
        [manualPath, navigateTo],
    );

    const handleSelectClick = useCallback(() => {
        if (mode === 'folder') {
            onSelect(selectedEntry?.path ?? currentPath);
        } else if (selectedEntry) {
            onSelect(selectedEntry.path);
        }
    }, [mode, currentPath, selectedEntry, onSelect]);

    const filteredEntries = entries.filter((entry) => {
        if (mode === 'file') {
            return true;
        }
        if (mode === 'folder') {
            return entry.type === 'folder';
        }
        return true;
    });

    const breadcrumbSegments = [
        { name: pathDetails.rootPath, path: pathDetails.rootPath },
        ...pathDetails.segments.map((segment, index) => ({
            name: segment,
            path: buildPath(currentPath, index + 1),
        })),
    ];

    const canSelect = mode === 'folder' || (selectedEntry && (mode === 'both' || selectedEntry.type === mode));

    const dialogTitle =
        title ?? (mode === 'file' ? 'Select File' : mode === 'folder' ? 'Select Folder' : 'Select Path');

    const dialogDescription =
        description ??
        (mode === 'file'
            ? 'Browse and select a file.'
            : mode === 'folder'
              ? 'Browse and select a folder.'
              : 'Browse and select a file or folder.');

    return (
        <Dialog open={open} onOpenChange={onOpenChange}>
            <DialogContent className="file-browser__dialog" data-component="file-browser">
                <DialogHeader>
                    <DialogTitle>{dialogTitle}</DialogTitle>
                    <DialogDescription>{dialogDescription}</DialogDescription>
                </DialogHeader>

                <div className="file-browser__content">
                    <div className="file-browser__toolbar">
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={navigateHome}
                            disabled={loading || isAtRoot}
                            aria-label="Go to root"
                            data-action="navigate-home"
                        >
                            <HouseIcon weight="bold" aria-hidden="true" />
                        </Button>
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={navigateUp}
                            disabled={loading || isAtRoot}
                            aria-label="Go to parent folder"
                            data-action="navigate-up"
                        >
                            <ArrowUpIcon weight="bold" aria-hidden="true" />
                        </Button>

                        <nav className="file-browser__breadcrumb" aria-label="Current path">
                            {breadcrumbSegments.map((segment, index) => (
                                <span key={segment.path} className="file-browser__breadcrumb-item">
                                    {index > 0 && (
                                        <CaretRightIcon
                                            size={12}
                                            weight="bold"
                                            className="file-browser__breadcrumb-separator"
                                            aria-hidden="true"
                                        />
                                    )}
                                    <button
                                        type="button"
                                        className="file-browser__breadcrumb-link"
                                        onClick={() => navigateTo(segment.path)}
                                        disabled={loading}
                                    >
                                        {segment.name}
                                    </button>
                                </span>
                            ))}
                        </nav>
                    </div>

                    <form onSubmit={handleManualPathSubmit} className="file-browser__path-form">
                        <Input
                            value={manualPath}
                            onChange={(e) => setManualPath(e.target.value)}
                            placeholder="Enter path manually..."
                            size="sm"
                            disabled={loading}
                            className="file-browser__path-input"
                            data-field="manual-path"
                        />
                        <Button
                            type="submit"
                            variant="ghost"
                            size="sm"
                            disabled={loading || !manualPath.trim()}
                            data-action="go-to-path"
                        >
                            Go
                        </Button>
                    </form>

                    <div className="file-browser__list-container">
                        {loading ? (
                            <div className="file-browser__loading">
                                <SpinnerGapIcon
                                    size={32}
                                    weight="bold"
                                    className="file-browser__spinner"
                                    aria-hidden="true"
                                />
                                <span>Loading...</span>
                            </div>
                        ) : error ? (
                            <div className="file-browser__error">
                                <WarningIcon size={32} weight="bold" aria-hidden="true" />
                                <span>{error}</span>
                            </div>
                        ) : filteredEntries.length === 0 ? (
                            <div className="file-browser__empty">
                                <FolderOpenIcon size={32} weight="light" aria-hidden="true" />
                                <span>This folder is empty</span>
                            </div>
                        ) : (
                            <ul className="file-browser__list" role="listbox" aria-label="Directory contents">
                                {filteredEntries.map((entry) => {
                                    const isSelected = selectedEntry?.path === entry.path;
                                    const isSelectable =
                                        mode === 'both' ||
                                        entry.type === mode ||
                                        (mode === 'folder' && entry.type === 'folder');

                                    return (
                                        <li
                                            key={entry.path}
                                            className={[
                                                'file-browser__entry',
                                                `file-browser__entry--${entry.type}`,
                                                isSelected && 'file-browser__entry--selected',
                                                !isSelectable && 'file-browser__entry--disabled',
                                            ]
                                                .filter(Boolean)
                                                .join(' ')}
                                            role="option"
                                            aria-selected={isSelected}
                                            data-entry-type={entry.type}
                                            data-entry-path={entry.path}
                                        >
                                            <button
                                                type="button"
                                                className="file-browser__entry-button"
                                                onClick={() => handleEntryClick(entry)}
                                                onDoubleClick={() => handleEntryDoubleClick(entry)}
                                            >
                                                <span className="file-browser__entry-icon">
                                                    {entry.type === 'folder' ? (
                                                        <FolderIcon weight="fill" aria-hidden="true" />
                                                    ) : (
                                                        <FileIcon weight="regular" aria-hidden="true" />
                                                    )}
                                                </span>
                                                <span className="file-browser__entry-name">{entry.name}</span>
                                            </button>
                                            {entry.type === 'folder' && mode !== 'folder' && (
                                                <Button
                                                    variant="ghost"
                                                    size="sm"
                                                    onClick={() => handleEntryOpen(entry)}
                                                    className="file-browser__entry-open"
                                                    aria-label={`Open ${entry.name}`}
                                                >
                                                    <CaretRightIcon weight="bold" aria-hidden="true" />
                                                </Button>
                                            )}
                                        </li>
                                    );
                                })}
                            </ul>
                        )}
                    </div>

                    {mode === 'folder' && (
                        <div className="file-browser__selection">
                            <span className="file-browser__selection-label">Selected:</span>
                            <span className="file-browser__selection-path">{selectedEntry?.path ?? currentPath}</span>
                        </div>
                    )}
                </div>

                <DialogFooter>
                    <DialogClose asChild>
                        <Button variant="ghost" data-action="cancel">
                            Cancel
                        </Button>
                    </DialogClose>
                    <Button variant="primary" onClick={handleSelectClick} disabled={!canSelect} data-action="select">
                        Select
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

FileBrowser.displayName = 'FileBrowser';

export default FileBrowser;
