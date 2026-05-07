import { useState, useCallback, useMemo } from 'react';

import { useToggle } from '@/hooks/useToggle';
import { useToast } from '@/components/ui/Toast';
import {
    useScheduleTriggers,
    type ApiScheduleTrigger,
    type ScheduleTriggerCreate,
    type ScheduleTriggerPatch,
} from '@/hooks/useScheduleTriggers';
import type { RunJobRequest } from '@/hooks/useJobs';
import type { ScheduleTriggerFormValues } from './ScheduleTriggerForm';

const ADMIN_SCOPE = 1 as const;

export interface UseScheduleTriggerCrudOptions {
    runJob: (request: RunJobRequest) => Promise<string>;
    refetchJobs: () => Promise<unknown>;
}

export function useScheduleTriggerCrud({ runJob, refetchJobs }: UseScheduleTriggerCrudOptions) {
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
    } = useScheduleTriggers();

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingTrigger, setEditingTrigger] = useState<ApiScheduleTrigger | null>(null);
    const [deleteDialogOpen, , openDeleteDialog, closeDeleteDialog] = useToggle();
    const [triggerToDelete, setTriggerToDelete] = useState<ApiScheduleTrigger | null>(null);
    const [runningJobs, setRunningJobs] = useState<Set<string>>(new Set());
    const [togglingTriggers, setTogglingTriggers] = useState<Set<string>>(new Set());

    const handleAdd = useCallback(() => {
        setEditingTrigger(null);
        openForm();
    }, [openForm]);

    const handleEdit = useCallback(
        (trigger: ApiScheduleTrigger) => {
            setEditingTrigger(trigger);
            openForm();
        },
        [openForm],
    );

    const handleSubmit = useCallback(
        async (values: ScheduleTriggerFormValues) => {
            try {
                if (values.id) {
                    const patch: ScheduleTriggerPatch = {
                        name: { value: values.name },
                        schedule: { value: values.cronExpression },
                        enabled: { value: values.enabled },
                    };
                    await updateTrigger({ id: values.id, patch });
                    toast({
                        title: 'Trigger Updated',
                        description: 'Schedule trigger updated successfully',
                        variant: 'success',
                    });
                } else {
                    const create: ScheduleTriggerCreate = {
                        name: values.name,
                        jobType: values.templateId,
                        schedule: values.cronExpression,
                        scope: ADMIN_SCOPE,
                        enabled: values.enabled,
                    };
                    await createTrigger(create);
                    toast({
                        title: 'Trigger Created',
                        description: 'Schedule trigger created successfully',
                        variant: 'success',
                    });
                }
                closeForm();
            } catch {
                toast({ title: 'Error', description: 'Failed to save trigger', variant: 'error' });
            }
        },
        [createTrigger, updateTrigger, toast, closeForm],
    );

    const handleDeleteClick = useCallback(
        (trigger: ApiScheduleTrigger) => {
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
                title: 'Trigger Deleted',
                description: 'Schedule trigger deleted successfully',
                variant: 'success',
            });
            closeDeleteDialog();
            setTriggerToDelete(null);
        } catch {
            toast({ title: 'Error', description: 'Failed to delete trigger', variant: 'error' });
        }
    }, [triggerToDelete, deleteTriggerApi, toast, closeDeleteDialog]);

    const handleToggle = useCallback(
        async (trigger: ApiScheduleTrigger, enabled: boolean) => {
            setTogglingTriggers((prev) => new Set([...prev, trigger.id]));
            try {
                await updateTrigger({ id: trigger.id, patch: { enabled: { value: enabled } } });
            } catch {
                toast({ title: 'Error', description: 'Failed to update trigger', variant: 'error' });
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

    const handleRunTrigger = useCallback(
        async (trigger: ApiScheduleTrigger) => {
            const triggerId = trigger.id;
            setRunningJobs((prev) => new Set([...prev, triggerId]));
            try {
                await runJob({
                    jobType: trigger.jobType,
                    extensionId: trigger.extensionId,
                    parameters: trigger.parameters,
                });
                toast({ title: trigger.name, description: 'Job started successfully', variant: 'success' });
                refetchJobs();
            } catch {
                toast({ title: trigger.name, description: 'Failed to start job', variant: 'error' });
            } finally {
                setRunningJobs((prev) => {
                    const next = new Set(prev);
                    next.delete(triggerId);
                    return next;
                });
            }
        },
        [runJob, toast, refetchJobs],
    );

    const formInitialValues = useMemo((): ScheduleTriggerFormValues | null => {
        if (!editingTrigger) {
            return null;
        }
        return {
            id: editingTrigger.id,
            name: editingTrigger.name,
            templateId: editingTrigger.jobType,
            cronExpression: editingTrigger.schedule,
            enabled: editingTrigger.enabled,
        };
    }, [editingTrigger]);

    return {
        triggers,
        isLoading,
        refetch,
        runningJobs,
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
        handleRunTrigger,
    };
}
