import {
    cloneElement,
    forwardRef,
    isValidElement,
    type ButtonHTMLAttributes,
    type ReactElement,
    type ReactNode,
} from 'react';

import './Button.css';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'destructive' | 'default' | 'outline';
export type ButtonSize = 'sm' | 'md' | 'lg';

interface ButtonBaseProps {
    variant?: ButtonVariant;
    size?: ButtonSize;
    loading?: boolean;
}

export interface ButtonProps extends ButtonBaseProps, ButtonHTMLAttributes<HTMLButtonElement> {
    asChild?: false;
    children: ReactNode;
}

export interface ButtonAsChildProps extends ButtonBaseProps {
    asChild: true;
    children: ReactElement;
    className?: string;
}

function buildClasses(variant: ButtonVariant, size: ButtonSize, loading: boolean, className?: string): string {
    return ['button', `button--${variant}`, `button--${size}`, loading && 'button--loading', className]
        .filter(Boolean)
        .join(' ');
}

export const Button = forwardRef<HTMLElement, ButtonProps | ButtonAsChildProps>((props, ref) => {
    const { variant = 'secondary', size = 'md', loading = false, className, children, ...rest } = props;

    const classes = buildClasses(variant, size, loading, className);

    if (props.asChild) {
        if (!isValidElement(children)) {
            return null;
        }

        return cloneElement(children as ReactElement<Record<string, unknown>>, {
            className: [classes, (children.props as Record<string, unknown>).className].filter(Boolean).join(' '),
            'data-component': 'button',
            'data-variant': variant,
            'data-size': size,
            'data-loading': loading || undefined,
            ref,
        });
    }

    const { disabled, type = 'button', ...buttonProps } = rest as ButtonHTMLAttributes<HTMLButtonElement>;

    return (
        <button
            ref={ref as React.Ref<HTMLButtonElement>}
            type={type}
            className={classes}
            disabled={disabled || loading}
            aria-disabled={disabled || loading}
            aria-busy={loading}
            data-component="button"
            data-variant={variant}
            data-size={size}
            data-loading={loading || undefined}
            {...buttonProps}
        >
            {loading && <span className="button__spinner" aria-hidden="true" />}
            <span className={`button__content${loading ? ' button__content--hidden' : ''}`}>{children}</span>
        </button>
    );
});

Button.displayName = 'Button';

export default Button;
