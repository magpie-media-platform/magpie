import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
    ArrowSquareOutIcon,
    CheckIcon,
    CheckCircleIcon,
    EyeIcon,
    FolderSimplePlusIcon,
    LinkSimpleIcon,
    MagnifyingGlassIcon,
    WarningCircleIcon,
    XCircleIcon,
} from '@phosphor-icons/react';
import { type ColumnDef, type PaginationState, type RowSelectionState, type Updater } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Checkbox } from '@/components/ui/Checkbox';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { PageHeader } from '@/components/ui/PageHeader';
import { PreviewDrawer, type PreviewDrawerTarget } from '@/components/content/PreviewDrawer';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { SelectAllBanner } from '@/components/ui/SelectAllBanner';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';

import { Role } from '@/auth';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useRootFolders } from '@/hooks/useRootFolders';
import { useCurrentUser } from '@/hooks/useUsers';
import { useBulkAcceptLibraryImportCandidates } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import {
    getParsedSignalNumber,
    getParsedSignalValue,
    PARSED_FILE_SIGNAL_TITLE,
    PARSED_FILE_SIGNAL_YEAR,
    useLibraryImportCandidates,
    type LibraryImportCandidate,
    type LibraryImportCandidateQueryParams,
} from '@/hooks/useLibraryImportCandidates';

import { ManualMatchDialog } from './ManualMatchDialog';
import { useImportScanController, type ScanSummary } from './useImportScanController';

import '@/styles/bulk-actions-toolbar.css';
import './LibraryImportPage.css';

const PAGE_SIZE = 25;

const ALL_VALUE = '__all__';

function fileName(path: string): string {
    const segments = path.split(/[/\\]/);
    return segments[segments.length - 1] || path;
}

function isMatched(match: LibraryImportCandidate): boolean {
    return match.matches.length === 1;
}

function isPending(match: LibraryImportCandidate): boolean {
    return match.status === 'Pending';
}

function isInFlight(match: LibraryImportCandidate): boolean {
    return match.status === 'Queued' || match.status === 'Importing';
}

function isSubmittable(match: LibraryImportCandidate): boolean {
    return isMatched(match) && isPending(match);
}

function summaryLine(summary: ScanSummary): string {
    if (summary.outcome === 'failed') {
        return `Scan of "${summary.rootFolderName}" failed.`;
    }
    if (summary.outcome === 'cancelled') {
        return `Magpie canceled the scan of "${summary.rootFolderName}".`;
    }
    if (summary.toImport === 0) {
        return `"${summary.rootFolderName}" is up to date.`;
    }
    return `"${summary.rootFolderName}": ${summary.toImport} file${summary.toImport === 1 ? '' : 's'} to import.`;
}

interface ScanHeaderProps {
    rootFolderFilter: string;
    rootFolderOptions: { value: string; label: string }[];
    onChangeRootFolder: (value: string) => void;
    onScan: () => void;
    isScanning: boolean;
    isStarting: boolean;
    progress: number | null;
    statusMessage: string | null;
    lastSummary: ScanSummary | null;
    onClearSummary: () => void;
}

