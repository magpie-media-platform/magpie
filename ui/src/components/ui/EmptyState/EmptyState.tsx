import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import './EmptyState.css';

export type EmptyStateSize = 'sm' | 'md' | 'lg';

export interface EmptyStateProps extends HTMLAttributes<HTMLDivElement> {
    icon?: ReactNode;
    title: string;
    description?: string;
    action?: ReactNode;
    size?: EmptyStateSize;
    titleLevel?: 1 | 2 | 3;
}

export const EmptyState = forwardRef<HTMLDivElement, EmptyStateProps>(
    ({ icon, title, description, action, size = 'md', titleLevel = 3, className, ...props }, ref) => {
        const classes = ['empty-state', `empty-state--${size}`, className].filter(Boolean).join(' ');
        const Title = `h${titleLevel}` as const;

        return (
            <div ref={ref} className={classes} data-component="empty-state" data-size={size} {...props}>
                {icon && (
                    <div className="empty-state__icon" data-testid="empty-state__icon" aria-hidden="true">
                        {icon}
                    </div>
                )}
                <Title className="empty-state__title">{title}</Title>
                {description && <p className="empty-state__description">{description}</p>}
                {action && <div className="empty-state__action">{action}</div>}
            </div>
        );
    },
);

EmptyState.displayName = 'EmptyState';

export default EmptyState;
