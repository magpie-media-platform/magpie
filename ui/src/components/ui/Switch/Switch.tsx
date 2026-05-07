import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import * as SwitchPrimitive from '@radix-ui/react-switch';

import './Switch.css';

export type SwitchSize = 'sm' | 'md' | 'lg';

export interface SwitchProps extends ComponentPropsWithoutRef<typeof SwitchPrimitive.Root> {
    size?: SwitchSize;

    error?: boolean;

    'aria-describedby'?: string;
}

const Switch = forwardRef<HTMLButtonElement, SwitchProps>(
    ({ size = 'md', error = false, className, 'aria-describedby': ariaDescribedBy, ...props }, ref) => {
        const classes = ['switch', `switch--${size}`, error && 'switch--error', className].filter(Boolean).join(' ');

        return (
            <SwitchPrimitive.Root
                ref={ref}
                className={classes}
                data-component="switch"
                data-size={size}
                data-error={error || undefined}
                aria-invalid={error || undefined}
                aria-describedby={ariaDescribedBy}
                {...props}
            >
                <SwitchPrimitive.Thumb className="switch__thumb" />
            </SwitchPrimitive.Root>
        );
    },
);

Switch.displayName = 'Switch';

export { Switch };