function ScanHeader({
    rootFolderFilter,
    rootFolderOptions,
    onChangeRootFolder,
    onScan,
    isScanning,
    isStarting,
    progress,
    statusMessage,
    lastSummary,
    onClearSummary,
}: ScanHeaderProps) {
    const selectedName = rootFolderFilter
        ? (rootFolderOptions.find((o) => o.value === rootFolderFilter)?.label ?? 'Unknown')
        : null;
    const rootFolderLabel = selectedName ?? 'All root folders';
    const hasRootFolders = rootFolderOptions.length > 0;
    const canScan = hasRootFolders && !isScanning && !isStarting;

    const scanButton = (
        <Button
            variant="primary"
            size="sm"
            disabled={!canScan}
            loading={isStarting}
            onClick={() => {
                if (hasRootFolders) {
                    onScan();
                }
            }}
            data-action="scan"
        >
            <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
            Scan
        </Button>
    );

    return (
        <div className="library-import-page__scan-header" data-component="library-import-scan-header">
            <div className="library-import-page__scan-controls">
                <Select
                    value={rootFolderFilter || ALL_VALUE}
                    onValueChange={(v) => onChangeRootFolder(v === ALL_VALUE ? '' : v)}
                    disabled={isScanning}
                >
                    <SelectTrigger
                        size="sm"
                        placeholder="All root folders"
                        aria-label="Root folder"
                        data-filter="root-folder"
                    >
                        {rootFolderLabel}
                    </SelectTrigger>
                    <SelectContent>
                        <SelectItem value={ALL_VALUE}>All root folders</SelectItem>
                        {rootFolderOptions.map((opt) => (
                            <SelectItem key={opt.value} value={opt.value}>
                                {opt.label}
                            </SelectItem>
                        ))}
                    </SelectContent>
                </Select>

                {!hasRootFolders ? (
                    <Tooltip content="Add a root folder to scan.">
                        <span className="library-import-page__scan-button-wrap">{scanButton}</span>
                    </Tooltip>
                ) : (
                    scanButton
                )}

                {!hasRootFolders && (
                    <span className="library-import-page__scan-hint" data-testid="library-import-scan-hint">
                        Add a root folder to enable scanning.
                    </span>
                )}

                {rootFolderFilter !== '' && !isScanning && (
                    <ClearFiltersButton
                        onClick={() => onChangeRootFolder('')}
                        className="library-import-page__clear-filters"
                    />
                )}
            </div>

            {isScanning && (
                <div className="library-import-page__scan-progress" data-component="library-import-scan-progress">
                    <div
                        className="library-import-page__scan-bar"
                        role="progressbar"
                        aria-label="Scan progress"
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-valuenow={progress ?? undefined}
                    >
                        <div
                            className={
                                progress && progress > 0
                                    ? 'library-import-page__scan-fill'
                                    : 'library-import-page__scan-fill library-import-page__scan-fill--indeterminate'
                            }
                            style={progress && progress > 0 ? { width: `${progress}%` } : undefined}
                        />
                    </div>
                    <span className="library-import-page__scan-status" data-testid="library-import-scan-status">
                        {statusMessage ?? 'Scanning...'}
                        {progress !== null && progress > 0 ? ` (${Math.round(progress)}%)` : ''}
                    </span>
                </div>
            )}

            {!isScanning && lastSummary && (
                <div
                    className={`library-import-page__scan-summary library-import-page__scan-summary--${lastSummary.outcome}`}
                    data-component="library-import-scan-summary"
                    role="status"
                >
                    {lastSummary.outcome === 'completed' ? (
                        <CheckCircleIcon weight="bold" aria-hidden="true" />
                    ) : (
                        <WarningCircleIcon weight="bold" aria-hidden="true" />
                    )}
                    <span className="library-import-page__scan-summary-text">{summaryLine(lastSummary)}</span>
                    <Button
                        variant="ghost"
                        size="sm"
                        onClick={onClearSummary}
                        className="library-import-page__scan-summary-dismiss"
                        data-action="dismiss-scan-summary"
                        aria-label="Dismiss scan summary"
                    >
                        <XCircleIcon weight="bold" aria-hidden="true" />
                    </Button>
                </div>
            )}
        </div>
    );
}

interface RowActionHandlers {
    onPreview: (match: LibraryImportCandidate) => void;
    onImport: (match: LibraryImportCandidate) => void;
    onMatch: (match: LibraryImportCandidate) => void;
    actingId: string | null;
}

