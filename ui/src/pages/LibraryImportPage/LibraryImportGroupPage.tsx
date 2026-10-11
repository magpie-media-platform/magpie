import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ArrowClockwiseIcon, CheckIcon, FolderSimplePlusIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { PageHeader } from '@/components/ui/PageHeader';
import { QuickActionsButton } from '@/components/ui/QuickActionsButton';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';

import { useMediaQuery } from '@/hooks/ui/useMediaQuery';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import {
    useBulkAcceptLibraryImportCandidates,
    type LibraryImportMonitoringMode,
} from '@/hooks/useBulkAcceptLibraryImportCandidates';
import {
    getParsedSignalValue,
    PARSED_FILE_SIGNAL_TITLE,
    PARSED_FILE_SIGNAL_YEAR,
    type LibraryImportCandidate,
} from '@/hooks/useLibraryImportCandidates';
import { useLibraryImportGroupCandidates } from '@/hooks/useLibraryImportGroupCandidates';
import { Alert } from '@/components/ui/Alert';
import { useLibraryTypes, type LibraryLevelDefinition } from '@/hooks/useLibraryTypes';

import { commonParentPath } from './commonParentPath';
import { countStatuses, fileName, GroupLink, GroupName, StatusRollup } from './importDisplay';
import { libraryImportPath } from './libraryImportPath';
import { CandidateLeafTable } from './CandidateLeafTable';
import { AcceptImportDialog, type ImportAcceptAction } from './AcceptImportDialog';

import '@/styles/bulk-actions-toolbar.css';
import './LibraryImportGroupPage.css';

const PAGE_SIZE = 25;

