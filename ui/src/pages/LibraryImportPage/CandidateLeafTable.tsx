import {
    createContext,
    useCallback,
    useContext,
    useLayoutEffect,
    useMemo,
    useRef,
    useState,
    type ReactNode,
} from 'react';
import {
    ArrowClockwiseIcon,
    ArrowCounterClockwiseIcon,
    CaretLeftIcon,
    CaretRightIcon,
    CheckIcon,
    DotsThreeVerticalIcon,
    EyeIcon,
    InfoIcon,
    LinkSimpleIcon,
    ProhibitIcon,
} from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { DetailsPanel } from '@/components/ui/DetailsPanel';
import {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
} from '@/components/ui/DropdownMenu';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/Dialog';
import { PreviewDrawer, type PreviewDrawerTarget } from '@/components/content/PreviewDrawer';
import { useToast, type ToastOptions } from '@/components/ui/Toast';

import { useMenuAction } from '@/hooks/ui/useMenuAction';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useBulkAcceptLibraryImportCandidates } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import type { LibraryImportCandidate } from '@/hooks/useLibraryImportCandidates';
import {
    useRejectLibraryImportCandidates,
    useRestoreLibraryImportCandidates,
} from '@/hooks/usePendingFileAssociations';
import { useInstanceName } from '@/hooks/useInstanceName';
import { returnFocusOnClose } from '@/lib/returnFocus';

import { fileName, STATUS_BADGE_VARIANT, statusLabel } from './importDisplay';
import { MatchCell } from '@/components/content/MatchCell';
import { ManualMatchDialog, type ManualMatchTarget } from './ManualMatchDialog';
import type { ImportAcceptAction, ImportRenameSelection } from './AcceptImportDialog';

import '@/styles/bulk-actions-toolbar.css';
import './CandidateLeafTable.css';

const PAGE_SIZE = 25;

function isMatched(candidate: LibraryImportCandidate): boolean {
    return candidate.matches.length === 1;
}

function isClaimable(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'Pending' || candidate.status === 'Failed';
}

function isRejected(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'Rejected';
}

function canReject(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'Pending' || candidate.status === 'Failed' || candidate.status === 'MatchingPending';
}

function isSubmittable(candidate: LibraryImportCandidate): boolean {
    return isMatched(candidate) && isClaimable(candidate);
}

function importLabel(candidate: LibraryImportCandidate): string {
    return candidate.status === 'Failed' ? 'Retry' : 'Import';
}

function ImportIcon({ candidate }: { candidate: LibraryImportCandidate }) {
    return candidate.status === 'Failed' ? (
        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />
    ) : (
        <CheckIcon weight="bold" aria-hidden="true" />
    );
}

function CandidateMatch({ candidate }: { candidate: LibraryImportCandidate }) {
    const match = candidate.matches[0];
    return match ? (
        <MatchCell match={{ title: match.title, subtitle: match.subtitle, providerType: match.providerType }} />
    ) : (
        <MatchCell match={null} emptyLabel="No match" testId="candidate-leaf-no-match" />
    );
}

type ReportOutcome = (outcome: ToastOptions) => void;

interface CandidateActionHandlers {
    acting: (candidate: LibraryImportCandidate) => boolean;
    onDetails: (candidate: LibraryImportCandidate) => void;
    onPreview: (candidate: LibraryImportCandidate) => void;
    onMatch: (candidate: LibraryImportCandidate) => void;
    onImport: (candidate: LibraryImportCandidate, report?: ReportOutcome) => void;
    onReject: (candidate: LibraryImportCandidate, report?: ReportOutcome) => void;
    onRestore: (candidate: LibraryImportCandidate, report?: ReportOutcome) => void;
}

const CandidateActionsContext = createContext<CandidateActionHandlers | null>(null);

