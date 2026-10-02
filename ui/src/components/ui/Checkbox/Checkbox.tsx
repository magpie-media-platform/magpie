import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import * as CheckboxPrimitive from '@radix-ui/react-checkbox';
import { CheckIcon } from '@phosphor-icons/react';

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
                        <CheckIcon size={12} weight="bold" aria-hidden="true" />
                    </CheckboxPrimitive.Indicator>
                </span>
            </CheckboxPrimitive.Root>
        );
    },
);

Checkbox.displayName = 'Checkbox';

export { Checkbox };
