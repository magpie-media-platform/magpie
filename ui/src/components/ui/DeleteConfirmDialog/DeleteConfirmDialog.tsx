import { useEffect, useId, useState, type ReactNode, type RefObject } from 'react';
import { TrashIcon } from '@phosphor-icons/react';

import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '../Dialog';
import { Button, type ButtonVariant } from '../Button';
import { Switch } from '../Switch';

import { DisabledTooltip } from '@/auth';
import { returnFocusOnClose } from '@/lib/returnFocus';

import './DeleteConfirmDialog.css';

interface DeleteConfirmExtraToggle {
    label: string;

    checked: boolean;

    onChange: (checked: boolean) => void;

    disabledReason?: string;
}

export interface DeleteConfirmDialogProps {
    open: boolean;

    onConfirm: () => void;

    onCancel: () => void;

    itemName: string;

    itemType: string;

    description?: ReactNode;

    isDeleting?: boolean;

    extraToggle?: DeleteConfirmExtraToggle;

    title?: ReactNode;

    confirmVariant?: ButtonVariant;

    confirmLabel?: string;

    confirmBusyLabel?: string;

    confirmIcon?: ReactNode;

    confirmDisabled?: boolean;

    /** Content between the toggle and the buttons. */
    children?: ReactNode;

    /** Where focus goes when the dialog closes. Without it, the dialog keeps its default focus behaviour. */
    returnFocusRef?: RefObject<HTMLElement | null>;

    /** Where focus goes when the control in returnFocusRef is gone. */
    fallbackFocusRef?: RefObject<HTMLElement | null>;
}

export function DeleteConfirmDialog(props: DeleteConfirmDialogProps) {
    const { open, onConfirm, onCancel, returnFocusRef, fallbackFocusRef } = props;
    const [lastOpenProps, setLastOpenProps] = useState(props);
    useEffect(() => {
        if (open) setLastOpenProps(props);
    }, [open, props]);
    const {
        itemName,
        itemType,
        description,
        isDeleting = false,
        extraToggle,
        title,
        confirmVariant = 'destructive',
        confirmLabel = 'Delete',
        confirmBusyLabel = 'Deleting...',
        confirmIcon,
        confirmDisabled = false,
        children,
    } = open ? props : lastOpenProps;
    const toggleId = useId();
    return (
        <Dialog
            open={open}
            onOpenChange={(isOpen) => {
                if (!isOpen) onCancel();
            }}
        >
            <DialogContent
                size="alert"
                data-component="delete-confirm-dialog"
                onCloseAutoFocus={returnFocusOnClose(returnFocusRef, fallbackFocusRef)}
            >
                <DialogHeader>
                    <DialogTitle>{title ?? `Delete ${itemType}`}</DialogTitle>
                    <DialogDescription>
                        {description ?? (
                            <>Are you sure you want to delete &quot;{itemName}&quot;? This action cannot be undone.</>
                        )}
                    </DialogDescription>
                </DialogHeader>
                {extraToggle && (
                    <div className="delete-confirm-dialog__toggle" data-component="delete-confirm-toggle">
                        <label className="delete-confirm-dialog__toggle-label" htmlFor={toggleId}>
                            {extraToggle.label}
                        </label>
                        <DisabledTooltip
                            disabled={extraToggle.disabledReason !== undefined}
                            message={extraToggle.disabledReason ?? ''}
                        >
                            <Switch
                                id={toggleId}
                                checked={extraToggle.checked}
                                onCheckedChange={(value) => extraToggle.onChange(value === true)}
                                disabled={isDeleting || extraToggle.disabledReason !== undefined}
                                data-action="extra-toggle"
                            />
                        </DisabledTooltip>
                    </div>
                )}
                {children}
                <DialogFooter>
                    <Button variant="ghost" onClick={onCancel} disabled={isDeleting} data-action="cancel" autoFocus>
                        Cancel
                    </Button>
                    <Button
                        variant={confirmVariant}
                        onClick={onConfirm}
                        loading={isDeleting}
                        disabled={confirmDisabled}
                        data-action="confirm-delete"
                    >
                        {confirmIcon ?? <TrashIcon weight="bold" aria-hidden="true" />}
                        {isDeleting ? confirmBusyLabel : confirmLabel}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

DeleteConfirmDialog.displayName = 'DeleteConfirmDialog';

export default DeleteConfirmDialog;
