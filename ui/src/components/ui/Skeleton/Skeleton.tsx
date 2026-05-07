import { forwardRef, type HTMLAttributes } from 'react';

import './Skeleton.css';

export interface SkeletonProps extends HTMLAttributes<HTMLDivElement> {
    variant?: 'text' | 'circle' | 'rect';

    width?: string | number;

    height?: string | number;
}

export const Skeleton = forwardRef<HTMLDivElement, SkeletonProps>(
    ({ variant = 'text', width, height, className, style, ...props }, ref) => {
        const classes = ['skeleton', `skeleton--${variant}`, className].filter(Boolean).join(' ');

        const inlineStyle: React.CSSProperties = {
            ...style,
            ...(width !== undefined && { width: typeof width === 'number' ? `${width}px` : width }),
            ...(height !== undefined && { height: typeof height === 'number' ? `${height}px` : height }),
        };

        return (
            <div
                ref={ref}
                className={classes}
                style={Object.keys(inlineStyle).length > 0 ? inlineStyle : undefined}
                role="status"
                aria-label="Loading"
                data-component="skeleton"
                data-variant={variant}
                {...props}
            />
        );
    },
);

Skeleton.displayName = 'Skeleton';

export default Skeleton;
