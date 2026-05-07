import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import * as SeparatorPrimitive from '@radix-ui/react-separator';

import './Separator.css';

export type SeparatorOrientation = 'horizontal' | 'vertical';

export interface SeparatorProps extends ComponentPropsWithoutRef<typeof SeparatorPrimitive.Root> {
    orientation?: SeparatorOrientation;
    decorative?: boolean;
}

export const Separator = forwardRef<React.ElementRef<typeof SeparatorPrimitive.Root>, SeparatorProps>(
    ({ orientation = 'horizontal', decorative = true, className, ...props }, ref) => {
        const classes = ['separator', `separator--${orientation}`, className].filter(Boolean).join(' ');

        return (
            <SeparatorPrimitive.Root
                ref={ref}
                decorative={decorative}
                orientation={orientation}
                className={classes}
                data-component="separator"
                data-orientation={orientation}
                {...props}
            />
        );
    },
);

Separator.displayName = 'Separator';

export default Separator;
