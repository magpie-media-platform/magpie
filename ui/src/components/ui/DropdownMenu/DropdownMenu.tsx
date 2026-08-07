import { forwardRef, type ComponentPropsWithoutRef, type ComponentRef } from 'react';
import * as DropdownMenuPrimitive from '@radix-ui/react-dropdown-menu';
import { CheckIcon, CaretRightIcon, CircleIcon } from '@phosphor-icons/react';

import './DropdownMenu.css';

export type DropdownMenuProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Root>;

export type DropdownMenuTriggerProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Trigger>;

export type DropdownMenuPortalProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Portal>;

export type DropdownMenuContentProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Content>;

export interface DropdownMenuItemProps extends ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Item> {
    inset?: boolean;
}

export type DropdownMenuCheckboxItemProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.CheckboxItem>;

export type DropdownMenuRadioItemProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.RadioItem>;

export type DropdownMenuLabelProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Label> & {
    inset?: boolean;
};

export type DropdownMenuSeparatorProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Separator>;

export type DropdownMenuGroupProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Group>;

export type DropdownMenuRadioGroupProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.RadioGroup>;

export type DropdownMenuSubProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.Sub>;

export type DropdownMenuSubTriggerProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.SubTrigger> & {
    inset?: boolean;
};

export type DropdownMenuSubContentProps = ComponentPropsWithoutRef<typeof DropdownMenuPrimitive.SubContent>;

export type DropdownMenuShortcutProps = ComponentPropsWithoutRef<'span'>;

const DropdownMenu = DropdownMenuPrimitive.Root;

const DropdownMenuTrigger = DropdownMenuPrimitive.Trigger;

const DropdownMenuPortal = DropdownMenuPrimitive.Portal;

const DropdownMenuGroup = DropdownMenuPrimitive.Group;

const DropdownMenuRadioGroup = DropdownMenuPrimitive.RadioGroup;

const DropdownMenuSub = DropdownMenuPrimitive.Sub;

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
    ({ className, inset, ...props }, ref) => {
        const classes = ['dropdown-menu-item', inset && 'dropdown-menu-item--inset', className]
            .filter(Boolean)
            .join(' ');

        return <DropdownMenuPrimitive.Item ref={ref} className={classes} data-inset={inset || undefined} {...props} />;
    },
);

DropdownMenuItem.displayName = 'DropdownMenuItem';

const DropdownMenuCheckboxItem = forwardRef<
    ComponentRef<typeof DropdownMenuPrimitive.CheckboxItem>,
    DropdownMenuCheckboxItemProps
>(({ className, children, checked, ...props }, ref) => {
    const classes = ['dropdown-menu-checkbox-item', className].filter(Boolean).join(' ');

    return (
        <DropdownMenuPrimitive.CheckboxItem ref={ref} className={classes} checked={checked} {...props}>
            <span className="dropdown-menu-item__indicator">
                <DropdownMenuPrimitive.ItemIndicator>
                    <CheckIcon size={14} weight="bold" aria-hidden="true" />
                </DropdownMenuPrimitive.ItemIndicator>
            </span>
            {children}
        </DropdownMenuPrimitive.CheckboxItem>
    );
});

DropdownMenuCheckboxItem.displayName = 'DropdownMenuCheckboxItem';

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
    ({ className, inset, ...props }, ref) => {
        const classes = ['dropdown-menu-label', inset && 'dropdown-menu-label--inset', className]
            .filter(Boolean)
            .join(' ');

        return <DropdownMenuPrimitive.Label ref={ref} className={classes} data-inset={inset || undefined} {...props} />;
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

const DropdownMenuSubTrigger = forwardRef<
    ComponentRef<typeof DropdownMenuPrimitive.SubTrigger>,
    DropdownMenuSubTriggerProps
>(({ className, inset, children, ...props }, ref) => {
    const classes = ['dropdown-menu-sub-trigger', inset && 'dropdown-menu-sub-trigger--inset', className]
        .filter(Boolean)
        .join(' ');

    return (
        <DropdownMenuPrimitive.SubTrigger ref={ref} className={classes} data-inset={inset || undefined} {...props}>
            {children}
            <CaretRightIcon size={14} weight="bold" className="dropdown-menu-sub-trigger__icon" aria-hidden="true" />
        </DropdownMenuPrimitive.SubTrigger>
    );
});

DropdownMenuSubTrigger.displayName = 'DropdownMenuSubTrigger';

const DropdownMenuSubContent = forwardRef<
    ComponentRef<typeof DropdownMenuPrimitive.SubContent>,
    DropdownMenuSubContentProps
>(({ className, ...props }, ref) => {
    const classes = ['dropdown-menu-sub-content', className].filter(Boolean).join(' ');

    return <DropdownMenuPrimitive.SubContent ref={ref} className={classes} {...props} />;
});

DropdownMenuSubContent.displayName = 'DropdownMenuSubContent';

const DropdownMenuShortcut = forwardRef<HTMLSpanElement, DropdownMenuShortcutProps>(({ className, ...props }, ref) => {
    const classes = ['dropdown-menu-shortcut', className].filter(Boolean).join(' ');

    return <span ref={ref} className={classes} {...props} />;
});

DropdownMenuShortcut.displayName = 'DropdownMenuShortcut';

export {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuPortal,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuCheckboxItem,
    DropdownMenuRadioItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuGroup,
    DropdownMenuRadioGroup,
    DropdownMenuSub,
    DropdownMenuSubTrigger,
    DropdownMenuSubContent,
    DropdownMenuShortcut,
};

export default DropdownMenu;
