import { useCallback, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
    ArrowLeftIcon,
    DownloadSimpleIcon,
    LinkSimpleIcon,
    MagnifyingGlassIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ColumnHeaderFilter } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Input } from '@/components/ui/Input';
import { MultiSelectColumnFilter } from '@/components/ui/MultiSelectColumnFilter';
import { ListFilterToolbar, type ListFilterField } from '@/components/ui/ListFilterToolbar';
import { PageHeader } from '@/components/ui/PageHeader';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useGrabInteractiveCandidate } from '@/hooks/useInteractiveSearch';
import {
    useIndexerSearch,
    useMatchIndexerRelease,
    type IndexerSearchCandidate,
    type IndexerSearchQuery,
    type IndexerSearchResult,
} from '@/hooks/useIndexerSearch';
import { useAllIndexerConnections, useIndexers } from '@/hooks/useIndexers';
import { useReleaseGrabGuard } from '@/hooks/useReleaseGrabGuard';
import { getErrorMessage } from '@/lib/errors';
import { formatFileSize } from '@/lib/formatSize';
import { formatRelativeTime } from '@/lib/formatTime';
import type { ContentListItem } from '@/types/content';

import { ContentPickerDialog, type ContentPickerMode } from './ContentPickerDialog';
import { availableQueryModes, fieldLabel } from './queryModes';

import './IndexerSearchPage.css';

const VALUE_PARAM_PREFIX = 'v_';
const GIBIBYTE = 1024 ** 3;

function buildQuery(params: URLSearchParams): IndexerSearchQuery | null {
    const mode = params.get('mode');
    if (!mode) {
        return null;
    }

    let title: string | undefined;
    const criteria: Record<string, string[]> = {};
    for (const field of mode.split(',').filter(Boolean)) {
        const value = params.get(`${VALUE_PARAM_PREFIX}${field}`)?.trim();
        if (!value) {
            continue;
        }
        if (field === 'q') {
            title = value;
        } else {
            criteria[field] = [value];
        }
    }

    if (!title && Object.keys(criteria).length === 0) {
        return null;
    }

    return { title, criteria };
}

function describeSkippedConnections(data: IndexerSearchResult): string {
    if (data.connections.length === 0) {
        return 'No selected indexer connection is available.';
    }
    const reasons = data.connections
        .filter((connection) => !connection.attempted)
        .map((connection) => connection.skippedReason);
    if (data.connectionsSkipped === 0) {
        return 'Every selected indexer failed before it could search. Review the warnings and try again.';
    }
    if (reasons.every((reason) => reason === 'disabled')) {
        return 'Every selected indexer connection is disabled. Enable a connection to search it.';
    }
    if (reasons.every((reason) => reason === 'unhealthy')) {
        return 'Every selected indexer connection is unhealthy. Test or fix the connections and try again.';
    }
    if (reasons.every((reason) => reason === 'request-not-supported')) {
        return 'The selected indexers do not support this query. Choose a compatible query mode.';
    }
    if (reasons.every((reason) => reason === 'content-type-not-applicable')) {
        return 'The selected indexers do not apply to this content type.';
    }
    const count = data.connections.length;
    return `${count} selected indexer connection${count === 1 ? ' was' : 's were'} not searched. Review their status and query support.`;
}

