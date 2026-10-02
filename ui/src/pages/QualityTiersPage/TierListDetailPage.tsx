import { useMemo, useCallback, useEffect, useState, useRef } from 'react';
import { useParams } from 'react-router-dom';
import { FunnelIcon, PlusIcon, SlidersHorizontalIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { Alert } from '@/components/ui/Alert';
import { Breadcrumb, type BreadcrumbItem } from '@/components/ui/Breadcrumb';
import { Button } from '@/components/ui/Button';
import { DeleteConfirmDialog } from '@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { useToast } from '@/components/ui/Toast';
import { UnsavedChangesDialog } from '@/components/ui/UnsavedChangesDialog';
import { useSortableList } from '@/components/ui/SortableList';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useExternalChangeConflict } from '@/hooks/useExternalChangeConflict';
import { stripBlankRows, useFormDirty } from '@/hooks/useFormDirty';
import {
    useQualityTierList,
    useQualityTierLists,
    useQualityTiers,
    type ApiQualityTierRule,
    type QualityTierListPatch,
    type QualityTierListRankPatch,
} from '@/hooks/useQualityTierLists';
import { useUnsavedChangesGuard } from '@/hooks/useUnsavedChangesGuard';
import { getErrorMessage, isNotFoundError } from '@/lib/errors';

import { isBlankCriterion } from './criteriaEditorUtils';
import { ListRulesSheet } from './ListRulesSheet';
import { type ExclusionDraft } from './RulesEditor';
import { normalizeExclusions } from './rulesEditorUtils';
import { TierForm, type Criterion, type TierFormValues } from './TierForm';
import { TierListDetailFallback } from './TierListDetailFallback';
import { TierListDetailHeader } from './TierListDetailHeader';
import { TierListDetailSkeleton } from './TierListDetailSkeleton';
import {
    mapCriteriaToApi,
    mapListRulesToApi,
    mapTierListToDisplay,
    mapTierRulesToApi,
    type TierWithCriteria,
} from './tierListDetailMappers';
import { TierListTiersSection } from './TierListTiersSection';

import '@/styles/card-actions.css';
import './TierListDetailPage.css';

const BASE_BREADCRUMBS: BreadcrumbItem[] = [
    { id: 'quality-tiers', label: 'Quality Tiers', to: '/admin/quality-tiers' },
];

function normalizeCriteria(criteria: readonly Criterion[]): Criterion[] {
    return stripBlankRows(criteria, isBlankCriterion);
}

