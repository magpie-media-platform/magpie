import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';

import './Card.css';

export interface CardProps extends HTMLAttributes<HTMLDivElement> {
    children: ReactNode;
    interactive?: boolean;
}

export interface CardHeaderProps extends HTMLAttributes<HTMLDivElement> {
    children: ReactNode;
}

export interface CardBodyProps extends HTMLAttributes<HTMLDivElement> {
    children: ReactNode;
}

export interface CardFooterProps extends HTMLAttributes<HTMLDivElement> {
    children: ReactNode;
}

export const Card = forwardRef<HTMLDivElement, CardProps>(
    ({ interactive = false, className, children, ...props }, ref) => {
        const classes = ['card', interactive && 'card--interactive', className].filter(Boolean).join(' ');

        return (
            <div
                ref={ref}
                className={classes}
                data-component="card"
                data-interactive={interactive || undefined}
                {...props}
            >
                {children}
            </div>
        );
    },
);

Card.displayName = 'Card';

export const CardHeader = forwardRef<HTMLDivElement, CardHeaderProps>(({ className, children, ...props }, ref) => {
    const classes = ['card-header', className].filter(Boolean).join(' ');

    return (
        <div ref={ref} className={classes} data-component="card-header" {...props}>
            {children}
        </div>
    );
});

CardHeader.displayName = 'CardHeader';

export const CardBody = forwardRef<HTMLDivElement, CardBodyProps>(({ className, children, ...props }, ref) => {
    const classes = ['card-body', className].filter(Boolean).join(' ');

    return (
        <div ref={ref} className={classes} data-component="card-body" {...props}>
            {children}
        </div>
    );
});

CardBody.displayName = 'CardBody';

export const CardFooter = forwardRef<HTMLDivElement, CardFooterProps>(({ className, children, ...props }, ref) => {
    const classes = ['card-footer', className].filter(Boolean).join(' ');

    return (
        <div ref={ref} className={classes} data-component="card-footer" {...props}>
            {children}
        </div>
    );
});

CardFooter.displayName = 'CardFooter';

export default Card;
