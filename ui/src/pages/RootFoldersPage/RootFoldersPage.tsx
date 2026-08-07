import { useState, useCallback, useMemo } from 'react';
import {
    FolderOpenIcon,
    HardDrivesIcon,
    TrashIcon,
    PencilSimpleIcon,
    PlusIcon,
    WarningCircleIcon,
} from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { DiskSpaceWidget } from '@/components/ui/DiskSpaceWidget';
import { EmptyState } from '@/components/ui/EmptyState';
import { Pager } from '@/components/ui/Pager';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Tooltip } from '@/components/ui/Tooltip';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useLibraryTypes, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { useRootFolders, type ApiRootFolder } from '@/hooks/useRootFolders';
import { useDiskSpace } from '@/hooks/useSystemStatus';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';

import { MonitoringDefaultPanel } from './MonitoringDefaultPanel';
import { RootFolderForm, type RootFolderFormValues } from '@/components/configuration/RootFolderForm';

import './RootFoldersPage.css';
import '@/styles/card-actions.css';

function getContentTypeName(id: string, contentTypes: LibraryTypeDefinition[]): string {
    const contentType = contentTypes.find((ct) => ct.id === id);
    return contentType?.name ?? 'Unknown';
}

function getMonitoringLabel(value: boolean | null): string {
    return value === null ? 'Inherit' : value ? 'Active' : 'Excluded';
}

function getMonitoringVariant(value: boolean | null): Parameters<typeof Badge>[0]['variant'] {
    return value === null ? 'info' : value ? 'healthy' : 'unknown';
}

function toRootFolderFormValues(folder: ApiRootFolder): RootFolderFormValues {
    return {
        id: folder.id,
        name: folder.name,
        path: folder.path,
        contentType: folder.contentType,
        isDefault: folder.isDefault,
        defaultMonitoring: folder.defaultMonitoring,
        renameTemplate: folder.renameTemplate ?? '',
        upgradeFileHandling: folder.upgradeFileHandling,
        defaults: folder.defaults ?? [],
    };
}

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'path', 'contentType', 'monitoring'] as const;

const DEFAULT_FILTERS = { contentType: '' };

