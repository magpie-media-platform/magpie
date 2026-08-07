import { useCallback, useMemo, useState } from 'react';
import { BellIcon, BellSimpleSlashIcon, PlusIcon, UserIcon, WarningCircleIcon } from '@phosphor-icons/react';
import { type ColumnDef, type SortingState } from '@tanstack/react-table';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { ClearFiltersButton } from '@/components/ui/ClearFiltersButton';
import { ColumnHeaderFilter, type FacetOption } from '@/components/ui/ColumnHeaderFilter';
import { DataTable } from '@/components/ui/DataTable';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { MultiSelectColumnFilter, encodeMultiValue, decodeMultiValue } from '@/components/ui/MultiSelectColumnFilter';
import { PageHeader } from '@/components/ui/PageHeader';
import { Pager } from '@/components/ui/Pager';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { useToast } from '@/components/ui/Toast';
import { EmptyState } from '@/components/ui/EmptyState';
import { IntegrationEmptyState } from '@/components/integrations/IntegrationEmptyState';
import { IntegrationRowActions } from '@/components/integrations/IntegrationRowActions';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useNotificationProviders, useNotificationRules, type NotificationRule } from '@/hooks/useNotificationRules';
import { useToggle } from '@/hooks/useToggle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useUserMap } from '@/hooks/useUserMap';
import { extractValidationResultFromError } from '@/hooks/useValidateSettings';
import { isApiError } from '@/lib/api';
import { editableCriteriaToEventContext, eventContextToEditableCriteria } from '@/lib/eventContextCriteria';
import { formatRelativeTime } from '@/lib/formatTime';
import type { ApiEventFilter } from '@/hooks/useEventTriggers';

import {
    NotificationRuleForm,
    type NotificationContextFilter,
    type NotificationRuleFormValues,
} from './NotificationRuleForm';

import './NotificationsPage.css';
import '@/styles/card-actions.css';

function testFailureReason(error: unknown): string {
    if (isApiError(error)) {
        const details = error.details;
        if (details && typeof details === 'object' && 'detail' in details) {
            const detail = (details as { detail?: unknown }).detail;
            if (typeof detail === 'string' && detail.trim() !== '') {
                return detail;
            }
        }
        if (error.message.trim() !== '') {
            return error.message;
        }
    } else if (error instanceof Error && error.message.trim() !== '') {
        return error.message;
    }
    return 'The provider did not give a reason.';
}

function ruleToFormValues(rule: NotificationRule): NotificationRuleFormValues {
    const filters: NotificationContextFilter[] = eventContextToEditableCriteria(rule.eventFilter.eventContext);
    return {
        id: rule.id,
        name: rule.name,
        providerType: rule.providerType,
        eventNamePattern: rule.eventFilter.eventName?.value ?? '',
        contextFilters: filters,
        settings: rule.settings ?? {},
        enabled: rule.enabled,
    };
}

function formValuesToEventFilter(values: NotificationRuleFormValues): ApiEventFilter {
    return {
        eventName: values.eventNamePattern ? { operator: 'regex', value: values.eventNamePattern } : undefined,
        eventContext: editableCriteriaToEventContext(values.contextFilters),
    };
}

interface NotificationRuleRow {
    rule: NotificationRule;
    ownerName: string;
}

const PAGE_SIZE = 25;

const DEFAULT_SORTING: SortingState = [{ id: 'name', desc: false }];

const SORT_KEYS = ['name', 'provider', 'owner', 'enabled', 'modified'] as const;

const DEFAULT_FILTERS = { provider: '', enabled: '' };

const enabledOptions: FacetOption[] = [
    { value: 'true', label: 'Enabled' },
    { value: 'false', label: 'Disabled' },
];

