import { useEffect, useMemo, useRef, useState } from 'react';
import type { SortingState } from '@tanstack/react-table';
import { PlusIcon } from '@phosphor-icons/react';

import { ContentGalleryGrid, ContentGalleryTile, ContentGalleryToolbar } from '@/components/content/ContentGallery';
import { BulkActionsToolbar } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable } from '@/components/ui/DataTable';
import { selectColumn } from '@/components/ui/DataTable/selectColumn';
import { Dialog, DialogBody, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/Dialog';
import { Label } from '@/components/ui/Label';
import { MobileListToolbar, type MobileListSortOption } from '@/components/ui/MobileListToolbar';
import { MobileMediaCard } from '@/components/ui/MobileMediaCard';
import { Select, SelectContent, SelectItem, SelectTrigger } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { useAddContentByExternalId } from '@/hooks/useAddContentByExternalId';
import { useBulkRun } from '@/hooks/useBulkRun';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { useCardLayout } from '@/hooks/ui/useCardLayout';
import { useLibraryTypes } from '@/hooks/useLibraryTypes';
import { resolveContentTypeLabel } from '@/lib/contentTypeDisplay';
import { sortTableRows } from '@/lib/sortTableRows';

import {
    buildAddableColumns,
    formatPublished,
    RowActionProvider,
    RowActions,
    SourceCell,
    TitleHeading,
    TypeCell,
    type AddableCandidate,
    type RowActionContext,
} from './buildAddableColumns';

interface AddContentResultsProps {
    candidates: AddableCandidate[];
    context: RowActionContext;
    view: string;
    sorting: SortingState;
    sortOptions: MobileListSortOption[];
    onSortingChange: (sorting: SortingState | ((previous: SortingState) => SortingState)) => void;
    searchKey: string;
    page: number;
    onAdded: (candidateKey: string, id: string) => void;
    onBusyChange: (busy: boolean) => void;
    loading: boolean;
}

export function AddContentResults({
    candidates,
    context,
    view,
    sorting,
    sortOptions,
    onSortingChange,
    searchKey,
    page,
    onAdded,
    onBusyChange,
    loading,
}: AddContentResultsProps) {
    const mobile = useCardLayout();
    const { contentTypes } = useLibraryTypes();
    const { toast } = useToast();
    const { mutateAsync: addContent } = useAddContentByExternalId();
    const runBulk = useBulkRun();
    const [running, setRunning] = useState(false);
    const runningRef = useRef(false);
    const [chooseFolders, setChooseFolders] = useState(false);
    const [folderChoices, setFolderChoices] = useState<Record<string, string>>({});
    const { foldersForContentType, addingKeys } = context;
    const canSelect = (candidate: AddableCandidate) =>
        !candidate.inLibrary &&
        !addingKeys.has(candidate.key) &&
        foldersForContentType(candidate.contentType).length > 0;
    const pageRowIds = useMemo(
        () =>
            candidates
                .filter(
                    (candidate) =>
                        !candidate.inLibrary &&
                        !addingKeys.has(candidate.key) &&
                        foldersForContentType(candidate.contentType).length > 0,
                )
                .map((candidate) => candidate.key),
        [candidates, foldersForContentType, addingKeys],
    );
    const selection = useBulkSelection({
        pageRowIds,
        totalCount: pageRowIds.length,
        allowSelectAllAcrossPages: false,
        resetKey: `${searchKey}:${page}`,
    });
    const busy = running || loading;
    const selectionMode = selection.selectionMode || selection.hasSelection;

    useEffect(() => {
        setChooseFolders(false);
        setFolderChoices({});
    }, [searchKey, page]);
    const toggleRow = selection.toggleRow;
    useEffect(() => {
        for (const candidate of candidates) {
            if (candidate.inLibrary || addingKeys.has(candidate.key)) toggleRow(candidate.key, false);
        }
    }, [candidates, addingKeys, toggleRow]);

    const selected = candidates
        .filter((candidate) => selection.isRowChecked(candidate.key) && canSelect(candidate))
        .map((candidate) => ({ id: candidate.key, candidate }));
    const types = [...new Set(selected.map(({ candidate }) => candidate.contentType))];
    const folderFor = (type: string, choices = folderChoices) => {
        const folders = context.foldersForContentType(type);
        return (
            folders.find((folder) => folder.id === choices[type])?.id ??
            folders.find((folder) => folder.isDefault)?.id ??
            (folders.length === 1 ? folders[0].id : undefined)
        );
    };
    const unresolvedTypes = types.filter((type) => !folderFor(type));
    const typeLabel = (type: string) => resolveContentTypeLabel({ contentType: type }, contentTypes) || type;

    const addSelected = async (confirmFolders = false) => {
        if (busy || runningRef.current || selected.length === 0) return;
        const choices = confirmFolders ? folderChoices : {};
        if (types.some((type) => !folderFor(type, choices))) {
            setFolderChoices({});
            setChooseFolders(true);
            return;
        }
        runningRef.current = true;
        setRunning(true);
        onBusyChange(true);
        setChooseFolders(false);
        const destinations = Object.fromEntries(types.map((type) => [type, folderFor(type, choices)!]));
        setFolderChoices({});
        try {
            const { succeeded, failed } = await runBulk(selected, selection, {
                verb: 'add',
                itemNounPlural: 'items',
                suppressToast: true,
                action: async ({ candidate }) => {
                    const id = await addContent({
                        providerType: candidate.providerType,
                        externalIdKey: candidate.externalIdSource,
                        externalId: candidate.externalId,
                        contentType: candidate.contentType,
                        levelSlug: candidate.levelSlug,
                        rootFolderId: destinations[candidate.contentType],
                    });
                    onAdded(candidate.key, id);
                },
            });
            toast({
                title:
                    failed.length === 0
                        ? 'All content added'
                        : succeeded.length === 0
                          ? 'Failed to add content'
                          : 'Some content added',
                description: `${succeeded.length} added, ${failed.length} failed.`,
                variant: failed.length === 0 ? 'success' : succeeded.length === 0 ? 'error' : 'warning',
            });
        } finally {
            runningRef.current = false;
            setRunning(false);
            onBusyChange(false);
        }
    };

    const baseColumns = useMemo(() => buildAddableColumns(), []);
    const columns = useMemo(
        () => [
            selectColumn<AddableCandidate>({
                selection,
                disabled: busy,
                getId: (candidate) => candidate.key,
                headerLabel: 'Select this page',
                rowLabel: (candidate) => candidate.title,
                canSelect: (candidate) =>
                    !candidate.inLibrary &&
                    !context.addingKeys.has(candidate.key) &&
                    context.foldersForContentType(candidate.contentType).length > 0,
            }),
            ...baseColumns,
        ],
        [selection, context, busy, baseColumns],
    );
    const sorted = useMemo(() => sortTableRows(candidates, columns, sorting), [candidates, columns, sorting]);
    const toggle = (candidate: AddableCandidate) =>
        selection.toggleRow(candidate.key, !selection.isRowChecked(candidate.key));
    const beginSelection = (candidate: AddableCandidate) => {
        if (!canSelect(candidate) || busy) return;
        if (!selection.selectionMode) selection.toggleSelectionMode();
        selection.toggleRow(candidate.key, true);
    };
    const selectionControl = {
        active: selectionMode,
        onToggle: () => {
            if (!selection.selectionMode && selection.hasSelection) selection.clear();
            else selection.toggleSelectionMode();
        },
        disabled: busy,
    };

    return (
        <>
            <BulkActionsToolbar
                selection={selection}
                label="Add content selection actions"
                itemNounPlural="items"
                desktopOnly={false}
                selectAll
                disabled={busy}
                visible={selectionMode}
            >
                <Button
                    variant="primary"
                    size="sm"
                    onClick={() => void addSelected()}
                    disabled={busy || !selected.length}
                    loading={running}
                    data-action="bulk-add-content"
                >
                    <PlusIcon aria-hidden="true" />
                    Add
                </Button>
            </BulkActionsToolbar>
            {!loading &&
                candidates.length > 0 &&
                (view === 'gallery' ? (
                    <>
                        <ContentGalleryToolbar
                            sorting={sorting}
                            sortOptions={sortOptions}
                            onSortingChange={onSortingChange}
                            disabled={busy}
                            selection={selectionControl}
                            selectedCount={selection.selectedCount}
                            showSelectionCount={false}
                        />
                        <ContentGalleryGrid items={sorted} getId={(candidate) => candidate.key}>
                            {(candidate) => (
                                <ContentGalleryTile
                                    item={{
                                        id: candidate.existingContentHierarchyItemId ?? candidate.key,
                                        title: candidate.title,
                                        posterUrl: candidate.thumbnailUrl ?? undefined,
                                        contentType: candidate.contentType,
                                        level: candidate.levelSlug,
                                        year: candidate.yearValue,
                                    }}
                                    localContentId={candidate.existingContentHierarchyItemId ?? null}
                                    previewTarget={
                                        !candidate.inLibrary
                                            ? {
                                                  providerType: candidate.providerType,
                                                  externalIdKey: candidate.externalIdSource,
                                                  externalId: candidate.externalId,
                                                  contentType: candidate.contentType,
                                                  levelSlug: candidate.levelSlug,
                                              }
                                            : undefined
                                    }
                                    to={null}
                                    onOpen={
                                        !candidate.inLibrary || candidate.existingContentHierarchyItemId
                                            ? (trigger) => context.onPreview(candidate, trigger)
                                            : undefined
                                    }
                                    selectionMode={selectionMode && !candidate.inLibrary}
                                    selected={selection.isRowChecked(candidate.key)}
                                    selectionDisabled={busy || !canSelect(candidate)}
                                    onToggle={() => toggle(candidate)}
                                    onLongPress={() => beginSelection(candidate)}
                                    muted={candidate.inLibrary}
                                    detail={
                                        <>
                                            <SourceCell candidate={candidate} />
                                            {candidate.inLibrary && <span className="sr-only">In library</span>}
                                        </>
                                    }
                                    posterActions={
                                        (!selectionMode || candidate.inLibrary) && (
                                            <RowActions candidate={candidate} ctx={context} iconOnly />
                                        )
                                    }
                                />
                            )}
                        </ContentGalleryGrid>
                    </>
                ) : mobile ? (
                    <>
                        <MobileListToolbar
                            className="library-add-page__mobile-toolbar"
                            sorting={sorting}
                            sortOptions={sortOptions}
                            onSortingChange={onSortingChange}
                            allowUnsorted
                            disabled={busy}
                            selection={selectionControl}
                        />
                        <div className="library-add-page__mobile-list" data-testid="library-add-page__mobile-list">
                            {sorted.map((candidate) => (
                                <MobileMediaCard
                                    key={candidate.key}
                                    data-result-key={candidate.key}
                                    data-provider={candidate.providerType}
                                    data-source={candidate.externalIdSource}
                                    data-external-id={candidate.externalId}
                                    data-in-library={candidate.inLibrary ? 'true' : undefined}
                                    posterUrl={candidate.thumbnailUrl ?? undefined}
                                    posterAlt=""
                                    selectable={selectionMode && canSelect(candidate)}
                                    selected={selection.isRowChecked(candidate.key)}
                                    onToggleSelected={() => {
                                        if (!busy) toggle(candidate);
                                    }}
                                    selectionLabel={`Select ${candidate.title}`}
                                    onLongPress={() => beginSelection(candidate)}
                                    title={
                                        selectionMode ? (
                                            candidate.title
                                        ) : (
                                            <TitleHeading candidate={candidate} onPreview={context.onPreview} />
                                        )
                                    }
                                    description={candidate.overview ?? undefined}
                                    trailing={
                                        !selectionMode && (
                                            <div className="library-add-page__card-actions">
                                                <RowActions candidate={candidate} ctx={context} />
                                            </div>
                                        )
                                    }
                                >
                                    <TypeCell candidate={candidate} />
                                    <span className="library-add-page__card-published">
                                        Published: {formatPublished(candidate)}
                                    </span>
                                    <SourceCell candidate={candidate} />
                                </MobileMediaCard>
                            ))}
                        </div>
                    </>
                ) : (
                    <div className="library-add-page__table" data-testid="library-add-page__result-list">
                        <RowActionProvider value={context}>
                            <DataTable<AddableCandidate>
                                columns={columns}
                                data={sorted}
                                manualSorting
                                size="sm"
                                enableSorting={!busy}
                                sorting={sorting}
                                onSortingChange={onSortingChange}
                                getRowId={(candidate) => candidate.key}
                                enableRowSelection
                                rowSelection={selection.rowSelection}
                            />
                        </RowActionProvider>
                    </div>
                ))}
            <Dialog
                open={chooseFolders}
                onOpenChange={(open) => {
                    setChooseFolders(open);
                    if (!open) setFolderChoices({});
                }}
            >
                <DialogContent aria-describedby={undefined}>
                    <DialogHeader>
                        <DialogTitle>Add selected content</DialogTitle>
                    </DialogHeader>
                    <DialogBody>
                        {types
                            .filter((type) => context.foldersForContentType(type).length > 1)
                            .map((type) => (
                                <div key={type}>
                                    <Label htmlFor={`bulk-root-${type}`}>Root folder for {typeLabel(type)}</Label>
                                    <Select
                                        value={folderFor(type) ?? ''}
                                        onValueChange={(id) =>
                                            setFolderChoices((current) => ({ ...current, [type]: id }))
                                        }
                                    >
                                        <SelectTrigger id={`bulk-root-${type}`} placeholder="Choose a root folder">
                                            {
                                                context
                                                    .foldersForContentType(type)
                                                    .find((folder) => folder.id === folderFor(type))?.name
                                            }
                                        </SelectTrigger>
                                        <SelectContent>
                                            {context.foldersForContentType(type).map((folder) => (
                                                <SelectItem key={folder.id} value={folder.id}>
                                                    {folder.name}
                                                </SelectItem>
                                            ))}
                                        </SelectContent>
                                    </Select>
                                </div>
                            ))}
                    </DialogBody>
                    <DialogFooter>
                        <Button
                            variant="ghost"
                            onClick={() => {
                                setChooseFolders(false);
                                setFolderChoices({});
                            }}
                        >
                            Cancel
                        </Button>
                        <Button
                            variant="primary"
                            disabled={unresolvedTypes.length > 0 || busy || selected.length === 0}
                            onClick={() => void addSelected(true)}
                        >
                            Add
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}
