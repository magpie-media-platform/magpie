import { useState, useCallback, useMemo } from 'react';

import { useToggle } from '@/hooks/useToggle';
import { useToast } from '@/components/ui/Toast';
import {
    useScheduleTriggers,
    type ApiScheduleTrigger,
    type DictionaryPatch,
    type ScheduleTriggerCreate,
    type ScheduleTriggerPatch,
    type ScheduleTriggerQueryParams,
} from '@/hooks/useScheduleTriggers';
import type { RunJobRequest, RunJobResult, JobScopeValue } from '@/hooks/useJobs';
import type { ScheduleTriggerFormValues } from './ScheduleTriggerForm';

const ADMIN_SCOPE = 'Administrative' as const;

function pruneEmptyParameters(parameters: Record<string, string>): Record<string, string> {
    const next: Record<string, string> = {};
    for (const [key, value] of Object.entries(parameters)) {
        if (value !== '') {
            next[key] = value;
        }
    }
    return next;
}

function buildParameterPatch(
    original: Record<string, string>,
    next: Record<string, string>,
): DictionaryPatch<string, string> | null {
    const add: Record<string, string> = {};
    const update: Record<string, string> = {};
    const remove: string[] = [];

    for (const [key, value] of Object.entries(next)) {
        if (value === '') {
            if (original[key] !== undefined) {
                remove.push(key);
            }
            continue;
        }
        if (original[key] === undefined) {
            add[key] = value;
        } else if (original[key] !== value) {
            update[key] = value;
        }
    }

    for (const key of Object.keys(original)) {
        if (next[key] === undefined) {
            remove.push(key);
        }
    }

    const patch: DictionaryPatch<string, string> = {};
    if (Object.keys(add).length > 0) {
        patch.add = add;
    }
    if (Object.keys(update).length > 0) {
        patch.update = update;
    }
    if (remove.length > 0) {
        patch.remove = remove;
    }
    if (!patch.add && !patch.update && !patch.remove) {
        return null;
    }
    return patch;
}

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
                    const parametersPatch = buildParameterPatch(original, values.parameters);
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
                    const nonEmptyParameters = pruneEmptyParameters(values.parameters);
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
                toast({ title: 'Error', description: 'Failed to save trigger', variant: 'error' });
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
