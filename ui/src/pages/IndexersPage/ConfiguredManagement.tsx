import { useState, useMemo, useCallback } from 'react';
import { DatabaseIcon, PlusIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { Pager } from '@/components/ui/Pager';
import { MultiSelectColumnFilter, encodeMultiValue, decodeMultiValue } from '@/components/ui/MultiSelectColumnFilter';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { useToast } from '@/components/ui/Toast';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { IntegrationEmptyState } from '@/components/integrations/IntegrationEmptyState';
import { IntegrationRowActions } from '@/components/integrations/IntegrationRowActions';

import {
    useIndexers,
    useAllIndexerConnections,
    useIndexerConnectionMutations,
    indexerStatusLabels,
    indexerStatusVariants,
    type ConfiguredConnectionRow,
} from '@/hooks/useIndexers';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { getErrorMessage } from '@/lib/errors';
import { formatRelativeTime } from '@/lib/formatTime';
import { mapStatusParam } from '@/lib/urlParams';

import { ConfigureInstanceDialog, type ConfigureConnectionTarget } from './ConfigureInstanceDialog';

interface ConfiguredManagementProps {
    onGoToCatalog: () => void;
}

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'indexer', 'status', 'lastChecked'] as const;

const DEFAULT_FILTERS = { provider: '', status: '', enabled: '' };

const enabledOptions: FacetOption[] = [
    { value: 'true', label: 'Enabled' },
    { value: 'false', label: 'Disabled' },
];