export function IndexerSearchPage() {
    useDocumentTitle('Interactive Search');
    const [searchParams, setSearchParams] = useSearchParams();
    const { toast } = useToast();

    const { indexers, isLoadingIndexers } = useIndexers();
    const { rows, isLoading: isLoadingConnections } = useAllIndexerConnections(indexers);

    const connectionIds = useMemo(
        () => (searchParams.get('connections') ?? '').split(',').filter(Boolean),
        [searchParams],
    );
    const selectedRows = useMemo(
        () => connectionIds.map((id) => rows.find((row) => row.connection.id === id)).filter((row) => !!row),
        [connectionIds, rows],
    );
    const selectedIndexers = useMemo(
        () => [...new Map(selectedRows.map((row) => [row.indexer.id, row.indexer])).values()],
        [selectedRows],
    );
    const modes = useMemo(() => availableQueryModes(selectedIndexers), [selectedIndexers]);

    const [modeKey, setModeKey] = useState(() => searchParams.get('mode') ?? '');
    const [values, setValues] = useState<Record<string, string>>(() => {
        const initial: Record<string, string> = {};
        for (const [key, value] of searchParams.entries()) {
            if (key.startsWith(VALUE_PARAM_PREFIX)) {
                initial[key.slice(VALUE_PARAM_PREFIX.length)] = value;
            }
        }
        return initial;
    });

    const activeMode = modes.find((mode) => mode.key === modeKey) ?? modes[0] ?? null;
    const executedQuery = useMemo(() => buildQuery(searchParams), [searchParams]);

    const { data, isFetching, error, refetch } = useIndexerSearch(connectionIds, executedQuery);

    const [picker, setPicker] = useState<{
        candidate: IndexerSearchCandidate;
        candidateKey: string;
        mode: ContentPickerMode;
    } | null>(null);
    const [pendingGrab, setPendingGrab] = useState<{
        candidate: IndexerSearchCandidate;
        candidateKey: string;
        content: ContentListItem;
    } | null>(null);
    const [indexerFilter, setIndexerFilter] = useState<string[]>([]);
    const [sizeRange, setSizeRange] = useState({ min: '', max: '' });
    const [sorting, setSorting] = useState<SortingState>([{ id: 'age', desc: true }]);

    const matchMutation = useMatchIndexerRelease();
    const grabMutation = useGrabInteractiveCandidate();
    const { pendingReleaseIds, succeededReleaseIds, tryBeginGrab, markGrabSucceeded, markGrabFailed } =
        useReleaseGrabGuard();

    const connectionNames = useMemo(() => {
        const names = new Map<string, string>();
        for (const row of selectedRows) {
            names.set(row.connection.id, row.connection.name);
        }
        return names;
    }, [selectedRows]);

    const warnings = useMemo(
        () => (data?.connections ?? []).filter((connection) => !!connection.warning),
        [data?.connections],
    );

    const candidates = useMemo(() => {
        const all = data?.candidates ?? [];
        const min = Number(sizeRange.min) * GIBIBYTE;
        const max = Number(sizeRange.max) * GIBIBYTE;
        return all.filter(
            (candidate) =>
                (indexerFilter.length === 0 || candidate.connectionIds.some((id) => indexerFilter.includes(id))) &&
                (!sizeRange.min || (candidate.release.size ?? 0) >= min) &&
                (!sizeRange.max || (candidate.release.size ?? 0) <= max),
        );
    }, [data?.candidates, indexerFilter, sizeRange]);

    const handleSubmit = useCallback(() => {
        if (!activeMode) {
            return;
        }

        const next = new URLSearchParams();
        next.set('connections', connectionIds.join(','));
        next.set('mode', activeMode.key);
        for (const field of activeMode.fields) {
            const value = values[field]?.trim();
            if (value) {
                next.set(`${VALUE_PARAM_PREFIX}${field}`, value);
            }
        }
        const current = new URLSearchParams(searchParams);
        current.sort();
        next.sort();
        if (next.toString() === current.toString()) {
            void refetch();
        } else {
            setSearchParams(next);
        }
    }, [activeMode, connectionIds, refetch, searchParams, setSearchParams, values]);

    const performGrab = useCallback(
        (candidate: IndexerSearchCandidate, content: ContentListItem, key: string) => {
            if (!tryBeginGrab(key)) {
                return;
            }

            grabMutation.mutate(
                { contentHierarchyItemId: content.id, release: candidate.release },
                {
                    onSuccess: () => {
                        markGrabSucceeded(key);
                        toast({
                            title: 'Release grabbed',
                            description: `Queued the release for "${content.title}".`,
                            variant: 'success',
                        });
                    },
                    onError: (grabError) => {
                        markGrabFailed(key);
                        toast({
                            title: 'Grab failed',
                            description: getErrorMessage(grabError, 'Failed to grab the release.'),
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [grabMutation, markGrabFailed, markGrabSucceeded, toast, tryBeginGrab],
    );

    const handleContentSelected = useCallback(
        (content: ContentListItem) => {
            if (!picker) {
                return;
            }

            const { candidate, candidateKey: key, mode } = picker;
            setPicker(null);
            if (mode === 'grab') {
                setPendingGrab({ candidate, candidateKey: key, content });
                return;
            }

            matchMutation.mutate(
                { contentHierarchyItemId: content.id, release: candidate.release },
                {
                    onSuccess: () =>
                        toast({
                            title: 'Release matched',
                            description: `Associated the release with "${content.title}".`,
                            variant: 'success',
                        }),
                    onError: (matchError) =>
                        toast({
                            title: 'Match failed',
                            description: getErrorMessage(matchError, 'Failed to match the release.'),
                            variant: 'error',
                        }),
                },
            );
        },
        [matchMutation, picker, toast],
    );

    const columns = useMemo<ColumnDef<IndexerSearchCandidate>[]>(
        () => [
            {
                id: 'title',
                accessorFn: (row) => row.release.title,
                header: 'Title',
                meta: { mobileRole: 'header' },
                cell: ({ row }) => (
                    <span className="indexer-search__release-title" title={row.original.release.title}>
                        {row.original.release.title}
                    </span>
                ),
            },
            {
                id: 'indexers',
                enableSorting: false,
                size: 190,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Indexer(s)"
                        options={selectedRows.map((row) => ({ value: row.connection.id, label: row.connection.name }))}
                        value={indexerFilter}
                        onChange={setIndexerFilter}
                    />
                ),
                meta: { headerLabel: 'Indexer(s)' },
                cell: ({ row }) => {
                    const ids = row.original.connectionIds;
                    const rest = ids.slice(1);
                    return (
                        <span className="indexer-search__provenance">
                            <Badge variant="default" size="md" badgeStyle="outline">
                                {connectionNames.get(ids[0]) ?? 'Unknown'}
                            </Badge>
                            {rest.length > 0 && (
                                <Badge
                                    className="indexer-search__provenance-more"
                                    variant="default"
                                    size="md"
                                    badgeStyle="outline"
                                    title={rest.map((id) => connectionNames.get(id) ?? 'Unknown').join(', ')}
                                >
                                    {`+${rest.length} more`}
                                </Badge>
                            )}
                        </span>
                    );
                },
            },
            {
                id: 'age',
                accessorFn: (row) => row.release.publishDate,
                header: 'Age',
                size: 140,
                cell: ({ row }) => (
                    <span className="indexer-search__age">{formatRelativeTime(row.original.release.publishDate)}</span>
                ),
            },
            {
                id: 'size',
                accessorFn: (row) => row.release.size ?? 0,
                enableSorting: false,
                header: () => (
                    <ColumnHeaderFilter
                        type="numberRange"
                        label="Size"
                        unit="GB"
                        active={!!sizeRange.min || !!sizeRange.max}
                        min={sizeRange.min}
                        max={sizeRange.max}
                        onChange={setSizeRange}
                    />
                ),
                meta: { headerLabel: 'Size' },
                size: 110,
                cell: ({ row }) => (
                    <span className="indexer-search__size">
                        {row.original.release.size ? formatFileSize(row.original.release.size) : '-'}
                    </span>
                ),
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                size: 230,
                cell: ({ row }) => {
                    const key = row.id;
                    if (succeededReleaseIds.has(key)) {
                        return (
                            <span className="indexer-search__row-actions">
                                <Badge variant="queued" size="sm" dot data-acquisition-status="Queued">
                                    Queued
                                </Badge>
                            </span>
                        );
                    }

                    return (
                        <span className="indexer-search__row-actions">
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => setPicker({ candidate: row.original, candidateKey: key, mode: 'match' })}
                                data-action="match"
                            >
                                <LinkSimpleIcon weight="bold" aria-hidden="true" />
                                Match
                            </Button>
                            <Button
                                variant="primary"
                                size="sm"
                                disabled={pendingReleaseIds.has(key)}
                                onClick={() => setPicker({ candidate: row.original, candidateKey: key, mode: 'grab' })}
                                data-action="grab"
                            >
                                <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                                Grab
                            </Button>
                        </span>
                    );
                },
            },
        ],
        [connectionNames, indexerFilter, pendingReleaseIds, selectedRows, sizeRange, succeededReleaseIds],
    );

    const isLoadingSelection = isLoadingIndexers || isLoadingConnections;
    const hasNoMode = !isLoadingSelection && selectedRows.length > 0 && modes.length === 0;
    const searchedNothing = !!data && data.connectionsAttempted === 0;
    const resultFilterFields: ListFilterField[] = [
        {
            id: 'indexers',
            label: 'Indexer(s)',
            kind: 'multi',
            value: indexerFilter,
            options: selectedRows.map((row) => ({ value: row.connection.id, label: row.connection.name })),
        },
        { id: 'size', label: 'Size', kind: 'numberRange', value: sizeRange, unit: 'GB' },
    ];

    return (
        <div className="indexer-search-page" data-page="indexer-search">
            <Button className="indexer-search__back" variant="ghost" size="sm" asChild>
                <Link to="/admin/indexers">
                    <ArrowLeftIcon weight="bold" aria-hidden="true" />
                    Back to Configured Indexers
                </Link>
            </Button>

            <PageHeader
                title="Interactive Search"
                description="Search the selected indexer connections directly and match or grab a release against library content."
                icon={<MagnifyingGlassIcon weight="duotone" aria-hidden="true" />}
            />

            <div className="indexer-search">
                <section className="indexer-search__panel" aria-label="Search the selected indexers">
                    <div className="indexer-search__panel-head">
                        <span className="indexer-search__panel-title">
                            {`Searching ${selectedRows.length} ${selectedRows.length === 1 ? 'indexer' : 'indexers'}`}
                        </span>
                    </div>

                    <div className="indexer-search__connections">
                        {selectedRows.map((row) => (
                            <Badge
                                key={row.connection.id}
                                variant="default"
                                size="md"
                                badgeStyle="outline"
                                data-connection-id={row.connection.id}
                            >
                                {row.connection.name}
                            </Badge>
                        ))}
                    </div>

                    {hasNoMode ? (
                        <EmptyState
                            role="status"
                            title="No search modes available"
                            description="The selected indexers do not offer any search fields."
                        />
                    ) : (
                        <div
                            className="indexer-search__form"
                            onKeyDown={(event) => {
                                if (
                                    event.key === 'Enter' &&
                                    !event.nativeEvent.isComposing &&
                                    event.target instanceof HTMLInputElement &&
                                    !isFetching &&
                                    !!activeMode &&
                                    connectionIds.length > 0
                                ) {
                                    event.preventDefault();
                                    handleSubmit();
                                }
                            }}
                        >
                            <div className="indexer-search__field">
                                <label htmlFor="indexer-search-mode">Query mode</label>
                                <Select
                                    value={activeMode?.key ?? ''}
                                    onValueChange={setModeKey}
                                    disabled={modes.length === 0}
                                >
                                    <SelectTrigger id="indexer-search-mode" aria-label="Query mode">
                                        <SelectValue placeholder="Query mode" />
                                    </SelectTrigger>
                                    <SelectContent>
                                        {modes.map((mode) => (
                                            <SelectItem key={mode.key} value={mode.key}>
                                                {mode.label}
                                            </SelectItem>
                                        ))}
                                    </SelectContent>
                                </Select>
                            </div>

                            {(activeMode?.fields ?? []).map((field) => (
                                <div
                                    key={field}
                                    className={`indexer-search__field${field === 'q' ? ' indexer-search__field--grow' : ''}`}
                                >
                                    <label htmlFor={`indexer-search-${field}`}>
                                        {field === 'q' ? 'Search' : fieldLabel(field)}
                                    </label>
                                    <Input
                                        id={`indexer-search-${field}`}
                                        value={values[field] ?? ''}
                                        onChange={(event) =>
                                            setValues((prev) => ({ ...prev, [field]: event.target.value }))
                                        }
                                        leadingIcon={
                                            field === 'q' ? (
                                                <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                                            ) : undefined
                                        }
                                    />
                                </div>
                            ))}

                            <Button
                                variant="primary"
                                onClick={handleSubmit}
                                loading={isFetching}
                                disabled={!activeMode || connectionIds.length === 0}
                                data-action="run-search"
                            >
                                <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                                Search
                            </Button>
                        </div>
                    )}
                </section>

                {warnings.length > 0 && (
                    <Alert variant="warning" title="Some indexers failed">
                        <ul className="indexer-search__warnings" role="list">
                            {warnings.map((connection) => (
                                <li
                                    key={connection.connectionId}
                                >{`${connection.connectionName}: ${connection.warning}`}</li>
                            ))}
                        </ul>
                    </Alert>
                )}

                {executedQuery && (
                    <>
                        <div className="indexer-search__results-head">
                            <h2 className="indexer-search__results-title">
                                Results <span className="indexer-search__results-count">{candidates.length}</span>
                            </h2>
                            {data && (
                                <span className="indexer-search__panel-note">
                                    {`${data.connectionsAttempted} of ${data.connections.length} indexers searched`}
                                </span>
                            )}
                        </div>

                        <ListFilterToolbar
                            fields={resultFilterFields}
                            onCommit={(values) => {
                                setIndexerFilter(values.indexers as string[]);
                                setSizeRange(values.size as { min: string; max: string });
                            }}
                            sorting={sorting}
                            sortOptions={[
                                { id: 'title', label: 'Title' },
                                { id: 'age', label: 'Age' },
                            ]}
                            onSortingChange={setSorting}
                        />

                        {isFetching ? (
                            <div className="indexer-search__loading" role="status" aria-live="polite">
                                <Skeleton />
                                <Skeleton />
                                <Skeleton />
                            </div>
                        ) : error ? (
                            <EmptyState
                                icon={<WarningCircleIcon weight="thin" aria-hidden="true" />}
                                title="Search failed"
                                description={getErrorMessage(error, 'Failed to search the selected indexers.')}
                                size="sm"
                            />
                        ) : searchedNothing ? (
                            <EmptyState
                                icon={<WarningCircleIcon weight="thin" aria-hidden="true" />}
                                title="No eligible indexers were searched"
                                description={describeSkippedConnections(data)}
                                size="sm"
                            />
                        ) : data?.candidates.length === 0 ? (
                            <EmptyState
                                icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                                title="No releases found"
                                description="No selected indexer reported a release for this query."
                                size="sm"
                            />
                        ) : candidates.length === 0 ? (
                            <EmptyState
                                icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                                title="No releases match the filters"
                                description="Change or clear the result filters to show releases."
                                size="sm"
                            />
                        ) : (
                            <DataTable<IndexerSearchCandidate>
                                className="indexer-search__results-table"
                                columns={columns}
                                data={candidates}
                                size="sm"
                                keepHeaderWhenEmpty
                                enableSorting
                                sorting={sorting}
                                onSortingChange={(updater) =>
                                    setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                                }
                                enablePagination={candidates.length > 25}
                                pageSize={25}
                                getRowId={(candidate) => candidate.key}
                                aria-label="Indexer search results"
                            />
                        )}
                    </>
                )}
            </div>

            <ContentPickerDialog
                open={picker !== null}
                mode={picker?.mode ?? 'match'}
                isSubmitting={matchMutation.isPending}
                onOpenChange={(open) => {
                    if (!open) {
                        setPicker(null);
                    }
                }}
                onSelect={handleContentSelected}
            />

            <DeleteConfirmDialog
                open={pendingGrab !== null}
                title="Grab anyway?"
                itemType="release"
                itemName={pendingGrab?.candidate.release.title ?? ''}
                description="Magpie did not assess this raw release against the selected content. Grab it anyway?"
                confirmVariant="warning"
                confirmLabel="Grab anyway"
                confirmIcon={<DownloadSimpleIcon weight="bold" aria-hidden="true" />}
                onConfirm={() => {
                    if (pendingGrab) {
                        performGrab(pendingGrab.candidate, pendingGrab.content, pendingGrab.candidateKey);
                    }
                    setPendingGrab(null);
                }}
                onCancel={() => setPendingGrab(null)}
            />
        </div>
    );
}

IndexerSearchPage.displayName = 'IndexerSearchPage';

export default IndexerSearchPage;
