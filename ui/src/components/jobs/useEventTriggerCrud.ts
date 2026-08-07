import { useState, useCallback, useMemo } from 'react';

import { useToggle } from '@/hooks/useToggle';
import { useToast } from '@/components/ui/Toast';
import {
    useEventTriggers,
    type ApiEventTrigger,
    type EventTriggerCreate,
    type EventTriggerPatch,
    type EventTriggerQueryParams,
} from '@/hooks/useEventTriggers';
import type { JobScopeValue } from '@/hooks/useJobs';
import { editableCriteriaToEventContext, eventContextToEditableCriteria } from '@/lib/eventContextCriteria';
import type { EventTriggerFormValues, EventTriggerFilterItem } from './EventTriggerForm';

const ADMIN_SCOPE = 'Administrative' as const;

interface UseEventTriggerCrudOptions {
    scope?: JobScopeValue;
    queryParams?: EventTriggerQueryParams;
}

export function useEventTriggerCrud({ scope = ADMIN_SCOPE, queryParams }: UseEventTriggerCrudOptions = {}) {
    const { toast } = useToast();
    const {
        triggers,
        createTrigger,
        updateTrigger,
        deleteTrigger: deleteTriggerApi,
        isCreating,
        isUpdating,
        isDeleting,
        isLoading,
        refetch,
    } = useEventTriggers(queryParams);

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingTrigger, setEditingTrigger] = useState<ApiEventTrigger | null>(null);
    const [deleteDialogOpen, , openDeleteDialog, closeDeleteDialog] = useToggle();
    const [triggerToDelete, setTriggerToDelete] = useState<ApiEventTrigger | null>(null);
    const [togglingTriggers, setTogglingTriggers] = useState<Set<string>>(new Set());
    const currentEditingTrigger = useMemo(() => {
        if (!editingTrigger) {
            return null;
        }
        return triggers.find((candidate) => candidate.id === editingTrigger.id) ?? editingTrigger;
    }, [editingTrigger, triggers]);

    const handleAdd = useCallback(() => {
        setEditingTrigger(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (trigger: ApiEventTrigger) => {
            setEditingTrigger(trigger);
            openForm();
        },
        [openForm],
    );

    const handleSubmit = useCallback(
        async (values: EventTriggerFormValues) => {
            try {
                const eventNamePatterns = values.eventNamePatterns
                    .map((pattern) => pattern.trim())
                    .filter((pattern) => pattern !== '');
                const originalEventName = currentEditingTrigger?.eventFilter.eventName;
                const originalValues = originalEventName?.values?.map((pattern) => pattern.trim()) ?? [];
                const preservesOriginalInCriterion =
                    originalEventName?.operator?.toLowerCase() === 'in' &&
                    originalValues.length === eventNamePatterns.length &&
                    originalValues.every((pattern, index) => pattern === eventNamePatterns[index]);
                const eventName =
                    preservesOriginalInCriterion || eventNamePatterns.length > 1
                        ? { operator: 'In', values: eventNamePatterns }
                        : eventNamePatterns.length === 1
                          ? { operator: 'regex', value: eventNamePatterns[0] }
                          : undefined;
                const eventFilter = {
                    eventName,
                    eventContext: editableCriteriaToEventContext(values.filters),
                };

                if (values.id) {
                    const patch: EventTriggerPatch = {
                        name: { value: values.name },
                        eventFilter: { value: eventFilter },
                        enabled: { value: values.enabled },
                        delaySeconds: { value: values.delaySeconds },
                    };
                    await updateTrigger({ id: values.id, patch });
                    toast({
                        title: 'Event Trigger Updated',
                        description: 'Event trigger updated successfully',
                        variant: 'success',
                    });
                } else {
                    const create: EventTriggerCreate = {
                        name: values.name,
                        jobType: values.templateId,
                        eventFilter,
                        scope,
                        enabled: values.enabled,
                        delaySeconds: values.delaySeconds,
                    };
                    await createTrigger(create);
                    toast({
                        title: 'Event Trigger Created',
                        description: 'Event trigger created successfully',
                        variant: 'success',
                    });
                }
                closeForm();
            } catch (error) {
                toast({ title: 'Error', description: 'Failed to save event trigger', variant: 'error' });
                throw error;
            }
        },
        [createTrigger, updateTrigger, toast, closeForm, scope, currentEditingTrigger],
    );

    const handleDeleteClick = useCallback(
        (trigger: ApiEventTrigger) => {
            setTriggerToDelete(trigger);
            openDeleteDialog();
        },
        [openDeleteDialog],
    );

    const handleDeleteConfirm = useCallback(async () => {
        if (!triggerToDelete) {
            return;
        }
        try {
            await deleteTriggerApi(triggerToDelete.id);
            toast({
                title: 'Event Trigger Deleted',
                description: 'Event trigger deleted successfully',
                variant: 'success',
            });
            closeDeleteDialog();
            setTriggerToDelete(null);
        } catch {
            toast({ title: 'Error', description: 'Failed to delete event trigger', variant: 'error' });
        }
    }, [triggerToDelete, deleteTriggerApi, toast, closeDeleteDialog]);

    const handleToggle = useCallback(
        async (trigger: ApiEventTrigger, enabled: boolean) => {
            setTogglingTriggers((prev) => new Set([...prev, trigger.id]));
            try {
                await updateTrigger({ id: trigger.id, patch: { enabled: { value: enabled } } });
            } catch {
                toast({ title: 'Error', description: 'Failed to update event trigger', variant: 'error' });
            } finally {
                setTogglingTriggers((prev) => {
                    const next = new Set(prev);
                    next.delete(trigger.id);
                    return next;
                });
            }
        },
        [updateTrigger, toast],
    );

    const formInitialValues = useMemo((): EventTriggerFormValues | null => {
        if (!currentEditingTrigger) {
            return null;
        }

        const filters: EventTriggerFilterItem[] = eventContextToEditableCriteria(
            currentEditingTrigger.eventFilter.eventContext,
        );

        const eventName = currentEditingTrigger.eventFilter.eventName;
        const eventNamePatterns =
            eventName?.values && eventName.values.length > 0
                ? eventName.values
                : eventName?.value
                  ? [eventName.value]
                  : [''];

        return {
            id: currentEditingTrigger.id,
            name: currentEditingTrigger.name,
            templateId: currentEditingTrigger.jobType,
            eventNamePatterns,
            filters,
            enabled: currentEditingTrigger.enabled,
            delaySeconds: currentEditingTrigger.delaySeconds ?? 0,
        };
    }, [currentEditingTrigger]);

    return {
        triggers,
        isLoading,
        refetch,
        togglingTriggers,
        formOpen,
        closeForm,
        formInitialValues,
        deleteDialogOpen,
        closeDeleteDialog,
        triggerToDelete,
        isSaving: isCreating || isUpdating,
        isDeleting,
        handleAdd,
        handleEdit,
        handleSubmit,
        handleDeleteClick,
        handleDeleteConfirm,
        handleToggle,
    };
}