function levelChain(
    levels: LibraryLevelDefinition[],
    root = levels.find((level) => !level.parentSlug),
): LibraryLevelDefinition[] {
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

function bucketName(value: string): string {
    return value === '' ? 'Unassigned' : value;
}

function ancestors(
    levels: LibraryLevelDefinition[],
    level: LibraryLevelDefinition | undefined,
): LibraryLevelDefinition[] {
    const parent = levels.find((candidate) => candidate.slug === level?.parentSlug);
    return parent ? [...ancestors(levels, parent), parent] : [];
}

function findGroupLevel(
    candidates: LibraryImportCandidate[],
    levels: LibraryLevelDefinition[],
): LibraryLevelDefinition | undefined {
    const match = candidates[0]?.matches.length === 1 ? candidates[0].matches[0] : undefined;
    const level = levels.find((candidate) => candidate.slug === match?.levelSlug);
    const isGroupMatch =
        match !== undefined &&
        level !== undefined &&
        candidates.every(
            ({ matches }) =>
                matches.length === 1 &&
                matches[0].providerType === match.providerType &&
                matches[0].externalIdKey === match.externalIdKey &&
                matches[0].externalId === match.externalId,
        ) &&
        [...ancestors(levels, level), level]
            .slice(1)
            .every(({ slug }) =>
                candidates.every((candidate) => bucketValue(candidate, slug) === bucketValue(candidates[0], slug)),
            );
    const roots = levels.filter((root) => !root.parentSlug);
    return isGroupMatch ? level : roots.length === 1 ? roots[0] : undefined;
}

function titleDepth(candidate: LibraryImportCandidate, chain: LibraryLevelDefinition[]): number {
    const title = getParsedSignalValue(candidate.parsedSignals, PARSED_FILE_SIGNAL_TITLE);
    for (let depth = chain.length - 1; depth > 0; depth--) {
        if (title !== '' && bucketValue(candidate, chain[depth].slug) === title) {
            return depth;
        }
    }
    return 0;
}

function groupYear(
    candidates: LibraryImportCandidate[],
    chain: LibraryLevelDefinition[],
    level: LibraryLevelDefinition | undefined,
    depth: number,
): string {
    if (!level) {
        return '';
    }
    for (const candidate of candidates) {
        const match = candidate.matches.length === 1 ? candidate.matches[0] : undefined;
        if (match?.subtitle && match.levelSlug === level.slug) {
            return match.subtitle;
        }
        const year = getParsedSignalValue(candidate.parsedSignals, PARSED_FILE_SIGNAL_YEAR);
        if (year !== '' && titleDepth(candidate, chain) === depth) {
            return year;
        }
    }
    return '';
}

interface Bucket {
    key: string;
    name: string;
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
        name: bucketName(key),
        label: `${level.displayName} ${bucketName(key)}`,
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
    const [searchParams] = useSearchParams();
    const phone = useMediaQuery('(max-width: 639px)');

    const groupKey = searchParams.get('key') ?? '';
    const rootFolderId = searchParams.get('rootFolderId') ?? undefined;
    const queryTitle = searchParams.get('title') ?? '';
    const urlPath = useMemo(() => searchParams.getAll('path'), [searchParams]);

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
    const [isRetryingFailed, setIsRetryingFailed] = useState(false);
    const [acceptAction, setAcceptAction] = useState<ImportAcceptAction | null>(null);
    const titleRef = useRef<HTMLHeadingElement>(null);
    const { mutateAsync: acceptCandidates } = useBulkAcceptLibraryImportCandidates();

    const buildUrl = useCallback(
        (nextPath: string[]) => libraryImportPath({ groupKey, rootFolderId, title: queryTitle, path: nextPath }),
        [groupKey, rootFolderId, queryTitle],
    );

    const contentType = candidates[0]?.contentType;
    const levels = useMemo(
        () => contentTypes.find((type) => type.slug === contentType)?.levels ?? [],
        [contentTypes, contentType],
    );
    const groupLevel = useMemo(() => findGroupLevel(candidates, levels), [candidates, levels]);
    const groupAncestors = useMemo(() => ancestors(levels, groupLevel), [levels, groupLevel]);
    const chain = useMemo(
        () => [...groupAncestors, ...levelChain(levels, groupLevel)],
        [groupAncestors, levels, groupLevel],
    );
    const groupPath = useMemo(
        () => chain.slice(1, groupAncestors.length + 1).map((level) => bucketValue(candidates[0], level.slug)),
        [chain, groupAncestors, candidates],
    );
    const path = useMemo(() => [...urlPath, ...groupPath.slice(urlPath.length)], [urlPath, groupPath]);
    const globalLeafLevel = chain[chain.length - 1];

    const scope = useMemo(
        () =>
            [...scopeCandidates(candidates, chain, path)].sort((a, b) =>
                a.filePath.localeCompare(b.filePath, undefined, { numeric: true }),
            ),
        [candidates, chain, path],
    );
    const groupingLevel = chain[path.length + 1];
    const atLeaf =
        !groupingLevel || groupingLevel.slug === globalLeafLevel?.slug || !hasLevelSignal(scope, groupingLevel.slug);
    const failedIds = useMemo(
        () =>
            scope
                .filter((candidate) => candidate.status === 'Failed' && candidate.matches.length === 1)
                .map((candidate) => candidate.id),
        [scope],
    );

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
    useEffect(() => clearSelection(), [clearSelection, urlPath]);

    const submitImport = useCallback(
        async (ids: string[], monitoringMode: LibraryImportMonitoringMode) => {
            try {
                const result = await acceptCandidates({ ids, monitoringMode });
                const queuedCount = result.queued.length;
                const skippedCount = result.skipped.length;
                toast({
                    title:
                        queuedCount === 0
                            ? 'Nothing queued for import'
                            : skippedCount > 0
                              ? 'Import partially queued'
                              : 'Import queued',
                    description:
                        `${queuedCount} file${queuedCount === 1 ? '' : 's'} queued for import` +
                        (skippedCount > 0 ? `, ${skippedCount} skipped` : '') +
                        '.',
                    variant: queuedCount === 0 ? 'error' : skippedCount > 0 ? 'warning' : 'success',
                });
            } catch {
                toast({
                    title: 'Import failed',
                    description: 'Failed to queue the files for import. Please try again.',
                    variant: 'error',
                });
            }
        },
        [acceptCandidates, toast],
    );

    const handleAccept = useCallback(
        async (bucket: Bucket, monitoringMode: LibraryImportMonitoringMode) => {
            setAcceptingKeys((prev) => new Set(prev).add(bucket.key));
            try {
                await submitImport(
                    bucket.candidates.map((c) => c.id),
                    monitoringMode,
                );
            } finally {
                setAcceptingKeys((prev) => {
                    const next = new Set(prev);
                    next.delete(bucket.key);
                    return next;
                });
            }
        },
        [submitImport],
    );

    const requestRetryFailed = useCallback(
        () =>
            setAcceptAction(() =>
                Object.assign(
                    async (mode: LibraryImportMonitoringMode) => {
                        setIsRetryingFailed(true);
                        try {
                            await submitImport(failedIds, mode);
                        } finally {
                            setIsRetryingFailed(false);
                        }
                    },
                    { renameSelection: { ids: failedIds }, fallbackFocusRef: titleRef },
                ),
            ),
        [failedIds, submitImport],
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
                const result = await acceptCandidates({ ids, monitoringMode });
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
        [selectedBuckets, acceptCandidates, toast, selection],
    );

    const requestBulkAccept = useCallback(
        () =>
            setAcceptAction(() =>
                Object.assign((mode: LibraryImportMonitoringMode) => handleBulkAccept(mode), {
                    renameSelection: {
                        ids: selectedBuckets.flatMap((bucket) => bucket.candidates.map((candidate) => candidate.id)),
                    },
                }),
            ),
        [handleBulkAccept, selectedBuckets],
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
                cell: ({ row }) => (
                    <GroupLink to={buildUrl([...path, row.original.key])}>
                        <GroupName level={groupingLevel?.displayName} name={row.original.name} />
                    </GroupLink>
                ),
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
                id: 'status',
                header: 'Status',
                enableSorting: false,
                cell: ({ row }) => <StatusRollup statusCounts={countStatuses(row.original.candidates)} />,
            },
            {
                id: 'actions',
                header: '',
                size: 120,
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
                                        setAcceptAction(() =>
                                            Object.assign(
                                                (mode: LibraryImportMonitoringMode) => handleAccept(bucket, mode),
                                                {
                                                    renameSelection: {
                                                        ids: bucket.candidates.map((candidate) => candidate.id),
                                                    },
                                                },
                                            ),
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
                        </div>
                    );
                },
            },
        ],
        [acceptingKeys, buildUrl, globalLeafLevel, groupingLevel, handleAccept, path, selection],
    );

    const title = queryTitle || fallbackTitle(candidates);
    useDocumentTitle(title || 'Library import');

    const trail = useMemo(() => {
        const groups = [{ level: groupLevel, name: title, levelFirst: false, depth: groupPath.length }];
        for (let index = 0; index < path.length; index++) {
            const level = chain[index + 1];
            if (!level) {
                break;
            }
            if (path[index] === groupPath[index]) {
                continue;
            }
            groups.push({
                level,
                name: bucketName(path[index]),
                levelFirst: !scope.some((candidate) => titleDepth(candidate, chain) === index + 1),
                depth: index + 1,
            });
        }
        return groups;
    }, [groupLevel, title, groupPath, path, chain, scope]);
    const breadcrumbItems: BreadcrumbItem[] = useMemo(
        () => [
            {
                id: 'root',
                label: 'Library import',
                to: libraryImportPath({ rootFolderId }),
            },
            ...trail.map(({ level, name, depth }, index) => ({
                id: `group-${index}`,
                label: <GroupName level={level?.displayName} name={name} />,
                to: buildUrl(index === 0 ? [] : path.slice(0, depth)),
            })),
        ],
        [rootFolderId, trail, buildUrl, path],
    );
    const current = trail[trail.length - 1];
    const year = groupYear(scope, chain, current.level, current.depth);
    const yearAndLevel = [current.name.includes(year) ? '' : year, current.levelFirst ? '' : current.level?.displayName]
        .filter(Boolean)
        .join(' · ');
    const sharedPath = useMemo(() => commonParentPath(scope.map((candidate) => candidate.filePath)), [scope]);
    const failedCount = countStatuses(scope).Failed ?? 0;
    const scopeCounts = (
        <p className="library-import-group-page__counts">
            <span>
                {scope.length} {scope.length === 1 ? 'file' : 'files'}
            </span>
            {failedCount > 0 && (
                <>
                    {' · '}
                    <span className="library-import-group-page__failed-count">{failedCount} failed</span>
                </>
            )}
        </p>
    );

    const retryFailedLabel = `Retry failed (${failedIds.length})`;
    const scopeAction =
        failedIds.length === 0 ? undefined : phone ? (
            <QuickActionsButton
                actions={[
                    {
                        id: 'retry-failed',
                        label: retryFailedLabel,
                        icon: <ArrowClockwiseIcon weight="bold" />,
                        onClick: requestRetryFailed,
                        disabled: isRetryingFailed,
                    },
                ]}
                showLabel
            />
        ) : (
            <Button
                variant="secondary"
                size="sm"
                onClick={requestRetryFailed}
                loading={isRetryingFailed}
                disabled={isRetryingFailed}
                data-action="retry-failed"
            >
                <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
                {retryFailedLabel}
            </Button>
        );

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
                onRetry={refetch}
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
                scopeCounts={scopeCounts}
                scopeAction={scopeAction}
                requestAccept={(action, renameSelection) =>
                    setAcceptAction(() => Object.assign(action, { renameSelection }))
                }
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
                <div className="library-import-group-page__toolbar">
                    {scopeCounts}
                    {!selection.hasSelection && scopeAction}
                    <div className="library-import-group-page__mobile-select-toggle">
                        <SelectModeToggle
                            active={selection.selectionMode}
                            onToggle={selection.toggleSelectionMode}
                            mobileOnly={false}
                        />
                    </div>
                </div>

                <BulkActionsToolbar
                    className="library-import-group-page__desktop-bulk-toolbar"
                    selection={selection}
                    label="Import group selection actions"
                    itemNounPlural="groups"
                >
                    <Button
                        variant="primary"
                        size="sm"
                        onClick={requestBulkAccept}
                        loading={isBulkAccepting}
                        data-action="bulk-accept"
                    >
                        <CheckIcon weight="bold" aria-hidden="true" />
                        <span className="bulk-actions-toolbar__btn-label">Accept selected</span>
                    </Button>
                </BulkActionsToolbar>

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

            {groupKey && (
                <section className="library-import-group-page__context" aria-labelledby="library-import-group-title">
                    <div className="library-import-group-page__title-row">
                        <h2
                            id="library-import-group-title"
                            ref={titleRef}
                            tabIndex={-1}
                            className="library-import-group-page__title"
                        >
                            <GroupName
                                level={current.levelFirst ? current.level?.displayName : undefined}
                                name={current.name}
                            />
                        </h2>
                        {yearAndLevel && (
                            <span className="import-display__level library-import-group-page__level">
                                {yearAndLevel}
                            </span>
                        )}
                    </div>
                    {sharedPath && <p className="library-import-group-page__path">{sharedPath}</p>}
                </section>
            )}

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
                        onClick={requestBulkAccept}
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