export function TierListDetailPage() {
    useDocumentTitle('Tier List');
    const { tierListId } = useParams<{ tierListId: string }>();
    const { hasRole } = useAuth();
    const canManage = hasRole(Role.Manager);
    const { toast } = useToast();
    const [deletingTier, setDeletingTier] = useState<{ id: string; name: string } | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);

    const [formOpen, setFormOpen] = useState(false);
    const [editingTier, setEditingTier] = useState<TierFormValues | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [cloningTierId, setCloningTierId] = useState<string | null>(null);

    const [listRulesOpen, setListRulesOpen] = useState(false);
    const [listCriteriaDraft, setListCriteriaDraft] = useState<Criterion[]>([]);
    const [listExclusionsDraft, setListExclusionsDraft] = useState<ExclusionDraft[]>([]);
    const [isSubmittingListRules, setIsSubmittingListRules] = useState(false);
    const [showListRulesUnsaved, setShowListRulesUnsaved] = useState(false);
    const [showListRulesStaleSave, setShowListRulesStaleSave] = useState(false);
    const cleanListCriteriaRef = useRef<Criterion[]>([]);
    const cleanListExclusionsRef = useRef<ExclusionDraft[]>([]);
    const cleanListSourceRulesRef = useRef<ApiQualityTierRule[]>([]);

    const { data: apiTierList, isLoading, error, refetch } = useQualityTierList(tierListId);
    const { tierLists, error: tierListsError, refetch: refetchTierLists, updateTierList } = useQualityTierLists();
    const { createTier, updateTier, deleteTier } = useQualityTiers();

    const tierList = useMemo(() => {
        if (!apiTierList) {
            return null;
        }
        return mapTierListToDisplay(apiTierList);
    }, [apiTierList]);
    const sharedListNames = useMemo(() => {
        if (!tierLists.some((list) => list.id === tierListId)) {
            return null;
        }
        const names = new Map<string, string[]>();
        for (const list of tierLists) {
            if (list.id === tierListId) {
                continue;
            }
            for (const rank of list.qualityTierRanks) {
                names.set(rank.qualityTierId, [...(names.get(rank.qualityTierId) ?? []), list.name]);
            }
        }
        return names;
    }, [tierLists, tierListId]);
    const currentEditingTier = useMemo(() => {
        if (!editingTier?.id) {
            return editingTier;
        }
        const tier = tierList?.tiers.find((candidate) => candidate.id === editingTier.id);
        if (!tier) {
            return editingTier;
        }
        return {
            id: tier.id,
            name: tier.name,
            isCutoff: tier.isCutoff,
            criteria: tier.criteria,
            exclusions: tier.exclusions,
            sourceRules: tier.sourceRules,
        };
    }, [editingTier, tierList]);

    const breadcrumbs: BreadcrumbItem[] = useMemo(() => {
        const items: BreadcrumbItem[] = [{ id: 'quality-tiers', label: 'Quality Tiers', to: '/admin/quality-tiers' }];

        if (tierList) {
            items.push({
                id: tierList.id,
                label: tierList.name,
                to: `/admin/quality-tiers/${tierList.id}`,
            });
        }

        return items;
    }, [tierList]);

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    const handleSaveMimeTypes = useCallback(
        async (mimeTypes: string[]) => {
            if (!tierListId) {
                throw new Error('Tier list is unavailable');
            }
            await updateTierList(tierListId, { mimeTypes: { value: mimeTypes } });
            await refetch();
        },
        [refetch, tierListId, updateTierList],
    );

    const handleAddTier = useCallback(() => {
        setEditingTier(null);
        setFormOpen(true);
    }, []);

    const handleEditTier = useCallback(
        (tierId: string) => {
            if (!tierList) {
                return;
            }
            const tier = tierList.tiers.find((t) => t.id === tierId);
            if (tier) {
                setEditingTier({
                    id: tier.id,
                    name: tier.name,
                    isCutoff: tier.isCutoff,
                    criteria: tier.criteria,
                    exclusions: tier.exclusions,
                    sourceRules: tier.sourceRules,
                });
                setFormOpen(true);
            }
        },
        [tierList],
    );

    const handleFormOpenChange = useCallback((open: boolean) => {
        if (!open) {
            setFormOpen(false);
            setEditingTier(null);
        }
    }, []);

    const handleFormSubmit = useCallback(
        async (values: TierFormValues) => {
            if (!tierListId || !tierList) {
                throw new Error('Tier list is unavailable');
            }

            setIsSubmitting(true);
            const criteria = mapCriteriaToApi(values.criteria);
            const rules = mapTierRulesToApi(values.exclusions ?? [], values.sourceRules ?? []);

            try {
                if (values.id) {
                    await updateTier(values.id, {
                        name: { value: values.name },
                        criteria: { value: criteria },
                        rules: { value: rules },
                    });

                    const existing = tierList.tiers.find((t) => t.id === values.id);
                    if (existing && existing.isCutoff !== values.isCutoff) {
                        await updateTierList(tierListId, {
                            qualityTierRanks: {
                                update: [{ qualityTierId: { value: values.id }, isCutoff: { value: values.isCutoff } }],
                            },
                        });
                    }

                    await refetch();
                    toast({
                        title: 'Updated',
                        description: `Updated tier "${values.name}"`,
                        variant: 'success',
                    });
                } else {
                    const newTier = await createTier({
                        name: values.name,
                        criteria,
                        rules,
                    });

                    const nextOrdinal = tierList.tiers.length;
                    const patch: QualityTierListPatch = {
                        qualityTierRanks: {
                            add: [
                                {
                                    qualityTierId: newTier.id,
                                    ordinal: nextOrdinal,
                                    isCutoff: values.isCutoff,
                                },
                            ],
                        },
                    };

                    await updateTierList(tierListId, patch);
                    await refetch();
                    toast({
                        title: 'Created',
                        description: `Added tier "${values.name}"`,
                        variant: 'success',
                    });
                }

                setFormOpen(false);
                setEditingTier(null);
            } catch (error) {
                toast({
                    title: 'Error',
                    description: `Failed to ${values.id ? 'update' : 'create'} tier`,
                    variant: 'error',
                });
                throw error;
            } finally {
                setIsSubmitting(false);
            }
        },
        [tierListId, tierList, createTier, updateTier, updateTierList, refetch, toast],
    );

    const handleDeleteTier = useCallback((tierId: string, tierName: string) => {
        setDeletingTier({ id: tierId, name: tierName });
    }, []);

    const handleConfirmDelete = useCallback(async () => {
        if (!deletingTier || !tierListId) {
            return;
        }

        setIsDeleting(true);

        try {
            const patch: QualityTierListPatch = {
                qualityTierRanks: {
                    remove: [deletingTier.id],
                },
            };
            await updateTierList(tierListId, patch);

            let tierRowKept = false;
            if (sharedListNames && !sharedListNames.has(deletingTier.id)) {
                try {
                    await deleteTier(deletingTier.id);
                } catch {
                    tierRowKept = true;
                }
            }

            await refetch();

            if (tierRowKept) {
                toast({
                    title: 'Tier kept',
                    description: `"${deletingTier.name}" is no longer in this list. It is still available for other lists.`,
                    variant: 'warning',
                });
                return;
            }

            toast({
                title: 'Deleted',
                description: `Removed tier "${deletingTier.name}"`,
                variant: 'success',
            });
        } catch {
            toast({
                title: 'Delete failed',
                description: 'Failed to delete the tier.',
                variant: 'error',
            });
        } finally {
            setIsDeleting(false);
            setDeletingTier(null);
        }
    }, [deletingTier, tierListId, sharedListNames, updateTierList, deleteTier, refetch, toast]);

    const handleCancelDelete = useCallback(() => {
        setDeletingTier(null);
    }, []);

    const handleToggleCutoff = useCallback(
        async (tierId: string) => {
            if (!tierListId || !tierList) {
                return;
            }

            const tier = tierList.tiers.find((t) => t.id === tierId);
            if (!tier) {
                return;
            }
            const nextCutoff = !tier.isCutoff;

            try {
                const patch: QualityTierListPatch = {
                    qualityTierRanks: {
                        update: [{ qualityTierId: { value: tierId }, isCutoff: { value: nextCutoff } }],
                    },
                };
                await updateTierList(tierListId, patch);
                await refetch();

                toast({
                    title: 'Cutoff Updated',
                    description: nextCutoff ? `Set "${tier.name}" as cutoff` : `Cleared cutoff on "${tier.name}"`,
                    variant: 'success',
                });
            } catch {
                toast({
                    title: 'Update failed',
                    description: 'Failed to update the cutoff.',
                    variant: 'error',
                });
            }
        },
        [tierListId, tierList, updateTierList, refetch, toast],
    );

    const handleCloneTier = useCallback(
        async (tierId: string) => {
            if (!tierListId || !tierList) {
                return;
            }

            const source = tierList.tiers.find((t) => t.id === tierId);
            if (!source) {
                return;
            }

            setCloningTierId(tierId);
            try {
                const rules = mapTierRulesToApi(source.exclusions, source.sourceRules);
                const newTier = await createTier({
                    name: `${source.name} (Copy)`,
                    criteria: mapCriteriaToApi(source.criteria),
                    rules,
                });

                await updateTierList(tierListId, {
                    qualityTierRanks: {
                        add: [{ qualityTierId: newTier.id, ordinal: tierList.tiers.length, isCutoff: false }],
                    },
                });
                await refetch();

                toast({
                    title: 'Cloned',
                    description: `Cloned "${source.name}" - add a condition to refine it`,
                    variant: 'success',
                });

                setEditingTier({
                    id: newTier.id,
                    name: newTier.name,
                    isCutoff: false,
                    criteria: source.criteria,
                    exclusions: source.exclusions,
                    sourceRules: rules,
                });
                setFormOpen(true);
            } catch {
                toast({
                    title: 'Clone failed',
                    description: 'Failed to clone the tier.',
                    variant: 'error',
                });
            } finally {
                setCloningTierId(null);
            }
        },
        [tierListId, tierList, createTier, updateTierList, refetch, toast],
    );

    const handleOpenListRules = useCallback(() => {
        const criteria = tierList?.criteria ?? [];
        const exclusions = tierList?.exclusions ?? [];
        const sourceRules = tierList?.sourceRules ?? [];
        setListCriteriaDraft(criteria);
        setListExclusionsDraft(exclusions);
        cleanListCriteriaRef.current = criteria;
        cleanListExclusionsRef.current = exclusions;
        cleanListSourceRulesRef.current = sourceRules;
        setShowListRulesUnsaved(false);
        setListRulesOpen(true);
    }, [tierList?.criteria, tierList?.exclusions, tierList?.sourceRules]);

    const tierListActions: QuickAction[] = useMemo(
        () => [
            { id: 'add-tier', label: 'Add Tier', icon: <PlusIcon weight="bold" />, onClick: handleAddTier },
            {
                id: 'list-rules',
                label: 'Criteria + Exclusions',
                icon: <FunnelIcon weight="bold" />,
                onClick: handleOpenListRules,
            },
        ],
        [handleAddTier, handleOpenListRules],
    );

    const listCriteriaDirty = useFormDirty(listCriteriaDraft, cleanListCriteriaRef.current, normalizeCriteria);
    const listExclusionsDirty = useFormDirty(listExclusionsDraft, cleanListExclusionsRef.current, normalizeExclusions);
    const listRulesDirty = listCriteriaDirty || listExclusionsDirty;
    useEffect(() => {
        if (!listRulesOpen || listRulesDirty) {
            return;
        }
        const criteria = tierList?.criteria ?? [];
        const exclusions = tierList?.exclusions ?? [];
        const sourceRules = tierList?.sourceRules ?? [];
        setListCriteriaDraft(criteria);
        setListExclusionsDraft(exclusions);
        cleanListCriteriaRef.current = criteria;
        cleanListExclusionsRef.current = exclusions;
        cleanListSourceRulesRef.current = sourceRules;
    }, [listRulesDirty, listRulesOpen, tierList?.criteria, tierList?.exclusions, tierList?.sourceRules]);
    const listRulesConflict = useExternalChangeConflict({
        dirty: listRulesDirty,
        entityId: tierListId,
        eventNames: ['qualityTierList.updated', 'qualityTierList.deleted'],
        deletedEventNames: ['qualityTierList.deleted'],
        contextKeys: ['qualityTierList.id'],
        enabled: listRulesOpen && !!tierListId,
    });

    useUnsavedChangesGuard(listRulesOpen && listRulesDirty && !showListRulesUnsaved);

    const handleCloseListRules = useCallback(() => {
        if (listRulesDirty) {
            setShowListRulesUnsaved(true);
            return;
        }
        setListRulesOpen(false);
    }, [listRulesDirty]);

    const handleListRulesOpenChange = useCallback(
        (nextOpen: boolean) => {
            if (!nextOpen) {
                handleCloseListRules();
                return;
            }
            setListRulesOpen(nextOpen);
        },
        [handleCloseListRules],
    );

    const handleDiscardListRules = useCallback(() => {
        setShowListRulesUnsaved(false);
        setListRulesOpen(false);
    }, []);

    const handleKeepEditingListRules = useCallback(() => {
        setShowListRulesUnsaved(false);
    }, []);

    const saveListRules = useCallback(async () => {
        if (!tierListId) {
            return;
        }

        listRulesConflict.beginOwnSave();
        setIsSubmittingListRules(true);
        try {
            const rules = mapListRulesToApi({
                criteria: listCriteriaDraft,
                exclusions: listExclusionsDraft,
                sourceRules: cleanListSourceRulesRef.current,
                criteriaChanged: listCriteriaDirty,
                exclusionsChanged: listExclusionsDirty,
            });
            await updateTierList(tierListId, { rules: { value: rules } });
            listRulesConflict.endOwnSave(true);
            await refetch();
            toast({
                title: 'Saved',
                description: 'Updated criteria and exclusions for every tier in the list',
                variant: 'success',
            });
            setShowListRulesUnsaved(false);
            setListRulesOpen(false);
        } catch {
            listRulesConflict.endOwnSave(false);
            toast({
                title: 'Save failed',
                description: 'Failed to save all tier criteria and exclusions.',
                variant: 'error',
            });
        } finally {
            setIsSubmittingListRules(false);
        }
    }, [
        tierListId,
        listCriteriaDraft,
        listExclusionsDraft,
        listCriteriaDirty,
        listExclusionsDirty,
        listRulesConflict,
        updateTierList,
        refetch,
        toast,
    ]);

    const handleSaveListRules = useCallback(() => {
        if (listRulesConflict.state === 'deleted') {
            return;
        }
        if (listRulesConflict.state === 'stale') {
            setShowListRulesStaleSave(true);
            return;
        }
        void saveListRules();
    }, [listRulesConflict.state, saveListRules]);

    const handleConfirmStaleListRulesSave = useCallback(() => {
        setShowListRulesStaleSave(false);
        void saveListRules();
    }, [saveListRules]);

    const tiers = useMemo(() => tierList?.tiers ?? [], [tierList?.tiers]);
    const editingTierId = currentEditingTier?.id;
    const editingSharedWith = sharedListNames && editingTierId ? (sharedListNames.get(editingTierId) ?? []) : null;

    const handleReorder = useCallback(
        async (orderedIds: string[]) => {
            if (!tierListId) {
                return;
            }

            const update: QualityTierListRankPatch[] = orderedIds.map((id, index) => ({
                qualityTierId: { value: id },
                ordinal: { value: index },
            }));

            await updateTierList(tierListId, { qualityTierRanks: { update } });
            await refetch();
        },
        [tierListId, updateTierList, refetch],
    );

    const sortable = useSortableList<TierWithCriteria>({
        items: tiers,
        getLabel: (tier) => tier.name,
        onReorder: handleReorder,
        onReorderSuccess: ({ item, position }) => {
            toast({
                title: 'Reordered',
                description: `Moved "${item.name}" to priority ${position + 1}`,
                variant: 'success',
            });
        },
        onReorderError: () => {
            toast({
                title: 'Reorder failed',
                description: 'Failed to save the tier order.',
                variant: 'error',
            });
        },
        disabled: !canManage,
    });

    if (isLoading) {
        return <TierListDetailSkeleton />;
    }

    if (error && !tierList) {
        if (isNotFoundError(error)) {
            return (
                <TierListDetailFallback
                    breadcrumbs={BASE_BREADCRUMBS}
                    onRefresh={handleRefresh}
                    icon={
                        <SlidersHorizontalIcon
                            weight="duotone"
                            className="tier-list-detail-page__not-found-icon"
                            aria-hidden="true"
                        />
                    }
                    title="Tier List Not Found"
                    message="The tier list you are looking for does not exist."
                />
            );
        }

        return (
            <TierListDetailFallback
                breadcrumbs={BASE_BREADCRUMBS}
                onRefresh={handleRefresh}
                icon={
                    <WarningCircleIcon
                        weight="duotone"
                        className="tier-list-detail-page__not-found-icon"
                        aria-hidden="true"
                    />
                }
                title="Failed to Load Tier List"
                message={error ? getErrorMessage(error) : ''}
                onRetry={() => refetch()}
            />
        );
    }

    if (!tierList) {
        return (
            <TierListDetailFallback
                breadcrumbs={BASE_BREADCRUMBS}
                onRefresh={handleRefresh}
                icon={
                    <SlidersHorizontalIcon
                        weight="duotone"
                        className="tier-list-detail-page__not-found-icon"
                        aria-hidden="true"
                    />
                }
                title="Tier List Not Found"
                message="The tier list you are looking for does not exist."
            />
        );
    }

    return (
        <div className="tier-list-detail-page" data-page="tier-list-detail">
            <PullToRefresh onRefresh={handleRefresh} className="tier-list-detail-page__pull-to-refresh">
                <Breadcrumb items={breadcrumbs} className="tier-list-detail-page__breadcrumb" />

                <TierListDetailHeader
                    tierListId={tierList.id}
                    name={tierList.name}
                    mimeTypes={tierList.mimeTypes}
                    criteriaCount={tierList.criteria.length}
                    exclusionCount={tierList.exclusions.length}
                    canManage={canManage}
                    onOpenListRules={handleOpenListRules}
                    onAddTier={handleAddTier}
                    onSaveMimeTypes={handleSaveMimeTypes}
                />

                {tierListsError && !sharedListNames && (
                    <Alert
                        variant="error"
                        title="Failed to load the tier lists"
                        className="tier-list-detail-page__lists-error"
                        data-testid="shared-tiers-error"
                    >
                        <p className="tier-list-detail-page__notice-text">
                            The lists that use these tiers are unknown.
                        </p>
                        <Button type="button" variant="secondary" size="sm" onClick={() => void refetchTierLists()}>
                            Retry
                        </Button>
                    </Alert>
                )}

                <TierListTiersSection
                    canManage={canManage}
                    sortable={sortable}
                    deletingTierId={deletingTier?.id ?? null}
                    isDeleting={isDeleting}
                    cloningTierId={cloningTierId}
                    sharedListNames={sharedListNames}
                    onEditTier={handleEditTier}
                    onCloneTier={handleCloneTier}
                    onDeleteTier={handleDeleteTier}
                    onToggleCutoff={handleToggleCutoff}
                />
            </PullToRefresh>

            {canManage && <QuickActionsButton actions={tierListActions} title="Tier List Actions" />}

            <TierForm
                open={formOpen}
                onOpenChange={handleFormOpenChange}
                initialValues={currentEditingTier}
                tierListName={tierList.name}
                tierListId={tierList.id}
                onSubmit={handleFormSubmit}
                isSubmitting={isSubmitting}
                sharedWith={editingSharedWith}
                sharedWithFailed={!!tierListsError}
                onRetrySharedWith={() => void refetchTierLists()}
                onClone={editingTierId ? () => void handleCloneTier(editingTierId) : undefined}
                isCloning={!!editingTierId && cloningTierId === editingTierId}
            />

            <ListRulesSheet
                open={listRulesOpen}
                onOpenChange={handleListRulesOpenChange}
                tierListName={tierList.name}
                criteria={listCriteriaDraft}
                onCriteriaChange={setListCriteriaDraft}
                exclusions={listExclusionsDraft}
                onExclusionsChange={setListExclusionsDraft}
                isSubmitting={isSubmittingListRules}
                onClose={handleCloseListRules}
                onSave={handleSaveListRules}
                conflictState={listRulesConflict.state}
                showStaleSaveConfirmation={showListRulesStaleSave}
                onCancelStaleSave={() => setShowListRulesStaleSave(false)}
                onConfirmStaleSave={handleConfirmStaleListRulesSave}
            />

            <UnsavedChangesDialog
                open={showListRulesUnsaved}
                onDiscard={handleDiscardListRules}
                onCancel={handleKeepEditingListRules}
            />

            <DeleteConfirmDialog
                open={!!deletingTier}
                onConfirm={handleConfirmDelete}
                onCancel={handleCancelDelete}
                itemName={deletingTier?.name ?? ''}
                itemType="Tier"
                isDeleting={isDeleting}
            />
        </div>
    );
}

TierListDetailPage.displayName = 'TierListDetailPage';

export default TierListDetailPage;
