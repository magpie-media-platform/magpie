import { useState, useCallback, useMemo } from 'react';

import { useToggle } from '@/hooks/useToggle';
import { useToast } from '@/components/ui/Toast';
import {
    useScheduleTriggers,
    type ApiScheduleTrigger,
    type ScheduleTriggerCreate,
    type ScheduleTriggerPatch,
    type ScheduleTriggerQueryParams,
} from '@/hooks/useScheduleTriggers';
import type { RunJobRequest, RunJobResult, JobScopeValue } from '@/hooks/useJobs';
import type { ScheduleTriggerFormValues } from './ScheduleTriggerForm';
import { buildJobParameterPatch, normalizeJobParameterValues } from './jobParameters';
import { getErrorMessage } from '@/lib/errors';

const ADMIN_SCOPE = 'Administrative' as const;

interface UseScheduleTriggerCrudOptions {
    runJob: (request: RunJobRequest) => Promise<RunJobResult>;
    refetchJobs: () => Promise<unknown>;
    scope?: JobScopeValue;
    queryParams?: ScheduleTriggerQueryParams;
}

export function useScheduleTriggerCrud({
    runJob,
    refetchJobs,
    scope = ADMIN_SCOPE,
    queryParams,
}: UseScheduleTriggerCrudOptions) {
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
        error,
        refetch,
    } = useScheduleTriggers(queryParams);

    const [formOpen, , openForm, closeForm] = useToggle();
    const [editingTrigger, setEditingTrigger] = useState<ApiScheduleTrigger | null>(null);
    const [deleteDialogOpen, , openDeleteDialog, closeDeleteDialog] = useToggle();
    const [triggerToDelete, setTriggerToDelete] = useState<ApiScheduleTrigger | null>(null);
    const [runningJobs, setRunningJobs] = useState<Set<string>>(new Set());
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
                    const original = currentEditingTrigger?.parameters ?? {};
                    const parametersPatch = buildJobParameterPatch(original, values.parameters);
                    const patch: ScheduleTriggerPatch = {
                        name: { value: values.name },
                        schedule: { value: values.cronExpression },
                        enabled: { value: values.enabled },
                    };
                    if (parametersPatch) {
                        patch.parameters = parametersPatch;
                    }
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
                        scope,
                        enabled: values.enabled,
                    };
                    const nonEmptyParameters = normalizeJobParameterValues(values.parameters);
                    if (Object.keys(nonEmptyParameters).length > 0) {
                        create.parameters = nonEmptyParameters;
                    }
                    await createTrigger(create);
                    toast({
                        title: 'Trigger Created',
                        description: 'Schedule trigger created successfully',
                        variant: 'success',
                    });
                }
                closeForm();
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, 'Failed to save trigger'),
                    variant: 'error',
                });
                throw error;
            }
        },
        [currentEditingTrigger, createTrigger, updateTrigger, toast, closeForm, scope],
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
        } catch (error) {
            toast({
                title: 'Error',
                description: getErrorMessage(error, 'Failed to delete trigger'),
                variant: 'error',
            });
        }
    }, [triggerToDelete, deleteTriggerApi, toast, closeDeleteDialog]);

    const handleToggle = useCallback(
        async (trigger: ApiScheduleTrigger, enabled: boolean) => {
            setTogglingTriggers((prev) => new Set([...prev, trigger.id]));
            try {
                await updateTrigger({ id: trigger.id, patch: { enabled: { value: enabled } } });
            } catch (error) {
                toast({
                    title: 'Error',
                    description: getErrorMessage(error, 'Failed to update trigger'),
                    variant: 'error',
                });
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
                await refetchJobs();
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
        if (!currentEditingTrigger) {
            return null;
        }
        return {
            id: currentEditingTrigger.id,
            name: currentEditingTrigger.name,
            templateId: currentEditingTrigger.jobType,
            cronExpression: currentEditingTrigger.schedule,
            enabled: currentEditingTrigger.enabled,
            parameters: { ...(currentEditingTrigger.parameters ?? {}) },
        };
    }, [currentEditingTrigger]);

    return {
        triggers,
        isLoading,
        error,
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
