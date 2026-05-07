import { useState, useMemo, useCallback, useRef } from 'react';
import { Database, Trash, PencilSimple, Clock, Plus, CircleNotch } from '@phosphor-icons/react';

import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Select, SelectTrigger, SelectContent, SelectItem } from '@/components/ui/Select';
import { useToast } from '@/components/ui/Toast';
import { Tooltip } from '@/components/ui/Tooltip';

import { useIndexers, type ApiIndexer, type IndexerAvailabilityValue } from '@/hooks/useIndexers';
import { useTestIndexer } from '@/hooks/useTestConnection';
import { useToggle } from '@/hooks/useToggle';
import { formatRelativeTime } from '@/lib/formatTime';

import { EmptyState } from '@/components/ui/EmptyState';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { IndexerForm, type IndexerFormValues } from './IndexerForm';

const availabilityLabels: Record<IndexerAvailabilityValue, string> = {
    0: 'Unknown',
    1: 'Public',
    2: 'Private',
    3: 'Semi-Private',
};

const availabilityVariants: Record<IndexerAvailabilityValue, string> = {
    0: 'unknown',
    1: 'healthy',
    2: 'unhealthy',
    3: 'degraded',
};

interface IndexerFilterState {
    availability: string;
    provider: string;
}

const ALL_VALUE = '__all__';

const availabilityOptions = [
    { value: '0', label: 'Unknown' },
    { value: '1', label: 'Public' },
    { value: '2', label: 'Private' },
    { value: '3', label: 'Semi-Private' },
];

const initialFilters: IndexerFilterState = { availability: '', provider: '' };

