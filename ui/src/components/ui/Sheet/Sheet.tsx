import { forwardRef, type ComponentPropsWithoutRef, type ComponentRef, type ReactNode } from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { XIcon } from '@phosphor-icons/react';

import { OverlayBodyGuard } from '../OverlayBodyGuard';

import './Sheet.css';

export type SheetSide = 'top' | 'right' | 'bottom' | 'left';

export interface SheetProps extends ComponentPropsWithoutRef<typeof DialogPrimitive.Root> {
    children: ReactNode;
}

export type SheetTriggerProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Trigger>;

export type SheetPortalProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Portal>;

export type SheetOverlayProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>;

export interface SheetContentProps extends ComponentPropsWithoutRef<typeof DialogPrimitive.Content> {
    side?: SheetSide;

    hideCloseButton?: boolean;
}

export type SheetHeaderProps = ComponentPropsWithoutRef<'div'>;

export type SheetFooterProps = ComponentPropsWithoutRef<'div'>;

export type SheetTitleProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Title>;

export type SheetDescriptionProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Description>;

export type SheetCloseProps = ComponentPropsWithoutRef<typeof DialogPrimitive.Close>;

const Sheet = DialogPrimitive.Root;

const SheetTrigger = DialogPrimitive.Trigger;

const SheetPortal = DialogPrimitive.Portal;

const SheetClose = DialogPrimitive.Close;

const SheetOverlay = forwardRef<ComponentRef<typeof DialogPrimitive.Overlay>, SheetOverlayProps>(
    ({ className, ...props }, ref) => {
        const classes = ['sheet-overlay', className].filter(Boolean).join(' ');

        return <DialogPrimitive.Overlay ref={ref} className={classes} {...props} />;
    },
);

SheetOverlay.displayName = 'SheetOverlay';

const SheetContent = forwardRef<ComponentRef<typeof DialogPrimitive.Content>, SheetContentProps>(
    (
        {
            className,
            children,
            side = 'right',
            hideCloseButton = false,
            'aria-describedby': ariaDescribedBy,
            onInteractOutside,
            ...props
        },
        ref,
    ) => {
        const classes = ['sheet-content', `sheet-content--${side}`, className].filter(Boolean).join(' ');

        return (
            <SheetPortal>
                <SheetOverlay />
                <DialogPrimitive.Content
                    ref={ref}
                    className={classes}
                    data-component="sheet"
                    data-side={side}
                    aria-describedby={ariaDescribedBy}
                    onInteractOutside={(event) => {
                        onInteractOutside?.(event);
                        if (event.defaultPrevented) {
                            return;
                        }
                        const target = event.target;
                        if (target instanceof Element && target.closest('.dialog-content, .dialog-overlay')) {
                            event.preventDefault();
                        }
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
            </SheetPortal>
        );
    },
);

SheetContent.displayName = 'SheetContent';

const SheetHeader = forwardRef<HTMLDivElement, SheetHeaderProps>(({ className, ...props }, ref) => {
    const classes = ['sheet-header', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

SheetHeader.displayName = 'SheetHeader';

const SheetFooter = forwardRef<HTMLDivElement, SheetFooterProps>(({ className, ...props }, ref) => {
    const classes = ['sheet-footer', className].filter(Boolean).join(' ');

    return <div ref={ref} className={classes} {...props} />;
});

SheetFooter.displayName = 'SheetFooter';

const SheetTitle = forwardRef<ComponentRef<typeof DialogPrimitive.Title>, SheetTitleProps>(
    ({ className, ...props }, ref) => {
        const classes = ['sheet-title', className].filter(Boolean).join(' ');

        return <DialogPrimitive.Title ref={ref} className={classes} {...props} />;
    },
);

SheetTitle.displayName = 'SheetTitle';

const SheetDescription = forwardRef<ComponentRef<typeof DialogPrimitive.Description>, SheetDescriptionProps>(
    ({ className, ...props }, ref) => {
        const classes = ['sheet-description', className].filter(Boolean).join(' ');

        return <DialogPrimitive.Description ref={ref} className={classes} {...props} />;
    },
);

SheetDescription.displayName = 'SheetDescription';

export {
    Sheet,
    SheetTrigger,
    SheetPortal,
    SheetOverlay,
    SheetContent,
    SheetHeader,
    SheetFooter,
    SheetTitle,
    SheetDescription,
    SheetClose,
};

export default Sheet;
