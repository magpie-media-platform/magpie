import { useCallback, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
    ArrowLeftIcon,
    CircleNotchIcon,
    DownloadSimpleIcon,
    MagnifyingGlassIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { Role, useAuth } from '@/auth';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ContentDetailsHeader } from '@/components/content/ContentDetailsHeader';
import { ReleaseDetailsDialog } from '@/components/releases/ReleaseDetailsDialog';
import { ReleaseStatusBadge } from '@/components/releases/ReleaseStatusBadge';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog';
import { DataTable } from '@/components/ui/DataTable';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useContentDetail } from '@/hooks/useContent';
import { useConfiguredIndexerCount } from '@/hooks/useIndexers';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useReleaseGrabGuard } from '@/hooks/useReleaseGrabGuard';
import {
    useGrabInteractiveCandidate,
    useInteractiveSearch,
    type InteractiveSearchCandidate,
} from '@/hooks/useInteractiveSearch';
import type { ApiError } from '@/lib/api';
import { formatDate } from '@/lib/dateFormatting';
import { getErrorMessage, isConflictError } from '@/lib/errors';
import { formatFileSize } from '@/lib/formatSize';

import './InteractiveSearchPage.css';

// The API sets these problem-details types on grab conflicts. The detail text can change.
const NO_DOWNLOAD_CLIENT_PROBLEM_TYPE = 'urn:magpie:problem:no-download-client';
const ACQUISITION_IN_PROGRESS_PROBLEM_TYPE = 'urn:magpie:problem:acquisition-in-progress';

function MatchCell({ candidate }: { candidate: InteractiveSearchCandidate }) {
    const positive = candidate.matchResult === 'Identity' || candidate.matchResult === 'Confident';
    return (
        <span className="interactive-search__match">
            <Badge variant={positive ? 'success' : 'warning'} size="sm" badgeStyle="outline">
                {candidate.matchResult}
            </Badge>
        </span>
    );
}

