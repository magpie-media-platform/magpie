import { PencilSimpleIcon, TrashIcon, LightningIcon } from '@phosphor-icons/react';

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
import type { ApiEventTrigger } from '@/hooks/useEventTriggers';
import { countExecutableEventContextCriteria } from '@/lib/eventContextCriteria';
import type { TemplateOption } from '@/lib/jobExecution';
import { EventTriggerForm } from './EventTriggerForm';
import { eventPatternLabel } from './eventPatternPresets';
import type { useEventTriggerCrud } from './useEventTriggerCrud';

import '@/styles/card-actions.css';

type EventTriggerCrud = ReturnType<typeof useEventTriggerCrud>;

interface EventTriggersTabProps {
    crud: EventTriggerCrud;
    templates: TemplateOption[];
    systemJobTypes?: ReadonlySet<string>;
    canManage: boolean;
}

export function EventTriggersTab({ crud, templates, systemJobTypes, canManage }: EventTriggersTabProps) {
    const {
        triggers,
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
    } = crud;

    return (
        <>
            {triggers.map((trigger) => {
                const isSystemScoped = trigger.scope === 'System' || (systemJobTypes?.has(trigger.jobType) ?? false);
                return (
                    <EventTriggerRow
                        key={trigger.id}
                        trigger={trigger}
                        isToggling={togglingTriggers.has(trigger.id)}
                        canManage={canManage && !isSystemScoped}
                        onEdit={() => handleEdit(trigger)}
                        onDelete={() => handleDeleteClick(trigger)}
                        onToggle={(enabled) => handleToggle(trigger, enabled)}
                    />
                );
            })}

            <EventTriggerForm
                open={formOpen}
                onOpenChange={(open) => {
                    if (!open) closeForm();
                }}
                initialValues={formInitialValues}
                templates={templates}
                currentTemplateId={formInitialValues?.templateId}
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
                        <DialogTitle>Delete Event Trigger</DialogTitle>
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
                            data-action="confirm-delete-event"
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

EventTriggersTab.displayName = 'EventTriggersTab';

function describeEventPattern(trigger: ApiEventTrigger): string {
    const eventName = trigger.eventFilter?.eventName;
    const values = eventName?.values?.filter((value) => value.trim().length > 0) ?? [];
    if (values.length > 0) {
        return values.join(', ');
    }

    const pattern = eventName?.value;
    if (!pattern) {
        return 'All events';
    }
    return eventPatternLabel(pattern);
}

interface EventTriggerRowProps {
    trigger: ApiEventTrigger;
    isToggling: boolean;
    canManage: boolean;
    onEdit: () => void;
    onDelete: () => void;
    onToggle: (enabled: boolean) => void;
}

function EventTriggerRow({ trigger, isToggling, canManage, onEdit, onDelete, onToggle }: EventTriggerRowProps) {
    const filterCount = countExecutableEventContextCriteria(trigger.eventFilter?.eventContext);
    const eventPatternDescription = describeEventPattern(trigger);
    const hasDelay = trigger.delaySeconds > 0;

    return (
        <div
            className={`jobs-page__event-trigger ${!trigger.enabled ? 'jobs-page__event-trigger--disabled' : ''}`}
            role="listitem"
            data-event-trigger-id={trigger.id}
            data-enabled={trigger.enabled}
        >
            <div className="jobs-page__event-trigger-main">
                <div className="jobs-page__event-trigger-toggle">
                    <Switch
                        checked={trigger.enabled}
                        onCheckedChange={onToggle}
                        disabled={!canManage || isToggling}
                        aria-label={`${trigger.enabled ? 'Disable' : 'Enable'} ${trigger.name}`}
                        data-action="toggle-event"
                        data-target={trigger.id}
                    />
                </div>
                <div className="jobs-page__event-trigger-info">
                    <div className="jobs-page__event-trigger-header">
                        <span className="jobs-page__event-trigger-name">{trigger.name}</span>
                    </div>
                    <div className="jobs-page__event-trigger-meta">
                        <span className="jobs-page__event-trigger-job">{trigger.jobType}</span>
                        <span className="jobs-page__event-trigger-pattern" title={eventPatternDescription}>
                            <LightningIcon weight="regular" aria-hidden="true" />
                            {eventPatternDescription}
                        </span>
                    </div>
                    {(filterCount > 0 || hasDelay) && (
                        <div className="jobs-page__event-trigger-details">
                            {filterCount > 0 && (
                                <span className="jobs-page__event-trigger-filters">
                                    {filterCount} filter{filterCount !== 1 ? 's' : ''}
                                </span>
                            )}
                            {hasDelay && (
                                <span className="jobs-page__event-trigger-delay" data-testid="event-trigger-delay">
                                    Delay: {trigger.delaySeconds}s
                                </span>
                            )}
                        </div>
                    )}
                </div>
                <div className="jobs-page__event-trigger-actions card-actions">
                    {canManage && (
                        <>
                            <button
                                type="button"
                                className="card-actions__btn"
                                onClick={onEdit}
                                aria-label={`Edit ${trigger.name}`}
                                data-action="edit-event"
                                data-target={trigger.id}
                                title="Edit event trigger"
                            >
                                <PencilSimpleIcon weight="bold" aria-hidden="true" />
                            </button>
                            <button
                                type="button"
                                className="card-actions__btn card-actions__btn--danger"
                                onClick={onDelete}
                                aria-label={`Delete ${trigger.name}`}
                                data-action="delete-event"
                                data-target={trigger.id}
                                title="Delete event trigger"
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
