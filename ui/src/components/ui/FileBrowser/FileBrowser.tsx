import { useState, useCallback, useEffect, type ReactNode } from 'react';
import { Folder, FolderOpen, File, House, ArrowUp, CaretRight, SpinnerGap, Warning } from '@phosphor-icons/react';

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

export interface FileBrowserProps {
    open: boolean;

    onOpenChange: (open: boolean) => void;

    onSelect: (path: string) => void;

    mode?: PathInputMode;

    initialPath?: string;

    title?: string;

    description?: string;
}

export function FileBrowser({
    open,
    onOpenChange,
    onSelect,
    mode = 'folder',
    initialPath,
    title,
    description,
}: FileBrowserProps): ReactNode {
    const defaultInitialPath = initialPath ?? '/';

    const [currentPath, setCurrentPath] = useState(defaultInitialPath);
    const [manualPath, setManualPath] = useState(defaultInitialPath);
    const [selectedEntry, setSelectedEntry] = useState<FileSystemEntry | null>(null);

    const { navigate: browseNavigate, entries, isLoading: loading, error: apiError, reset } = useFileBrowser();

    const error = apiError?.message ?? null;

    const isAtRoot = currentPath === '/';

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
        // navigateTo is stable; only re-run when open / initialPath change
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [open, defaultInitialPath, reset]);

    const navigateUp = useCallback(() => {
        if (isAtRoot) return;
        const parentPath = currentPath.split('/').slice(0, -1).join('/') || '/';
        navigateTo(parentPath);
    }, [currentPath, isAtRoot, navigateTo]);

    const navigateHome = useCallback(() => {
        navigateTo('/');
    }, [navigateTo]);

    const handleEntryClick = useCallback(
        (entry: FileSystemEntry) => {
            if (entry.type === 'folder') {
                navigateTo(entry.path);
            } else {
                setSelectedEntry(entry);
            }
        },
        [navigateTo],
    );

    const handleEntryDoubleClick = useCallback(
        (entry: FileSystemEntry) => {
            if (entry.type === 'folder') {
                if (mode === 'folder' || mode === 'both') {
                    onSelect(entry.path);
                }
            } else {
                if (mode === 'file' || mode === 'both') {
                    onSelect(entry.path);
                }
            }
        },
        [mode, onSelect],
    );

    const handleSelectFolder = useCallback((entry: FileSystemEntry) => {
        setSelectedEntry(entry);
    }, []);

    const handleManualPathSubmit = useCallback(
        (e: React.FormEvent) => {
            e.preventDefault();
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

    const breadcrumbSegments =
        currentPath === '/'
            ? [{ name: '/', path: '/' }]
            : [
                  { name: '/', path: '/' },
                  ...currentPath
                      .split('/')
                      .filter(Boolean)
                      .map((segment, index, arr) => ({
                          name: segment,
                          path: '/' + arr.slice(0, index + 1).join('/'),
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
                            <House weight="bold" aria-hidden="true" />
                        </Button>
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={navigateUp}
                            disabled={loading || isAtRoot}
                            aria-label="Go to parent folder"
                            data-action="navigate-up"
                        >
                            <ArrowUp weight="bold" aria-hidden="true" />
                        </Button>

                        <nav className="file-browser__breadcrumb" aria-label="Current path">
                            {breadcrumbSegments.map((segment, index) => (
                                <span key={segment.path} className="file-browser__breadcrumb-item">
                                    {index > 0 && (
                                        <CaretRight
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
                                <SpinnerGap
                                    size={32}
                                    weight="bold"
                                    className="file-browser__spinner"
                                    aria-hidden="true"
                                />
                                <span>Loading...</span>
                            </div>
                        ) : error ? (
                            <div className="file-browser__error">
                                <Warning size={32} weight="bold" aria-hidden="true" />
                                <span>{error}</span>
                            </div>
                        ) : filteredEntries.length === 0 ? (
                            <div className="file-browser__empty">
                                <FolderOpen size={32} weight="light" aria-hidden="true" />
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
                                                onClick={() =>
                                                    entry.type === 'folder' && mode !== 'folder'
                                                        ? handleEntryClick(entry)
                                                        : entry.type === 'folder'
                                                          ? handleSelectFolder(entry)
                                                          : handleEntryClick(entry)
                                                }
                                                onDoubleClick={() => handleEntryDoubleClick(entry)}
                                            >
                                                <span className="file-browser__entry-icon">
                                                    {entry.type === 'folder' ? (
                                                        <Folder weight="fill" aria-hidden="true" />
                                                    ) : (
                                                        <File weight="regular" aria-hidden="true" />
                                                    )}
                                                </span>
                                                <span className="file-browser__entry-name">{entry.name}</span>
                                            </button>
                                            {entry.type === 'folder' && mode !== 'folder' && (
                                                <Button
                                                    variant="ghost"
                                                    size="sm"
                                                    onClick={() => handleEntryClick(entry)}
                                                    className="file-browser__entry-open"
                                                    aria-label={`Open ${entry.name}`}
                                                >
                                                    <CaretRight weight="bold" aria-hidden="true" />
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
