import { forwardRef, useId, type InputHTMLAttributes, type ReactNode } from 'react';

import './Input.css';

export type InputSize = 'sm' | 'md' | 'lg';

export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> {
    size?: InputSize;

    error?: boolean;

    errorMessage?: string;

    leadingIcon?: ReactNode;

    trailingIcon?: ReactNode;

    trailingAction?: ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
    (
        {
            size = 'md',
            error = false,
            errorMessage,
            leadingIcon,
            trailingIcon,
            trailingAction,
            disabled,
            className,
            id,
            'aria-describedby': ariaDescribedBy,
            ...props
        },
        ref,
    ) => {
        const generatedId = useId();
        const inputId = id ?? generatedId;
        const errorId = errorMessage ? `${inputId}-error` : undefined;
        const describedBy = [ariaDescribedBy, errorId].filter(Boolean).join(' ') || undefined;

        const hasTrailing = !!trailingIcon || !!trailingAction;

        const wrapperClasses = [
            'input-wrapper',
            `input-wrapper--${size}`,
            error && 'input-wrapper--error',
            disabled && 'input-wrapper--disabled',
            leadingIcon && 'input-wrapper--has-leading',
            hasTrailing && 'input-wrapper--has-trailing',
            className,
        ]
            .filter(Boolean)
            .join(' ');

        return (
            <div className="input-container" data-component="input">
                <div
                    className={wrapperClasses}
                    data-size={size}
                    data-error={error || undefined}
                    data-disabled={disabled || undefined}
                    data-has-leading={!!leadingIcon || undefined}
                    data-has-trailing={hasTrailing || undefined}
                >
                    {leadingIcon && (
                        <span className="input__icon input__icon--leading" aria-hidden="true">
                            {leadingIcon}
                        </span>
                    )}
                    <input
                        ref={ref}
                        id={inputId}
                        className="input"
                        disabled={disabled}
                        aria-invalid={error}
                        aria-describedby={describedBy}
                        {...props}
                    />
                    {trailingIcon && (
                        <span className="input__icon input__icon--trailing" aria-hidden="true">
                            {trailingIcon}
                        </span>
                    )}
                    {trailingAction && <span className="input__action input__action--trailing">{trailingAction}</span>}
                </div>
                {errorMessage && (
                    <span id={errorId} className="input__error" role="alert">
                        {errorMessage}
                    </span>
                )}
            </div>
        );
    },
);

Input.displayName = 'Input';

export default Input;
