import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import * as LabelPrimitive from '@radix-ui/react-label';

import './Label.css';

export type LabelSize = 'sm' | 'md' | 'lg';

export interface LabelProps extends ComponentPropsWithoutRef<typeof LabelPrimitive.Root> {
    size?: LabelSize;

    error?: boolean;

    required?: boolean;
}

const Label = forwardRef<HTMLLabelElement, LabelProps>(
    ({ size = 'md', error = false, required = false, className, children, ...props }, ref) => {
        const classes = ['label', `label--${size}`, error && 'label--error', className].filter(Boolean).join(' ');

        return (
            <LabelPrimitive.Root
                ref={ref}
                className={classes}
                data-component="label"
                data-size={size}
                data-error={error || undefined}
                {...props}
            >
                {children}
                {required && (
                    <span className="label__required" aria-hidden="true">
                        *
                    </span>
                )}
            </LabelPrimitive.Root>
        );
    },
);

Label.displayName = 'Label';

export { Label };
export default Label;
