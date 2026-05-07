import { PencilSimple, Trash, Lightning } from '@phosphor-icons/react';

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
import type { TemplateOption } from '@/lib/jobExecution';
import { EventTriggerForm } from './EventTriggerForm';
import type { useEventTriggerCrud } from './useEventTriggerCrud';

import '@/styles/card-actions.css';

type EventTriggerCrud = ReturnType<typeof useEventTriggerCrud>;

export interface EventTriggersTabProps {
    crud: EventTriggerCrud;
    templates: TemplateOption[];
    canManage: boolean;
}

export function EventTriggersTab({ crud, templates, canManage }: EventTriggersTabProps) {
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
            {triggers.map((trigger) => (
                <EventTriggerRow
                    key={trigger.id}
                    trigger={trigger}
                    isToggling={togglingTriggers.has(trigger.id)}
                    canManage={canManage}
                    onEdit={() => handleEdit(trigger)}
                    onDelete={() => handleDeleteClick(trigger)}
                    onToggle={(enabled) => handleToggle(trigger, enabled)}
                />
            ))}

            <EventTriggerForm
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
                            <Trash weight="bold" aria-hidden="true" />
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
    const pattern = trigger.eventFilter?.eventName?.value;
    if (!pattern) {
        return 'All events';
    }
    const presets: Record<string, string> = {
        '.*': 'All events',
        'content\\.added': 'Content added',
        'content\\.updated': 'Content updated',
        'content\\.removed': 'Content removed',
        'download\\.completed': 'Download completed',
        'download\\.failed': 'Download failed',
        'file\\.imported': 'File imported',
        'metadata\\.refreshed': 'Metadata refreshed',
    };
    return presets[pattern] ?? pattern;
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
    const filterCount = trigger.eventFilter?.eventContext ? Object.keys(trigger.eventFilter.eventContext).length : 0;

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
                        <span
                            className="jobs-page__event-trigger-pattern"
                            title={trigger.eventFilter?.eventName?.value ?? ''}
                        >
                            <Lightning weight="regular" aria-hidden="true" />
                            {describeEventPattern(trigger)}
                        </span>
                    </div>
                    <div className="jobs-page__event-trigger-details">
                        <span className="jobs-page__event-trigger-mode">
                            {trigger.executionMode === 1 ? 'Immediate' : 'Queue'}
                        </span>
                        {filterCount > 0 && (
                            <span className="jobs-page__event-trigger-filters">
                                {filterCount} filter{filterCount !== 1 ? 's' : ''}
                            </span>
                        )}
                    </div>
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
                                <PencilSimple weight="bold" aria-hidden="true" />
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
                                <Trash weight="bold" aria-hidden="true" />
                            </button>
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}
