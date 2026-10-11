import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import { Button } from '@/components/ui/Button';

import './EmptyState.css';

export type EmptyStateSize = 'sm' | 'md' | 'lg';

export interface EmptyStateProps extends HTMLAttributes<HTMLDivElement> {
    icon?: ReactNode;
    title: string;
    description?: string;
    action?: ReactNode;
    size?: EmptyStateSize;
    titleLevel?: 1 | 2 | 3;
    /** Called when the user clicks Retry. When set, a Retry button shows in place of action. */
    onRetry?: () => void;
}

export const EmptyState = forwardRef<HTMLDivElement, EmptyStateProps>(
    ({ icon, title, description, action, size = 'md', titleLevel = 3, onRetry, className, ...props }, ref) => {
        const classes = ['empty-state', `empty-state--${size}`, className].filter(Boolean).join(' ');
        const Title = `h${titleLevel}` as const;
        const shownAction = onRetry ? (
            <Button variant="secondary" size="sm" onClick={() => onRetry()}>
                Retry
            </Button>
        ) : (
            action
        );

        return (
            <div ref={ref} className={classes} data-component="empty-state" data-size={size} {...props}>
                {icon && (
                    <div className="empty-state__icon" data-testid="empty-state__icon" aria-hidden="true">
                        {icon}
                    </div>
                )}
                <Title className="empty-state__title">{title}</Title>
                {description && <p className="empty-state__description">{description}</p>}
                {shownAction && <div className="empty-state__action">{shownAction}</div>}
            </div>
        );
    },
);

EmptyState.displayName = 'EmptyState';

export default EmptyState;
