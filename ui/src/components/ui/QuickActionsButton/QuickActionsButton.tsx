import { forwardRef, useId, useState, type ComponentPropsWithoutRef, type ReactNode, type Ref } from 'react';
import { DotsThreeVerticalIcon } from '@phosphor-icons/react';

import { Button } from '../Button';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '../Sheet';

import './QuickActionsButton.css';

export interface QuickAction {
    id: string;

    label: string;

    icon: ReactNode;

    onClick: () => void;

    disabled?: boolean;

    /** A short note shown under the label in the sheet, for example why the action is disabled. */
    description?: string;

    variant?: 'default' | 'primary' | 'destructive';
}

export interface QuickActionsButtonProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    actions: QuickAction[];

    title?: string;

    visible?: boolean;

    /** Receives the trigger button, so a dialog opened from the sheet can return focus to it. */
    triggerRef?: Ref<HTMLElement>;

    /** Extra content shown in the sheet below the actions, for example extension-provided controls. */
    sheetContent?: ReactNode;
}

export const QuickActionsButton = forwardRef<HTMLDivElement, QuickActionsButtonProps>(
    ({ actions, title = 'Quick Actions', visible = true, triggerRef, sheetContent, className, ...props }, ref) => {
        const [open, setOpen] = useState(false);
        const descriptionIdPrefix = useId();

        const handleActionClick = (action: QuickAction) => {
            if (action.disabled) return;
            action.onClick();
            setOpen(false);
        };

        if (!visible || (actions.length === 0 && !sheetContent)) {
            return null;
        }

        const classes = ['quick-actions-button', className].filter(Boolean).join(' ');

        if (actions.length === 1 && !sheetContent) {
            const action = actions[0];
            return (
                <div ref={ref} className={classes} {...props}>
                    <Button
                        ref={triggerRef}
                        variant="primary"
                        size="lg"
                        className="quick-actions-button__fab"
                        aria-label={action.label}
                        data-action={action.id}
                        disabled={action.disabled}
                        onClick={action.onClick}
                    >
                        <span aria-hidden="true">{action.icon}</span>
                    </Button>
                </div>
            );
        }

        return (
            <div ref={ref} className={classes} {...props}>
                <Sheet open={open} onOpenChange={setOpen}>
                    <SheetTrigger asChild>
                        <Button
                            ref={triggerRef}
                            variant="primary"
                            size="lg"
                            className="quick-actions-button__fab"
                            aria-label={title}
                            data-action="open-quick-actions"
                        >
                            <DotsThreeVerticalIcon weight="bold" aria-hidden="true" />
                        </Button>
                    </SheetTrigger>
                    <SheetContent side="bottom" className="quick-actions-button__sheet">
                        <SheetHeader className="quick-actions-button__sheet-header">
                            <SheetTitle>{title}</SheetTitle>
                        </SheetHeader>
                        <div className="quick-actions-button__actions" role="group" aria-label="Quick actions">
                            {actions.map((action) => (
                                <button
                                    key={action.id}
                                    type="button"
                                    className={`quick-actions-button__action quick-actions-button__action--${action.variant || 'default'}`}
                                    onClick={() => handleActionClick(action)}
                                    disabled={action.disabled}
                                    data-action={action.id}
                                    data-variant={action.variant || 'default'}
                                    aria-describedby={
                                        action.description ? `${descriptionIdPrefix}-${action.id}` : undefined
                                    }
                                >
                                    <span className="quick-actions-button__action-icon" aria-hidden="true">
                                        {action.icon}
                                    </span>
                                    <span className="quick-actions-button__action-label">
                                        {action.label}
                                        {action.description && (
                                            <span
                                                id={`${descriptionIdPrefix}-${action.id}`}
                                                className="quick-actions-button__action-description"
                                                aria-hidden="true"
                                            >
                                                {action.description}
                                            </span>
                                        )}
                                    </span>
                                </button>
                            ))}
                        </div>
                        {sheetContent && <div className="quick-actions-button__sheet-extra">{sheetContent}</div>}
                    </SheetContent>
                </Sheet>
            </div>
        );
    },
);

QuickActionsButton.displayName = 'QuickActionsButton';

export default QuickActionsButton;
