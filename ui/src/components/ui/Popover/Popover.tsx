import { forwardRef, type ComponentPropsWithoutRef, type ComponentRef } from 'react';
import * as PopoverPrimitive from '@radix-ui/react-popover';

import './Popover.css';

export type PopoverProps = ComponentPropsWithoutRef<typeof PopoverPrimitive.Root>;

export type PopoverTriggerProps = ComponentPropsWithoutRef<typeof PopoverPrimitive.Trigger>;

export type PopoverContentProps = ComponentPropsWithoutRef<typeof PopoverPrimitive.Content>;

const Popover = PopoverPrimitive.Root;

const PopoverTrigger = PopoverPrimitive.Trigger;

const PopoverAnchor = PopoverPrimitive.Anchor;

const PopoverContent = forwardRef<ComponentRef<typeof PopoverPrimitive.Content>, PopoverContentProps>(
    ({ className, align = 'center', sideOffset = 4, ...props }, ref) => {
        const classes = ['popover-content', className].filter(Boolean).join(' ');

        return (
            <PopoverPrimitive.Portal>
                <PopoverPrimitive.Content
                    ref={ref}
                    align={align}
                    sideOffset={sideOffset}
                    className={classes}
                    data-component="popover"
                    {...props}
                />
            </PopoverPrimitive.Portal>
        );
    },
);

PopoverContent.displayName = 'PopoverContent';

export { Popover, PopoverAnchor, PopoverTrigger, PopoverContent };

export default Popover;
