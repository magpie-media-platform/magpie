import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { CheckIcon, EyeIcon, LinkSimpleIcon } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { PreviewDrawer, type PreviewDrawerTarget } from '@/components/content/PreviewDrawer';
import { useToast } from '@/components/ui/Toast';

import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useBulkAcceptLibraryImportCandidates } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import {
    getParsedSignalNumber,
    getParsedSignalValue,
    PARSED_FILE_SIGNAL_TITLE,
    PARSED_FILE_SIGNAL_YEAR,
    type LibraryImportCandidate,
} from '@/hooks/useLibraryImportCandidates';

import { fileName } from './importDisplay';
import { MatchCell } from './MatchCell';
import { ManualMatchDialog, type ManualMatchTarget } from './ManualMatchDialog';
import type { ImportAcceptAction } from './AcceptImportDialog';

import '@/styles/bulk-actions-toolbar.css';
import './CandidateLeafTable.css';

const PAGE_SIZE = 25;

function isMatched(candidate: LibraryImportCandidate): boolean {
    return candidate.matches.length === 1;
}

function isClaimable(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'Pending' || candidate.status === 'Failed';
}

function isInFlight(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'Queued' || candidate.status === 'Importing';
}

function isMatchingPending(candidate: LibraryImportCandidate): boolean {
    return candidate.status === 'MatchingPending';
}

function isSubmittable(candidate: LibraryImportCandidate): boolean {
    return isMatched(candidate) && isClaimable(candidate);
}

interface CandidateLeafTableProps {
    items: LibraryImportCandidate[];

    emptyState: ReactNode;

    requestAccept: (action: ImportAcceptAction) => void;
}