export function IndexerManagement() {
    const [filters, setFilters] = useState<IndexerFilterState>(initialFilters);
    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingIndexer, setEditingIndexer] = useState<IndexerFormValues | null>(null);
    const returnFocusRef = useRef<HTMLElement | null>(null);
    const [deletingIndexer, setDeletingIndexer] = useState<ApiIndexer | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const { toast } = useToast();

    const {
        indexers,
        isLoading,
        createIndexer: apiCreateIndexer,
        updateIndexer: apiUpdateIndexer,
        deleteIndexer: apiDeleteIndexer,
        isCreatingIndexer,
        isUpdatingIndexer,
    } = useIndexers();

    const { test: testIndexer, isPending: isTesting } = useTestIndexer();

    const filteredIndexers = useMemo(() => {
        let items = [...indexers];

        if (filters.availability) {
            items = items.filter((i) => i.availability === Number(filters.availability));
        }

        if (filters.provider) {
            items = items.filter((i) => i.providerType === filters.provider);
        }

        return items;
    }, [indexers, filters]);

    const availabilityCounts = useMemo(
        () => ({
            public: indexers.filter((i) => i.availability === 1).length,
            semiPrivate: indexers.filter((i) => i.availability === 3).length,
            private: indexers.filter((i) => i.availability === 2).length,
        }),
        [indexers],
    );

    const providerOptions = useMemo(() => {
        const unique = [...new Set(indexers.map((i) => i.providerType))].sort();
        return unique.map((p) => ({ value: p, label: p }));
    }, [indexers]);

    const hasActiveFilters = filters.availability !== '' || filters.provider !== '';

    const availabilityLabel = filters.availability
        ? (availabilityOptions.find((o) => o.value === filters.availability)?.label ?? 'Unknown')
        : 'All Availability';

    const providerLabel = filters.provider
        ? (providerOptions.find((o) => o.value === filters.provider)?.label ?? 'Unknown')
        : 'All Providers';

    const handleAvailabilityChange = useCallback((value: string) => {
        setFilters((prev) => ({
            ...prev,
            availability: value === ALL_VALUE ? '' : value,
        }));
    }, []);

    const handleProviderChange = useCallback((value: string) => {
        setFilters((prev) => ({
            ...prev,
            provider: value === ALL_VALUE ? '' : value,
        }));
    }, []);

    const handleClearFilters = useCallback(() => {
        setFilters(initialFilters);
    }, []);

    const handleTest = useCallback(
        async (id: string) => {
            const indexer = indexers.find((i) => i.id === id);
            const name = indexer?.name ?? 'Indexer';
            try {
                const result = await testIndexer(id);
                toast({
                    title: name,
                    description: result.message,
                    variant: result.success ? 'success' : 'error',
                });
            } catch (error) {
                toast({
                    title: name,
                    description: error instanceof Error ? error.message : 'Failed to test connection',
                    variant: 'error',
                });
            }
        },
        [indexers, testIndexer, toast],
    );

    const handleAdd = useCallback(() => {
        returnFocusRef.current = document.activeElement as HTMLElement;
        setEditingIndexer(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (id: string) => {
            const indexer = indexers.find((i) => i.id === id);
            if (indexer) {
                returnFocusRef.current = document.activeElement as HTMLElement;
                setEditingIndexer({
                    id: indexer.id,
                    name: indexer.name,
                    providerType: indexer.providerType,
                    description: indexer.description ?? undefined,
                });
                openForm();
            }
        },
        [indexers, openForm],
    );

    const handleDelete = useCallback(
        (id: string) => {
            const indexer = indexers.find((i) => i.id === id);
            if (!indexer) {
                return;
            }
            setDeletingIndexer(indexer);
        },
        [indexers],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingIndexer) {
            return;
        }
        setIsDeleting(true);

        try {
            await apiDeleteIndexer(deletingIndexer.id);
            toast({
                title: 'Indexer deleted',
                description: `"${deletingIndexer.name}" has been removed`,
                variant: 'success',
            });
        } catch (error) {
            toast({
                title: 'Error',
                description: error instanceof Error ? error.message : 'Failed to delete indexer',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingIndexer(null);
        }
    }, [deletingIndexer, apiDeleteIndexer, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingIndexer(null);
    }, []);

    const handleFormSubmit = useCallback(
        async (values: IndexerFormValues) => {
            try {
                if (values.id) {
                    await apiUpdateIndexer(values.id, {
                        name: values.name,
                        providerType: values.providerType,
                        description: values.description,
                    });
                    toast({
                        title: 'Indexer updated',
                        description: `${values.name} has been updated`,
                        variant: 'success',
                    });
                } else {
                    await apiCreateIndexer({
                        name: values.name,
                        providerType: values.providerType,
                        description: values.description,
                    });
                    toast({
                        title: 'Indexer created',
                        description: `${values.name} has been added`,
                        variant: 'success',
                    });
                }
                closeForm();
            } catch (error) {
                toast({
                    title: 'Error',
                    description: error instanceof Error ? error.message : 'Failed to save indexer',
                    variant: 'error',
                });
            }
        },
        [apiCreateIndexer, apiUpdateIndexer, toast, closeForm],
    );

    const addActions: QuickAction[] = useMemo(
        () => [{ id: 'add-indexer', label: 'Add Indexer', icon: <Plus weight="bold" />, onClick: handleAdd }],
        [handleAdd],
    );

    return (
        <>
            <div className="list-toolbar__summary" aria-label="Availability summary">
                <Badge variant="healthy" size="sm" badgeStyle="outline" dot count={availabilityCounts.public}>
                    Public
                </Badge>
                <Badge variant="degraded" size="sm" badgeStyle="outline" dot count={availabilityCounts.semiPrivate}>
                    Semi-Private
                </Badge>
                <Badge variant="unhealthy" size="sm" badgeStyle="outline" dot count={availabilityCounts.private}>
                    Private
                </Badge>
            </div>

            <div className="list-toolbar__filters" data-component="indexer-filters">
                <div className="list-toolbar__filters-controls">
                    <Select value={filters.availability || ALL_VALUE} onValueChange={handleAvailabilityChange}>
                        <SelectTrigger
                            size="sm"
                            placeholder="All Availability"
                            aria-label="Filter by availability"
                            data-filter="availability"
                        >
                            {availabilityLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All Availability</SelectItem>
                            {availabilityOptions.map((opt) => (
                                <SelectItem key={opt.value} value={opt.value}>
                                    {opt.label}
                                </SelectItem>
                            ))}
                        </SelectContent>
                    </Select>

                    <Select value={filters.provider || ALL_VALUE} onValueChange={handleProviderChange}>
                        <SelectTrigger
                            size="sm"
                            placeholder="All Providers"
                            aria-label="Filter by provider type"
                            data-filter="provider"
                        >
                            {providerLabel}
                        </SelectTrigger>
                        <SelectContent>
                            <SelectItem value={ALL_VALUE}>All Providers</SelectItem>
                            {providerOptions.map((opt) => (
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

            <div className="indexers-page__list" role="list" aria-label="Indexers">
                {isLoading ? (
                    <div className="indexers-page__loading">
                        <CircleNotch weight="bold" className="indexers-page__spinner" aria-hidden="true" />
                        <p>Loading indexers...</p>
                    </div>
                ) : filteredIndexers.length === 0 ? (
                    <EmptyState
                        icon={<Database weight="duotone" />}
                        title="No indexers found"
                        description={
                            hasActiveFilters
                                ? 'Try adjusting your filters'
                                : 'Add an indexer to start searching for content'
                        }
                        action={
                            !hasActiveFilters ? (
                                <Button variant="primary" size="sm" onClick={handleAdd} data-action="add-indexer-empty">
                                    <Plus weight="bold" aria-hidden="true" />
                                    <span>Add Indexer</span>
                                </Button>
                            ) : undefined
                        }
                    />
                ) : (
                    filteredIndexers.map((indexer) => (
                        <IndexerRow key={indexer.id} indexer={indexer} onEdit={handleEdit} onDelete={handleDelete} />
                    ))
                )}
            </div>

            <QuickActionsButton actions={addActions} title="Add Indexer" />

            <IndexerForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) {
                        closeForm();
                    }
                }}
                initialValues={editingIndexer}
                onSubmit={handleFormSubmit}
                isSubmitting={isCreatingIndexer || isUpdatingIndexer}
                onTest={editingIndexer?.id ? () => handleTest(editingIndexer.id!) : undefined}
                isTesting={isTesting}
                returnFocusRef={returnFocusRef}
            />

            <DeleteConfirmDialog
                open={!!deletingIndexer}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingIndexer?.name ?? ''}
                itemType="Indexer"
                isDeleting={isDeleting}
            />
        </>
    );
}

IndexerManagement.displayName = 'IndexerManagement';

interface IndexerRowProps {
    indexer: ApiIndexer;
    onEdit: (id: string) => void;
    onDelete: (id: string) => void;
}

function IndexerRow({ indexer, onEdit, onDelete }: IndexerRowProps) {
    const variant = availabilityVariants[indexer.availability];
    const availabilityLabel = availabilityLabels[indexer.availability];

    return (
        <div
            className="indexers-page__item"
            role="listitem"
            data-testid="indexers-page__item"
            data-indexer-id={indexer.id}
            data-availability={variant}
        >
            <div className="indexers-page__item-main">
                <div className="indexers-page__item-info">
                    <div className="indexers-page__item-header">
                        <span className="indexers-page__item-name">{indexer.name}</span>
                        <Badge variant={variant as Parameters<typeof Badge>[0]['variant']} size="sm" dot>
                            {availabilityLabel}
                        </Badge>
                    </div>
                    <div className="indexers-page__item-meta">
                        <span className="indexers-page__item-definition">{indexer.providerType}</span>
                        <span className="indexers-page__item-checked">
                            <Clock weight="regular" aria-hidden="true" />
                            Updated {formatRelativeTime(indexer.modifiedAt ?? indexer.createdAt)}
                        </span>
                    </div>
                </div>
                <div className="indexers-page__item-actions card-actions">
                    <Tooltip content="Edit">
                        <button
                            type="button"
                            className="indexers-page__action-btn card-actions__btn"
                            data-testid="indexers-page__action-btn"
                            onClick={() => onEdit(indexer.id)}
                            aria-label={`Edit ${indexer.name}`}
                            data-action="edit"
                            data-target={indexer.id}
                        >
                            <PencilSimple weight="bold" />
                        </button>
                    </Tooltip>
                    <Tooltip content="Delete">
                        <button
                            type="button"
                            className="indexers-page__action-btn indexers-page__action-btn--danger card-actions__btn card-actions__btn--danger"
                            data-testid="indexers-page__action-btn"
                            onClick={() => onDelete(indexer.id)}
                            aria-label={`Delete ${indexer.name}`}
                            data-action="delete"
                            data-target={indexer.id}
                        >
                            <Trash weight="bold" />
                        </button>
                    </Tooltip>
                </div>
            </div>
        </div>
    );
}
