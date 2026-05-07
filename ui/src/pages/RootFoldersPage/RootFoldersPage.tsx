import { useState, useCallback, useMemo } from 'react';
import { FolderOpen, Trash, PencilSimple, Plus } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useLibraryTypes, type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import { useRootFolders, type ApiRootFolder } from '@/hooks/useRootFolders';
import { useToggle } from '@/hooks/useToggle';

import { EmptyState } from '@/components/ui/EmptyState';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { Tooltip } from '@/components/ui/Tooltip';

import { RootFolderForm, type RootFolderFormValues } from './RootFolderForm';

import './RootFoldersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

function getContentTypeName(contentTypeId: string, contentTypes: LibraryTypeDefinition[]): string {
    const contentType = contentTypes.find((ct) => ct.id === contentTypeId);
    return contentType?.name ?? 'Unknown';
}

const ALL_VALUE = '__all__';

export function RootFoldersPage() {
    useDocumentTitle('Root Folders');
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingFolder, setEditingFolder] = useState<RootFolderFormValues | null>(null);
    const [deletingFolder, setDeletingFolder] = useState<ApiRootFolder | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const { contentTypes } = useLibraryTypes();

    const { folders, isLoading, createFolder, isCreating, updateFolder, isUpdating, deleteFolder, refetch } =
        useRootFolders();

    const isSubmitting = isCreating || isUpdating;

    const totalFolders = folders.length;

    const [contentTypeFilter, setContentTypeFilter] = useState('');

    const contentTypeOptions = useMemo(() => {
        const usedIds = [...new Set(folders.map((f) => f.contentTypeId))];
        return usedIds
            .map((id) => {
                const ct = contentTypes.find((c) => c.id === id);
                return { value: id, label: ct?.name ?? 'Unknown' };
            })
            .sort((a, b) => a.label.localeCompare(b.label));
    }, [folders, contentTypes]);

    const contentTypeCounts = useMemo(() => {
        const counts: Record<string, number> = {};
        for (const folder of folders) {
            const name = getContentTypeName(folder.contentTypeId, contentTypes);
            counts[name] = (counts[name] ?? 0) + 1;
        }
        return counts;
    }, [folders, contentTypes]);

    const filteredFolders = useMemo(() => {
        if (!contentTypeFilter) {
            return folders;
        }
        return folders.filter((f) => f.contentTypeId === contentTypeFilter);
    }, [folders, contentTypeFilter]);

    const handleContentTypeFilterChange = useCallback((value: string) => {
        setContentTypeFilter(value === ALL_VALUE ? '' : value);
    }, []);

    const handleClearFilters = useCallback(() => {
        setContentTypeFilter('');
    }, []);

    const hasActiveFilters = contentTypeFilter !== '';

    const contentTypeLabel = contentTypeFilter
        ? (contentTypeOptions.find((o) => o.value === contentTypeFilter)?.label ?? 'Unknown')
        : 'All Types';

    const handleAddFolder = useCallback(() => {
        setEditingFolder(null);
        openForm();
    }, [openForm]);

    const addFolderActions: QuickAction[] = useMemo(
        () => [{ id: 'add-folder', label: 'Add Folder', icon: <Plus weight="bold" />, onClick: handleAddFolder }],
        [handleAddFolder],
    );

    const handleEditFolder = useCallback(
        (id: string) => {
            const folder = folders.find((f) => f.id === id);
            if (folder) {
                setEditingFolder({
                    id: folder.id,
                    name: folder.name,
                    path: folder.path,
                    contentTypeId: folder.contentTypeId,
                });
                openForm();
            }
        },
        [folders, openForm],
    );

    const handleFormSubmit = useCallback(
        async (values: RootFolderFormValues) => {
            try {
                if (values.id) {
                    await updateFolder(values.id, {
                        name: values.name,
                        path: values.path,
                        contentTypeId: values.contentTypeId,
                    });
                    toast({
                        title: values.name,
                        description: 'Root folder updated successfully',
                        variant: 'success',
                    });
                } else {
                    await createFolder({
                        name: values.name,
                        path: values.path,
                        contentTypeId: values.contentTypeId,
                    });
                    toast({
                        title: values.name,
                        description: 'Root folder added successfully',
                        variant: 'success',
                    });
                }

                closeForm();
                setEditingFolder(null);
            } catch {
                toast({
                    title: values.name,
                    description: 'Failed to save root folder',
                    variant: 'error',
                });
            }
        },
        [toast, createFolder, updateFolder, closeForm],
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
            toast({
                title: deletingFolder.name,
                description: 'Root folder deleted successfully',
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
    }, [deletingFolder, deleteFolder, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingFolder(null);
    }, []);

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    return (
        <div className="root-folders-page" data-page="root-folders">
            <PullToRefresh onRefresh={handleRefresh} className="root-folders-page__pull-to-refresh">
                <header className="root-folders-page__header">
                    <div className="root-folders-page__header-left">
                        <FolderOpen
                            weight="duotone"
                            className="root-folders-page__icon"
                            aria-hidden="true"
                            data-testid="root-folders-page__icon"
                        />
                        <h1 className="root-folders-page__title">Root Folders</h1>
                        <span className="page-count root-folders-page__count" data-testid="root-folders-page__count">
                            {totalFolders}
                        </span>
                    </div>
                    <div className="root-folders-page__header-actions">
                        <Button variant="primary" size="sm" data-action="add-folder" onClick={handleAddFolder}>
                            <Plus weight="bold" aria-hidden="true" />
                            <span className="root-folders-page__add-label">Add Folder</span>
                        </Button>
                    </div>
                </header>

                {contentTypeOptions.length > 0 && (
                    <div className="list-toolbar__summary" aria-label="Content type summary">
                        {contentTypeOptions.map((opt) => (
                            <Badge
                                key={opt.value}
                                variant="info"
                                size="sm"
                                badgeStyle="outline"
                                count={contentTypeCounts[opt.label]}
                            >
                                {opt.label}
                            </Badge>
                        ))}
                    </div>
                )}

                {contentTypeOptions.length > 1 && (
                    <div className="list-toolbar__filters" data-component="folder-filters">
                        <div className="list-toolbar__filters-controls">
                            <Select
                                value={contentTypeFilter || ALL_VALUE}
                                onValueChange={handleContentTypeFilterChange}
                            >
                                <SelectTrigger
                                    size="sm"
                                    placeholder="All Types"
                                    aria-label="Filter by content type"
                                    data-filter="content-type"
                                >
                                    {contentTypeLabel}
                                </SelectTrigger>
                                <SelectContent>
                                    <SelectItem value={ALL_VALUE}>All Types</SelectItem>
                                    {contentTypeOptions.map((opt) => (
                                        <SelectItem key={opt.value} value={opt.value}>
                                            {opt.label}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>

                            {hasActiveFilters && (
                                <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={handleClearFilters}
                                    className="list-toolbar__clear-filters"
                                    data-action="clear-filters"
                                >
                                    Clear
                                </Button>
                            )}
                        </div>
                    </div>
                )}

                <div className="root-folders-page__list" role="list" aria-label="Root folders">
                    {isLoading ? (
                        <EmptyState title="Loading..." size="sm" />
                    ) : folders.length === 0 ? (
                        <EmptyState
                            icon={<FolderOpen weight="duotone" />}
                            title="No root folders configured"
                            description="Add a root folder to specify where your content is stored"
                            action={
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={handleAddFolder}
                                    data-action="add-folder-empty"
                                >
                                    <Plus weight="bold" aria-hidden="true" />
                                    <span>Add Folder</span>
                                </Button>
                            }
                        />
                    ) : filteredFolders.length === 0 ? (
                        <EmptyState
                            icon={<FolderOpen weight="duotone" />}
                            title="No root folders found"
                            description="Try adjusting your filters"
                        />
                    ) : (
                        filteredFolders.map((folder) => (
                            <FolderRow
                                key={folder.id}
                                folder={folder}
                                contentTypes={contentTypes}
                                onEdit={handleEditFolder}
                                onDelete={handleDeleteFolder}
                            />
                        ))
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
                initialValues={editingFolder}
                contentTypes={contentTypes}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
            />
        </div>
    );
}

RootFoldersPage.displayName = 'RootFoldersPage';

interface FolderRowProps {
    folder: ApiRootFolder;
    contentTypes: LibraryTypeDefinition[];
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
}

function FolderRow({ folder, contentTypes, onEdit, onDelete }: FolderRowProps) {
    const contentTypeName = getContentTypeName(folder.contentTypeId, contentTypes);

    return (
        <div className="root-folders-page__item" role="listitem" data-folder-id={folder.id}>
            <div className="root-folders-page__item-main">
                <div className="root-folders-page__item-info">
                    <div className="root-folders-page__item-header">
                        <span className="root-folders-page__item-name">{folder.name}</span>
                        <Badge
                            variant={contentTypeName === 'Unknown' ? 'unknown' : 'info'}
                            size="sm"
                            dot={contentTypeName === 'Unknown'}
                        >
                            {contentTypeName}
                        </Badge>
                    </div>
                    <div className="root-folders-page__item-meta">
                        <span className="root-folders-page__item-path">{folder.path}</span>
                    </div>
                </div>
                <div className="root-folders-page__item-actions card-actions">
                    <Tooltip content="Edit">
                        <button
                            type="button"
                            className="root-folders-page__action-btn card-actions__btn"
                            data-testid="root-folders-page__action-btn"
                            onClick={() => onEdit(folder.id)}
                            aria-label={`Edit ${folder.name}`}
                            data-action="edit"
                            data-target={folder.id}
                        >
                            <PencilSimple weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="root-folders-page__action-btn root-folders-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="root-folders-page__action-btn"
                            onClick={() => onDelete(folder.id)}
                            aria-label={`Delete ${folder.name}`}
                            data-action="delete"
                            data-target={folder.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}

export default RootFoldersPage;
