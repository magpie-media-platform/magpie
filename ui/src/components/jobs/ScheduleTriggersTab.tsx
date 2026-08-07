import {
    CircleNotchIcon,
    PlayIcon,
    PencilSimpleIcon,
    TrashIcon,
    CalendarBlankIcon,
    ClockIcon,
    LockSimpleIcon,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
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
import { describeCronUtc } from './cronDescription';

import '@/styles/card-actions.css';

type ScheduleTriggerCrud = ReturnType<typeof useScheduleTriggerCrud>;

interface ScheduleTriggersTabProps {
    crud: ScheduleTriggerCrud;
    templates: TemplateOption[];
    systemJobTypes?: ReadonlySet<string>;
    canManage: boolean;
    canRun: boolean;
}

function isOnDemand(jobType: string, templates: TemplateOption[]): boolean {
    const spec = templates.find((template) => template.id === jobType);
    if (spec === undefined || spec.onDemand === undefined) {
        return true;
    }
    return spec.onDemand;
}

export function ScheduleTriggersTab({ crud, templates, systemJobTypes, canManage, canRun }: ScheduleTriggersTabProps) {
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
            {triggers.map((trigger) => {
                const isSystemScoped = trigger.scope === 'System' || (systemJobTypes?.has(trigger.jobType) ?? false);
                return (
                    <ScheduleTriggerRow
                        key={trigger.id}
                        trigger={trigger}
                        isRunning={runningJobs.has(trigger.id)}
                        isToggling={togglingTriggers.has(trigger.id)}
                        isSystemScoped={isSystemScoped}
                        canManage={canManage && !isSystemScoped}
                        canRun={canRun && isOnDemand(trigger.jobType, templates)}
                        onEdit={() => handleEdit(trigger)}
                        onDelete={() => handleDeleteClick(trigger)}
                        onToggle={(enabled) => handleToggle(trigger, enabled)}
                        onRunNow={() => handleRunTrigger(trigger)}
                    />
                );
            })}

            <ScheduleTriggerForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) closeForm();
                }}
                initialValues={formInitialValues}
                templates={templates}
                currentTemplateId={formInitialValues?.templateId}
                readOnly={
                    formInitialValues?.templateId ? (systemJobTypes?.has(formInitialValues.templateId) ?? false) : false
                }
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
                            <TrashIcon weight="bold" aria-hidden="true" />
                            {isDeleting ? 'Deleting...' : 'Delete'}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}

ScheduleTriggersTab.displayName = 'ScheduleTriggersTab';

interface ScheduleTriggerRowProps {
    trigger: ApiScheduleTrigger;
    isRunning: boolean;
    isToggling: boolean;
    isSystemScoped: boolean;
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
    isSystemScoped,
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
            data-system-scoped={isSystemScoped}
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
                        {isSystemScoped && (
                            <Badge
                                variant="default"
                                size="sm"
                                badgeStyle="outline"
                                data-field="system-badge"
                                title="Built-in triggers are managed by Magpie and cannot be edited."
                            >
                                <LockSimpleIcon weight="regular" aria-hidden="true" />
                                System
                            </Badge>
                        )}
                    </div>
                    <div className="jobs-page__trigger-meta">
                        <span className="jobs-page__trigger-job">{trigger.jobType}</span>
                        <span className="jobs-page__trigger-schedule" title={`${trigger.schedule} interpreted in UTC`}>
                            <CalendarBlankIcon weight="regular" aria-hidden="true" />
                            {describeCronUtc(trigger.schedule)}
                        </span>
                    </div>
                    {trigger.nextRunAt && trigger.enabled && (
                        <div className="jobs-page__trigger-next-run">
                            <ClockIcon weight="regular" aria-hidden="true" />
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
                                <CircleNotchIcon weight="bold" className="card-actions__spinner" aria-hidden="true" />
                            ) : (
                                <PlayIcon weight="fill" aria-hidden="true" />
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
                                <PencilSimpleIcon weight="bold" aria-hidden="true" />
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
                                <TrashIcon weight="bold" aria-hidden="true" />
                            </button>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}
