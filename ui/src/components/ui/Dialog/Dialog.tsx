import { forwardRef, type ComponentPropsWithoutRef, type ComponentRef, type ReactNode } from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { XIcon } from '@phosphor-icons/react';

import { OverlayBodyGuard } from '../OverlayBodyGuard';
import { yieldEscapeToOpenCombobox } from './yieldEscapeToOpenCombobox';

import './Dialog.css';

export interface DialogProps extends ComponentPropsWithoutRef<typeof DialogPrimitive.Root> {
    children: ReactNode;
}

export type DialogTriggerProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Trigger>;

export type DialogPortalProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Portal>;

export type DialogOverlayProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>;

export interface DialogContentProps extends ComponentPropsWithoutRef<typeof DialogPrimitive.Content> {
    hideCloseButton?: boolean;

    size?: 'default' | 'alert';
}

export type DialogHeaderProps = ComponentPropsWithoutRef<'div'>;

export type DialogBodyProps = ComponentPropsWithoutRef<'div'>;

export type DialogFooterProps = ComponentPropsWithoutRef<'div'>;

export type DialogTitleProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Title>;

export type DialogDescriptionProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Description>;

export type DialogCloseProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Close>;

const Dialog = DialogPrimitive.Root;

const DialogTrigger = DialogPrimitive.Trigger;

const DialogPortal = DialogPrimitive.Portal;

const DialogClose = DialogPrimitive.Close;

const DialogOverlay = forwardRef<ComponentRef<typeof DialogPrimitive.Overlay>, DialogOverlayProps>(
    ({ className, ...props }, ref) => {
        const classes = ['dialog-overlay', className].filter(Boolean).join(' ');

        return <DialogPrimitive.Overlay data-dialog-overlay ref={ref} className={classes} {...props} />;
    },
);

DialogOverlay.displayName = 'DialogOverlay';

const DialogContent = forwardRef<ComponentRef<typeof DialogPrimitive.Content>, DialogContentProps>(
    (
        {
            className,
            children,
            hideCloseButton = false,
            size = 'default',
            'aria-describedby': ariaDescribedBy,
            onEscapeKeyDown,
            ...props
        },
        ref,
    ) => {
        const classes = ['dialog-content', className].filter(Boolean).join(' ');

        return (
            <DialogPortal>
                <DialogOverlay />
                <DialogPrimitive.Content
                    ref={ref}
                    className={classes}
                    data-component="dialog"
                    data-overlay-content
                    data-dialog-content
                    data-size={size}
                    aria-describedby={ariaDescribedBy}
                    onEscapeKeyDown={(event) => {
                        onEscapeKeyDown?.(event);
                        yieldEscapeToOpenCombobox(event);
                    }}
                    {...props}
                >
                    <OverlayBodyGuard />
                    {children}
                    {!hideCloseButton && (
                        <DialogPrimitive.Close className="overlay-close" aria-label="Close">
                            <XIcon size={20} weight="bold" aria-hidden="true" />
                        </DialogPrimitive.Close>
                    )}
                </DialogPrimitive.Content>
            </DialogPortal>
        );
    },
);

DialogContent.displayName = 'DialogContent';

const DialogHeader = forwardRef<HTMLDivElement, DialogHeaderProps>(({ className, ...props }, ref) => {
    const classes = ['dialog-header', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

DialogHeader.displayName = 'DialogHeader';

const DialogBody = forwardRef<HTMLDivElement, DialogBodyProps>(({ className, ...props }, ref) => {
    const classes = ['dialog-body', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

DialogBody.displayName = 'DialogBody';

const DialogFooter = forwardRef<HTMLDivElement, DialogFooterProps>(({ className, ...props }, ref) => {
    const classes = ['dialog-footer', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

DialogFooter.displayName = 'DialogFooter';

const DialogTitle = forwardRef<ComponentRef<typeof DialogPrimitive.Title>, DialogTitleProps>(
    ({ className, ...props }, ref) => {
        const classes = ['dialog-title', className].filter(Boolean).join(' ');

        return <DialogPrimitive.Title ref={ref} className={classes} {...props} />;
    },
);

DialogTitle.displayName = 'DialogTitle';

const DialogDescription = forwardRef<ComponentRef<typeof DialogPrimitive.Description>, DialogDescriptionProps>(
    ({ className, ...props }, ref) => {
        const classes = ['dialog-description', className].filter(Boolean).join(' ');

        return <DialogPrimitive.Description ref={ref} className={classes} {...props} />;
    },
);

DialogDescription.displayName = 'DialogDescription';

export {
    Dialog,
    DialogTrigger,
    DialogPortal,
    DialogOverlay,
    DialogContent,
    DialogHeader,
    DialogBody,
    DialogFooter,
    DialogTitle,
    DialogDescription,
    DialogClose,
};

export default Dialog;
