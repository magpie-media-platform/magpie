import { forwardRef, useCallback, useEffect, useRef, useState, type ChangeEvent, type FocusEvent } from 'react';

import { Input, type InputProps } from '@/components/ui/Input';
import { useMergedRef } from '@/hooks/ui/useMergedRef';

import './SecretInput.css';

export interface SecretInputProps extends Omit<InputProps, 'type' | 'trailingAction' | 'trailingIcon'> {
    configured: boolean;

    fieldLabel: string;

    value: string;

    onChange: (event: ChangeEvent<HTMLInputElement>) => void;
}

export const SecretInput = forwardRef<HTMLInputElement, SecretInputProps>(function SecretInput(
    { configured, fieldLabel, value, onChange, disabled, placeholder, onBlur, ...rest },
    forwardedRef,
) {
    const [unlocked, setUnlocked] = useState(false);
    const innerRef = useRef<HTMLInputElement | null>(null);

    const assignRef = useMergedRef(innerRef, forwardedRef);

    const masked = configured && !unlocked;

    useEffect(() => {
        if (unlocked) {
            innerRef.current?.focus();
        }
    }, [unlocked]);

    const handleUnlock = useCallback(() => {
        setUnlocked(true);
    }, []);

    const handleBlur = useCallback(
        (event: FocusEvent<HTMLInputElement>) => {
            if (configured && value === '') {
                setUnlocked(false);
            }
            onBlur?.(event);
        },
        [configured, value, onBlur],
    );

    return (
        <Input
            {...rest}
            ref={assignRef}
            type="password"
            autoComplete="new-password"
            value={masked ? '' : value}
            onChange={onChange}
            onBlur={handleBlur}
            disabled={disabled}
            readOnly={masked}
            placeholder={masked ? '********' : placeholder}
            trailingAction={
                masked ? (
                    <button
                        type="button"
                        className="secret-input__replace"
                        onClick={handleUnlock}
                        disabled={disabled}
                        aria-label={`Replace ${fieldLabel}`}
                        data-action="replace-secret"
                    >
                        Replace
                    </button>
                ) : undefined
            }
        />
    );
});

SecretInput.displayName = 'SecretInput';

export default SecretInput;