export function NotificationsPage() {
    useDocumentTitle('Notifications');
    const { toast } = useToast();
    const userMap = useUserMap();

    const {
        rules,
        isLoading,
        error,
        refetch,
        createRule,
        isCreating,
        updateRule,
        isUpdating,
        deleteRule,
        testRule,
        isTesting,
    } = useNotificationRules();
    const {
        providers,
        isLoading: isProvidersLoading,
        error: providersError,
        refetch: refetchProviders,
    } = useNotificationProviders();

    const { sorting, filters, page, setSorting, setFilters, setPage } = useUrlTableState({
        defaultSort: DEFAULT_SORTING,
        defaultFilters: DEFAULT_FILTERS,
        sortKeys: SORT_KEYS,
    });

    const provider = useMemo(() => decodeMultiValue(filters.provider), [filters.provider]);
    const enabled = filters.enabled;

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingRule, setEditingRule] = useState<NotificationRule | null>(null);
    const [deletingRule, setDeletingRule] = useState<NotificationRule | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [togglingIds, setTogglingIds] = useState<Set<string>>(new Set());
    const [testingId, setTestingId] = useState<string | null>(null);
    const currentEditingRule = useMemo(() => {
        if (!editingRule) {
            return null;
        }
        return rules.find((candidate) => candidate.id === editingRule.id) ?? editingRule;
    }, [editingRule, rules]);

    const isSubmitting = isCreating || isUpdating;

    const handleAdd = useCallback(() => {
        setEditingRule(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (rule: NotificationRule) => {
            setEditingRule(rule);
            openForm();
        },
        [openForm],
    );

    const handleFormOpenChange = useCallback(
        (open: boolean) => {
            if (!open) {
                closeForm();
                setEditingRule(null);
            }
        },
        [closeForm],
    );

    const handleFormSubmit = useCallback(
        async (values: NotificationRuleFormValues) => {
            const isNew = !values.id;
            const eventFilter = formValuesToEventFilter(values);
            try {
                if (isNew) {
                    await createRule({
                        name: values.name,
                        providerType: values.providerType,
                        eventFilter,
                        settings: values.settings,
                        enabled: values.enabled,
                    });
                } else {
                    const settings = Object.keys(values.settings).length > 0 ? values.settings : undefined;
                    await updateRule({
                        id: values.id!,
                        name: values.name,
                        providerType: values.providerType,
                        eventFilter,
                        settings,
                        enabled: values.enabled,
                        ownerUserId: currentEditingRule?.ownerUserId ?? null,
                    });
                }
                toast({
                    title: isNew ? 'Rule Created' : 'Rule Updated',
                    description: `Magpie ${isNew ? 'created' : 'updated'} "${values.name}".`,
                    variant: 'success',
                });
                closeForm();
                setEditingRule(null);
            } catch (error) {
                if (extractValidationResultFromError(error)) {
                    throw error;
                }
                toast({
                    title: isNew ? 'Create Failed' : 'Update Failed',
                    description: `Failed to ${isNew ? 'create' : 'update'} the notification rule.`,
                    variant: 'error',
                });
                throw error;
            }
        },
        [createRule, updateRule, closeForm, currentEditingRule, toast],
    );

    const handleDelete = useCallback((rule: NotificationRule) => {
        setDeletingRule(rule);
    }, []);

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingRule) {
            return;
        }
        setIsDeleting(true);
        try {
            await deleteRule(deletingRule.id);
            toast({
                title: 'Rule Deleted',
                description: `Magpie deleted "${deletingRule.name}".`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete Failed',
                description: `Failed to delete "${deletingRule.name}".`,
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingRule(null);
        }
    }, [deletingRule, deleteRule, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingRule(null);
    }, []);

    const handleToggle = useCallback(
        async (rule: NotificationRule, isEnabled: boolean) => {
            setTogglingIds((prev) => new Set([...prev, rule.id]));
            try {
                await updateRule({
                    id: rule.id,
                    name: rule.name,
                    providerType: rule.providerType,
                    eventFilter: rule.eventFilter,
                    enabled: isEnabled,
                    ownerUserId: rule.ownerUserId ?? null,
                });
            } catch {
                toast({
                    title: rule.name,
                    description: 'Failed to update notification rule',
                    variant: 'error',
                });
            } finally {
                setTogglingIds((prev) => {
                    const next = new Set(prev);
                    next.delete(rule.id);
                    return next;
                });
            }
        },
        [updateRule, toast],
    );

    const handleTest = useCallback(
        async (rule: NotificationRule) => {
            setTestingId(rule.id);
            try {
                await testRule(rule.id);
                toast({
                    title: 'Test Dispatched',
                    description: `Sent a synthetic event through "${rule.name}".`,
                    variant: 'success',
                });
            } catch (error) {
                toast({
                    title: 'Test Failed',
                    description: `Failed to test "${rule.name}": ${testFailureReason(error)}`,
                    variant: 'error',
                });
            } finally {
                setTestingId(null);
            }
        },
        [testRule, toast],
    );

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const allRows = useMemo<NotificationRuleRow[]>(
        () =>
            rules.map((rule) => ({
                rule,
                ownerName: rule.ownerUserId ? (userMap.get(rule.ownerUserId) ?? 'Unknown') : 'System',
            })),
        [rules, userMap],
    );

    const filteredRows = useMemo(() => {
        return allRows.filter(({ rule }) => {
            if (provider.length > 0 && !provider.includes(rule.providerType)) {
                return false;
            }
            if (enabled && rule.enabled !== (enabled === 'true')) {
                return false;
            }
            return true;
        });
    }, [allRows, provider, enabled]);

    const providerOptions = useMemo(() => [...new Set(rules.map((r) => r.providerType))].sort(), [rules]);

    const setMulti = useCallback(
        (key: 'provider', next: string[]) => {
            setFilters({ [key]: encodeMultiValue(next) });
        },
        [setFilters],
    );

    const handleClearFilters = useCallback(() => {
        setFilters({ provider: '', enabled: '' });
    }, [setFilters]);

    const initialFormValues = useMemo(
        () => (currentEditingRule ? ruleToFormValues(currentEditingRule) : null),
        [currentEditingRule],
    );

    const quickActions: QuickAction[] = useMemo(
        () => [{ id: 'add-rule', label: 'Add Rule', icon: <PlusIcon weight="bold" />, onClick: handleAdd }],
        [handleAdd],
    );

    const columns = useMemo<ColumnDef<NotificationRuleRow>[]>(
        () => [
            {
                id: 'name',
                accessorFn: (row) => row.rule.name,
                header: 'Name',
                size: 260,
                cell: ({ row }) => {
                    const pattern = row.original.rule.eventFilter.eventName?.value;
                    return (
                        <div className="notifications-table__name-cell">
                            <span className="notifications-table__name" title={row.original.rule.name}>
                                {row.original.rule.name}
                            </span>
                            {pattern && (
                                <code
                                    className="notifications-table__pattern"
                                    data-testid="notifications-page__item-pattern"
                                    title={pattern}
                                >
                                    {pattern}
                                </code>
                            )}
                        </div>
                    );
                },
            },
            {
                id: 'provider',
                accessorFn: (row) => row.rule.providerType,
                header: () => (
                    <MultiSelectColumnFilter
                        label="Provider"
                        options={providerOptions}
                        value={provider}
                        onChange={(next) => setMulti('provider', next)}
                    />
                ),
                meta: { headerLabel: 'Provider' },
                size: 180,
                cell: ({ row }) => (
                    <Badge variant="info" size="sm">
                        {row.original.rule.providerType}
                    </Badge>
                ),
            },
            {
                id: 'owner',
                accessorFn: (row) => row.ownerName,
                header: 'Owner',
                size: 160,
                cell: ({ row }) => (
                    <span className="notifications-table__owner">
                        <UserIcon weight="regular" aria-hidden="true" />
                        {row.original.ownerName}
                    </span>
                ),
            },
            {
                id: 'enabled',
                accessorFn: (row) => row.rule.enabled,
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
                size: 110,
                cell: ({ row }) => (
                    <Badge variant={row.original.rule.enabled ? 'healthy' : 'unknown'} size="sm">
                        {row.original.rule.enabled ? 'Enabled' : 'Disabled'}
                    </Badge>
                ),
            },
            {
                id: 'modified',
                accessorFn: (row) => {
                    const raw = row.rule.modifiedAt || row.rule.createdAt;
                    const time = raw ? new Date(raw).getTime() : NaN;
                    return isNaN(time) ? undefined : time;
                },
                sortUndefined: 'last',
                header: 'Modified',
                size: 150,
                cell: ({ row }) => (
                    <span className="notifications-table__modified">
                        {formatRelativeTime(row.original.rule.modifiedAt || row.original.rule.createdAt)}
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
                        prefix="notifications-page"
                        name={row.original.rule.name}
                        id={row.original.rule.id}
                        enabled={row.original.rule.enabled}
                        isTesting={
                            (isTesting && testingId === row.original.rule.id) || togglingIds.has(row.original.rule.id)
                        }
                        onTest={() => handleTest(row.original.rule)}
                        onToggleEnabled={(value) => handleToggle(row.original.rule, value)}
                        onEdit={() => handleEdit(row.original.rule)}
                        onDelete={() => handleDelete(row.original.rule)}
                    />
                ),
            },
        ],
        [
            providerOptions,
            provider,
            enabled,
            setMulti,
            setFilters,
            isTesting,
            testingId,
            togglingIds,
            handleTest,
            handleToggle,
            handleEdit,
            handleDelete,
        ],
    );

    const hasActiveFilters = provider.length > 0 || enabled !== '';

    const pageCount = Math.max(1, Math.ceil(filteredRows.length / PAGE_SIZE));
    const currentPage = Math.min(page, pageCount);
    const pagination = useMemo(() => ({ pageIndex: currentPage - 1, pageSize: PAGE_SIZE }), [currentPage]);

    const getRowId = useCallback((row: NotificationRuleRow) => row.rule.id, []);

    const emptyState = (
        <IntegrationEmptyState
            icon={<BellSimpleSlashIcon weight="duotone" />}
            entity="notification rules"
            hasActiveFilters={hasActiveFilters}
            addDescription="Create a rule to route matching events through a provider."
            onClearFilters={handleClearFilters}
        />
    );

    const errorState = (
        <EmptyState
            icon={<WarningCircleIcon weight="duotone" />}
            title="Failed to load notification rules"
            action={
                <Button variant="secondary" size="sm" onClick={() => refetch()}>
                    Retry
                </Button>
            }
        />
    );

    return (
        <div className="notifications-page" data-page="notifications">
            <PullToRefresh onRefresh={handleRefresh} className="notifications-page__pull-to-refresh">
                <PageHeader
                    className="notifications-page__header"
                    title="Notifications"
                    description="Magpie events can be sent through configured notification providers."
                    icon={<BellIcon weight="duotone" aria-hidden="true" data-testid="notifications-page__icon" />}
                    actions={
                        <div className="notifications-page__header-actions">
                            {hasActiveFilters && <ClearFiltersButton onClick={handleClearFilters} />}
                            <Button variant="primary" size="sm" data-action="add-rule" onClick={handleAdd}>
                                <PlusIcon weight="bold" aria-hidden="true" />
                                <span className="notifications-page__add-label">Add Rule</span>
                            </Button>
                        </div>
                    }
                />

                <div className="notifications-table" data-area="rules">
                    <DataTable<NotificationRuleRow>
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
                        emptyState={error ? errorState : emptyState}
                        keepHeaderWhenEmpty={hasActiveFilters}
                        aria-label="Notification rules"
                    />
                    {filteredRows.length > PAGE_SIZE && (
                        <Pager
                            className="notifications-table__pager"
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

            <QuickActionsButton actions={quickActions} title="Add Rule" />

            <NotificationRuleForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={initialFormValues}
                providers={providers}
                isProvidersLoading={isProvidersLoading}
                providersError={providersError}
                onRetryProviders={() => void refetchProviders()}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
            />

            <DeleteConfirmDialog
                open={!!deletingRule}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingRule?.name ?? ''}
                itemType="Notification Rule"
                isDeleting={isDeleting}
            />
        </div>
    );
}

NotificationsPage.displayName = 'NotificationsPage';

export default NotificationsPage;
