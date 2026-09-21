import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import type { BadgeVariant, BadgeSize, BadgeStyle } from '@/types/badge';

import './Badge.css';

export type { BadgeVariant, BadgeSize, BadgeStyle };

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
    variant?: BadgeVariant;
    size?: BadgeSize;
    badgeStyle?: BadgeStyle;
    dot?: boolean;
    count?: number;
    children: ReactNode;
}

export const Badge = forwardRef<HTMLSpanElement, BadgeProps>(
    (
        { variant = 'default', size = 'md', badgeStyle = 'filled', dot = false, count, className, children, ...props },
        ref,
    ) => {
        const classes = [
            'badge',
            `badge--${variant}`,
            `badge--${size}`,
            badgeStyle === 'outline' ? 'badge--outline' : '',
            className,
        ]
            .filter(Boolean)
            .join(' ');

        return (
            <span
                ref={ref}
                className={classes}
                data-component="badge"
                data-variant={variant}
                data-size={size}
                data-style={badgeStyle}
                data-status={variant}
                {...props}
            >
                {dot && <span className="badge__dot" aria-hidden="true" />}
                {children}
                {count !== undefined && count !== 0 && <span className="badge__count">{count}</span>}
            </span>
        );
    },
);

Badge.displayName = 'Badge';

export default Badge;
