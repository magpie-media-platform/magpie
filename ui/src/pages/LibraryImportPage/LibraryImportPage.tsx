import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import {
    ArrowSquareOutIcon,
    CheckIcon,
    CheckCircleIcon,
    FolderSimplePlusIcon,
    LinkSimpleIcon,
    MagnifyingGlassIcon,
    WarningCircleIcon,
    XCircleIcon,
} from '@phosphor-icons/react';
import { type ColumnDef, type PaginationState, type Updater } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Skeleton } from '@/components/ui/Skeleton';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import type { BadgeVariant } from '@/types/badge';

import { Role } from '@/auth';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useRootFolders } from '@/hooks/useRootFolders';
import { useCurrentUser } from '@/hooks/useUsers';
import { useAcceptLibraryImportCandidateGroup } from '@/hooks/useAcceptLibraryImportCandidateGroup';
import { useBulkAcceptLibraryImportCandidateGroups } from '@/hooks/useBulkAcceptLibraryImportCandidateGroups';
import type { LibraryImportMonitoringMode } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import { isApiError } from '@/lib/api';
import {
    useLibraryImportCandidateGroups,
    type LibraryImportCandidateGroup,
    type LibraryImportCandidateStatusCounts,
} from '@/hooks/useLibraryImportCandidateGroups';

import { libraryImportPath } from './libraryImportPath';
import { ManualMatchDialog, type ManualMatchTarget } from './ManualMatchDialog';
import { AcceptImportDialog, type ImportAcceptAction } from './AcceptImportDialog';
import { LibraryImportGroupPage } from './LibraryImportGroupPage';
import { useImportScanController, type ScanSummary } from './useImportScanController';

import '@/styles/bulk-actions-toolbar.css';
import './LibraryImportPage.css';

const PAGE_SIZE = 25;

const ALL_VALUE = '__all__';

const STATUS_BADGE_VARIANT: Record<string, BadgeVariant> = {
    Pending: 'pending',
    Queued: 'queued',
    Importing: 'importing',
    Failed: 'failed',
    Invalidated: 'stale',
    MatchingPending: 'warning',
};

function isMatchingPending(group: LibraryImportCandidateGroup): boolean {
    return !group.match && (group.statusCounts.MatchingPending ?? 0) > 0;
}

// Mirrors the claim criteria SubmitImportAsync applies.
function canAcceptGroup(group: LibraryImportCandidateGroup): boolean {
    return Boolean(group.match) && ((group.statusCounts.Pending ?? 0) > 0 || (group.statusCounts.Failed ?? 0) > 0);
}

