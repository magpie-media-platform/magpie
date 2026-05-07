import { forwardRef, useState, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import { DotsThreeVertical } from '@phosphor-icons/react';

import { Button } from '../Button';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '../Sheet';

import './QuickActionsButton.css';

export interface QuickAction {
    id: string;

    label: string;

    icon: ReactNode;

    onClick: () => void;

    disabled?: boolean;

    variant?: 'default' | 'primary' | 'destructive';
}

export interface QuickActionsButtonProps extends Omit<ComponentPropsWithoutRef<'div'>, 'children'> {
    actions: QuickAction[];

    title?: string;

    visible?: boolean;
}

export const QuickActionsButton = forwardRef<HTMLDivElement, QuickActionsButtonProps>(
    ({ actions, title = 'Quick Actions', visible = true, className, ...props }, ref) => {
        const [open, setOpen] = useState(false);

        const handleActionClick = (action: QuickAction) => {
            if (action.disabled) return;
            action.onClick();
            setOpen(false);
        };

        if (!visible || actions.length === 0) {
            return null;
        }

        const classes = ['quick-actions-button', className].filter(Boolean).join(' ');

        if (actions.length === 1) {
            const action = actions[0];
            return (
                <div ref={ref} className={classes} {...props}>
                    <Button
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
                            variant="primary"
                            size="lg"
                            className="quick-actions-button__fab"
                            aria-label={title}
                            data-action="open-quick-actions"
                        >
                            <DotsThreeVertical weight="bold" aria-hidden="true" />
                        </Button>
                    </SheetTrigger>
                    <SheetContent side="bottom" className="quick-actions-button__sheet">
                        <SheetHeader className="quick-actions-button__sheet-header">
                            <SheetTitle>{title}</SheetTitle>
                            <SheetDescription className="sr-only">
                                Select an action from the list below.
                            </SheetDescription>
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
                                >
                                    <span className="quick-actions-button__action-icon" aria-hidden="true">
                                        {action.icon}
                                    </span>
                                    <span className="quick-actions-button__action-label">{action.label}</span>
                                </button>
                            ))}
                        </div>
                    </SheetContent>
                </Sheet>
            </div>
        );
    },
);

QuickActionsButton.displayName = 'QuickActionsButton';

export default QuickActionsButton;
