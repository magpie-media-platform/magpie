import { forwardRef, type ComponentPropsWithoutRef, type ComponentRef } from 'react';
import * as DropdownMenuPrimitive from '@radix-ui/react-dropdown-menu';
import { CircleIcon } from '@phosphor-icons/react';

import './DropdownMenu.css';

export type DropdownMenuProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Root>;

export type DropdownMenuTriggerProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Trigger>;

export type DropdownMenuContentProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Content>;

export type DropdownMenuItemProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Item>;

export type DropdownMenuRadioItemProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.RadioItem>;

export type DropdownMenuLabelProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Label>;

export type DropdownMenuSeparatorProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Separator>;

export type DropdownMenuRadioGroupProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.RadioGroup>;

const DropdownMenu = DropdownMenuPrimitive.Root;

const DropdownMenuTrigger = DropdownMenuPrimitive.Trigger;

const DropdownMenuRadioGroup = DropdownMenuPrimitive.RadioGroup;

const DropdownMenuContent = forwardRef<ComponentRef<typeof DropdownMenuPrimitive.Content>, DropdownMenuContentProps>(
    ({ className, sideOffset = 4, ...props }, ref) => {
        const classes = ['dropdown-menu-content', className].filter(Boolean).join(' ');

        return (
            <DropdownMenuPrimitive.Portal>
                <DropdownMenuPrimitive.Content
                    ref={ref}
                    sideOffset={sideOffset}
                    className={classes}
                    data-component="dropdown-menu"
                    {...props}
                />
            </DropdownMenuPrimitive.Portal>
        );
    },
);

DropdownMenuContent.displayName = 'DropdownMenuContent';

const DropdownMenuItem = forwardRef<ComponentRef<typeof DropdownMenuPrimitive.Item>, DropdownMenuItemProps>(
    ({ className, ...props }, ref) => {
        const classes = ['dropdown-menu-item', className].filter(Boolean).join(' ');

        return <DropdownMenuPrimitive.Item ref={ref} className={classes} {...props} />;
    },
);

DropdownMenuItem.displayName = 'DropdownMenuItem';

const DropdownMenuRadioItem = forwardRef<
    ComponentRef<typeof DropdownMenuPrimitive.RadioItem>,
    DropdownMenuRadioItemProps
>(({ className, children, ...props }, ref) => {
    const classes = ['dropdown-menu-radio-item', className].filter(Boolean).join(' ');

    return (
        <DropdownMenuPrimitive.RadioItem ref={ref} className={classes} {...props}>
            <span className="dropdown-menu-item__indicator">
                <DropdownMenuPrimitive.ItemIndicator>
                    <CircleIcon size={8} weight="fill" aria-hidden="true" />
                </DropdownMenuPrimitive.ItemIndicator>
            </span>
            {children}
        </DropdownMenuPrimitive.RadioItem>
    );
});

DropdownMenuRadioItem.displayName = 'DropdownMenuRadioItem';

const DropdownMenuLabel = forwardRef<ComponentRef<typeof DropdownMenuPrimitive.Label>, DropdownMenuLabelProps>(
    ({ className, ...props }, ref) => {
        const classes = ['dropdown-menu-label', className].filter(Boolean).join(' ');

        return <DropdownMenuPrimitive.Label ref={ref} className={classes} {...props} />;
    },
);

DropdownMenuLabel.displayName = 'DropdownMenuLabel';

const DropdownMenuSeparator = forwardRef<
    ComponentRef<typeof DropdownMenuPrimitive.Separator>,
    DropdownMenuSeparatorProps
>(({ className, ...props }, ref) => {
    const classes = ['dropdown-menu-separator', className].filter(Boolean).join(' ');

    return <DropdownMenuPrimitive.Separator ref={ref} className={classes} {...props} />;
});

DropdownMenuSeparator.displayName = 'DropdownMenuSeparator';

export {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuRadioItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuRadioGroup,
};

export default DropdownMenu;
