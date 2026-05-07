import { CircleNotch, Play, PencilSimple, Trash, CalendarBlank, Clock } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogFooter,
    DialogTitle,
    DialogDescription,
} from '@/components/ui/Dialog';
import type { ApiScheduleTrigger } from '@/hooks/useScheduleTriggers';
import { formatJobDateTime, type TemplateOption } from '@/lib/jobExecution';
import { ScheduleTriggerForm } from './ScheduleTriggerForm';
import type { useScheduleTriggerCrud } from './useScheduleTriggerCrud';

import '@/styles/card-actions.css';

type ScheduleTriggerCrud = ReturnType<typeof useScheduleTriggerCrud>;

export interface ScheduleTriggersTabProps {
    crud: ScheduleTriggerCrud;
    templates: TemplateOption[];
    canManage: boolean;
    canRun: boolean;
}

export function ScheduleTriggersTab({ crud, templates, canManage, canRun }: ScheduleTriggersTabProps) {
    const {
        triggers,
        runningJobs,
        togglingTriggers,
        formOpen,
        closeForm,
        formInitialValues,
        deleteDialogOpen,
        closeDeleteDialog,
        triggerToDelete,
        isSaving,
        isDeleting,
        handleEdit,
        handleSubmit,
        handleDeleteClick,
        handleDeleteConfirm,
        handleToggle,
        handleRunTrigger,
    } = crud;

    return (
        <>
            {triggers.map((trigger) => (
                <ScheduleTriggerRow
                    key={trigger.id}
                    trigger={trigger}
                    isRunning={runningJobs.has(trigger.id)}
                    isToggling={togglingTriggers.has(trigger.id)}
                    canManage={canManage}
                    canRun={canRun}
                    onEdit={() => handleEdit(trigger)}
                    onDelete={() => handleDeleteClick(trigger)}
                    onToggle={(enabled) => handleToggle(trigger, enabled)}
                    onRunNow={() => handleRunTrigger(trigger)}
                />
            ))}

            <ScheduleTriggerForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) closeForm();
                }}
                initialValues={formInitialValues}
                templates={templates}
                onSubmit={handleSubmit}
                isSubmitting={isSaving}
            />

            <Dialog
                open={deleteDialogOpen}
                onOpenChange={(open) => {
                    if (!open) closeDeleteDialog();
                }}
            >
                <DialogContent>
                    <DialogHeader>
                        <DialogTitle>Delete Schedule Trigger</DialogTitle>
                        <DialogDescription>
                            Are you sure you want to delete "{triggerToDelete?.name}"? This action cannot be undone.
                        </DialogDescription>
                    </DialogHeader>
                    <DialogFooter>
                        <Button variant="ghost" onClick={closeDeleteDialog} disabled={isDeleting} data-action="cancel">
                            Cancel
                        </Button>
                        <Button
                            variant="destructive"
                            onClick={handleDeleteConfirm}
                            disabled={isDeleting}
                            data-action="confirm-delete"
                        >
                            <Trash weight="bold" aria-hidden="true" />
                            {isDeleting ? 'Deleting...' : 'Delete'}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}

ScheduleTriggersTab.displayName = 'ScheduleTriggersTab';

function describeCron(cronExpression: string): string {
    const presets: Record<string, string> = {
        '0 * * * *': 'Every hour',
        '0 */6 * * *': 'Every 6 hours',
        '0 */12 * * *': 'Every 12 hours',
        '0 0 * * *': 'Daily at midnight',
        '0 6 * * *': 'Daily at 6 AM',
        '0 2 * * *': 'Daily at 2 AM',
        '0 0 * * 0': 'Weekly on Sunday',
    };
    return presets[cronExpression] ?? cronExpression;
}

interface ScheduleTriggerRowProps {
    trigger: ApiScheduleTrigger;
    isRunning: boolean;
    isToggling: boolean;
    canManage: boolean;
    canRun: boolean;
    onEdit: () => void;
    onDelete: () => void;
    onToggle: (enabled: boolean) => void;
    onRunNow: () => void;
}

function ScheduleTriggerRow({
    trigger,
    isRunning,
    isToggling,
    canManage,
    canRun,
    onEdit,
    onDelete,
    onToggle,
    onRunNow,
}: ScheduleTriggerRowProps) {
    return (
        <div
            className={`jobs-page__trigger ${!trigger.enabled ? 'jobs-page__trigger--disabled' : ''}`}
            role="listitem"
            data-trigger-id={trigger.id}
            data-enabled={trigger.enabled}
        >
            <div className="jobs-page__trigger-main">
                <div className="jobs-page__trigger-toggle">
                    <Switch
                        checked={trigger.enabled}
                        onCheckedChange={onToggle}
                        disabled={!canManage || isToggling}
                        aria-label={`${trigger.enabled ? 'Disable' : 'Enable'} ${trigger.name}`}
                        data-action="toggle"
                        data-target={trigger.id}
                    />
                </div>
                <div className="jobs-page__trigger-info">
                    <div className="jobs-page__trigger-header">
                        <span className="jobs-page__trigger-name">{trigger.name}</span>
                    </div>
                    <div className="jobs-page__trigger-meta">
                        <span className="jobs-page__trigger-job">{trigger.jobType}</span>
                        <span className="jobs-page__trigger-schedule" title={trigger.schedule}>
                            <CalendarBlank weight="regular" aria-hidden="true" />
                            {describeCron(trigger.schedule)}
                        </span>
                    </div>
                    {trigger.nextRunAt && trigger.enabled && (
                        <div className="jobs-page__trigger-next-run">
                            <Clock weight="regular" aria-hidden="true" />
                            <span>Next run: {formatJobDateTime(trigger.nextRunAt)}</span>
                        </div>
                    )}
                </div>
                <div className="jobs-page__trigger-actions card-actions">
                    {canRun && (
                        <button
                            type="button"
                            className={`card-actions__btn ${isRunning ? 'card-actions__btn--loading' : ''}`}
                            onClick={onRunNow}
                            disabled={isRunning}
                            aria-label={`Run ${trigger.jobType} now`}
                            data-action="run-now"
                            data-target={trigger.id}
                            title="Run now"
                        >
                            {isRunning ? (
                                <CircleNotch weight="bold" className="card-actions__spinner" aria-hidden="true" />
                            ) : (
                                <Play weight="fill" aria-hidden="true" />
                            )}
                        </button>
                    )}
                    {canManage && (
                        <>
                            <button
                                type="button"
                                className="card-actions__btn"
                                onClick={onEdit}
                                aria-label={`Edit ${trigger.name}`}
                                data-action="edit"
                                data-target={trigger.id}
                                title="Edit trigger"
                            >
                                <PencilSimple weight="bold" aria-hidden="true" />
                            </button>
                            <button
                                type="button"
                                className="card-actions__btn card-actions__btn--danger"
                                onClick={onDelete}
                                aria-label={`Delete ${trigger.name}`}
                                data-action="delete"
                                data-target={trigger.id}
                                title="Delete trigger"
                            >
                                <Trash weight="bold" aria-hidden="true" />
                            </button>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}