export function RootFoldersPage() {
    useDocumentTitle('Root Folders');
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingFolder, setEditingFolder] = useState<RootFolderFormValues | null>(null);
    const [deletingFolder, setDeletingFolder] = useState<ApiRootFolder | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const { contentTypes } = useLibraryTypes();

    const { folders, isLoading, error, createFolder, isCreating, updateFolder, isUpdating, deleteFolder, refetch } =
        useRootFolders();
    const currentEditingFolder = useMemo(() => {
        if (!editingFolder?.id) {
            return editingFolder;
        }
        const folder = folders.find((candidate) => candidate.id === editingFolder.id);
        return folder ? toRootFolderFormValues(folder) : editingFolder;
    }, [editingFolder, folders]);
    const {
        folders: diskSpaceFolders,
        totals: diskSpaceTotals,
        isLoading: isDiskSpaceLoading,
        refetch: refetchDiskSpace,
    } = useDiskSpace();

    const isSubmitting = isCreating || isUpdating;

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
    });

    const contentTypeFilter = filters.contentType;

    const contentTypeOptions = useMemo<FacetOption[]>(() => {
        const usedIds = [...new Set(folders.map((f) => f.contentType))];
        return usedIds
            .map((id) => ({ value: id, label: getContentTypeName(id, contentTypes) }))
            .sort((a, b) => a.label.localeCompare(b.label));
    }, [folders, contentTypes]);

    const filteredFolders = useMemo(() => {
        if (!contentTypeFilter) {
            return folders;
        }
        return folders.filter((f) => f.contentType === contentTypeFilter);
    }, [folders, contentTypeFilter]);

    const handleClearFilters = useCallback(() => {
        setFilters({ contentType: '' });
    }, [setFilters]);

    const handleAddFolder = useCallback(() => {
        setEditingFolder(null);
        openForm();
    }, [openForm]);

    const addFolderActions: QuickAction[] = useMemo(
        () => [{ id: 'add-folder', label: 'Add Folder', icon: <PlusIcon weight="bold" />, onClick: handleAddFolder }],
        [handleAddFolder],
    );

    const handleEditFolder = useCallback(
        (id: string) => {
            const folder = folders.find((f) => f.id === id);
            if (folder) {
                setEditingFolder(toRootFolderFormValues(folder));
                openForm();
            }
        },
        [folders, openForm],
    );

    const handleFormSubmit = useCallback(
        async (values: RootFolderFormValues) => {
            try {
                const trimmedTemplate = values.renameTemplate.trim();
                const renameTemplateValue = trimmedTemplate === '' ? null : trimmedTemplate;
                if (values.id) {
                    await updateFolder(values.id, {
                        name: values.name,
                        path: values.path,
                        contentType: values.contentType,
                        isDefault: values.isDefault,
                        defaultMonitoring: { value: values.defaultMonitoring },
                        renameTemplate: { value: renameTemplateValue },
                        upgradeFileHandling: values.upgradeFileHandling,
                        defaults: values.defaults,
                    });
                    toast({
                        title: values.name,
                        description: 'Root folder updated.',
                        variant: 'success',
                    });
                } else {
                    await createFolder({
                        name: values.name,
                        path: values.path,
                        contentType: values.contentType,
                        isDefault: values.isDefault,
                        defaultMonitoring: values.defaultMonitoring,
                        renameTemplate: renameTemplateValue,
                        upgradeFileHandling: values.upgradeFileHandling,
                        defaults: values.defaults,
                    });
                    toast({
                        title: values.name,
                        description: 'Root folder added.',
                        variant: 'success',
                    });
                }

                void refetchDiskSpace();

                closeForm();
                setEditingFolder(null);
            } catch (error) {
                toast({
                    title: values.name,
                    description: error instanceof Error ? error.message : 'Failed to save root folder',
                    variant: 'error',
                });
                throw error;
            }
        },
        [toast, createFolder, updateFolder, refetchDiskSpace, closeForm],
    );

    const handleSetDefaultFolder = useCallback(
        async (folder: ApiRootFolder) => {
            if (folder.isDefault) {
                return;
            }

            try {
                await updateFolder(folder.id, { isDefault: true });
                toast({
                    title: folder.name,
                    description: 'Default root folder updated.',
                    variant: 'success',
                });
            } catch {
                toast({
                    title: folder.name,
                    description: 'Failed to update the default root folder',
                    variant: 'error',
                });
            }
        },
        [toast, updateFolder],
    );

    const handleFormClose = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
                setEditingFolder(null);
            }
        },
        [closeForm],
    );

    const handleDeleteFolder = useCallback(
        (id: string) => {
            const folder = folders.find((f) => f.id === id);
            if (!folder) {
                return;
            }
            setDeletingFolder(folder);
        },
        [folders],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingFolder) {
            return;
        }

        setIsDeleting(true);

        try {
            await deleteFolder(deletingFolder.id);
            void refetchDiskSpace();
            toast({
                title: deletingFolder.name,
                description: 'Root folder deleted.',
                variant: 'success',
            });
        } catch {
            toast({
                title: deletingFolder.name,
                description: 'Failed to delete root folder',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingFolder(null);
        }
    }, [deletingFolder, deleteFolder, refetchDiskSpace, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingFolder(null);
    }, []);

    const handleRefresh = useCallback(async () => {
        await Promise.all([refetch(), refetchDiskSpace()]);
    }, [refetchDiskSpace, refetch]);

    const columns = useMemo<ColumnDef<ApiRootFolder>[]>(
        () => [
            {
                id: 'name',
                accessorFn: (row) => row.name,
                header: 'Name',
                size: 220,
                cell: ({ row }) => (
                    <span className="root-folders-table__name" title={row.original.name}>
                        {row.original.name}
                    </span>
                ),
            },
            {
                id: 'path',
                accessorFn: (row) => row.path,
                header: 'Path',
                size: 280,
                cell: ({ row }) => (
                    <span className="root-folders-table__path" title={row.original.path}>
                        {row.original.path}
                    </span>
                ),
            },
            {
                id: 'contentType',
                accessorFn: (row) => getContentTypeName(row.contentType, contentTypes),
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Type"
                        active={contentTypeFilter !== ''}
                        options={contentTypeOptions}
                        value={contentTypeFilter}
                        onChange={(value) => setFilters({ contentType: value })}
                    />
                ),
                meta: { headerLabel: 'Type' },
                size: 160,
                cell: ({ row }) => {
                    const name = getContentTypeName(row.original.contentType, contentTypes);
                    return (
                        <Badge variant={name === 'Unknown' ? 'unknown' : 'info'} size="sm" dot={name === 'Unknown'}>
                            {name}
                        </Badge>
                    );
                },
            },
            {
                id: 'monitoring',
                accessorFn: (row) => row.defaultMonitoring,
                header: 'Monitoring',
                size: 130,
                cell: ({ row }) => {
                    const monitoring = row.original.defaultMonitoring;
                    return (
                        <Badge variant={getMonitoringVariant(monitoring)} size="sm">
                            {getMonitoringLabel(monitoring)}
                        </Badge>
                    );
                },
            },
            {
                id: 'default',
                accessorFn: (row) => row.isDefault,
                header: 'Default',
                enableSorting: false,
                size: 100,
                cell: ({ row }) => {
                    const folder = row.original;
                    const contentTypeName = getContentTypeName(folder.contentType, contentTypes);
                    return (
                        <label className="root-folders-table__default-radio">
                            <input
                                type="radio"
                                name={`default-root-folder-${folder.contentType}`}
                                checked={folder.isDefault}
                                onChange={() => void handleSetDefaultFolder(folder)}
                                disabled={isUpdating}
                                aria-label={`Set ${folder.name} as default for ${contentTypeName}`}
                                data-action="set-default"
                                data-target={folder.id}
                            />
                            <span className="sr-only">Default for {contentTypeName}</span>
                        </label>
                    );
                },
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                size: 120,
                cell: ({ row }) => (
                    <div className="root-folders-page__item-actions card-actions">
                        <Tooltip content="Edit">
                            <button
                                type="button"
                                className="root-folders-page__action-btn card-actions__btn"
                                data-testid="root-folders-page__action-btn"
                                onClick={() => handleEditFolder(row.original.id)}
                                aria-label={`Edit ${row.original.name}`}
                                data-action="edit"
                                data-target={row.original.id}
                            >
                                <PencilSimpleIcon weight="bold" />
                            </button>
                        </Tooltip>
                        <Tooltip content="Delete">
                            <button
                                type="button"
                                className="root-folders-page__action-btn root-folders-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                                data-testid="root-folders-page__action-btn"
                                onClick={() => handleDeleteFolder(row.original.id)}
                                aria-label={`Delete ${row.original.name}`}
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
        [
            contentTypes,
            contentTypeOptions,
            contentTypeFilter,
            setFilters,
            handleEditFolder,
            handleDeleteFolder,
            handleSetDefaultFolder,
            isUpdating,
        ],
    );

    const hasActiveFilters = contentTypeFilter !== '';

    const pageCount = Math.max(1, Math.ceil(filteredFolders.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: ApiRootFolder) => row.id, []);

    const emptyState = (
        <EmptyState
            icon={<FolderOpenIcon weight="duotone" />}
            title={hasActiveFilters ? 'No root folders found' : 'No root folders configured'}
            description={
                hasActiveFilters
                    ? 'No folders match the active filter. Adjust or clear the filter to see more.'
                    : 'A root folder determines where imported content is stored.'
            }
            action={
                hasActiveFilters ? (
                    <Button variant="secondary" size="sm" onClick={handleClearFilters}>
                        Clear filter
                    </Button>
                ) : undefined
            }
            size="sm"
        />
    );

    const errorState = (
        <EmptyState
            icon={<WarningCircleIcon weight="duotone" />}
            title="Failed to load root folders"
            action={
                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                    Retry
                </Button>
            }
            size="sm"
        />
    );

    return (
        <div className="root-folders-page" data-page="root-folders">
            <PullToRefresh onRefresh={handleRefresh} className="root-folders-page__pull-to-refresh">
                <PageHeader
                    className="root-folders-page__header"
                    title="Root Folders"
                    description="Locations where imported content is stored and scanned."
                    icon={<FolderOpenIcon weight="duotone" aria-hidden="true" data-testid="root-folders-page__icon" />}
                    actions={
                        <div className="root-folders-page__header-actions">
                            {hasActiveFilters && <ClearFiltersButton onClick={handleClearFilters} />}
                            <Button variant="primary" size="sm" data-action="add-folder" onClick={handleAddFolder}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="root-folders-page__add-label">Add Folder</span>
                            </Button>
                        </div>
                    }
                />

                <MonitoringDefaultPanel />

                <section className="root-folders-page__disk-space" aria-labelledby="root-folders-disk-space-heading">
                    <h2 id="root-folders-disk-space-heading" className="root-folders-page__section-title">
                        <HardDrivesIcon weight="duotone" aria-hidden="true" />
                        Disk Space
                    </h2>
                    <DiskSpaceWidget
                        heading="Root Folders"
                        icon={<HardDrivesIcon weight="duotone" />}
                        folders={diskSpaceFolders}
                        totals={diskSpaceTotals}
                        loading={isDiskSpaceLoading}
                        headingLevel={3}
                    />
                </section>

                <div className="root-folders-table" data-area="root-folders">
                    <DataTable<ApiRootFolder>
                        columns={columns}
                        data={filteredFolders}
                        enableSorting
                        sorting={sorting}
                        onSortingChange={(updater) =>
                            setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                        }
                        enablePagination={filteredFolders.length > PAGE_SIZE}
                        hidePagination
                        pageSize={PAGE_SIZE}
                        pagination={pagination}
                        getRowId={getRowId}
                        loading={isLoading}
                        emptyState={error ? errorState : emptyState}
                        keepHeaderWhenEmpty={hasActiveFilters}
                        aria-label="Root folders"
                    />
                    {filteredFolders.length > PAGE_SIZE && (
                        <Pager
                            className="root-folders-table__pager"
                            page={currentPage}
                            pageCount={pageCount}
                            onFirst={() => setPage(1)}
                            onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                            onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                            onLast={() => setPage(pageCount)}
                        />
                    )}
                </div>
            </PullToRefresh>

            <QuickActionsButton actions={addFolderActions} title="Add Folder" />

            <DeleteConfirmDialog
                open={!!deletingFolder}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingFolder?.name ?? ''}
                itemType="Root Folder"
                isDeleting={isDeleting}
            />

            <RootFolderForm
                open={formOpen}
                onOpenChange={handleFormClose}
                initialValues={currentEditingFolder}
                contentTypes={contentTypes}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
            />
        </div>
    );
}

RootFoldersPage.displayName = 'RootFoldersPage';

export default RootFoldersPage;
