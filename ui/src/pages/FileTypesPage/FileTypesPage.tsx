import { useCallback, useEffect, useMemo, useState } from 'react';
import { ShieldWarningIcon, TrashIcon, PencilSimpleIcon, PlusIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { useBulkRun } from '@/hooks/useBulkRun';
import { useBulkSelection } from '@/hooks/useBulkSelection';
import { BulkActionsToolbar, SelectModeToggle } from '@/components/ui/BulkActionsToolbar';
import { Button } from '@/components/ui/Button';
import { DataTable, selectColumn } from '@/components/ui/DataTable';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { EmptyState } from '@/components/ui/EmptyState';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { MobileSelectionBar } from '@/components/ui/MobileSelectionBar';
import { Pager } from '@/components/ui/Pager';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/Tabs';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useDenyList, useMimeMappings, type BlockedFileType, type MimeMapping } from '@/hooks/useFileTypes';
import { getErrorMessage } from '@/lib/errors';

import '@/styles/bulk-actions-toolbar.css';
import './FileTypesPage.css';

export function FileTypesPage() {
    useDocumentTitle('File Types');

    const denyList = useDenyList();
    const mimeRegistry = useMimeMappings();
    const [activeTab, setActiveTab] = useState('deny-list');
    const [denyAddOpen, , openDenyAdd, closeDenyAdd] = useToggle();
    const [mimeAddOpen, , openMimeAdd, closeMimeAdd] = useToggle();
    const tabActions: QuickAction[] = [
        activeTab === 'deny-list'
            ? { id: 'add-deny-list-entry', label: 'Add Entry', icon: <PlusIcon weight="bold" />, onClick: openDenyAdd }
            : { id: 'add-mime-mapping', label: 'Add Mapping', icon: <PlusIcon weight="bold" />, onClick: openMimeAdd },
    ];

    const handleRefresh = useCallback(async () => {
        await Promise.all([denyList.refetch(), mimeRegistry.refetch()]);
    }, [denyList, mimeRegistry]);

    return (
        <div className="file-types-page" data-page="file-types">
            <PullToRefresh onRefresh={handleRefresh} className="file-types-page__pull-to-refresh">
                <PageHeader
                    className="file-types-page__header"
                    title="File Types"
                    icon={<ShieldWarningIcon weight="duotone" aria-hidden="true" data-testid="file-types-page__icon" />}
                />

                <Tabs value={activeTab} onValueChange={setActiveTab} className="file-types-page__tabs">
                    <TabsList aria-label="File-type sections">
                        <TabsTrigger value="deny-list" data-tab="deny-list">
                            Deny list
                            <span className="tab-count" data-testid="deny-list__count">
                                {denyList.entries.length}
                            </span>
                        </TabsTrigger>
                        <TabsTrigger value="mime-registry" data-tab="mime-registry">
                            MIME registry
                            <span className="tab-count" data-testid="mime-registry__count">
                                {mimeRegistry.mappings.length}
                            </span>
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="deny-list" forceMount className="file-types-page__tab-content">
                        <DenyListSection
                            denyList={denyList}
                            addOpen={denyAddOpen}
                            openAdd={openDenyAdd}
                            closeAdd={closeDenyAdd}
                        />
                    </TabsContent>

                    <TabsContent value="mime-registry" forceMount className="file-types-page__tab-content">
                        <MimeRegistrySection
                            registry={mimeRegistry}
                            addOpen={mimeAddOpen}
                            openAdd={openMimeAdd}
                            closeAdd={closeMimeAdd}
                        />
                    </TabsContent>
                </Tabs>
            </PullToRefresh>

            <QuickActionsButton actions={tabActions} title="File Type Actions" />
        </div>
    );
}

FileTypesPage.displayName = 'FileTypesPage';

interface AddDialogState {
    addOpen: boolean;
    openAdd: () => void;
    closeAdd: () => void;
}

interface DenyListSectionProps extends AddDialogState {
    denyList: ReturnType<typeof useDenyList>;
}

function DenyListSection({ denyList, addOpen, openAdd, closeAdd }: DenyListSectionProps) {
    const { toast } = useToast();
    const runBulk = useBulkRun();
    const [deletingEntry, setDeletingEntry] = useState<BlockedFileType | null>(null);
    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const [isBulkDeleteOpen, setIsBulkDeleteOpen] = useState(false);
    const [isBulkDeleting, setIsBulkDeleting] = useState(false);

    const handleAdd = useCallback(
        async (pattern: string): Promise<string | null> => {
            try {
                await denyList.createEntry({ pattern });
                toast({ title: pattern, description: 'Blocked file pattern added.', variant: 'success' });
                return null;
            } catch (err) {
                const message = getErrorMessage(err, 'Failed to add deny-list entry');
                toast({ title: pattern, description: message, variant: 'error' });
                return message;
            }
        },
        [denyList, toast],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingEntry) {
            return;
        }
        const entry = deletingEntry;
        try {
            await denyList.deleteEntry(entry.id);
            toast({ title: entry.pattern, description: 'Blocked file pattern removed.', variant: 'success' });
        } catch (err) {
            toast({
                title: entry.pattern,
                description: getErrorMessage(err, 'Failed to remove deny-list entry'),
                variant: 'error',
            });
        } finally {
            setDeletingEntry(null);
        }
    }, [deletingEntry, denyList, toast]);

    const getRowId = useCallback((entry: BlockedFileType) => entry.id, []);

    const selection = useBulkSelection({ pageRowIds, totalCount: denyList.entries.length });
    const selectedEntries = useMemo(
        () => denyList.entries.filter((e) => selection.isRowChecked(e.id)),
        [denyList.entries, selection],
    );

    const handleBulkDelete = useCallback(async () => {
        setIsBulkDeleting(true);
        try {
            await runBulk(selectedEntries, selection, {
                action: (entry) => denyList.deleteEntry(entry.id),
                verb: 'remove',
                itemNounPlural: 'deny-list entries',
            });
        } finally {
            setIsBulkDeleting(false);
            setIsBulkDeleteOpen(false);
        }
    }, [denyList, runBulk, selectedEntries, selection]);

    const columns = useMemo<ColumnDef<BlockedFileType>[]>(
        () => [
            selectColumn<BlockedFileType>({
                selection,
                getId: (row) => row.id,
                headerLabel: 'Select all deny-list entries',
                rowLabel: (row) => row.pattern,
            }),
            {
                id: 'pattern',
                accessorKey: 'pattern',
                header: 'Pattern',
                meta: { mobileRole: 'header' },
                cell: ({ row }) => <code className="file-types-page__code">{row.original.pattern}</code>,
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                meta: { mobileRole: 'header' },
                cell: ({ row }) => (
                    <div className="file-types-page__row-actions">
                        <Tooltip content="Delete">
                            <button
                                type="button"
                                className="file-types-page__action-btn file-types-page__action-btn--danger"
                                onClick={() => setDeletingEntry(row.original)}
                                aria-label={`Delete ${row.original.pattern}`}
                                data-action="delete"
                                data-target={row.original.id}
                            >
                                <TrashIcon weight="bold" />
                            </button>
                        </Tooltip>
                    </div>
                ),
            },
        ],
        [selection],
    );

    return (
        <section data-section="deny-list">
            <p className="file-types-page__area-description">
                Blocked file extension or MIME type patterns. Downloads containing a match are rejected automatically.
            </p>

            <div className="file-types-page__header-actions">
                <Button variant="primary" size="sm" onClick={openAdd} data-action="add-deny-list-entry">
                    <PlusIcon weight="bold" aria-hidden="true" />
                    <span>Add Entry</span>
                </Button>
            </div>

            {denyList.isLoading ? (
                <EmptyState title="Loading..." size="sm" />
            ) : denyList.error ? (
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Failed to load blocked file patterns"
                    action={
                        <Button variant="secondary" size="sm" onClick={() => denyList.refetch()}>
                            Retry
                        </Button>
                    }
                />
            ) : denyList.entries.length === 0 ? (
                <EmptyState
                    title="No deny-list entries"
                    description="A blocked file extension or MIME type pattern can be added here."
                />
            ) : (
                <>
                    <BulkActionsToolbar
                        selection={selection}
                        label="Deny-list selection actions"
                        itemNounPlural="deny-list entries"
                    >
                        <Button
                            variant="destructive"
                            size="sm"
                            onClick={() => setIsBulkDeleteOpen(true)}
                            aria-label="Delete selected deny-list entries"
                            data-action="bulk-delete"
                        >
                            <TrashIcon weight="bold" aria-hidden="true" />
                            <span className="bulk-actions-toolbar__btn-label">Delete</span>
                        </Button>
                    </BulkActionsToolbar>

                    <SelectModeToggle active={selection.selectionMode} onToggle={selection.toggleSelectionMode} />

                    <div data-table="deny-list">
                        <DataTable<BlockedFileType>
                            columns={columns}
                            data={denyList.entries}
                            enableSorting={false}
                            getRowId={getRowId}
                            onPageRowIdsChange={setPageRowIds}
                            mobileSelectionActive={selection.selectionMode}
                            mobileSelectionOverlay={{
                                isSelected: (entry) => selection.isRowChecked(entry.id),
                                onToggle: (entry) => selection.toggleRow(entry.id, !selection.isRowChecked(entry.id)),
                            }}
                            aria-label="Deny list"
                        />
                    </div>
                </>
            )}

            <DenyListAddDialog open={addOpen} onClose={closeAdd} onAdd={handleAdd} isCreating={denyList.isCreating} />

            <DeleteConfirmDialog
                open={!!deletingEntry}
                onConfirm={handleConfirmDelete}
                onCancel={() => setDeletingEntry(null)}
                itemName={deletingEntry?.pattern ?? ''}
                itemType="Deny-List Entry"
                isDeleting={denyList.isDeleting}
            />

            <DeleteConfirmDialog
                open={isBulkDeleteOpen}
                onConfirm={handleBulkDelete}
                onCancel={() => setIsBulkDeleteOpen(false)}
                itemName={`${selection.selectedCount} selected`}
                itemType="Deny-List Entries"
                isDeleting={isBulkDeleting}
            />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="deny-list-mobile-selection-bar"
                    selection={selection}
                    itemNounPlural="deny-list entries"
                >
                    <Button
                        variant="destructive"
                        size="sm"
                        disabled={!selection.hasSelection}
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected deny-list entries"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        Delete
                    </Button>
                </MobileSelectionBar>
            )}
        </section>
    );
}

