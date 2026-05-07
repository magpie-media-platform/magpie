import { useState, useCallback, useMemo } from 'react';

import { useToggle } from '@/hooks/useToggle';
import { useToast } from '@/components/ui/Toast';
import {
    useEventTriggers,
    type ApiEventTrigger,
    type EventTriggerCreate,
    type EventTriggerPatch,
} from '@/hooks/useEventTriggers';
import type { EventTriggerFormValues, EventTriggerFilterItem } from './EventTriggerForm';

const ADMIN_SCOPE = 1 as const;

export function useEventTriggerCrud() {
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
    } = useEventTriggers();

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingTrigger, setEditingTrigger] = useState<ApiEventTrigger | null>(null);
    const [deleteDialogOpen, , openDeleteDialog, closeDeleteDialog] = useToggle();
    const [triggerToDelete, setTriggerToDelete] = useState<ApiEventTrigger | null>(null);
    const [togglingTriggers, setTogglingTriggers] = useState<Set<string>>(new Set());

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
                const eventFilter = {
                    eventName: values.eventNamePattern
                        ? { operator: 'regex', value: values.eventNamePattern }
                        : undefined,
                    eventContext:
                        values.filters.length > 0
                            ? Object.fromEntries(
                                  values.filters.map((f) => [
                                      f.key,
                                      { operator: 'regex', value: f.pattern ?? '', negate: f.negate },
                                  ]),
                              )
                            : undefined,
                };

                if (values.id) {
                    const patch: EventTriggerPatch = {
                        name: { value: values.name },
                        eventFilter: { value: eventFilter },
                        executionMode: { value: values.executionMode === 'immediate' ? 1 : 0 },
                        enabled: { value: values.enabled },
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
                        scope: ADMIN_SCOPE,
                        executionMode: values.executionMode === 'immediate' ? 1 : 0,
                        enabled: values.enabled,
                    };
                    await createTrigger(create);
                    toast({
                        title: 'Event Trigger Created',
                        description: 'Event trigger created successfully',
                        variant: 'success',
                    });
                }
                closeForm();
            } catch {
                toast({ title: 'Error', description: 'Failed to save event trigger', variant: 'error' });
            }
        },
        [createTrigger, updateTrigger, toast, closeForm],
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
        if (!editingTrigger) {
            return null;
        }

        const filters: EventTriggerFilterItem[] = editingTrigger.eventFilter.eventContext
            ? Object.entries(editingTrigger.eventFilter.eventContext).map(([key, criterion]) => ({
                  key,
                  pattern: criterion.value ?? null,
                  negate: criterion.negate ?? false,
              }))
            : [];

        return {
            id: editingTrigger.id,
            name: editingTrigger.name,
            templateId: editingTrigger.jobType,
            eventNamePattern: editingTrigger.eventFilter.eventName?.value ?? '',
            filters,
            executionMode: editingTrigger.executionMode === 1 ? 'immediate' : 'queue',
            enabled: editingTrigger.enabled,
        };
    }, [editingTrigger]);

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
