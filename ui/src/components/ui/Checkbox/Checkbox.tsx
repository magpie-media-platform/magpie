import { forwardRef, type ComponentPropsWithoutRef, type ReactNode } from 'react';
import * as CheckboxPrimitive from '@radix-ui/react-checkbox';

import './Checkbox.css';

export type CheckboxSize = 'sm' | 'md' | 'lg';

export interface CheckboxProps extends ComponentPropsWithoutRef<typeof CheckboxPrimitive.Root> {
    size?: CheckboxSize;

    error?: boolean;

    'aria-describedby'?: string;
}

const Checkbox = forwardRef<HTMLButtonElement, CheckboxProps>(
    ({ size = 'md', error = false, className, 'aria-describedby': ariaDescribedBy, ...props }, ref) => {
        const classes = ['checkbox', `checkbox--${size}`, error && 'checkbox--error', className]
            .filter(Boolean)
            .join(' ');

        return (
            <CheckboxPrimitive.Root
                ref={ref}
                className={classes}
                data-component="checkbox"
                data-size={size}
                data-error={error || undefined}
                aria-invalid={error || undefined}
                aria-describedby={ariaDescribedBy}
                {...props}
            >
                <span className="checkbox__control" aria-hidden="true">
                    <CheckboxPrimitive.Indicator className="checkbox__indicator">
                        <CheckIcon />
                    </CheckboxPrimitive.Indicator>
                </span>
            </CheckboxPrimitive.Root>
        );
    },
);

Checkbox.displayName = 'Checkbox';

function CheckIcon(): ReactNode {
    return (
        <svg
            width="12"
            height="12"
            viewBox="0 0 12 12"
            fill="none"
            xmlns="http://www.w3.org/2000/svg"
            aria-hidden="true"
        >
            <path
                d="M2.5 6L5 8.5L9.5 4"
                stroke="currentColor"
                strokeWidth="2"
                strokeLinecap="round"
                strokeLinejoin="round"
            />
        </svg>
    );
}

export { Checkbox };