interface DenyListAddDialogProps {
    open: boolean;
    onClose: () => void;
    onAdd: (pattern: string) => Promise<string | null>;
    isCreating: boolean;
}

function DenyListAddDialog({ open, onClose, onAdd, isCreating }: DenyListAddDialogProps) {
    const [pattern, setPattern] = useState('');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (open) {
            setPattern('');
            setError(null);
        }
    }, [open]);

    const handleSubmit = useCallback(async () => {
        const trimmed = pattern.trim();
        if (!trimmed) {
            setError('Pattern must not be empty.');
            return;
        }
        setError(null);
        const failure = await onAdd(trimmed);
        if (failure) {
            setError(failure);
            return;
        }
        onClose();
    }, [pattern, onAdd, onClose]);

    return (
        <Dialog open={open} onOpenChange={(next) => (!next ? onClose() : undefined)}>
            <DialogContent data-form="add-deny-list-entry">
                <DialogHeader>
                    <DialogTitle>Add deny-list entry</DialogTitle>
                    <DialogDescription>
                        Blocked file extension or MIME type patterns. Downloads containing a match are rejected
                        automatically.
                    </DialogDescription>
                </DialogHeader>
                <div className="file-types-page__dialog-body">
                    <div className="file-types-page__dialog-field">
                        <Label htmlFor="deny-list-pattern">Pattern</Label>
                        <Input
                            id="deny-list-pattern"
                            value={pattern}
                            onChange={(e) => setPattern(e.target.value)}
                            placeholder=".iso or application/x-msdownload"
                            error={!!error}
                            errorMessage={error || undefined}
                            data-field="deny-list-pattern"
                        />
                    </div>
                </div>
                <DialogFooter>
                    <Button variant="ghost" onClick={onClose} disabled={isCreating} data-action="cancel">
                        Cancel
                    </Button>
                    <Button variant="primary" onClick={handleSubmit} loading={isCreating} data-action="submit">
                        Add
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

interface MimeRegistrySectionProps extends AddDialogState {
    registry: ReturnType<typeof useMimeMappings>;
}

const MIME_PAGE_SIZE = 50;

const MIME_SORT_KEYS = ['extension', 'mimeType'] as const;
const MIME_DEFAULT_SORTING: SortingState = [{ id: 'extension', desc: false }];

function MimeRegistrySection({ registry, addOpen, openAdd, closeAdd }: MimeRegistrySectionProps) {
    const { toast } = useToast();
    const [editingMapping, setEditingMapping] = useState<MimeMapping | null>(null);
    const [deletingMapping, setDeletingMapping] = useState<MimeMapping | null>(null);
    const [pageRowIds, setPageRowIds] = useState<string[]>([]);
    const [isBulkDeleteOpen, setIsBulkDeleteOpen] = useState(false);
    const [isBulkDeleting, setIsBulkDeleting] = useState(false);
    const runBulk = useBulkRun();

    const { sorting, page, setSorting, setPage } = useUrlTableState({
        defaultSort: MIME_DEFAULT_SORTING,
        defaultFilters: {},
        sortKeys: MIME_SORT_KEYS,
        pageParam: 'mpage',
        sortParam: 'msort',
    });

    const pageCount = Math.max(1, Math.ceil(registry.mappings.length / MIME_PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: MIME_PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((mapping: MimeMapping) => mapping.extension, []);

    const handleAdd = useCallback(
        async (extension: string, mimeType: string): Promise<string | null> => {
            try {
                await registry.saveMapping({ extension, mimeType });
                toast({ title: extension, description: 'File extension association added.', variant: 'success' });
                return null;
            } catch (err) {
                const message = getErrorMessage(err, 'Failed to add MIME mapping');
                toast({ title: extension, description: message, variant: 'error' });
                return message;
            }
        },
        [registry, toast],
    );

    const handleEditSave = useCallback(
        async (mapping: MimeMapping, nextMime: string): Promise<string | null> => {
            try {
                await registry.saveMapping({ extension: mapping.extension, mimeType: nextMime });
                toast({
                    title: mapping.extension,
                    description: 'File extension association updated.',
                    variant: 'success',
                });
                return null;
            } catch (err) {
                const message = getErrorMessage(err, 'Failed to update MIME mapping');
                toast({ title: mapping.extension, description: message, variant: 'error' });
                return message;
            }
        },
        [registry, toast],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingMapping) {
            return;
        }
        const mapping = deletingMapping;
        try {
            await registry.deleteMapping(mapping.extension);
            toast({ title: mapping.extension, description: 'File extension association removed.', variant: 'success' });
        } catch (err) {
            toast({
                title: mapping.extension,
                description: getErrorMessage(err, 'Failed to remove MIME mapping'),
                variant: 'error',
            });
        } finally {
            setDeletingMapping(null);
        }
    }, [deletingMapping, registry, toast]);

    const selection = useBulkSelection({ pageRowIds, totalCount: registry.mappings.length });
    const selectedMappings = useMemo(
        () => registry.mappings.filter((m) => selection.isRowChecked(m.extension)).map((m) => ({ id: m.extension })),
        [registry.mappings, selection],
    );

    const handleBulkDelete = useCallback(async () => {
        setIsBulkDeleting(true);
        try {
            await runBulk(selectedMappings, selection, {
                action: (mapping) => registry.deleteMapping(mapping.id),
                verb: 'remove',
                itemNounPlural: 'MIME mappings',
            });
        } finally {
            setIsBulkDeleting(false);
            setIsBulkDeleteOpen(false);
        }
    }, [registry, runBulk, selectedMappings, selection]);

    const columns = useMemo<ColumnDef<MimeMapping>[]>(
        () => [
            selectColumn<MimeMapping>({
                selection,
                getId: (row) => row.extension,
                headerLabel: 'Select all MIME mappings',
                rowLabel: (row) => row.extension,
            }),
            {
                id: 'extension',
                accessorKey: 'extension',
                header: 'Extension',
                meta: { mobileRole: 'header' },
                cell: ({ row }) => <code className="file-types-page__code">{row.original.extension}</code>,
            },
            {
                id: 'mimeType',
                accessorKey: 'mimeType',
                header: 'MIME type',
                cell: ({ row }) => <code className="file-types-page__code">{row.original.mimeType}</code>,
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                meta: { mobileRole: 'header' },
                cell: ({ row }) => {
                    const mapping = row.original;
                    return (
                        <div className="file-types-page__row-actions">
                            <Tooltip content="Edit">
                                <button
                                    type="button"
                                    className="file-types-page__action-btn"
                                    onClick={() => setEditingMapping(mapping)}
                                    aria-label={`Edit ${mapping.extension}`}
                                    data-action="edit"
                                    data-target={mapping.extension}
                                >
                                    <PencilSimpleIcon weight="bold" />
                                </button>
                            </Tooltip>
                            <Tooltip content="Delete">
                                <button
                                    type="button"
                                    className="file-types-page__action-btn file-types-page__action-btn--danger"
                                    onClick={() => setDeletingMapping(mapping)}
                                    aria-label={`Delete ${mapping.extension}`}
                                    data-action="delete"
                                    data-target={mapping.extension}
                                >
                                    <TrashIcon weight="bold" />
                                </button>
                            </Tooltip>
                        </div>
                    );
                },
            },
        ],
        [selection],
    );

    return (
        <section data-section="mime-registry">
            <p className="file-types-page__area-description">File extension to MIME type associations.</p>

            <div className="file-types-page__header-actions">
                <Button variant="primary" size="sm" onClick={openAdd} data-action="add-mime-mapping">
                    <PlusIcon weight="bold" aria-hidden="true" />
                    <span>Add Mapping</span>
                </Button>
            </div>

            {registry.isLoading ? (
                <EmptyState title="Loading..." size="sm" />
            ) : registry.error ? (
                <EmptyState
                    icon={<WarningCircleIcon weight="duotone" />}
                    title="Failed to load file type associations"
                    action={
                        <Button variant="secondary" size="sm" onClick={() => registry.refetch()}>
                            Retry
                        </Button>
                    }
                />
            ) : registry.mappings.length === 0 ? (
                <EmptyState
                    title="No MIME mappings"
                    description="A file extension association can be added to get started."
                />
            ) : (
                <>
                    <BulkActionsToolbar
                        selection={selection}
                        label="MIME mapping selection actions"
                        itemNounPlural="MIME mappings"
                    >
                        <Button
                            variant="destructive"
                            size="sm"
                            onClick={() => setIsBulkDeleteOpen(true)}
                            aria-label="Delete selected MIME mappings"
                            data-action="bulk-delete"
                        >
                            <TrashIcon weight="bold" aria-hidden="true" />
                            <span className="bulk-actions-toolbar__btn-label">Delete</span>
                        </Button>
                    </BulkActionsToolbar>

                    <SelectModeToggle active={selection.selectionMode} onToggle={selection.toggleSelectionMode} />

                    <div data-table="mime-registry">
                        <DataTable<MimeMapping>
                            columns={columns}
                            data={registry.mappings}
                            enableSorting
                            sorting={sorting}
                            onSortingChange={(updater) =>
                                setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                            }
                            enablePagination={registry.mappings.length > MIME_PAGE_SIZE}
                            hidePagination
                            pageSize={MIME_PAGE_SIZE}
                            pagination={pagination}
                            getRowId={getRowId}
                            onPageRowIdsChange={setPageRowIds}
                            mobileSelectionActive={selection.selectionMode}
                            mobileSelectionOverlay={{
                                isSelected: (mapping) => selection.isRowChecked(mapping.extension),
                                onToggle: (mapping) =>
                                    selection.toggleRow(mapping.extension, !selection.isRowChecked(mapping.extension)),
                            }}
                            aria-label="MIME registry"
                        />
                    </div>

                    {pageCount > 1 && (
                        <Pager
                            className="file-types-page__pagination"
                            data-component="mime-pagination"
                            page={currentPage}
                            pageCount={pageCount}
                            totalItems={registry.mappings.length}
                            onFirst={() => setPage(1)}
                            onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                            onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                            onLast={() => setPage(pageCount)}
                        />
                    )}
                </>
            )}

            <MimeMappingAddDialog open={addOpen} onClose={closeAdd} onAdd={handleAdd} isCreating={registry.isSaving} />

            <MimeMappingEditDialog
                mapping={editingMapping}
                onClose={() => setEditingMapping(null)}
                onSave={handleEditSave}
                isSaving={registry.isSaving}
            />

            <DeleteConfirmDialog
                open={!!deletingMapping}
                onConfirm={handleConfirmDelete}
                onCancel={() => setDeletingMapping(null)}
                itemName={deletingMapping?.extension ?? ''}
                itemType="MIME Mapping"
                isDeleting={registry.isDeleting}
            />

            <DeleteConfirmDialog
                open={isBulkDeleteOpen}
                onConfirm={handleBulkDelete}
                onCancel={() => setIsBulkDeleteOpen(false)}
                itemName={`${selection.selectedCount} selected`}
                itemType="MIME Mappings"
                isDeleting={isBulkDeleting}
            />

            {selection.selectionMode && (
                <MobileSelectionBar
                    data-component="mime-registry-mobile-selection-bar"
                    selection={selection}
                    itemNounPlural="MIME mappings"
                >
                    <Button
                        variant="destructive"
                        size="sm"
                        disabled={!selection.hasSelection}
                        onClick={() => setIsBulkDeleteOpen(true)}
                        aria-label="Delete selected MIME mappings"
                        data-action="bulk-delete"
                    >
                        <TrashIcon weight="bold" aria-hidden="true" />
                        Delete
                    </Button>
                </MobileSelectionBar>
            )}
        </section>
    );
}

interface MimeMappingAddDialogProps {
    open: boolean;
    onClose: () => void;
    onAdd: (extension: string, mimeType: string) => Promise<string | null>;
    isCreating: boolean;
}

function MimeMappingAddDialog({ open, onClose, onAdd, isCreating }: MimeMappingAddDialogProps) {
    const [extension, setExtension] = useState('');
    const [mimeType, setMimeType] = useState('');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (open) {
            setExtension('');
            setMimeType('');
            setError(null);
        }
    }, [open]);

    const handleSubmit = useCallback(async () => {
        const trimmedExt = extension.trim();
        const trimmedMime = mimeType.trim();
        if (!trimmedExt || !trimmedMime) {
            setError('Extension and MIME type are required.');
            return;
        }
        setError(null);
        const failure = await onAdd(trimmedExt, trimmedMime);
        if (failure) {
            setError(failure);
            return;
        }
        onClose();
    }, [extension, mimeType, onAdd, onClose]);

    return (
        <Dialog open={open} onOpenChange={(next) => (!next ? onClose() : undefined)}>
            <DialogContent data-form="add-mime-mapping">
                <DialogHeader>
                    <DialogTitle>Add MIME mapping</DialogTitle>
                    <DialogDescription>Associate a file extension with a MIME type.</DialogDescription>
                </DialogHeader>
                <div className="file-types-page__dialog-body">
                    <div className="file-types-page__dialog-field">
                        <Label htmlFor="mime-extension">Extension</Label>
                        <Input
                            id="mime-extension"
                            value={extension}
                            onChange={(e) => setExtension(e.target.value)}
                            placeholder=".mkv"
                            error={!!error}
                            data-field="mime-extension"
                        />
                    </div>
                    <div className="file-types-page__dialog-field">
                        <Label htmlFor="mime-type">MIME type</Label>
                        <Input
                            id="mime-type"
                            value={mimeType}
                            onChange={(e) => setMimeType(e.target.value)}
                            placeholder="video/x-matroska"
                            error={!!error}
                            errorMessage={error || undefined}
                            data-field="mime-type"
                        />
                    </div>
                </div>
                <DialogFooter>
                    <Button variant="ghost" onClick={onClose} disabled={isCreating} data-action="cancel">
                        Cancel
                    </Button>
                    <Button variant="primary" onClick={handleSubmit} loading={isCreating} data-action="submit">
                        Add
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

interface MimeMappingEditDialogProps {
    mapping: MimeMapping | null;
    onClose: () => void;
    onSave: (mapping: MimeMapping, nextMime: string) => Promise<string | null>;
    isSaving: boolean;
}

function MimeMappingEditDialog({ mapping, onClose, onSave, isSaving }: MimeMappingEditDialogProps) {
    const [draft, setDraft] = useState('');
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (mapping) {
            setDraft(mapping.mimeType);
            setError(null);
        }
    }, [mapping]);

    const handleSubmit = useCallback(async () => {
        if (!mapping) {
            return;
        }
        const trimmed = draft.trim();
        if (!trimmed) {
            setError('MIME type must not be empty.');
            return;
        }
        setError(null);
        const failure = await onSave(mapping, trimmed);
        if (failure) {
            setError(failure);
            return;
        }
        onClose();
    }, [mapping, draft, onSave, onClose]);

    return (
        <Dialog open={!!mapping} onOpenChange={(next) => (!next ? onClose() : undefined)}>
            <DialogContent data-form="edit-mime-mapping">
                <DialogHeader>
                    <DialogTitle>Edit MIME mapping</DialogTitle>
                    <DialogDescription>
                        Update the MIME type that {mapping?.extension ?? 'this extension'} resolves to.
                    </DialogDescription>
                </DialogHeader>
                <div className="file-types-page__dialog-body">
                    <div className="file-types-page__dialog-field">
                        <Label htmlFor="mime-edit-extension">Extension</Label>
                        <Input id="mime-edit-extension" value={mapping?.extension ?? ''} disabled />
                    </div>
                    <div className="file-types-page__dialog-field">
                        <Label htmlFor="mime-edit-input">MIME type</Label>
                        <Input
                            id="mime-edit-input"
                            value={draft}
                            onChange={(e) => setDraft(e.target.value)}
                            placeholder="video/x-matroska"
                            error={!!error}
                            errorMessage={error || undefined}
                            data-field="mime-edit-input"
                        />
                    </div>
                </div>
                <DialogFooter>
                    <Button variant="ghost" onClick={onClose} disabled={isSaving} data-action="cancel">
                        Cancel
                    </Button>
                    <Button variant="primary" onClick={handleSubmit} loading={isSaving} data-action="save">
                        Save
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

export default FileTypesPage;
