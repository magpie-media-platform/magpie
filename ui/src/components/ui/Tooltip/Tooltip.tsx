import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import * as TooltipPrimitive from '@radix-ui/react-tooltip';

import './Tooltip.css';

export interface TooltipProps extends ComponentPropsWithoutRef<typeof TooltipPrimitive.Root> {
    content: ReactNode;

    side?: 'top' | 'right' | 'bottom' | 'left';

    align?: 'start' | 'center' | 'end';

    sideOffset?: number;

    delayDuration?: number;

    contentProps?: Omit<TooltipContentProps, 'children'>;

    children: ReactNode;
}

export type TooltipContentProps = ComponentPropsWithoutRef<typeof TooltipPrimitive.Content>;

export type TooltipTriggerProps = ComponentPropsWithoutRef<typeof TooltipPrimitive.Trigger>;

const TooltipProvider = TooltipPrimitive.Provider;

const TooltipRoot = TooltipPrimitive.Root;

const TooltipTrigger = TooltipPrimitive.Trigger;

const TooltipPortal = TooltipPrimitive.Portal;

const TooltipContent = forwardRef<HTMLDivElement, TooltipContentProps>(
    ({ className, sideOffset = 4, ...props }, ref) => {
        const classes = ['tooltip-content', className].filter(Boolean).join(' ');

        return (
            <TooltipPrimitive.Content
                ref={ref}
                className={classes}
                sideOffset={sideOffset}
                data-component="tooltip"
                {...props}
            />
        );
    },
);

TooltipContent.displayName = 'TooltipContent';

const TooltipArrow = forwardRef<SVGSVGElement, ComponentPropsWithoutRef<typeof TooltipPrimitive.Arrow>>(
    ({ className, ...props }, ref) => {
        const classes = ['tooltip-arrow', className].filter(Boolean).join(' ');

        return <TooltipPrimitive.Arrow ref={ref} className={classes} {...props} />;
    },
);

TooltipArrow.displayName = 'TooltipArrow';

function Tooltip({
    content,
    side = 'top',
    align = 'center',
    sideOffset = 4,
    delayDuration = 200,
    contentProps,
    children,
    ...rootProps
}: TooltipProps) {
    return (
        <TooltipRoot delayDuration={delayDuration} {...rootProps}>
            <TooltipTrigger asChild>{children}</TooltipTrigger>
            <TooltipPortal>
                <TooltipContent side={side} align={align} sideOffset={sideOffset} {...contentProps}>
                    {content}
                    <TooltipArrow />
                </TooltipContent>
            </TooltipPortal>
        </TooltipRoot>
    );
}

Tooltip.displayName = 'Tooltip';

export { Tooltip, TooltipProvider, TooltipRoot, TooltipTrigger, TooltipPortal, TooltipContent, TooltipArrow };

export default Tooltip;
