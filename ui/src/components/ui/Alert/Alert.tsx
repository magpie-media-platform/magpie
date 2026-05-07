import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import { X } from '@phosphor-icons/react';

import './Alert.css';

export type AlertVariant = 'info' | 'success' | 'warning' | 'error';

export interface AlertProps extends Omit<HTMLAttributes<HTMLDivElement>, 'title'> {
    variant?: AlertVariant;

    title?: ReactNode;
    icon?: ReactNode;
    dismissible?: boolean;
    onDismiss?: () => void;
    children?: ReactNode;
}

export const Alert = forwardRef<HTMLDivElement, AlertProps>(
    ({ variant = 'info', title, icon, dismissible = false, onDismiss, className, children, ...props }, ref) => {
        const classes = ['alert', `alert--${variant}`, className].filter(Boolean).join(' ');

        return (
            <div ref={ref} role="alert" className={classes} data-component="alert" data-variant={variant} {...props}>
                {icon && (
                    <span className="alert__icon" aria-hidden="true">
                        {icon}
                    </span>
                )}
                <div className="alert__content">
                    {title && <div className="alert__title">{title}</div>}
                    {children && <div className="alert__description">{children}</div>}
                </div>
                {dismissible && (
                    <button type="button" className="alert__dismiss" onClick={onDismiss} aria-label="Dismiss alert">
                        <X size={16} weight="bold" aria-hidden="true" />
                    </button>
                )}
            </div>
        );
    },
);

Alert.displayName = 'Alert';

export default Alert;