function CandidateActionsMenu({ candidate }: { candidate: LibraryImportCandidate }) {
    const handlers = useContext(CandidateActionsContext)!;
    const { triggerRef, schedule, onCloseAutoFocus } = useMenuAction();
    const acting = handlers.acting(candidate);
    const name = fileName(candidate.filePath);
    return (
        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <Button
                    ref={triggerRef}
                    variant="ghost"
                    size="sm"
                    className="candidate-leaf-table__menu-trigger"
                    aria-label={`Actions for ${name}`}
                    data-action="file-menu"
                    data-candidate-id={candidate.id}
                >
                    <DotsThreeVerticalIcon weight="bold" aria-hidden="true" />
                </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" onCloseAutoFocus={onCloseAutoFocus}>
                <DropdownMenuItem onSelect={schedule(() => handlers.onDetails(candidate))} data-action="view-details">
                    <InfoIcon weight="bold" aria-hidden="true" />
                    View details
                </DropdownMenuItem>
                {isMatched(candidate) && (
                    <DropdownMenuItem onSelect={schedule(() => handlers.onPreview(candidate))} data-action="preview">
                        <EyeIcon weight="bold" aria-hidden="true" />
                        Preview
                    </DropdownMenuItem>
                )}
                {!isMatched(candidate) && !isRejected(candidate) && (
                    <DropdownMenuItem onSelect={schedule(() => handlers.onMatch(candidate))} data-action="match">
                        <LinkSimpleIcon weight="bold" aria-hidden="true" />
                        Match
                    </DropdownMenuItem>
                )}
                {isSubmittable(candidate) && (
                    <DropdownMenuItem
                        onSelect={schedule(() => handlers.onImport(candidate))}
                        disabled={acting}
                        data-action="import"
                    >
                        <ImportIcon candidate={candidate} />
                        {importLabel(candidate)}
                    </DropdownMenuItem>
                )}
                {(canReject(candidate) || isRejected(candidate)) && <DropdownMenuSeparator />}
                {canReject(candidate) && (
                    <DropdownMenuItem
                        onSelect={() => handlers.onReject(candidate)}
                        disabled={acting}
                        data-action="reject"
                    >
                        <ProhibitIcon weight="bold" aria-hidden="true" />
                        Reject
                    </DropdownMenuItem>
                )}
                {isRejected(candidate) && (
                    <DropdownMenuItem
                        onSelect={() => handlers.onRestore(candidate)}
                        disabled={acting}
                        data-action="restore"
                    >
                        <ArrowCounterClockwiseIcon weight="bold" aria-hidden="true" />
                        Restore
                    </DropdownMenuItem>
                )}
            </DropdownMenuContent>
        </DropdownMenu>
    );
}