function summaryLine(summary: ScanSummary): string {
    if (summary.outcome === 'failed') {
        return `Scan of "${summary.rootFolderName}" failed.`;
    }
    if (summary.outcome === 'cancelled') {
        return `Magpie canceled the scan of "${summary.rootFolderName}".`;
    }
    if (summary.toImport === null) {
        return `Scan of "${summary.rootFolderName}" completed. The import count is unavailable.`;
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

function GroupTitleCell({ group }: { group: LibraryImportCandidateGroup }) {
    const sourcePath = group.sourcePath ?? '';

    return (
        <div className="library-import-page__group-title-cell" data-group-key={group.groupKey}>
            <span className="library-import-page__group-title">
                {group.title}
                {group.subtitle ? <span className="library-import-page__group-subtitle"> {group.subtitle}</span> : null}
            </span>
            {group.match ? (
                <Badge size="sm" variant="info" badgeStyle="outline">
                    {group.match.providerType}
                </Badge>
            ) : (
                <span className="library-import-page__muted-dash" data-testid="library-import-group-unmatched">
                    {isMatchingPending(group) ? 'Matching pending' : 'No match'}
                </span>
            )}
            {sourcePath && (
                <span className="library-import-page__source-path" title={sourcePath}>
                    {sourcePath}
                </span>
            )}
        </div>
    );
}

function statusRollup(statusCounts: LibraryImportCandidateStatusCounts): ReactNode {
    const entries = Object.entries(statusCounts).filter(([status, count]) => status !== 'Pending' && (count ?? 0) > 0);
    if (entries.length === 0) {
        return null;
    }
    return (
        <div className="library-import-page__status-rollup">
            {entries.map(([status, count]) => (
                <Badge key={status} size="sm" variant={STATUS_BADGE_VARIANT[status] ?? 'default'}>
                    {count} {status}
                </Badge>
            ))}
        </div>
    );
}

interface GroupRowActionsProps {
    group: LibraryImportCandidateGroup;
    onAccept: (group: LibraryImportCandidateGroup) => void;
    onDigIn: (group: LibraryImportCandidateGroup) => void;
    onMatch: (group: LibraryImportCandidateGroup) => void;
    acceptingKeys: Set<string>;
}

function GroupRowActions({ group, onAccept, onDigIn, onMatch, acceptingKeys }: GroupRowActionsProps) {
    const hasDescendantLevels = group.topLevelSlug !== group.leafLevelSlug;
    const digInButton = (
        <Button variant="secondary" size="sm" onClick={() => onDigIn(group)} data-action="dig-in">
            {hasDescendantLevels && group.childLevelDisplayName ? group.childLevelDisplayName : 'Details'}
        </Button>
    );

    if (isMatchingPending(group)) {
        return (
            <div className="library-import-page__row-actions">
                <Button variant="secondary" size="sm" disabled data-action="matching-pending">
                    Matching pending
                </Button>
                {digInButton}
            </div>
        );
    }

    if (!group.match) {
        return (
            <div className="library-import-page__row-actions">
                <Button
                    variant="primary"
                    size="sm"
                    onClick={() => onMatch(group)}
                    data-action="match"
                    data-group-key={group.groupKey}
                >
                    <LinkSimpleIcon weight="bold" aria-hidden="true" />
                    Match
                </Button>
                {digInButton}
            </div>
        );
    }

    return (
        <div className="library-import-page__row-actions">
            <Button
                variant="primary"
                size="sm"
                onClick={() => onAccept(group)}
                loading={acceptingKeys.has(group.groupKey)}
                disabled={acceptingKeys.has(group.groupKey) || !canAcceptGroup(group)}
                data-action="accept"
                data-group-key={group.groupKey}
            >
                <CheckIcon weight="bold" aria-hidden="true" />
                Accept
            </Button>
            {digInButton}
        </div>
    );
}

interface LibraryImportPageProps {
    monitoringMode: LibraryImportMonitoringMode | '';
    onMonitoringModeChange: (mode: LibraryImportMonitoringMode) => void;
}

export function LibraryImportPage({ monitoringMode, onMonitoringModeChange }: LibraryImportPageProps) {
    const usesCardLayout = useCardLayout();
    useDocumentTitle('Library import');
    const { toast } = useToast();
    const navigate = useNavigate();
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

    const queryParams = useMemo(
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
    } = useLibraryImportCandidateGroups(queryParams);
    const totalItems = serverPagination?.totalItems ?? 0;

    const [acceptingKeys, setAcceptingKeys] = useState<Set<string>>(() => new Set());
    const [manualMatchTarget, setManualMatchTarget] = useState<ManualMatchTarget | null>(null);
    const [isBulkAccepting, setIsBulkAccepting] = useState(false);
    const [acceptAction, setAcceptAction] = useState<ImportAcceptAction | null>(null);

    const acceptGroupMutation = useAcceptLibraryImportCandidateGroup();
    const acceptGroupBatchMutation = useAcceptLibraryImportCandidateGroup({ deferInvalidation: true });
    const bulkAcceptGroupsMutation = useBulkAcceptLibraryImportCandidateGroups();
    const scan = useImportScanController();

    const groupRowIds = useMemo(() => items.map((group) => group.groupKey), [items]);
    const selection = useBulkSelection({ pageRowIds: groupRowIds, totalCount: totalItems });
    const selectedGroups = useMemo(
        () => items.filter((group) => selection.isRowChecked(group.groupKey)),
        [items, selection],
    );
    const bulkFilter = useMemo(() => ({ rootFolderId: rootFolderFilter || undefined }), [rootFolderFilter]);

    const clearSelection = selection.clear;
    useEffect(() => clearSelection(), [clearSelection, page, rootFolderFilter]);

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
        [setSearchParams],
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

    const handleDigIn = useCallback(
        (group: LibraryImportCandidateGroup) => {
            void navigate(
                libraryImportPath({ groupKey: group.groupKey, rootFolderId: rootFolderFilter, title: group.title }),
            );
        },
        [navigate, rootFolderFilter],
    );

    const handleAccept = useCallback(
        async (group: LibraryImportCandidateGroup, monitoringMode: LibraryImportMonitoringMode) => {
            setAcceptingKeys((prev) => new Set(prev).add(group.groupKey));
            try {
                const result = await acceptGroupMutation.mutateAsync({
                    groupKey: group.groupKey,
                    rootFolderId: rootFolderFilter || undefined,
                    monitoringMode,
                });
                const queuedCount = result.queued.length;
                const skippedCount = result.skipped.length;
                toast({
                    title: 'Import queued',
                    description:
                        `${queuedCount} ${queuedCount === 1 ? 'item' : 'items'} queued for import` +
                        (skippedCount > 0 ? `, ${skippedCount} skipped` : '') +
                        '.',
                    variant: skippedCount > 0 ? 'warning' : 'success',
                });
            } catch (error) {
                if (isApiError(error) && error.status === 404) {
                    toast({
                        title: 'Group is gone',
                        description: 'The queue was out of date. Magpie refreshed it and removed this group.',
                        variant: 'error',
                    });
                } else {
                    toast({
                        title: 'Import failed',
                        description: 'Failed to queue the group for import. Please try again.',
                        variant: 'error',
                    });
                }
            } finally {
                setAcceptingKeys((prev) => {
                    const next = new Set(prev);
                    next.delete(group.groupKey);
                    return next;
                });
            }
        },
        [acceptGroupMutation, rootFolderFilter, toast],
    );

    const handleBulkAccept = useCallback(
        async (monitoringMode: LibraryImportMonitoringMode) => {
            setIsBulkAccepting(true);
            try {
                const resolved = selection.resolve();
                if (resolved.mode === 'all') {
                    try {
                        const result = await bulkAcceptGroupsMutation.mutateAsync({
                            ...bulkFilter,
                            excludeGroupKeys: resolved.excludeIds,
                            monitoringMode,
                        });
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
                    } catch {
                        toast({
                            title: 'Import failed',
                            description: 'Failed to queue the selected groups for import. Please try again.',
                            variant: 'error',
                        });
                    }
                    return;
                }

                let queuedCount = 0;
                let skippedCount = 0;
                let notReadyCount = 0;
                let goneCount = 0;
                let failedCount = 0;
                for (const group of selectedGroups) {
                    if (!canAcceptGroup(group)) {
                        notReadyCount += 1;
                        continue;
                    }
                    try {
                        const result = await acceptGroupBatchMutation.mutateAsync({
                            groupKey: group.groupKey,
                            rootFolderId: rootFolderFilter || undefined,
                            monitoringMode,
                        });
                        queuedCount += result.queued.length;
                        skippedCount += result.skipped.length;
                    } catch (error) {
                        if (isApiError(error) && error.status === 404) {
                            goneCount += 1;
                        } else {
                            failedCount += 1;
                        }
                    }
                }
                acceptGroupBatchMutation.invalidate();

                const details = [
                    queuedCount > 0 ? `${queuedCount} file${queuedCount === 1 ? '' : 's'} queued for import.` : '',
                    skippedCount > 0 ? `${skippedCount} file${skippedCount === 1 ? '' : 's'} skipped.` : '',
                    notReadyCount > 0
                        ? `${notReadyCount} group${notReadyCount === 1 ? ' is' : 's are'} not ready to import.`
                        : '',
                    goneCount > 0 ? `${goneCount} group${goneCount === 1 ? ' is' : 's are'} gone from the queue.` : '',
                    failedCount > 0 ? `${failedCount} group${failedCount === 1 ? '' : 's'} failed to queue.` : '',
                ].filter(Boolean);
                const allQueued = skippedCount + notReadyCount + goneCount + failedCount === 0;
                toast({
                    title:
                        queuedCount === 0
                            ? 'Nothing queued for import'
                            : allQueued
                              ? 'Import queued'
                              : 'Import partially queued',
                    description: details.join(' '),
                    variant: queuedCount === 0 ? 'error' : allQueued ? 'success' : 'warning',
                });
                selection.clear();
            } finally {
                setIsBulkAccepting(false);
            }
        },
        [
            selectedGroups,
            selection,
            acceptGroupBatchMutation,
            bulkAcceptGroupsMutation,
            bulkFilter,
            rootFolderFilter,
            toast,
        ],
    );

    const columns: ColumnDef<LibraryImportCandidateGroup>[] = useMemo(
        () => [
            selectColumn<LibraryImportCandidateGroup>({
                selection,
                getId: (group) => group.groupKey,
                headerLabel: 'Select all groups',
                rowLabel: (group) => group.title,
            }),
            {
                id: 'title',
                header: 'Title',
                meta: { mobileRole: 'header' },
                enableSorting: false,
                cell: ({ row }) => <GroupTitleCell group={row.original} />,
            },
            {
                id: 'count',
                header: 'Files',
                enableSorting: false,
                cell: ({ row }) => (
                    <span className="library-import-page__leaf-count">
                        {row.original.leafLevelDisplayName}: {row.original.leafCount}
                    </span>
                ),
            },
            {
                id: 'status',
                header: 'Status',
                enableSorting: false,
                cell: ({ row }) => statusRollup(row.original.statusCounts),
            },
            {
                id: 'actions',
                header: '',
                size: 220,
                enableSorting: false,
                cell: ({ row }) => (
                    <GroupRowActions
                        group={row.original}
                        onAccept={(group) =>
                            setAcceptAction(() => (mode: LibraryImportMonitoringMode) => handleAccept(group, mode))
                        }
                        onDigIn={handleDigIn}
                        onMatch={(group) =>
                            setManualMatchTarget({ kind: 'group', group, rootFolderId: rootFolderFilter || undefined })
                        }
                        acceptingKeys={acceptingKeys}
                    />
                ),
            },
        ],
        [acceptingKeys, handleAccept, handleDigIn, rootFolderFilter, selection],
    );

    const getRowId = useCallback((group: LibraryImportCandidateGroup) => group.groupKey, []);

    const hasRootFolderFilter = rootFolderFilter !== '';
    const hasNoRootFolders = rootFolderOptions.length === 0;

    const emptyState = hasRootFolderFilter ? (
        <EmptyState
            icon={<MagnifyingGlassIcon weight="duotone" />}
            title="No matching groups"
            description="No files are waiting to import in this root folder. Clear the filter to see the rest."
            action={<ClearFiltersButton onClick={() => updateRootFolder('')} />}
            size="sm"
            data-testid="library-import-empty-filtered"
        />
    ) : (
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
            <DataTable<LibraryImportCandidateGroup>
                columns={columns}
                data={items}
                size="sm"
                enableSorting={false}
                manualSorting
                enableRowSelection={false}
                getRowId={getRowId}
                enablePagination
                manualPagination
                pagination={pagination}
                onPaginationChange={handlePaginationChange}
                rowCount={totalItems}
                pageSize={PAGE_SIZE}
                emptyState={emptyState}
                keepHeaderWhenEmpty={hasRootFolderFilter}
                mobileSelectionActive={selection.selectionMode}
                mobileSelectionOverlay={{
                    isSelected: (group) => selection.isRowChecked(group.groupKey),
                    onToggle: (group) => selection.toggleRow(group.groupKey, !selection.isRowChecked(group.groupKey)),
                    rowLabel: (group) => group.title,
                }}
                data-table="import"
                aria-label="Import queue"
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
                    <>
                        <div className="library-import-page__mobile-select-toggle">
                            <SelectModeToggle
                                active={selection.selectionMode}
                                onToggle={selection.toggleSelectionMode}
                                mobileOnly={false}
                            />
                        </div>
                        {isAdmin && (
                            <Button asChild variant="secondary" size="sm" data-action="manage-root-folders">
                                <Link to="/admin/root-folders">
                                    <ArrowSquareOutIcon weight="bold" aria-hidden="true" />
                                    Manage root folders
                                </Link>
                            </Button>
                        )}
                    </>
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

            <BulkActionsToolbar
                className="library-import-page__desktop-bulk-toolbar"
                selection={selection}
                label="Import group selection actions"
                itemNounPlural="groups"
            >
                <Button
                    variant="primary"
                    size="sm"
                    onClick={() => setAcceptAction(() => handleBulkAccept)}
                    loading={isBulkAccepting}
                    data-action="bulk-accept"
                >
                    <CheckIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Accept selected</span>
                </Button>
            </BulkActionsToolbar>

            <div className="library-import-page__panel">{body}</div>

            <ManualMatchDialog
                target={manualMatchTarget}
                onClose={() => setManualMatchTarget(null)}
                onMatched={() => {
                    if (manualMatchTarget?.kind === 'group') {
                        selection.toggleRow(manualMatchTarget.group.groupKey, false);
                    }
                }}
            />
            <AcceptImportDialog
                action={acceptAction}
                monitoringMode={monitoringMode}
                onMonitoringModeChange={onMonitoringModeChange}
                onClose={() => setAcceptAction(null)}
            />

            {selection.selectionMode && (
                <MobileSelectionBar
                    className="library-import-page__selection-bar"
                    data-component="library-import-selection-bar"
                    selection={selection}
                    pageSelectedCount={selectedGroups.length}
                    totalCount={totalItems}
                    onClear={selection.clear}
                    itemNounPlural="groups"
                >
                    <Button
                        variant="primary"
                        size="sm"
                        onClick={() => setAcceptAction(() => handleBulkAccept)}
                        loading={isBulkAccepting}
                        disabled={!selection.hasSelection}
                        data-action="bulk-accept"
                    >
                        <CheckIcon weight="bold" aria-hidden="true" />
                        Accept selected
                    </Button>
                </MobileSelectionBar>
            )}
        </div>
    );
}

LibraryImportPage.displayName = 'LibraryImportPage';

export default function LibraryImportRoute() {
    const [searchParams] = useSearchParams();
    const [monitoringMode, setMonitoringMode] = useState<LibraryImportMonitoringMode | ''>('');
    return searchParams.has('key') ? (
        <LibraryImportGroupPage monitoringMode={monitoringMode} onMonitoringModeChange={setMonitoringMode} />
    ) : (
        <LibraryImportPage monitoringMode={monitoringMode} onMonitoringModeChange={setMonitoringMode} />
    );
}
