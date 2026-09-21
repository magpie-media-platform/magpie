import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { CheckIcon, FolderSimplePlusIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';

import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import {
    useBulkAcceptLibraryImportCandidates,
    type LibraryImportMonitoringMode,
} from '@/hooks/useBulkAcceptLibraryImportCandidates';
import {
    getParsedSignalValue,
    PARSED_FILE_SIGNAL_TITLE,
    type LibraryImportCandidate,
} from '@/hooks/useLibraryImportCandidates';
import { useLibraryImportGroupCandidates } from '@/hooks/useLibraryImportGroupCandidates';
import { Alert } from '@/components/ui/Alert';
import { useLibraryTypes, type LibraryLevelDefinition } from '@/hooks/useLibraryTypes';

import { commonParentPath } from './commonParentPath';
import { fileName } from './importDisplay';
import { libraryImportPath } from './libraryImportPath';
import { CandidateLeafTable } from './CandidateLeafTable';
import { AcceptImportDialog, type ImportAcceptAction } from './AcceptImportDialog';

import '@/styles/bulk-actions-toolbar.css';
import './LibraryImportGroupPage.css';

const PAGE_SIZE = 25;

function levelChain(levels: LibraryLevelDefinition[]): LibraryLevelDefinition[] {
    const root = levels.find((level) => !level.parentSlug);
    if (!root) {
        return [];
    }
    const chain = [root];
    let current = root;
    while (true) {
        const child = levels.find((level) => level.parentSlug === current.slug);
        if (!child) {
            return chain;
        }
        chain.push(child);
        current = child;
    }
}

function bucketValue(candidate: LibraryImportCandidate, levelSlug: string): string {
    return getParsedSignalValue(candidate.parsedSignals, levelSlug);
}

function hasLevelSignal(candidates: LibraryImportCandidate[], levelSlug: string): boolean {
    return candidates.some((candidate) => bucketValue(candidate, levelSlug) !== '');
}

function scopeCandidates(
    all: LibraryImportCandidate[],
    chain: LibraryLevelDefinition[],
    path: string[],
): LibraryImportCandidate[] {
    let scope = all;
    for (let index = 0; index < path.length; index++) {
        const level = chain[index + 1];
        if (!level) {
            break;
        }
        scope = scope.filter((candidate) => bucketValue(candidate, level.slug) === path[index]);
    }
    return scope;
}

function bucketLabel(level: LibraryLevelDefinition, value: string): string {
    return value === '' ? `Unassigned ${level.displayName}` : `${level.displayName} ${value}`;
}

interface Bucket {
    key: string;
    label: string;
    candidates: LibraryImportCandidate[];
}

function buildBuckets(scope: LibraryImportCandidate[], level: LibraryLevelDefinition): Bucket[] {
    const byValue = new Map<string, LibraryImportCandidate[]>();
    for (const candidate of scope) {
        const value = bucketValue(candidate, level.slug);
        const list = byValue.get(value);
        if (list) {
            list.push(candidate);
        } else {
            byValue.set(value, [candidate]);
        }
    }

    const buckets = [...byValue.entries()].map(([key, candidates]) => ({
        key,
        label: bucketLabel(level, key),
        candidates,
    }));

    const assigned = buckets.filter((bucket) => bucket.key !== '');
    const unassigned = buckets.filter((bucket) => bucket.key === '');
    const allNumeric = assigned.every((bucket) => /^\d+$/.test(bucket.key));
    assigned.sort((a, b) => (allNumeric ? Number(a.key) - Number(b.key) : a.key.localeCompare(b.key)));

    return [...assigned, ...unassigned];
}

function fallbackTitle(candidates: LibraryImportCandidate[]): string {
    const first = candidates[0];
    if (!first) {
        return 'Import group';
    }
    return getParsedSignalValue(first.parsedSignals, PARSED_FILE_SIGNAL_TITLE) || fileName(first.filePath);
}

function isBucketMatched(candidates: LibraryImportCandidate[]): boolean {
    return candidates.some((candidate) => candidate.matches.length === 1);
}

function isBucketMatchingPending(candidates: LibraryImportCandidate[]): boolean {
    return !isBucketMatched(candidates) && candidates.some((candidate) => candidate.status === 'MatchingPending');
}

// Mirrors the claim criteria SubmitImportAsync applies per candidate.
function canAcceptBucket(candidates: LibraryImportCandidate[]): boolean {
    return candidates.some(
        (candidate) =>
            candidate.matches.length === 1 && (candidate.status === 'Pending' || candidate.status === 'Failed'),
    );
}

interface LibraryImportGroupPageProps {
    monitoringMode: LibraryImportMonitoringMode | '';
    onMonitoringModeChange: (mode: LibraryImportMonitoringMode) => void;
}

export function LibraryImportGroupPage({ monitoringMode, onMonitoringModeChange }: LibraryImportGroupPageProps) {
    const { toast } = useToast();
    const navigate = useNavigate();
    const [searchParams] = useSearchParams();

    const groupKey = searchParams.get('key') ?? '';
    const rootFolderId = searchParams.get('rootFolderId') ?? undefined;
    const queryTitle = searchParams.get('title') ?? '';
    const path = useMemo(() => searchParams.getAll('path'), [searchParams]);

    const { contentTypes, isLoading: typesLoading } = useLibraryTypes();
    const {
        items: candidates,
        isLoading: candidatesLoading,
        isError,
        isTruncated,
        refetch,
    } = useLibraryImportGroupCandidates(groupKey ? { groupKey, rootFolderId } : null);

    const [acceptingKeys, setAcceptingKeys] = useState<Set<string>>(() => new Set());
    const [isBulkAccepting, setIsBulkAccepting] = useState(false);
    const [acceptAction, setAcceptAction] = useState<ImportAcceptAction | null>(null);
    const bulkAcceptMutation = useBulkAcceptLibraryImportCandidates();

    const buildUrl = useCallback(
        (nextPath: string[]) => libraryImportPath({ groupKey, rootFolderId, title: queryTitle, path: nextPath }),
        [groupKey, rootFolderId, queryTitle],
    );

    const contentType = candidates[0]?.contentType;
    const chain = useMemo(() => {
        const definition = contentTypes.find((type) => type.slug === contentType);
        return levelChain(definition?.levels ?? []);
    }, [contentTypes, contentType]);
    const globalLeafLevel = chain[chain.length - 1];

    const scope = useMemo(() => scopeCandidates(candidates, chain, path), [candidates, chain, path]);
    const groupingLevel = chain[path.length + 1];
    const atLeaf =
        !groupingLevel || groupingLevel.slug === globalLeafLevel?.slug || !hasLevelSignal(scope, groupingLevel.slug);
    const digInLevel = chain[path.length + 2];
    const digInLabel = digInLevel ? digInLevel.displayName : 'Details';

    const buckets = useMemo(
        () => (atLeaf || !groupingLevel ? [] : buildBuckets(scope, groupingLevel)),
        [atLeaf, groupingLevel, scope],
    );

    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const selection = useBulkSelection({ pageRowIds, totalCount: buckets.length });
    const selectedBuckets = useMemo(
        () => buckets.filter((bucket) => selection.isRowChecked(bucket.key)),
        [buckets, selection],
    );

    const clearSelection = selection.clear;
    useEffect(() => clearSelection(), [clearSelection, path]);

    const handleAccept = useCallback(
        async (bucket: Bucket, monitoringMode: LibraryImportMonitoringMode) => {
            setAcceptingKeys((prev) => new Set(prev).add(bucket.key));
            try {
                const result = await bulkAcceptMutation.mutateAsync({
                    ids: bucket.candidates.map((c) => c.id),
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
            } catch {
                toast({
                    title: 'Import failed',
                    description: 'Failed to queue the selected files for import. Please try again.',
                    variant: 'error',
                });
            } finally {
                setAcceptingKeys((prev) => {
                    const next = new Set(prev);
                    next.delete(bucket.key);
                    return next;
                });
            }
        },
        [bulkAcceptMutation, toast],
    );

    const handleBulkAccept = useCallback(
        async (monitoringMode: LibraryImportMonitoringMode) => {
            const eligible = selectedBuckets.filter((bucket) => canAcceptBucket(bucket.candidates));
            const notReadyCount = selectedBuckets.length - eligible.length;
            const ids = eligible.flatMap((bucket) => bucket.candidates.map((candidate) => candidate.id));

            if (ids.length === 0) {
                toast({
                    title: 'Nothing queued for import',
                    description: `${notReadyCount} group${notReadyCount === 1 ? ' is' : 's are'} not ready to import.`,
                    variant: 'error',
                });
                selection.clear();
                return;
            }

            setIsBulkAccepting(true);
            try {
                const result = await bulkAcceptMutation.mutateAsync({ ids, monitoringMode });
                const queuedCount = result.queued.length;
                const skippedCount = result.skipped.length;
                const details = [
                    `${queuedCount} file${queuedCount === 1 ? '' : 's'} queued for import.`,
                    skippedCount > 0 ? `${skippedCount} file${skippedCount === 1 ? '' : 's'} skipped.` : '',
                    notReadyCount > 0
                        ? `${notReadyCount} group${notReadyCount === 1 ? ' is' : 's are'} not ready to import.`
                        : '',
                ].filter(Boolean);
                const allQueued = skippedCount + notReadyCount === 0;
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
            } catch {
                toast({
                    title: 'Import failed',
                    description: 'Failed to queue the selected groups for import. Please try again.',
                    variant: 'error',
                });
            } finally {
                setIsBulkAccepting(false);
            }
        },
        [selectedBuckets, bulkAcceptMutation, toast, selection],
    );

    const getBucketId = useCallback((bucket: Bucket) => bucket.key, []);

    const bucketColumns: ColumnDef<Bucket>[] = useMemo(
        () => [
            selectColumn<Bucket>({
                selection,
                getId: (bucket) => bucket.key,
                headerLabel: 'Select all groups',
                rowLabel: (bucket) => bucket.label,
            }),
            {
                id: 'title',
                header: 'Title',
                meta: { mobileRole: 'header' },
                enableSorting: false,
                cell: ({ row }) => {
                    const sourcePath = commonParentPath(row.original.candidates.map((candidate) => candidate.filePath));
                    return (
                        <div className="library-import-group-page__bucket-label">
                            <span>{row.original.label}</span>
                            <span className="library-import-group-page__bucket-path" title={sourcePath}>
                                {sourcePath}
                            </span>
                        </div>
                    );
                },
            },
            {
                id: 'count',
                header: 'Files',
                enableSorting: false,
                cell: ({ row }) => (
                    <span className="library-import-group-page__bucket-count">
                        {globalLeafLevel?.displayName ?? 'File'}: {row.original.candidates.length}
                    </span>
                ),
            },
            {
                id: 'actions',
                header: '',
                size: 220,
                enableSorting: false,
                cell: ({ row }) => {
                    const bucket = row.original;
                    return (
                        <div className="library-import-group-page__bucket-actions">
                            {isBucketMatchingPending(bucket.candidates) ? (
                                <Button variant="secondary" size="sm" disabled data-action="matching-pending">
                                    Matching pending
                                </Button>
                            ) : (
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() =>
                                        setAcceptAction(
                                            () => (mode: LibraryImportMonitoringMode) => handleAccept(bucket, mode),
                                        )
                                    }
                                    loading={acceptingKeys.has(bucket.key)}
                                    disabled={acceptingKeys.has(bucket.key) || !canAcceptBucket(bucket.candidates)}
                                    data-action="accept"
                                    data-bucket-key={bucket.key}
                                >
                                    <CheckIcon weight="bold" aria-hidden="true" />
                                    Accept
                                </Button>
                            )}
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => void navigate(buildUrl([...path, bucket.key]))}
                                data-action="dig-in"
                            >
                                {digInLabel}
                            </Button>
                        </div>
                    );
                },
            },
        ],
        [acceptingKeys, buildUrl, digInLabel, globalLeafLevel, handleAccept, navigate, path, selection],
    );

    const title = queryTitle || fallbackTitle(candidates);
    useDocumentTitle(title || 'Library import');

    const breadcrumbItems: BreadcrumbItem[] = useMemo(() => {
        const items: BreadcrumbItem[] = [
            {
                id: 'root',
                label: 'Library import',
                to: libraryImportPath({ rootFolderId }),
            },
            { id: 'group', label: title, to: buildUrl([]) },
        ];
        for (let index = 0; index < path.length; index++) {
            const level = chain[index + 1];
            if (!level) {
                break;
            }
            items.push({
                id: `level-${index}`,
                label: bucketLabel(level, path[index]),
                to: buildUrl(path.slice(0, index + 1)),
            });
        }
        return items;
    }, [rootFolderId, title, buildUrl, path, chain]);

    let body: ReactNode;
    if (!groupKey) {
        body = (
            <EmptyState
                icon={<WarningCircleIcon weight="duotone" />}
                title="No group given"
                description="Open this page from the library import queue."
                size="sm"
            />
        );
    } else if (candidatesLoading || typesLoading) {
        body = (
            <div className="library-import-group-page__loading" data-testid="library-import-group-loading">
                <Skeleton className="library-import-group-page__skeleton" />
                <Skeleton className="library-import-group-page__skeleton" />
                <Skeleton className="library-import-group-page__skeleton" />
            </div>
        );
    } else if (isError) {
        body = (
            <EmptyState
                icon={<WarningCircleIcon weight="duotone" />}
                title="Failed to load this group"
                action={
                    <Button variant="secondary" size="sm" onClick={() => refetch()}>
                        Retry
                    </Button>
                }
                size="sm"
            />
        );
    } else if (candidates.length === 0) {
        body = (
            <EmptyState
                icon={<FolderSimplePlusIcon weight="duotone" />}
                title={isTruncated ? 'No files were loaded' : 'This group has no files left'}
                description={
                    isTruncated
                        ? 'The response was incomplete. Reload this group to try again.'
                        : 'Nothing is left to import here. Go back to the import queue for the other groups.'
                }
                size="sm"
                data-testid="library-import-group-empty"
            />
        );
    } else if (atLeaf) {
        body = (
            <CandidateLeafTable
                items={scope}
                requestAccept={(action) => setAcceptAction(() => action)}
                emptyState={
                    <EmptyState
                        icon={<FolderSimplePlusIcon weight="duotone" />}
                        title="No files in this group"
                        size="sm"
                    />
                }
            />
        );
    } else {
        body = (
            <>
                <BulkActionsToolbar
                    className="library-import-group-page__desktop-bulk-toolbar"
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

                <div className="library-import-group-page__mobile-select-toggle">
                    <SelectModeToggle
                        active={selection.selectionMode}
                        onToggle={selection.toggleSelectionMode}
                        mobileOnly={false}
                    />
                </div>

                <DataTable<Bucket>
                    columns={bucketColumns}
                    data={buckets}
                    size="sm"
                    enableSorting={false}
                    getRowId={getBucketId}
                    onPageRowIdsChange={setPageRowIds}
                    enablePagination
                    pageSize={PAGE_SIZE}
                    mobileSelectionActive={selection.selectionMode}
                    mobileSelectionOverlay={{
                        isSelected: (bucket) => selection.isRowChecked(bucket.key),
                        onToggle: (bucket) => selection.toggleRow(bucket.key, !selection.isRowChecked(bucket.key)),
                        rowLabel: (bucket) => bucket.label,
                    }}
                    data-testid="library-import-group-buckets"
                    aria-label="Import groups"
                />
            </>
        );
    }

    return (
        <div className="library-import-group-page" data-page="library-import-group">
            <PageHeader
                className="library-import-group-page__header"
                title="Library import"
                description="Scan root folders for files to import."
                icon={<FolderSimplePlusIcon weight="duotone" aria-hidden="true" />}
            />

            <Breadcrumb items={breadcrumbItems} showHomeIcon className="library-import-group-page__breadcrumb" />

            {isTruncated && (
                <Alert variant="warning" title="Not every file is listed">
                    Only part of this group was loaded. Counts and actions apply only to the loaded files.
                </Alert>
            )}
            <div className="library-import-group-page__panel">{body}</div>

            <AcceptImportDialog
                action={acceptAction}
                monitoringMode={monitoringMode}
                onMonitoringModeChange={onMonitoringModeChange}
                onClose={() => setAcceptAction(null)}
            />

            {selection.selectionMode && !atLeaf && (
                <MobileSelectionBar
                    className="library-import-group-page__selection-bar"
                    data-component="library-import-group-selection-bar"
                    selection={selection}
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

LibraryImportGroupPage.displayName = 'LibraryImportGroupPage';

export default LibraryImportGroupPage;