export function CandidateLeafTable({ items, emptyState, requestAccept }: CandidateLeafTableProps) {
    const { toast } = useToast();

    const [actingIds, setActingIds] = useState<Set<string>>(() => new Set());
    const [isBulkActing, setIsBulkActing] = useState(false);
    const [manualMatchTarget, setManualMatchTarget] = useState<ManualMatchTarget | null>(null);
    const [previewTarget, setPreviewTarget] = useState<PreviewDrawerTarget | null>(null);
    const [previewOpen, setPreviewOpen] = useState(false);

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

    const bulkAcceptMutation = useBulkAcceptLibraryImportCandidates();

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
        (candidate: LibraryImportCandidate) =>
            requestAccept(async (monitoringMode) => {
                setActingIds((prev) => new Set(prev).add(candidate.id));
                try {
                    const result = await bulkAcceptMutation.mutateAsync({ ids: [candidate.id], monitoringMode });
                    const skipped = result.skipped[0];
                    toast(
                        result.queued.length > 0
                            ? {
                                  title: 'Import queued',
                                  description: 'Magpie queued the file for import.',
                                  variant: 'success',
                              }
                            : {
                                  title: 'Import skipped',
                                  description: skipped?.reason ?? 'Magpie did not queue the file for import.',
                                  variant: 'warning',
                              },
                    );
                } catch {
                    toast({
                        title: 'Import failed',
                        description: 'Failed to queue the file for import. Please try again.',
                        variant: 'error',
                    });
                } finally {
                    setActingIds((prev) => {
                        const next = new Set(prev);
                        next.delete(candidate.id);
                        return next;
                    });
                }
            }),
        [bulkAcceptMutation, requestAccept, toast],
    );

    const handleBulkAccept = useCallback(() => {
        if (selectedIds.length === 0) {
            return;
        }
        requestAccept(async (monitoringMode) => {
            setIsBulkActing(true);
            try {
                const result = await bulkAcceptMutation.mutateAsync({ ids: selectedIds, monitoringMode });
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
                    description: 'Failed to queue the selected files for import. Please try again.',
                    variant: 'error',
                });
            } finally {
                setIsBulkActing(false);
            }
        });
    }, [selectedIds, selection, bulkAcceptMutation, requestAccept, toast]);

    const getRowId = useCallback((candidate: LibraryImportCandidate) => candidate.id, []);

    const columns: ColumnDef<LibraryImportCandidate>[] = useMemo(
        () => [
            selectColumn<LibraryImportCandidate>({
                selection,
                getId: (candidate) => candidate.id,
                headerLabel: 'Select all matched files',
                rowLabel: (candidate) => fileName(candidate.filePath),
                canSelect: isSubmittable,
            }),
            {
                id: 'filename',
                header: 'Filename',
                meta: { mobileRole: 'header' },
                enableSorting: false,
                cell: ({ row }) => {
                    const candidate = row.original;
                    const parsedTitle = getParsedSignalValue(candidate.parsedSignals, PARSED_FILE_SIGNAL_TITLE);
                    const parsedYear = getParsedSignalNumber(candidate.parsedSignals, PARSED_FILE_SIGNAL_YEAR);
                    return (
                        <div className="candidate-leaf-table__file-cell" data-candidate-id={candidate.id}>
                            {parsedTitle && (
                                <span className="candidate-leaf-table__parsed-title">
                                    {parsedTitle}
                                    {parsedYear ? (
                                        <span className="candidate-leaf-table__candidate-year"> {parsedYear}</span>
                                    ) : null}
                                </span>
                            )}
                            <span className="candidate-leaf-table__file-path" title={candidate.filePath}>
                                {candidate.filePath}
                            </span>
                        </div>
                    );
                },
            },
            {
                id: 'match',
                header: 'Matched content',
                enableSorting: false,
                cell: ({ row }) => {
                    const candidate = row.original;
                    const match = candidate.matches[0];
                    if (!match) {
                        return isMatchingPending(candidate) ? (
                            <MatchCell
                                match={null}
                                emptyLabel="Matching pending"
                                testId="candidate-leaf-matching-pending"
                            />
                        ) : (
                            <MatchCell
                                match={null}
                                emptyLabel="No match"
                                emptyDetail={candidate.notes}
                                testId="candidate-leaf-no-match"
                            />
                        );
                    }
                    return (
                        <MatchCell
                            match={{ title: match.title, subtitle: match.subtitle, providerType: match.providerType }}
                        />
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
                    if (isMatchingPending(candidate)) {
                        return (
                            <div className="candidate-leaf-table__row-actions">
                                <Button variant="secondary" size="sm" disabled data-action="matching-pending">
                                    Matching pending
                                </Button>
                            </div>
                        );
                    }
                    if (!isMatched(candidate)) {
                        return (
                            <div className="candidate-leaf-table__row-actions">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() => setManualMatchTarget({ kind: 'candidate', candidate })}
                                    data-action="match"
                                    data-candidate-id={candidate.id}
                                >
                                    <LinkSimpleIcon weight="bold" aria-hidden="true" />
                                    Match
                                </Button>
                            </div>
                        );
                    }
                    return (
                        <div className="candidate-leaf-table__row-actions">
                            <Button
                                variant="secondary"
                                size="sm"
                                onClick={() => handlePreview(candidate)}
                                data-action="detail"
                                data-candidate-id={candidate.id}
                            >
                                <EyeIcon weight="bold" aria-hidden="true" />
                                Detail
                            </Button>
                            {isSubmittable(candidate) ? (
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() => void handleImport(candidate)}
                                    loading={actingIds.has(candidate.id)}
                                    disabled={actingIds.has(candidate.id)}
                                    title={candidate.status === 'Failed' ? (candidate.notes ?? undefined) : undefined}
                                    data-action="import"
                                    data-candidate-id={candidate.id}
                                >
                                    <CheckIcon weight="bold" aria-hidden="true" />
                                    {candidate.status === 'Failed' ? 'Retry' : 'Import'}
                                </Button>
                            ) : (
                                <Button variant="secondary" size="sm" disabled data-action="import-status">
                                    {isInFlight(candidate) ? 'Import queued' : candidate.status}
                                </Button>
                            )}
                        </div>
                    );
                },
            },
        ],
        [actingIds, handleImport, handlePreview, selection],
    );

    return (
        <div className="candidate-leaf-table" data-component="candidate-leaf-table">
            <BulkActionsToolbar
                className="candidate-leaf-table__desktop-bulk-toolbar"
                selection={selection}
                label="Candidate selection actions"
                itemNounPlural="candidates"
                displaySelectedCount={selectedIds.length}
            >
                <Button
                    variant="primary"
                    size="sm"
                    onClick={handleBulkAccept}
                    loading={isBulkActing}
                    disabled={!hasSelection}
                    data-action="bulk-accept"
                >
                    <CheckIcon weight="bold" aria-hidden="true" />
                    <span className="bulk-actions-toolbar__btn-label">Import checked</span>
                </Button>
            </BulkActionsToolbar>

            <div className="candidate-leaf-table__mobile-select-toggle">
                <SelectModeToggle
                    active={selection.selectionMode}
                    onToggle={selection.toggleSelectionMode}
                    mobileOnly={false}
                />
            </div>

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
                mobileSelectionActive={selection.selectionMode}
                mobileSelectionOverlay={{
                    isSelected: (candidate) => selection.isRowChecked(candidate.id),
                    onToggle: (candidate) => selection.toggleRow(candidate.id, !selection.isRowChecked(candidate.id)),
                    canSelect: (candidate) => isSubmittable(candidate),
                    rowLabel: (candidate) => fileName(candidate.filePath),
                }}
                data-table="candidate-leaf"
                aria-label="Files in this group"
            />

            <ManualMatchDialog
                target={manualMatchTarget}
                onClose={() => setManualMatchTarget(null)}
                onMatched={() => selection.clear()}
            />

            <PreviewDrawer open={previewOpen} onOpenChange={setPreviewOpen} target={previewTarget} />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="candidate-leaf-table-mobile-selection-bar"
                    selection={selection}
                    displaySelectedCount={selectedIds.length}
                    onClear={selection.clear}
                    itemNounPlural="files"
                >
                    <Button
                        variant="primary"
                        size="sm"
                        onClick={handleBulkAccept}
                        loading={isBulkActing}
                        disabled={!hasSelection}
                        data-action="bulk-accept"
                    >
                        <CheckIcon weight="bold" aria-hidden="true" />
                        Import checked
                    </Button>
                </MobileSelectionBar>
            )}
        </div>
    );
}

CandidateLeafTable.displayName = 'CandidateLeafTable';

export default CandidateLeafTable;