export function InteractiveSearchPage() {
    useDocumentTitle('Interactive Search');
    const usesCardLayout = useCardLayout();
    const { contentHierarchyItemId } = useParams<{ contentHierarchyItemId: string }>();
    const { hasPermission, hasRole } = useAuth();
    const configuredIndexerCount = useConfiguredIndexerCount();
    const { toast } = useToast();
    const canGrab = hasPermission('content.request');

    const detailQuery = useContentDetail(contentHierarchyItemId);
    const { candidates, partial, warning, retryAt, isLoading, isFetching, error, refetch } =
        useInteractiveSearch(contentHierarchyItemId);
    const grabMutation = useGrabInteractiveCandidate();
    const { pendingReleaseIds, succeededReleaseIds, tryBeginGrab, markGrabSucceeded, markGrabFailed } =
        useReleaseGrabGuard();
    const [pendingGrab, setPendingGrab] = useState<InteractiveSearchCandidate | null>(null);
    const [detailsCandidate, setDetailsCandidate] = useState<InteractiveSearchCandidate | null>(null);

    const detailPath = contentHierarchyItemId ? `/library/${contentHierarchyItemId}` : '/library';

    const performGrab = useCallback(
        (candidate: InteractiveSearchCandidate) => {
            if (!contentHierarchyItemId || !tryBeginGrab(candidate.release.id)) {
                return;
            }

            grabMutation.mutate(
                { contentHierarchyItemId, release: candidate.release },
                {
                    onSuccess: () => {
                        markGrabSucceeded(candidate.release.id);
                        toast({
                            title: 'Release grabbed',
                            description: 'Queued the release for download.',
                            variant: 'success',
                        });
                    },
                    onError: (grabError) => {
                        markGrabFailed(candidate.release.id);
                        const conflict = isConflictError(grabError) ? (grabError as ApiError).details : undefined;
                        const problemType =
                            conflict && typeof conflict === 'object' && 'type' in conflict ? conflict.type : undefined;
                        if (problemType === NO_DOWNLOAD_CLIENT_PROBLEM_TYPE) {
                            toast({
                                title: 'No download client configured',
                                description: 'Add a download client on the Download Clients page to download releases.',
                                variant: 'warning',
                            });
                            return;
                        }
                        if (problemType === ACQUISITION_IN_PROGRESS_PROBLEM_TYPE) {
                            toast({
                                title: 'Already downloading',
                                description: 'This release is already queued or downloading.',
                                variant: 'info',
                            });
                            return;
                        }
                        const message = getErrorMessage(grabError, 'Failed to grab the release.');
                        toast({
                            title: 'Grab failed',
                            description: message,
                            variant: 'error',
                        });
                    },
                },
            );
        },
        [contentHierarchyItemId, grabMutation, markGrabFailed, markGrabSucceeded, toast, tryBeginGrab],
    );

    const handleGrab = useCallback(
        (candidate: InteractiveSearchCandidate) => {
            if (!contentHierarchyItemId) {
                return;
            }

            if (candidate.matchResult !== 'Identity' && candidate.matchResult !== 'Confident') {
                setPendingGrab(candidate);
                return;
            }

            performGrab(candidate);
        },
        [contentHierarchyItemId, performGrab],
    );

    const columns = useMemo<ColumnDef<InteractiveSearchCandidate>[]>(
        () => [
            {
                id: 'title',
                accessorFn: (row) => row.release.title,
                header: 'Title',
                cell: ({ row }) => (
                    <button
                        type="button"
                        className="interactive-search__title"
                        title={row.original.release.title}
                        onClick={() => setDetailsCandidate(row.original)}
                        data-action="release-details"
                        data-release-id={row.original.release.id}
                    >
                        {row.original.release.title}
                    </button>
                ),
            },
            {
                id: 'indexer',
                accessorFn: (row) => row.indexerName ?? '',
                header: 'Indexer',
                size: 130,
                cell: ({ row }) =>
                    row.original.indexerName ? (
                        <span>{row.original.indexerName}</span>
                    ) : (
                        <span className="interactive-search__empty-cell">-</span>
                    ),
            },
            {
                id: 'size',
                accessorFn: (row) => row.release.size ?? 0,
                header: 'Size',
                size: 80,
                cell: ({ row }) => {
                    const size = row.original.release.size;
                    return size && size > 0 ? (
                        <span>{formatFileSize(size)}</span>
                    ) : (
                        <span className="interactive-search__empty-cell">-</span>
                    );
                },
            },
            {
                id: 'match',
                accessorFn: (row) =>
                    ({
                        NoMatch: 0,
                        Indeterminate: 1,
                        Confident: 2,
                        Identity: 3,
                    })[row.matchResult],
                header: 'Match',
                size: 120,
                cell: ({ row }) => <MatchCell candidate={row.original} />,
            },
            {
                id: 'status',
                accessorFn: (row) => row.acquisitionStatus ?? '',
                header: 'Status',
                meta: { mobileRole: 'header' },
                size: 110,
                cell: ({ row }) => {
                    const status = row.original.acquisitionStatus;
                    return status ? (
                        <ReleaseStatusBadge status={status} failureReason={row.original.failureReason} />
                    ) : usesCardLayout ? null : (
                        <span className="interactive-search__empty-cell">-</span>
                    );
                },
            },
            {
                id: 'publishDate',
                accessorFn: (row) => row.release.publishDate,
                header: 'Published',
                size: 100,
                cell: ({ row }) => formatDate(row.original.release.publishDate),
            },
            {
                id: 'action',
                header: '',
                enableSorting: false,
                size: 90,
                cell: ({ row }) => {
                    const releaseId = row.original.release.id;
                    if (succeededReleaseIds.has(releaseId)) {
                        return (
                            <Badge variant="queued" size="sm" dot data-acquisition-status="Queued">
                                Queued
                            </Badge>
                        );
                    }

                    const isGrabbing = pendingReleaseIds.has(releaseId);
                    return canGrab ? (
                        <Button
                            variant="secondary"
                            size="sm"
                            onClick={(event) => {
                                event.stopPropagation();
                                handleGrab(row.original);
                            }}
                            disabled={isGrabbing}
                            aria-busy={isGrabbing || undefined}
                            data-action="grab"
                            data-release-id={releaseId}
                        >
                            {isGrabbing ? (
                                <>
                                    <CircleNotchIcon weight="bold" className="animate-spin" aria-hidden="true" />
                                    Grabbing...
                                </>
                            ) : (
                                <>
                                    <DownloadSimpleIcon weight="bold" aria-hidden="true" />
                                    Grab
                                </>
                            )}
                        </Button>
                    ) : null;
                },
            },
        ],
        [canGrab, handleGrab, pendingReleaseIds, succeededReleaseIds, usesCardLayout],
    );

    const getRowId = useCallback((row: InteractiveSearchCandidate) => row.release.id, []);

    return (
        <div
            className="interactive-search"
            data-page="interactive-search"
            data-layout={usesCardLayout ? 'card' : 'table'}
        >
            <div className="interactive-search__nav">
                <Button variant="ghost" size="sm" asChild>
                    <Link to={detailPath}>
                        <ArrowLeftIcon weight="bold" aria-hidden="true" />
                        Back to item
                    </Link>
                </Button>
            </div>

            {detailQuery.data ? (
                <ContentDetailsHeader content={detailQuery.data} />
            ) : detailQuery.isLoading ? (
                <Skeleton className="interactive-search__header-skeleton" />
            ) : null}

            <section className="interactive-search__results" aria-label="Candidate releases">
                <div className="interactive-search__results-head">
                    <h2 className="interactive-search__results-title">
                        <MagnifyingGlassIcon weight="bold" aria-hidden="true" />
                        Candidate releases
                    </h2>
                    <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => refetch()}
                        loading={isFetching}
                        disabled={isFetching || !contentHierarchyItemId}
                        data-action="search-again"
                    >
                        Search again
                    </Button>
                </div>

                {!isLoading && !error && partial && (
                    <Alert variant="warning">
                        {warning ?? 'The search is incomplete. Some providers did not return results.'}
                        {retryAt && <> Retry after {new Date(retryAt).toLocaleString()}.</>}
                    </Alert>
                )}

                {isLoading ? (
                    <>
                        <div
                            className="interactive-search__searching"
                            role="status"
                            aria-live="polite"
                            data-testid="interactive-search-progress"
                        >
                            <CircleNotchIcon weight="bold" className="animate-spin" aria-hidden="true" />
                            <span>Searching indexers...</span>
                        </div>
                        <div className="interactive-search__loading">
                            <Skeleton />
                            <Skeleton />
                            <Skeleton />
                        </div>
                    </>
                ) : error ? (
                    <EmptyState
                        icon={<WarningCircleIcon weight="thin" aria-hidden="true" />}
                        title="Search failed"
                        description={getErrorMessage(error, 'Failed to search indexers for this item.')}
                        action={
                            <Button variant="secondary" size="sm" onClick={() => refetch()}>
                                Retry
                            </Button>
                        }
                        size="sm"
                    />
                ) : candidates.length === 0 && configuredIndexerCount === 0 ? (
                    <EmptyState
                        icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                        title="No indexers configured"
                        description={
                            hasRole(Role.Admin)
                                ? 'Magpie has no indexer connection to search. Add one, then search again.'
                                : 'Magpie has no indexer connection to search. Ask an administrator to add one.'
                        }
                        action={
                            hasRole(Role.Admin) ? (
                                <Button asChild variant="secondary" size="sm">
                                    <Link to="/admin/indexers">Configure indexers</Link>
                                </Button>
                            ) : undefined
                        }
                        size="sm"
                    />
                ) : candidates.length === 0 ? (
                    <EmptyState
                        icon={<MagnifyingGlassIcon weight="thin" aria-hidden="true" />}
                        title={partial ? 'Search incomplete' : 'No releases found'}
                        description={
                            partial
                                ? 'Some providers could not complete the search. Search again after they recover.'
                                : "No indexer found a release for this item. Try again later or review and correct the item's metadata."
                        }
                        size="sm"
                    />
                ) : (
                    <DataTable<InteractiveSearchCandidate>
                        className="interactive-search__table"
                        columns={columns}
                        data={candidates}
                        size="sm"
                        enableSorting={true}
                        enablePagination={candidates.length > 25}
                        pageSize={25}
                        getRowId={getRowId}
                    />
                )}
            </section>

            <DeleteConfirmDialog
                open={pendingGrab !== null}
                title="Grab anyway?"
                itemType="release"
                itemName={pendingGrab?.release.title ?? ''}
                description={
                    pendingGrab ? `Magpie classified this release as ${pendingGrab.matchResult}. Grab it anyway?` : ''
                }
                confirmVariant="warning"
                confirmLabel="Grab anyway"
                confirmIcon={<DownloadSimpleIcon weight="bold" aria-hidden="true" />}
                onConfirm={() => {
                    if (pendingGrab) {
                        performGrab(pendingGrab);
                    }
                    setPendingGrab(null);
                }}
                onCancel={() => setPendingGrab(null)}
            />
            <ReleaseDetailsDialog
                release={
                    detailsCandidate
                        ? {
                              ...detailsCandidate.release,
                              indexerName: detailsCandidate.indexerName,
                          }
                        : null
                }
                open={detailsCandidate !== null}
                onOpenChange={(open) => {
                    if (!open) {
                        setDetailsCandidate(null);
                    }
                }}
            />
        </div>
    );
}

InteractiveSearchPage.displayName = 'InteractiveSearchPage';

export default InteractiveSearchPage;