export function LibraryImportPage() {
    const usesCardLayout = useCardLayout();
    useDocumentTitle('Library import');
    const { toast } = useToast();
    const [searchParams, setSearchParams] = useSearchParams();

    const rootFolderFilter = searchParams.get('rootFolderId') ?? '';
    const page = useMemo(() => {
        const raw = Number(searchParams.get('page'));
        return Number.isInteger(raw) && raw >= 1 ? raw : 1;
    }, [searchParams]);

    const { folders } = useRootFolders();
    const { currentUser } = useCurrentUser();
    const isAdmin = currentUser?.role === Role.Admin;

    const rootFolderOptions = useMemo(() => folders.map((f) => ({ value: f.id, label: f.name })), [folders]);

    const pagination = useMemo<PaginationState>(() => ({ pageIndex: page - 1, pageSize: PAGE_SIZE }), [page]);

    const queryParams = useMemo<LibraryImportCandidateQueryParams>(
        () => ({
            rootFolderId: rootFolderFilter || undefined,
            pageNumber: page,
            pageSize: PAGE_SIZE,
        }),
        [rootFolderFilter, page],
    );

    const {
        items,
        pagination: serverPagination,
        isLoading,
        isError,
        refetch,
    } = useLibraryImportCandidates(queryParams);
    const totalItems = serverPagination?.totalItems ?? 0;

    const matchedCountParams = useMemo<LibraryImportCandidateQueryParams>(
        () => ({
            rootFolderId: rootFolderFilter || undefined,
            status: 'Pending',
            hasMatches: true,
            pageNumber: 1,
            pageSize: 1,
        }),
        [rootFolderFilter],
    );
    const { pagination: matchedPagination } = useLibraryImportCandidates(matchedCountParams);
    const totalMatched = matchedPagination?.totalItems ?? 0;

    const [actingId, setActingId] = useState<string | null>(null);
    const [isBulkActing, setIsBulkActing] = useState(false);
    const [manualMatch, setManualMatch] = useState<LibraryImportCandidate | null>(null);
    const [previewTarget, setPreviewTarget] = useState<PreviewDrawerTarget | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);

    const pageMatchedIds = useMemo(() => items.filter(isSubmittable).map((m) => m.id), [items]);
    const selection = useBulkSelection({ pageRowIds: pageMatchedIds, totalCount: totalMatched });

    const bulkAcceptMutation = useBulkAcceptLibraryImportCandidates();
    const scan = useImportScanController();

    const setPage = useCallback(
        (next: number) => {
            setSearchParams(
                (prev) => {
                    const params = new URLSearchParams(prev);
                    if (next <= 1) {
                        params.delete('page');
                    } else {
                        params.set('page', String(next));
                    }
                    return params;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const handlePaginationChange = useCallback(
        (updater: Updater<PaginationState>) => {
            const next = typeof updater === 'function' ? updater(pagination) : updater;
            setPage(next.pageIndex + 1);
        },
        [pagination, setPage],
    );

    const updateRootFolder = useCallback(
        (value: string) => {
            selection.clear();
            setSearchParams((prev) => {
                const next = new URLSearchParams(prev);
                if (value) {
                    next.set('rootFolderId', value);
                } else {
                    next.delete('rootFolderId');
                }
                next.delete('page');
                return next;
            });
        },
        [setSearchParams, selection],
    );

    const handleScan = useCallback(async () => {
        try {
            if (rootFolderFilter) {
                const folder = rootFolderOptions.find((o) => o.value === rootFolderFilter);
                if (!folder) {
                    return;
                }
                await scan.startScan(folder.value, folder.label);
            } else {
                const targets = rootFolderOptions.map((o) => ({ id: o.value, name: o.label }));
                if (targets.length === 0) {
                    return;
                }
                await scan.startScanAll(targets);
            }
            await refetch();
        } catch {
            toast({
                title: 'Scan failed to start',
                description: 'Try the scan again.',
                variant: 'error',
            });
        }
    }, [rootFolderFilter, rootFolderOptions, scan, refetch, toast]);

    const handlePreview = useCallback((match: LibraryImportCandidate) => {
        const candidate = match.matches[0];
        if (!candidate) {
            return;
        }
        setPreviewTarget({
            providerType: candidate.providerType,
            externalIdKey: candidate.externalIdKey,
            externalId: candidate.externalId,
            contentType: match.contentType,
            levelSlug: candidate.levelSlug ?? undefined,
            fallbackTitle: candidate.title,
            fallbackYear: candidate.subtitle ?? undefined,
        });
        setPreviewOpen(true);
    }, []);

    const handleImport = useCallback(
        async (match: LibraryImportCandidate) => {
            setActingId(match.id);
            try {
                await bulkAcceptMutation.mutateAsync({ ids: [match.id] });
                toast({
                    title: 'Import queued',
                    description: 'Magpie queued the file for import.',
                    variant: 'success',
                });
                await refetch();
            } catch {
                toast({
                    title: 'Import failed',
                    description: 'Failed to queue the file for import. Please try again.',
                    variant: 'error',
                });
            } finally {
                setActingId(null);
            }
        },
        [bulkAcceptMutation, toast, refetch],
    );

    const selectedCount = selection.selectedCount;
    const pageSelectedCount = useMemo(
        () => pageMatchedIds.filter((id) => selection.isRowChecked(id)).length,
        [pageMatchedIds, selection],
    );

    const handleRowSelectionChange = useCallback(
        (updater: RowSelectionState | ((prev: RowSelectionState) => RowSelectionState)) => {
            const current = selection.rowSelection;
            const next = typeof updater === 'function' ? updater(current) : updater;

            const changed = pageMatchedIds.filter((id) => selection.isRowChecked(id) !== Boolean(next[id]));
            if (changed.length === 0) {
                return;
            }

            if (changed.length > 1) {
                selection.toggleHeader(Boolean(next[changed[0]]));
                return;
            }

            const id = changed[0];
            selection.toggleRow(id, Boolean(next[id]));
        },
        [pageMatchedIds, selection],
    );

    const handleBulkAccept = useCallback(async () => {
        const resolved = selection.resolve();
        if (resolved.mode === 'ids' && resolved.ids.length === 0) {
            return;
        }
        setIsBulkActing(true);
        try {
            const result = await bulkAcceptMutation.mutateAsync(
                resolved.mode === 'all'
                    ? { filter: { rootFolderId: rootFolderFilter || undefined, excludeIds: resolved.excludeIds } }
                    : { ids: resolved.ids },
            );
            const queuedCount = result.queued.length;
            const skippedCount = result.skipped.length;
            toast({
                title: 'Import queued',
                description:
                    `${queuedCount} file${queuedCount === 1 ? '' : 's'} queued for import` +
                    (skippedCount > 0 ? `, ${skippedCount} skipped` : '') +
                    '.',
                variant: skippedCount > 0 ? 'warning' : 'success',
            });
            selection.clear();
            await refetch();
        } catch {
            toast({
                title: 'Import failed',
                description: 'Failed to queue the selected files for import. Please try again.',
                variant: 'error',
            });
        } finally {
            setIsBulkActing(false);
        }
    }, [selection, rootFolderFilter, bulkAcceptMutation, toast, refetch]);

    const rowHandlers: RowActionHandlers = useMemo(
        () => ({
            onPreview: handlePreview,
            onImport: handleImport,
            onMatch: setManualMatch,
            actingId,
        }),
        [handlePreview, handleImport, actingId],
    );

    const getRowId = useCallback((match: LibraryImportCandidate) => match.id, []);

    const columns: ColumnDef<LibraryImportCandidate>[] = useMemo(
        () => [
            {
                id: 'select',
                size: 44,
                enableSorting: false,
                header: ({ table }) => (
                    <div className="library-import-page__select-cell">
                        <Checkbox
                            checked={
                                table.getIsAllRowsSelected()
                                    ? true
                                    : table.getIsSomeRowsSelected()
                                      ? 'indeterminate'
                                      : false
                            }
                            onCheckedChange={(value) => table.toggleAllRowsSelected(!!value)}
                            aria-label="Select all matched files"
                        />
                    </div>
                ),
                cell: ({ row }) => (
                    <div className="library-import-page__select-cell">
                        <Checkbox
                            checked={row.getIsSelected()}
                            disabled={!row.getCanSelect()}
                            onCheckedChange={(value) => row.toggleSelected(!!value)}
                            aria-label={`Select ${fileName(row.original.filePath)}`}
                        />
                    </div>
                ),
            },
            {
                id: 'filename',
                header: 'Filename',
                enableSorting: false,
                cell: ({ row }) => {
                    const match = row.original;
                    const parsedTitle = getParsedSignalValue(match.parsedSignals, PARSED_FILE_SIGNAL_TITLE);
                    const parsedYear = getParsedSignalNumber(match.parsedSignals, PARSED_FILE_SIGNAL_YEAR);
                    return (
                        <div className="library-import-page__file-cell" data-match-id={match.id}>
                            {parsedTitle && (
                                <span className="library-import-page__parsed-title">
                                    {parsedTitle}
                                    {parsedYear ? (
                                        <span className="library-import-page__candidate-year"> {parsedYear}</span>
                                    ) : null}
                                </span>
                            )}
                            <span className="library-import-page__file-path">{fileName(match.filePath)}</span>
                        </div>
                    );
                },
            },
            {
                id: 'match',
                header: 'Matched content',
                enableSorting: false,
                cell: ({ row }) => {
                    const match = row.original;
                    const candidate = match.matches[0];
                    if (!candidate) {
                        return (
                            <div className="library-import-page__quarantine-cell" data-testid="library-import-no-match">
                                <span className="library-import-page__muted-dash">No match</span>
                                {match.notes ? (
                                    <span className="library-import-page__quarantine-reason">{match.notes}</span>
                                ) : null}
                            </div>
                        );
                    }
                    return (
                        <div className="library-import-page__candidate-cell">
                            <span className="library-import-page__parsed-title">
                                {candidate.title}
                                {candidate.subtitle ? (
                                    <span className="library-import-page__candidate-year"> {candidate.subtitle}</span>
                                ) : null}
                            </span>
                            <Badge size="sm" variant="info" badgeStyle="outline">
                                {candidate.providerType}
                            </Badge>
                        </div>
                    );
                },
            },
            {
                id: 'actions',
                header: '',
                size: 220,
                enableSorting: false,
                cell: ({ row }) => {
                    const match = row.original;
                    if (!isMatched(match)) {
                        return (
                            <div className="library-import-page__row-actions">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() => rowHandlers.onMatch(match)}
                                    data-action="match"
                                    data-match-id={match.id}
                                >
                                    <LinkSimpleIcon weight="bold" aria-hidden="true" />
                                    Match
                                </Button>
                            </div>
                        );
                    }
                    return (
                        <div className="library-import-page__row-actions">
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => rowHandlers.onPreview(match)}
                                data-action="detail"
                                data-match-id={match.id}
                            >
                                <EyeIcon weight="bold" aria-hidden="true" />
                                Detail
                            </Button>
                            {isSubmittable(match) ? (
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() => rowHandlers.onImport(match)}
                                    loading={rowHandlers.actingId === match.id}
                                    disabled={rowHandlers.actingId !== null}
                                    data-action="import"
                                    data-match-id={match.id}
                                >
                                    <CheckIcon weight="bold" aria-hidden="true" />
                                    Import
                                </Button>
                            ) : (
                                <Button
                                    variant="secondary"
                                    size="sm"
                                    disabled
                                    data-action="import-status"
                                    data-match-id={match.id}
                                >
                                    {isInFlight(match) ? 'Import queued' : match.status}
                                </Button>
                            )}
                        </div>
                    );
                },
            },
        ],
        [rowHandlers],
    );

    const hasRootFolderFilter = rootFolderFilter !== '';
    const hasNoRootFolders = rootFolderOptions.length === 0;

    const emptyState = (
        <EmptyState
            icon={<FolderSimplePlusIcon weight="duotone" />}
            title="No files to import"
            description={
                hasNoRootFolders
                    ? 'Add a root folder to scan for files to import.'
                    : 'No files to import. Run a folder scan.'
            }
            size="sm"
            data-testid="library-import-empty"
        />
    );

    let body: ReactNode;
    if (isLoading) {
        body = (
            <div className="library-import-page__loading" data-testid="library-import-loading">
                <Skeleton className="library-import-page__skeleton" />
                <Skeleton className="library-import-page__skeleton" />
                <Skeleton className="library-import-page__skeleton" />
            </div>
        );
    } else if (isError) {
        body = (
            <EmptyState
                icon={<WarningCircleIcon weight="duotone" />}
                title="Failed to load import queue"
                action={
                    <Button variant="secondary" size="sm" onClick={() => refetch()}>
                        Retry
                    </Button>
                }
                size="sm"
            />
        );
    } else {
        body = (
            <DataTable<LibraryImportCandidate>
                columns={columns}
                data={items}
                size="sm"
                enableSorting={false}
                manualSorting
                enableRowSelection={(row) => isSubmittable(row.original)}
                rowSelection={selection.rowSelection}
                onRowSelectionChange={handleRowSelectionChange}
                getRowId={getRowId}
                enablePagination
                manualPagination
                pagination={pagination}
                onPaginationChange={handlePaginationChange}
                rowCount={totalItems}
                pageSize={PAGE_SIZE}
                emptyState={emptyState}
                keepHeaderWhenEmpty={hasRootFolderFilter}
                data-table="import"
            />
        );
    }

    return (
        <div className="library-import-page" data-page="library-import" data-layout={usesCardLayout ? 'card' : 'table'}>
            <PageHeader
                className="library-import-page__header"
                title="Library import"
                description="Scan root folders for files to import."
                icon={
                    <FolderSimplePlusIcon weight="duotone" aria-hidden="true" data-testid="library-import-page__icon" />
                }
                actions={
                    isAdmin ? (
                        <Button asChild variant="secondary" size="sm" data-action="manage-root-folders">
                            <Link to="/admin/root-folders">
                                <ArrowSquareOutIcon weight="bold" aria-hidden="true" />
                                Manage root folders
                            </Link>
                        </Button>
                    ) : undefined
                }
            />

            <ScanHeader
                rootFolderFilter={rootFolderFilter}
                rootFolderOptions={rootFolderOptions}
                onChangeRootFolder={updateRootFolder}
                onScan={() => void handleScan()}
                isScanning={scan.isScanning}
                isStarting={scan.isStarting}
                progress={scan.progress}
                statusMessage={scan.statusMessage}
                lastSummary={scan.lastSummary}
                onClearSummary={scan.clearSummary}
            />

            {selection.hasSelection && (
                <div className="library-import-page__bulk-bar" data-component="library-import-bulk-bar">
                    {(selection.showAcrossPagesPrompt || selection.allPages) && (
                        <SelectAllBanner
                            allPages={selection.allPages}
                            pageSelectedCount={pageSelectedCount}
                            totalCount={totalMatched}
                            selectedCount={selectedCount}
                            onSelectAllAcrossPages={selection.selectAllAcrossPages}
                            onClear={selection.clear}
                            itemNounPlural="files"
                        />
                    )}
                    <span className="library-import-page__bulk-count">{selectedCount} selected</span>
                    <div className="library-import-page__bulk-actions">
                        <Button
                            variant="primary"
                            size="sm"
                            onClick={handleBulkAccept}
                            loading={isBulkActing}
                            data-action="bulk-accept"
                        >
                            <CheckIcon weight="bold" aria-hidden="true" />
                            Import checked
                        </Button>
                    </div>
                </div>
            )}

            <div className="library-import-page__panel">
                {usesCardLayout && !isLoading && !isError && pageMatchedIds.length > 0 && (
                    <div className="library-import-page__card-toolbar" data-component="library-import-card-toolbar">
                        <Checkbox
                            id="library-import-card-selectall"
                            checked={selection.headerChecked}
                            onCheckedChange={(value) => selection.toggleHeader(!!value)}
                            aria-label="Select all matched files on this page"
                        />
                        <label
                            htmlFor="library-import-card-selectall"
                            className="library-import-page__card-toolbar-label"
                        >
                            Select all matched on this page
                        </label>
                    </div>
                )}
                {body}
            </div>

            <ManualMatchDialog
                match={manualMatch}
                onClose={() => setManualMatch(null)}
                onMatched={() => {
                    selection.clear();
                    void refetch();
                }}
            />

            <PreviewDrawer open={previewOpen} onOpenChange={setPreviewOpen} target={previewTarget} />
        </div>
    );
}

LibraryImportPage.displayName = 'LibraryImportPage';

export default LibraryImportPage;