function CandidateDetailsDialog({
    items,
    candidateId,
    onCandidateChange,
    handlers,
}: {
    items: LibraryImportCandidate[];
    candidateId: string | null;
    onCandidateChange: (id: string | null) => void;
    handlers: CandidateActionHandlers;
}) {
    const { toast } = useToast();
    const openerRef = useRef<HTMLElement | null>(null);
    const shownIdRef = useRef<string | null>(null);
    const [failure, setFailure] = useState<{ candidateId: string; outcome: ToastOptions } | null>(null);
    const isOpen = candidateId !== null;
    useLayoutEffect(() => {
        if (isOpen) {
            openerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
            setFailure(null);
        }
    }, [isOpen]);
    useLayoutEffect(() => {
        shownIdRef.current = candidateId;
    }, [candidateId]);
    const focusFailure = useCallback((alert: HTMLDivElement | null) => alert?.focus(), []);

    const index = items.findIndex((candidate) => candidate.id === candidateId);
    const candidate = items[index];
    if (!candidate) {
        return null;
    }
    const acting = handlers.acting(candidate);
    const previous = items[index - 1];
    const next = items[index + 1];
    const show = (id: string | null) => {
        setFailure(null);
        onCandidateChange(id);
    };
    const run = (action: (candidate: LibraryImportCandidate, report: ReportOutcome) => void) => () => {
        setFailure(null);
        action(candidate, (outcome) => {
            if (outcome.variant === 'success') return;
            if (shownIdRef.current === candidate.id) setFailure({ candidateId: candidate.id, outcome });
            else toast(outcome);
        });
    };

    return (
        <Dialog open onOpenChange={(open) => !open && show(null)}>
            <DialogContent
                aria-describedby={undefined}
                onCloseAutoFocus={returnFocusOnClose(openerRef)}
                data-component="candidate-details-dialog"
                data-candidate-id={candidate.id}
            >
                <DialogHeader>
                    <DialogTitle>{fileName(candidate.filePath)}</DialogTitle>
                    <Badge
                        size="sm"
                        variant={STATUS_BADGE_VARIANT[candidate.status]}
                        className="candidate-details-dialog__status"
                        aria-live="polite"
                    >
                        {statusLabel(candidate.status)}
                    </Badge>
                </DialogHeader>
                <DialogBody className="candidate-details-dialog__body">
                    {candidate.notes && (
                        <Alert variant={candidate.status === 'Failed' ? 'error' : 'info'}>{candidate.notes}</Alert>
                    )}
                    <DetailsPanel
                        rows={[
                            { key: 'match', label: 'Matched content', value: <CandidateMatch candidate={candidate} /> },
                            { key: 'path', label: 'Path', value: candidate.filePath, mono: true, multiline: true },
                        ]}
                    />
                    <div className="candidate-details-dialog__match-actions">
                        {isMatched(candidate) ? (
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => handlers.onPreview(candidate)}
                                data-action="preview"
                            >
                                <EyeIcon weight="bold" aria-hidden="true" />
                                Preview
                            </Button>
                        ) : (
                            !isRejected(candidate) && (
                                <Button
                                    variant="secondary"
                                    size="sm"
                                    onClick={() => handlers.onMatch(candidate)}
                                    data-action="match"
                                >
                                    <LinkSimpleIcon weight="bold" aria-hidden="true" />
                                    Match
                                </Button>
                            )
                        )}
                    </div>
                    <div className="candidate-details-dialog__pager">
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => previous && show(previous.id)}
                            aria-disabled={!previous}
                            tabIndex={previous ? undefined : -1}
                            aria-label="Previous file"
                            data-action="previous-file"
                        >
                            <CaretLeftIcon weight="bold" aria-hidden="true" />
                        </Button>
                        <span aria-live="polite">
                            {index + 1} of {items.length}
                        </span>
                        <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => next && show(next.id)}
                            aria-disabled={!next}
                            tabIndex={next ? undefined : -1}
                            aria-label="Next file"
                            data-action="next-file"
                        >
                            <CaretRightIcon weight="bold" aria-hidden="true" />
                        </Button>
                    </div>
                </DialogBody>
                {failure?.candidateId === candidate.id && (
                    <Alert
                        ref={focusFailure}
                        variant="error"
                        title={failure.outcome.title}
                        className="candidate-details-dialog__failure"
                        tabIndex={-1}
                    >
                        {failure.outcome.description}
                    </Alert>
                )}
                {(canReject(candidate) || isRejected(candidate) || isSubmittable(candidate)) && (
                    <DialogFooter>
                        {canReject(candidate) && (
                            <Button
                                variant="secondary"
                                onClick={run(handlers.onReject)}
                                disabled={acting}
                                data-action="reject"
                            >
                                <ProhibitIcon weight="bold" aria-hidden="true" />
                                Reject
                            </Button>
                        )}
                        {isRejected(candidate) && (
                            <Button
                                variant="secondary"
                                onClick={run(handlers.onRestore)}
                                loading={acting}
                                disabled={acting}
                                data-action="restore"
                            >
                                <ArrowCounterClockwiseIcon weight="bold" aria-hidden="true" />
                                Restore
                            </Button>
                        )}
                        {isSubmittable(candidate) && (
                            <Button
                                variant="primary"
                                onClick={run(handlers.onImport)}
                                loading={acting}
                                disabled={acting}
                                data-action="import"
                            >
                                <ImportIcon candidate={candidate} />
                                {importLabel(candidate)}
                            </Button>
                        )}
                    </DialogFooter>
                )}
            </DialogContent>
        </Dialog>
    );
}

interface CandidateLeafTableProps {
    items: LibraryImportCandidate[];

    emptyState: ReactNode;

    requestAccept: (action: ImportAcceptAction, renameSelection?: ImportRenameSelection) => void;

    /** The counts of the files of the table. */
    scopeCounts?: ReactNode;

    /** The action on every file of the table. */
    scopeAction?: ReactNode;
}

