import { useCallback, useMemo, useState } from 'react';
import { CheckIcon, FileMagnifyingGlassIcon, ProhibitIcon } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { Button } from '@/components/ui/Button';
import { DataTable } from '@/components/ui/DataTable';
import { useToast } from '@/components/ui/Toast';
import type { LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';
import {
    useAssociatePendingFiles,
    usePendingFileAssociations,
    useRejectLibraryImportCandidates,
} from '@/hooks/usePendingFileAssociations';
import { useStartRootFolderContentDiscovery } from '@/hooks/useStartRootFolderContentDiscovery';
import { MatchCell } from '@/components/content/MatchCell';

import './PendingFileAssociations.css';

function isInFlight(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'Queued' || candidate.status === 'Importing';
}

interface PendingFilesIndicatorProps {
    contentHierarchyItemId: string | undefined;

    onOpen: () => void;
}

export function PendingFilesIndicator({ contentHierarchyItemId, onOpen }: PendingFilesIndicatorProps) {
    const { data } = usePendingFileAssociations(contentHierarchyItemId);
    const count = data?.filter((candidate) => !isInFlight(candidate)).length ?? 0;
    if (count === 0) {
        return null;
    }

    return (
        <Button
            variant="secondary"
            size="sm"
            className="pending-files-indicator"
            onClick={onOpen}
            data-component="pending-files-indicator"
        >
            <FileMagnifyingGlassIcon weight="bold" aria-hidden="true" />
            {count} pending file{count === 1 ? '' : 's'}
        </Button>
    );
}

interface PendingFileAssociationsProps {
    contentHierarchyItemId: string;

    rootFolderId: string | null | undefined;

    title: string;

    canManage: boolean;
}

export function PendingFileAssociations({
    contentHierarchyItemId,
    rootFolderId,
    title,
    canManage,
}: PendingFileAssociationsProps) {
    const { toast } = useToast();
    const { data, isSuccess } = usePendingFileAssociations(contentHierarchyItemId);
    const items = useMemo(() => data ?? [], [data]);
    const [actingIds, setActingIds] = useState<Set<string>>(() => new Set());
    const { mutateAsync: associateFiles } = useAssociatePendingFiles();
    const { mutateAsync: rejectCandidates } = useRejectLibraryImportCandidates();
    const discoveryMutation = useStartRootFolderContentDiscovery();
    const acceptableIds = useMemo(
        () => items.filter((candidate) => !isInFlight(candidate)).map((candidate) => candidate.id),
        [items],
    );

    const act = useCallback(async (ids: string[], action: () => Promise<void>) => {
        setActingIds((prev) => new Set([...prev, ...ids]));
        try {
            await action();
        } finally {
            setActingIds((prev) => new Set([...prev].filter((id) => !ids.includes(id))));
        }
    }, []);

    const handleAccept = useCallback(
        (ids: string[]) =>
            act(ids, async () => {
                try {
                    const result = await associateFiles({ contentHierarchyItemId, ids });
                    const skipped = result.skipped.length;
                    toast({
                        title: 'Association queued',
                        description:
                            `${result.queued.length} file${result.queued.length === 1 ? '' : 's'} queued` +
                            (skipped > 0 ? `, ${skipped} skipped` : '') +
                            '. Files stay where they are.',
                        variant: skipped > 0 ? 'warning' : 'success',
                    });
                } catch {
                    toast({
                        title: 'Association failed',
                        description: 'Failed to queue the association. Please try again.',
                        variant: 'error',
                    });
                }
            }),
        [act, associateFiles, contentHierarchyItemId, toast],
    );

    const handleReject = useCallback(
        (candidate: LibraryImportCandidate) =>
            act([candidate.id], async () => {
                try {
                    const result = await rejectCandidates({ ids: [candidate.id] });
                    toast(
                        result.count === 0
                            ? {
                                  title: 'Match not rejected',
                                  description: 'The file changed state. Refresh the list and try again.',
                                  variant: 'warning',
                              }
                            : {
                                  title: 'Match rejected',
                                  description: 'The file stays silenced on the import page until you restore it.',
                                  variant: 'success',
                              },
                    );
                } catch {
                    toast({
                        title: 'Reject failed',
                        description: 'Failed to reject the match. Please try again.',
                        variant: 'error',
                    });
                }
            }),
        [act, rejectCandidates, toast],
    );

    const handleDiscover = useCallback(() => {
        if (!rootFolderId) {
            return;
        }
        discoveryMutation.mutate(
            { rootFolderId, contentHierarchyItemId, rootFolderName: title },
            {
                onSuccess: () =>
                    toast({
                        title: 'Scan queued',
                        description: `Magpie is looking for missing files of "${title}".`,
                        variant: 'info',
                    }),
                onError: () =>
                    toast({
                        title: 'Scan failed',
                        description: 'Failed to start the scan for missing files. Please try again.',
                        variant: 'error',
                    }),
            },
        );
    }, [contentHierarchyItemId, discoveryMutation, rootFolderId, title, toast]);

    const columns: ColumnDef<LibraryImportCandidate>[] = useMemo(
        () => [
            {
                id: 'file',
                header: 'File',
                meta: { mobileRole: 'header' },
                enableSorting: false,
                cell: ({ row }) => (
                    <span className="pending-file-associations__path" title={row.original.filePath}>
                        {row.original.filePath}
                    </span>
                ),
            },
            {
                id: 'match',
                header: 'Match',
                enableSorting: false,
                cell: ({ row }) => {
                    const match = row.original.matches[0];
                    return match ? (
                        <MatchCell
                            match={{ title: match.title, subtitle: match.subtitle, providerType: match.providerType }}
                        />
                    ) : (
                        <MatchCell match={null} emptyLabel="No match" />
                    );
                },
            },
            {
                id: 'actions',
                header: '',
                size: 220,
                enableSorting: false,
                cell: ({ row }) => {
                    const candidate = row.original;
                    if (isInFlight(candidate)) {
                        return (
                            <div className="pending-file-associations__actions">
                                <Button variant="secondary" size="sm" disabled data-action="associating">
                                    Associating
                                </Button>
                            </div>
                        );
                    }
                    if (!canManage) {
                        return null;
                    }
                    const acting = actingIds.has(candidate.id);
                    return (
                        <div className="pending-file-associations__actions">
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => void handleReject(candidate)}
                                disabled={acting}
                                data-action="reject"
                                data-candidate-id={candidate.id}
                            >
                                <ProhibitIcon weight="bold" aria-hidden="true" />
                                Reject
                            </Button>
                            <Button
                                variant="primary"
                                size="sm"
                                onClick={() => void handleAccept([candidate.id])}
                                loading={acting}
                                disabled={acting}
                                title={candidate.status === 'Failed' ? (candidate.notes ?? undefined) : undefined}
                                data-action="accept"
                                data-candidate-id={candidate.id}
                            >
                                <CheckIcon weight="bold" aria-hidden="true" />
                                {candidate.status === 'Failed' ? 'Retry' : 'Accept'}
                            </Button>
                        </div>
                    );
                },
            },
        ],
        [actingIds, canManage, handleAccept, handleReject],
    );

    const getRowId = useCallback((candidate: LibraryImportCandidate) => candidate.id, []);

    const discoverButton = canManage && (
        <Button
            variant={items.length > 0 ? 'secondary' : 'ghost'}
            size="sm"
            onClick={handleDiscover}
            loading={discoveryMutation.isStarting}
            disabled={!rootFolderId || discoveryMutation.isStarting}
            data-action="discover"
        >
            <FileMagnifyingGlassIcon weight="bold" aria-hidden="true" />
            Find missing files
        </Button>
    );

    // When the query confirms that nothing is pending, the panel is one line: the state and the scan action. While it
    // loads or after an error, the full panel shows, because nothing is known about pending files yet.
    if (isSuccess && items.length === 0) {
        return (
            <section
                className="pending-file-associations pending-file-associations--empty"
                aria-labelledby="pending-file-associations-title"
                data-component="pending-file-associations"
            >
                <h3 id="pending-file-associations-title" className="pending-file-associations__title">
                    No pending files
                </h3>
                {discoverButton}
            </section>
        );
    }

    return (
        <section
            className="pending-file-associations"
            aria-labelledby="pending-file-associations-title"
            data-component="pending-file-associations"
        >
            <div className="pending-file-associations__header">
                <div className="pending-file-associations__heading">
                    <h3 id="pending-file-associations-title" className="pending-file-associations__title">
                        Pending files
                    </h3>
                    <p className="pending-file-associations__help">
                        Discovered files that match this item or its descendants. Accepting links a file where it is. It
                        does not copy, move, or rename it.
                    </p>
                </div>
                {canManage && (
                    <div className="pending-file-associations__actions">
                        {discoverButton}
                        {acceptableIds.length > 1 && (
                            <Button
                                variant="primary"
                                size="sm"
                                onClick={() => void handleAccept(acceptableIds)}
                                disabled={acceptableIds.some((id) => actingIds.has(id))}
                                data-action="accept-all"
                            >
                                <CheckIcon weight="bold" aria-hidden="true" />
                                Accept all ({acceptableIds.length})
                            </Button>
                        )}
                    </div>
                )}
            </div>
            {items.length > 0 && (
                <DataTable<LibraryImportCandidate>
                    columns={columns}
                    data={items}
                    size="sm"
                    enableSorting={false}
                    getRowId={getRowId}
                    data-table="pending-file-associations"
                    aria-label="Pending files"
                />
            )}
        </section>
    );
}

PendingFileAssociations.displayName = 'PendingFileAssociations';