export function ConfiguredManagement({ onGoToCatalog }: ConfiguredManagementProps) {
    const { indexers, isLoadingIndexers } = useIndexers();
    const { rows, isLoading: isLoadingConnections } = useAllIndexerConnections(indexers);
    const { updateConnection, deleteConnection, testConnection } = useIndexerConnectionMutations();
    const { toast } = useToast();

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
        pageParam: 'cpage',
        sortParam: 'csort',
    });

    const provider = useMemo(() => decodeMultiValue(filters.provider), [filters.provider]);
    const status = useMemo(
        () => decodeMultiValue(filters.status).map(mapStatusParam).filter(Boolean),
        [filters.status],
    );
    const enabled = filters.enabled;

    const [editTarget, setEditTarget] = useState<ConfigureConnectionTarget | null>(null);
    const [deleting, setDeleting] = useState<ConfiguredConnectionRow | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [testingId, setTestingId] = useState<string | null>(null);

    const providerOptions = useMemo(() => [...new Set(rows.map((r) => r.indexer.providerType))].sort(), [rows]);
    const statusOptions = useMemo<FacetOption[]>(() => {
        const present = [...new Set(rows.map((r) => r.connection.status))];
        return present.map((s) => ({ value: s, label: indexerStatusLabels[s] }));
    }, [rows]);

    const filteredRows = useMemo(() => {
        return rows.filter((r) => {
            if (provider.length > 0 && !provider.includes(r.indexer.providerType)) {
                return false;
            }
            if (status.length > 0 && !status.includes(r.connection.status)) {
                return false;
            }
            if (enabled && r.connection.enabled !== (enabled === 'true')) {
                return false;
            }
            return true;
        });
    }, [rows, provider, status, enabled]);

    const setMulti = useCallback(
        (key: 'provider' | 'status', next: string[]) => {
            setFilters({ [key]: encodeMultiValue(next) });
        },
        [setFilters],
    );

    const handleClearFilters = useCallback(() => {
        setFilters({ provider: '', status: '', enabled: '' });
    }, [setFilters]);

    const handleTest = useCallback(
        async (row: ConfiguredConnectionRow) => {
            setTestingId(row.connection.id);
            try {
                const result = await testConnection(row.indexer.id, row.connection.id);
                toast({
                    title: row.connection.name,
                    description: result.message,
                    variant: result.success ? 'success' : 'error',
                });
            } catch (error) {
                toast({
                    title: row.connection.name,
                    description: error instanceof Error ? error.message : 'Failed to test connection',
                    variant: 'error',
                });
            } finally {
                setTestingId(null);
            }
        },
        [testConnection, toast],
    );

    const handleToggleEnabled = useCallback(
        async (row: ConfiguredConnectionRow, isEnabled: boolean) => {
            try {
                await updateConnection(row.indexer.id, row.connection.id, { enabled: { value: isEnabled } });
            } catch (error) {
                toast({
                    title: row.connection.name,
                    description: getErrorMessage(error, `Failed to ${isEnabled ? 'enable' : 'disable'} instance`),
                    variant: 'error',
                });
            }
        },
        [updateConnection, toast],
    );

    const handleConfirmDelete = useCallback(async () => {
        if (!deleting) {
            return;
        }
        setIsDeleting(true);
        try {
            await deleteConnection(deleting.indexer.id, deleting.connection.id);
            toast({
                title: 'Instance deleted',
                description: `Magpie removed "${deleting.connection.name}".`,
                variant: 'success',
            });
        } catch (error) {
            toast({
                title: 'Error',
                description: error instanceof Error ? error.message : 'Failed to delete instance',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeleting(null);
        }
    }, [deleting, deleteConnection, toast]);

    const columns = useMemo<ColumnDef<ConfiguredConnectionRow>[]>(
        () => [
            {
                id: 'name',
                accessorFn: (row) => row.connection.name,
                header: 'Name',
                size: 240,
                cell: ({ row }) => (
                    <span className="indexers-table__name" title={row.original.connection.name}>
                        <span className="indexers-table__name-text">{row.original.connection.name}</span>
                    </span>
                ),
            },
            {
                id: 'indexer',
                accessorFn: (row) => row.indexer.providerType,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Provider"
                        options={providerOptions}
                        value={provider}
                        onChange={(next) => setMulti('provider', next)}
                    />
                ),
                meta: { headerLabel: 'Provider' },
                size: 200,
                cell: ({ row }) => (
                    <span className="indexers-table__definition">{row.original.indexer.providerType}</span>
                ),
            },
            {
                id: 'status',
                accessorFn: (row) => row.connection.status,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Status"
                        options={statusOptions}
                        value={status}
                        onChange={(next) => setMulti('status', next)}
                    />
                ),
                meta: { headerLabel: 'Status' },
                size: 140,
                cell: ({ row }) => {
                    const s = row.original.connection.status;
                    return (
                        <Badge
                            variant={indexerStatusVariants[s] as Parameters<typeof Badge>[0]['variant']}
                            size="sm"
                            dot
                        >
                            {indexerStatusLabels[s]}
                        </Badge>
                    );
                },
            },
            {
                id: 'enabled',
                accessorFn: (row) => row.connection.enabled,
                enableSorting: false,
                header: () => (
                    <ColumnHeaderFilter
                        type="facet"
                        label="Enabled"
                        active={enabled !== ''}
                        options={enabledOptions}
                        value={enabled}
                        onChange={(value) => setFilters({ enabled: value })}
                    />
                ),
                meta: { headerLabel: 'Enabled' },
                size: 140,
                cell: ({ row }) => (
                    <Badge variant={row.original.connection.enabled ? 'healthy' : 'unknown'} size="sm">
                        {row.original.connection.enabled ? 'Enabled' : 'Disabled'}
                    </Badge>
                ),
            },
            {
                id: 'lastChecked',
                accessorFn: (row) => {
                    const raw = row.connection.lastChecked;
                    const time = raw ? new Date(raw).getTime() : NaN;
                    return isNaN(time) ? undefined : time;
                },
                sortUndefined: 'last',
                header: 'Checked',
                size: 150,
                cell: ({ row }) => (
                    <span className="indexers-table__checked">
                        {formatRelativeTime(row.original.connection.lastChecked)}
                    </span>
                ),
            },
            {
                id: 'actions',
                header: '',
                enableSorting: false,
                size: 180,
                cell: ({ row }) => (
                    <IntegrationRowActions
                        prefix="indexers-page"
                        name={row.original.connection.name}
                        id={row.original.connection.id}
                        enabled={row.original.connection.enabled}
                        isTesting={testingId === row.original.connection.id}
                        onTest={() => handleTest(row.original)}
                        onToggleEnabled={(value) => handleToggleEnabled(row.original, value)}
                        onEdit={() =>
                            setEditTarget({
                                indexerId: row.original.indexer.id,
                                providerType: row.original.indexer.providerType,
                                connection: row.original.connection,
                            })
                        }
                        onDelete={() => setDeleting(row.original)}
                    />
                ),
            },
        ],
        [
            providerOptions,
            statusOptions,
            provider,
            status,
            enabled,
            setMulti,
            setFilters,
            testingId,
            handleTest,
            handleToggleEnabled,
        ],
    );

    const isLoading = isLoadingIndexers || isLoadingConnections;
    const hasActiveFilters = provider.length > 0 || status.length > 0 || enabled !== '';

    const pageCount = Math.max(1, Math.ceil(filteredRows.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: ConfiguredConnectionRow) => row.connection.id, []);

    const addActions: QuickAction[] = useMemo(
        () => [{ id: 'add-indexer', label: 'Add Indexer', icon: <PlusIcon weight="bold" />, onClick: onGoToCatalog }],
        [onGoToCatalog],
    );

    const emptyState = (
        <IntegrationEmptyState
            icon={<DatabaseIcon weight="duotone" />}
            entity="indexers"
            hasActiveFilters={hasActiveFilters}
            addDescription="Add an indexer from the Available catalog to start searching for content"
            onClearFilters={handleClearFilters}
            size="sm"
        />
    );

    return (
        <>
            <p className="indexers-page__area-description">Configured instance of an indexer.</p>

            <div className="indexers-page__header-actions">
                {hasActiveFilters && <ClearFiltersButton onClick={handleClearFilters} />}
                <Button variant="primary" size="sm" data-action="add-indexer" onClick={onGoToCatalog}>
                    <PlusIcon weight="bold" aria-hidden="true" />
                    <span>Add Indexer</span>
                </Button>
            </div>

            <div className="indexers-table" data-area="configured">
                <DataTable<ConfiguredConnectionRow>
                    columns={columns}
                    data={filteredRows}
                    enableSorting
                    sorting={sorting}
                    onSortingChange={(updater) =>
                        setSorting(typeof updater === 'function' ? updater(sorting) : updater)
                    }
                    enablePagination={filteredRows.length > PAGE_SIZE}
                    hidePagination
                    pageSize={PAGE_SIZE}
                    pagination={pagination}
                    getRowId={getRowId}
                    loading={isLoading}
                    emptyState={emptyState}
                    keepHeaderWhenEmpty={hasActiveFilters}
                    aria-label="Configured indexers"
                />
                {filteredRows.length > PAGE_SIZE && (
                    <Pager
                        className="indexers-table__pager"
                        page={currentPage}
                        pageCount={pageCount}
                        onFirst={() => setPage(1)}
                        onPrevious={() => setPage(Math.max(1, currentPage - 1))}
                        onNext={() => setPage(Math.min(pageCount, currentPage + 1))}
                        onLast={() => setPage(pageCount)}
                    />
                )}
            </div>

            <QuickActionsButton actions={addActions} title="Add Indexer" />

            <ConfigureInstanceDialog
                open={!!editTarget}
                onOpenChange={(open) => {
                    if (!open) {
                        setEditTarget(null);
                    }
                }}
                editTarget={editTarget}
            />

            <DeleteConfirmDialog
                open={!!deleting}
                onConfirm={handleConfirmDelete}
                onCancel={() => setDeleting(null)}
                itemName={deleting?.connection.name ?? ''}
                itemType="Indexer Instance"
                isDeleting={isDeleting}
            />
        </>
    );
}

ConfiguredManagement.displayName = 'ConfiguredManagement';

export default ConfiguredManagement;
