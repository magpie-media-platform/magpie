import { forwardRef, type ComponentPropsWithoutRef, type ComponentRef, type ReactNode } from 'react';
import * as ToastPrimitive from '@radix-ui/react-toast';
import { XIcon } from '@phosphor-icons/react';

import type { ToastVariant } from '@/hooks/ui/useToast';

import './Toast.css';

export type { ToastVariant } from '@/hooks/ui/useToast';

export type ToastProviderProps = ComponentPropsWithoutRef<typeof ToastPrimitive.Provider>;

export interface ToastViewportProps extends ComponentPropsWithoutRef<typeof ToastPrimitive.Viewport> {
    position?: 'top-right' | 'top-left' | 'bottom-right' | 'bottom-left';
}

export interface ToastProps extends ComponentPropsWithoutRef<typeof ToastPrimitive.Root> {
    variant?: ToastVariant;

    children: ReactNode;
}

export type ToastActionProps = ComponentPropsWithoutRef<typeof ToastPrimitive.Action>;

export type ToastCloseProps = ComponentPropsWithoutRef<typeof ToastPrimitive.Close>;

export type ToastTitleProps = ComponentPropsWithoutRef<typeof ToastPrimitive.Title>;

export type ToastDescriptionProps = ComponentPropsWithoutRef<typeof ToastPrimitive.Description>;

const ToastProvider = ToastPrimitive.Provider;

const ToastViewport = forwardRef<ComponentRef<typeof ToastPrimitive.Viewport>, ToastViewportProps>(
    ({ className, position = 'bottom-right', ...props }, ref) => {
        const classes = ['toast-viewport', `toast-viewport--${position}`, className].filter(Boolean).join(' ');

        return <ToastPrimitive.Viewport ref={ref} className={classes} data-position={position} {...props} />;
    },
);

ToastViewport.displayName = 'ToastViewport';

const Toast = forwardRef<ComponentRef<typeof ToastPrimitive.Root>, ToastProps>(
    ({ className, variant = 'default', children, ...props }, ref) => {
        const classes = ['toast', `toast--${variant}`, className].filter(Boolean).join(' ');

        return (
            <ToastPrimitive.Root ref={ref} className={classes} data-component="toast" data-variant={variant} {...props}>
                {children}
            </ToastPrimitive.Root>
        );
    },
);

Toast.displayName = 'Toast';

const ToastAction = forwardRef<ComponentRef<typeof ToastPrimitive.Action>, ToastActionProps>(
    ({ className, onPointerDown, ...props }, ref) => {
        const classes = ['toast-action', className].filter(Boolean).join(' ');

        return (
            <ToastPrimitive.Action
                ref={ref}
                className={classes}
                onPointerDown={(event) => {
                    onPointerDown?.(event);
                    event.stopPropagation();
                }}
                draggable={false}
                {...props}
            />
        );
    },
);

ToastAction.displayName = 'ToastAction';

const ToastClose = forwardRef<ComponentRef<typeof ToastPrimitive.Close>, ToastCloseProps>(
    ({ className, onPointerDown, ...props }, ref) => {
        const classes = ['toast-close', className].filter(Boolean).join(' ');

        return (
            <ToastPrimitive.Close
                ref={ref}
                className={classes}
                aria-label="Close"
                onPointerDown={(event) => {
                    onPointerDown?.(event);
                    event.stopPropagation();
                }}
                {...props}
            >
                <XIcon size={16} weight="bold" aria-hidden="true" />
            </ToastPrimitive.Close>
        );
    },
);

ToastClose.displayName = 'ToastClose';

const ToastTitle = forwardRef<ComponentRef<typeof ToastPrimitive.Title>, ToastTitleProps>(
    ({ className, ...props }, ref) => {
        const classes = ['toast-title', className].filter(Boolean).join(' ');

        return <ToastPrimitive.Title ref={ref} className={classes} data-testid="toast-title" {...props} />;
    },
);

ToastTitle.displayName = 'ToastTitle';

const ToastDescription = forwardRef<ComponentRef<typeof ToastPrimitive.Description>, ToastDescriptionProps>(
    ({ className, ...props }, ref) => {
        const classes = ['toast-description', className].filter(Boolean).join(' ');

        return <ToastPrimitive.Description ref={ref} className={classes} {...props} />;
    },
);

ToastDescription.displayName = 'ToastDescription';

export { ToastProvider, ToastViewport, Toast, ToastAction, ToastClose, ToastTitle, ToastDescription };

export default Toast;
