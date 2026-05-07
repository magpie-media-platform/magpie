import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import * as SelectPrimitive from '@radix-ui/react-select';

import './Select.css';

export type SelectSize = 'sm' | 'md' | 'lg';

export interface SelectProps extends ComponentPropsWithoutRef<typeof SelectPrimitive.Root> {
    size?: SelectSize;

    error?: boolean;

    placeholder?: string;

    'aria-label'?: string;
}

export interface SelectTriggerProps extends ComponentPropsWithoutRef<typeof SelectPrimitive.Trigger> {
    size?: SelectSize;

    error?: boolean;

    placeholder?: string;

    'aria-describedby'?: string;
}

export type SelectContentProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Content>;

export type SelectItemProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Item>;

export type SelectGroupProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Group>;

export type SelectLabelProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Label>;

export type SelectSeparatorProps = ComponentPropsWithoutRef<typeof SelectPrimitive.Separator>;

const SelectRoot = SelectPrimitive.Root;

const SelectTrigger = forwardRef<HTMLButtonElement, SelectTriggerProps>(
    (
        { size = 'md', error = false, placeholder, className, children, 'aria-describedby': ariaDescribedBy, ...props },
        ref,
    ) => {
        const classes = ['select-trigger', `select-trigger--${size}`, error && 'select-trigger--error', className]
            .filter(Boolean)
            .join(' ');

        return (
            <SelectPrimitive.Trigger
                ref={ref}
                className={classes}
                data-component="select"
                data-size={size}
                data-error={error || undefined}
                aria-invalid={error || undefined}
                aria-describedby={ariaDescribedBy}
                {...props}
            >
                {children ?? <SelectPrimitive.Value placeholder={placeholder} />}
                <SelectPrimitive.Icon className="select-trigger__icon" aria-hidden="true">
                    <ChevronDownIcon />
                </SelectPrimitive.Icon>
            </SelectPrimitive.Trigger>
        );
    },
);

SelectTrigger.displayName = 'SelectTrigger';

const SelectContent = forwardRef<HTMLDivElement, SelectContentProps>(
    ({ className, children, position = 'popper', sideOffset = 4, ...props }, ref) => {
        const classes = ['select-content', className].filter(Boolean).join(' ');

        return (
            <SelectPrimitive.Portal>
                <SelectPrimitive.Content
                    ref={ref}
                    className={classes}
                    position={position}
                    sideOffset={sideOffset}
                    {...props}
                >
                    <SelectPrimitive.Viewport className="select-viewport">{children}</SelectPrimitive.Viewport>
                </SelectPrimitive.Content>
            </SelectPrimitive.Portal>
        );
    },
);

SelectContent.displayName = 'SelectContent';

const SelectItem = forwardRef<HTMLDivElement, SelectItemProps>(({ className, children, ...props }, ref) => {
    const classes = ['select-item', className].filter(Boolean).join(' ');

    return (
        <SelectPrimitive.Item ref={ref} className={classes} {...props}>
            <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
            <SelectPrimitive.ItemIndicator className="select-item__indicator">
                <CheckIcon />
            </SelectPrimitive.ItemIndicator>
        </SelectPrimitive.Item>
    );
});

SelectItem.displayName = 'SelectItem';

const SelectGroup = SelectPrimitive.Group;

const SelectLabel = forwardRef<HTMLDivElement, SelectLabelProps>(({ className, ...props }, ref) => {
    const classes = ['select-label', className].filter(Boolean).join(' ');
    return <SelectPrimitive.Label ref={ref} className={classes} {...props} />;
});

SelectLabel.displayName = 'SelectLabel';

const SelectSeparator = forwardRef<HTMLDivElement, SelectSeparatorProps>(({ className, ...props }, ref) => {
    const classes = ['select-separator', className].filter(Boolean).join(' ');
    return <SelectPrimitive.Separator ref={ref} className={classes} {...props} />;
});

SelectSeparator.displayName = 'SelectSeparator';

function ChevronDownIcon(): ReactNode {
    return (
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path
                d="M4 6L8 10L12 6"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
            />
        </svg>
    );
}

function CheckIcon(): ReactNode {
    return (
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path
                d="M3 8L6.5 11.5L13 5"
                stroke="currentColor"
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
            />
        </svg>
    );
}

const SelectValue = SelectPrimitive.Value;

export {
    SelectRoot as Select,
    SelectTrigger,
    SelectContent,
    SelectItem,
    SelectGroup,
    SelectLabel,
    SelectSeparator,
    SelectValue,
};

export default SelectRoot;