export function CandidateLeafTable({
    items,
    emptyState,
    requestAccept,
    scopeCounts,
    scopeAction,
}: CandidateLeafTableProps) {
    const instanceName = useInstanceName();
    const { toast } = useToast();

    const [actingIds, setActingIds] = useState<Set<string>>(() => new Set());
    const [isBulkImporting, setIsBulkImporting] = useState(false);
    const [isBulkRejecting, setIsBulkRejecting] = useState(false);
    const [manualMatchTarget, setManualMatchTarget] = useState<ManualMatchTarget | null>(null);
    const [previewTarget, setPreviewTarget] = useState<PreviewDrawerTarget | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);
    const [detailsId, setDetailsId] = useState<string | null>(null);
    const openDetailsId = items.some((candidate) => candidate.id === detailsId) ? detailsId : null;

    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const submittableIds = useMemo(() => items.filter(isSubmittable).map((candidate) => candidate.id), [items]);
    const submittablePageRowIds = useMemo(
        () => pageRowIds.filter((id) => submittableIds.includes(id)),
        [pageRowIds, submittableIds],
    );
    const selection = useBulkSelection({ pageRowIds: submittablePageRowIds, totalCount: submittableIds.length });
    const selectedIds = useMemo(
        () => submittableIds.filter((id) => selection.isRowChecked(id)),
        [submittableIds, selection],
    );
    const hasSelection = selectedIds.length > 0;

    const { mutateAsync: acceptCandidates } = useBulkAcceptLibraryImportCandidates();
    const { mutateAsync: restoreCandidates } = useRestoreLibraryImportCandidates();
    const { mutateAsync: rejectCandidates } = useRejectLibraryImportCandidates();

    const markActing = useCallback((ids: string[], acting: boolean) => {
        setActingIds((prev) => {
            const next = new Set(prev);
            for (const id of ids) {
                if (acting) {
                    next.add(id);
                } else {
                    next.delete(id);
                }
            }
            return next;
        });
    }, []);

    const handleTransition = useCallback(
        async (candidate: LibraryImportCandidate, reject: boolean, report: ReportOutcome = toast) => {
            markActing([candidate.id], true);
            try {
                const result = await (reject ? rejectCandidates : restoreCandidates)({ ids: [candidate.id] });
                report(
                    result.count === 0
                        ? {
                              title: reject ? 'Match not rejected' : 'File not restored',
                              description: 'The file changed state. Refresh the list and try again.',
                              variant: 'warning',
                          }
                        : reject
                          ? {
                                title: 'Match rejected',
                                description: 'Rescans keep this file unmatched until you restore it.',
                                variant: 'success',
                            }
                          : {
                                title: 'File restored',
                                description: `${instanceName} will match the file again.`,
                                variant: 'success',
                            },
                );
            } catch {
                report({
                    title: reject ? 'Reject failed' : 'Restore failed',
                    description: `Failed to ${reject ? 'reject' : 'restore'} the file. Please try again.`,
                    variant: 'error',
                });
            } finally {
                markActing([candidate.id], false);
            }
        },
        [markActing, rejectCandidates, restoreCandidates, toast, instanceName],
    );

    const handlePreview = useCallback((candidate: LibraryImportCandidate) => {
        const match = candidate.matches[0];
        if (!match) {
            return;
        }
        setPreviewTarget({
            providerType: match.providerType,
            externalIdKey: match.externalIdKey,
            externalId: match.externalId,
            contentType: candidate.contentType,
            levelSlug: match.levelSlug ?? undefined,
            fallbackTitle: match.title,
            fallbackYear: match.subtitle ?? undefined,
        });
        setPreviewOpen(true);
    }, []);

    const handleImport = useCallback(
        (candidate: LibraryImportCandidate, report: ReportOutcome = toast) =>
            requestAccept(
                async (monitoringMode) => {
                    markActing([candidate.id], true);
                    try {
                        const result = await acceptCandidates({ ids: [candidate.id], monitoringMode });
                        const skipped = result.skipped[0];
                        report(
                            result.queued.length > 0
                                ? {
                                      title: 'Import queued',
                                      description: `${instanceName} queued the file for import.`,
                                      variant: 'success',
                                  }
                                : {
                                      title: 'Import skipped',
                                      description:
                                          skipped?.reason ?? `${instanceName} did not queue the file for import.`,
                                      variant: 'warning',
                                  },
                        );
                    } catch {
                        report({
                            title: 'Import failed',
                            description: 'Failed to queue the file for import. Please try again.',
                            variant: 'error',
                        });
                    } finally {
                        markActing([candidate.id], false);
                    }
                },
                { ids: [candidate.id] },
            ),
        [acceptCandidates, markActing, requestAccept, toast, instanceName],
    );

    const handleBulkAccept = useCallback(() => {
        if (selectedIds.length === 0) {
            return;
        }
        requestAccept(
            async (monitoringMode) => {
                setIsBulkImporting(true);
                try {
                    const result = await acceptCandidates({ ids: selectedIds, monitoringMode });
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
                    selection.clear();
                } catch {
                    toast({
                        title: 'Import failed',
                        description: 'Failed to queue the selected files for import. Please try again.',
                        variant: 'error',
                    });
                } finally {
                    setIsBulkImporting(false);
                }
            },
            { ids: selectedIds },
        );
    }, [selectedIds, selection, acceptCandidates, requestAccept, toast]);

    const handleBulkReject = useCallback(async () => {
        const ids = selectedIds;
        if (ids.length === 0) {
            return;
        }
        setIsBulkRejecting(true);
        markActing(ids, true);
        try {
            const result = await rejectCandidates({ ids });
            toast(
                result.count === 0
                    ? {
                          title: 'Matches not rejected',
                          description: 'The files changed state. Refresh the list and try again.',
                          variant: 'warning',
                      }
                    : {
                          title: 'Matches rejected',
                          description: `${result.count} of ${ids.length} files rejected. Rescans keep them unmatched until you restore them.`,
                          variant: result.count < ids.length ? 'warning' : 'success',
                      },
            );
            if (result.count > 0) {
                selection.clear();
            }
        } catch {
            toast({
                title: 'Reject failed',
                description: 'Failed to reject the selected files. Please try again.',
                variant: 'error',
            });
        } finally {
            markActing(ids, false);
            setIsBulkRejecting(false);
        }
    }, [selectedIds, selection, markActing, rejectCandidates, toast]);

    const handlers: CandidateActionHandlers = useMemo(
        () => ({
            acting: (candidate) => actingIds.has(candidate.id),
            onDetails: (candidate) => setDetailsId(candidate.id),
            onPreview: handlePreview,
            onMatch: (candidate) => setManualMatchTarget({ kind: 'candidate', candidate }),
            onImport: handleImport,
            onReject: (candidate, report) => void handleTransition(candidate, true, report),
            onRestore: (candidate, report) => void handleTransition(candidate, false, report),
        }),
        [actingIds, handleImport, handlePreview, handleTransition],
    );

    const getRowId = useCallback((candidate: LibraryImportCandidate) => candidate.id, []);

    const fileColumns: ColumnDef<LibraryImportCandidate>[] = useMemo(
        () => [
            {
                id: 'filename',
                header: 'Filename',
                meta: { mobileRole: 'header' },
                enableSorting: false,
                cell: ({ row }) => {
                    const candidate = row.original;
                    return (
                        <div
                            className={
                                isRejected(candidate)
                                    ? 'candidate-leaf-table__file-cell candidate-leaf-table__file-cell--silenced'
                                    : 'candidate-leaf-table__file-cell'
                            }
                            data-candidate-id={candidate.id}
                        >
                            <button
                                type="button"
                                className="candidate-leaf-table__file-name"
                                onClick={() => setDetailsId(candidate.id)}
                                data-action="file-details"
                            >
                                {fileName(candidate.filePath)}
                            </button>
                        </div>
                    );
                },
            },
            {
                id: 'match',
                header: 'Matched content',
                enableSorting: false,
                cell: ({ row }) => <CandidateMatch candidate={row.original} />,
            },
            {
                id: 'status',
                header: 'Status',
                size: 140,
                enableSorting: false,
                cell: ({ row }) => (
                    <button
                        type="button"
                        className="candidate-leaf-table__status"
                        onClick={() => setDetailsId(row.original.id)}
                        aria-label={`${statusLabel(row.original.status)}: ${fileName(row.original.filePath)}. View details`}
                        data-action="status-details"
                    >
                        <Badge size="sm" variant={STATUS_BADGE_VARIANT[row.original.status]}>
                            {statusLabel(row.original.status)}
                        </Badge>
                    </button>
                ),
            },
            {
                id: 'actions',
                header: '',
                size: 56,
                meta: { mobileRole: 'header' },
                enableSorting: false,
                cell: ({ row }) => <CandidateActionsMenu candidate={row.original} />,
            },
        ],
        [],
    );
    const columns: ColumnDef<LibraryImportCandidate>[] = useMemo(
        () => [
            selectColumn<LibraryImportCandidate>({
                selection,
                getId: (candidate) => candidate.id,
                headerLabel: 'Select all matched files',
                rowLabel: (candidate) => fileName(candidate.filePath),
                canSelect: isSubmittable,
            }),
            ...fileColumns,
        ],
        [fileColumns, selection],
    );

    const selectedActions = (
        <>
            <Button
                variant="secondary"
                size="sm"
                onClick={() => void handleBulkReject()}
                loading={isBulkRejecting}
                disabled={!hasSelection || isBulkImporting}
                aria-label="Reject selected"
                data-action="bulk-reject"
            >
                <ProhibitIcon weight="bold" aria-hidden="true" />
                <span className="bulk-actions-toolbar__btn-label">Reject selected</span>
            </Button>
            <Button
                variant="primary"
                size="sm"
                onClick={handleBulkAccept}
                loading={isBulkImporting}
                disabled={!hasSelection || isBulkRejecting}
                aria-label="Import selected"
                data-action="bulk-accept"
            >
                <CheckIcon weight="bold" aria-hidden="true" />
                <span className="bulk-actions-toolbar__btn-label">Import selected</span>
            </Button>
        </>
    );

    return (
        <div className="candidate-leaf-table" data-component="candidate-leaf-table">
            <div className="candidate-leaf-table__toolbar">
                {scopeCounts}
                {!hasSelection && scopeAction}
                <div className="candidate-leaf-table__mobile-select-toggle">
                    <SelectModeToggle
                        active={selection.selectionMode}
                        onToggle={selection.toggleSelectionMode}
                        mobileOnly={false}
                    />
                </div>
            </div>

            <BulkActionsToolbar
                className="candidate-leaf-table__desktop-bulk-toolbar"
                selection={selection}
                label="File selection actions"
                itemNounPlural="files"
                displaySelectedCount={selectedIds.length}
            >
                {selectedActions}
            </BulkActionsToolbar>

            <CandidateActionsContext value={handlers}>
                <DataTable<LibraryImportCandidate>
                    columns={columns}
                    data={items}
                    size="sm"
                    enableSorting={false}
                    rowSelection={selection.rowSelection}
                    getRowId={getRowId}
                    onPageRowIdsChange={setPageRowIds}
                    enablePagination
                    pageSize={PAGE_SIZE}
                    emptyState={emptyState}
                    mobileVariant="compact"
                    mobileSelectionActive={selection.selectionMode}
                    mobileSelectionOverlay={{
                        isSelected: (candidate) => selection.isRowChecked(candidate.id),
                        onToggle: (candidate) =>
                            selection.toggleRow(candidate.id, !selection.isRowChecked(candidate.id)),
                        canSelect: (candidate) => isSubmittable(candidate),
                        rowLabel: (candidate) => fileName(candidate.filePath),
                    }}
                    data-table="candidate-leaf"
                    aria-label="Files in this group"
                />
            </CandidateActionsContext>

            <CandidateDetailsDialog
                items={items}
                candidateId={openDetailsId}
                onCandidateChange={setDetailsId}
                handlers={handlers}
            />

            <ManualMatchDialog
                target={manualMatchTarget}
                onClose={() => setManualMatchTarget(null)}
                onMatched={() => selection.clear()}
                suppressSuccessToast={openDetailsId !== null}
            />

            <PreviewDrawer open={previewOpen} onOpenChange={setPreviewOpen} target={previewTarget} />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="candidate-leaf-table-mobile-selection-bar"
                    selection={selection}
                    displaySelectedCount={selectedIds.length}
                    onClear={selection.clear}
                    itemNounPlural="files"
                    actionsClassName="candidate-leaf-table__selection-actions"
                >
                    {selectedActions}
                </MobileSelectionBar>
            )}
        </div>
    );
}

CandidateLeafTable.displayName = 'CandidateLeafTable';

export default CandidateLeafTable;
