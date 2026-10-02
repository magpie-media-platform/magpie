import { useCallback, useEffect, useRef } from 'react';
import { CheckCircleIcon, InfoIcon, WarningIcon, XCircleIcon } from '@phosphor-icons/react';

import { Toast, ToastAction, ToastClose, ToastTitle, ToastDescription, type ToastVariant } from './Toast';
import { useToast, type ToastData } from '@/hooks/ui/useToast';
import { useMediaQuery } from '@/hooks/ui/useMediaQuery';

function getToastRole(variant?: ToastVariant): 'alert' | 'status' {
    return variant === 'error' || variant === 'warning' ? 'alert' : 'status';
}

/** How many of the newest toasts stay visible in the mobile phone-notification stack. */
const MAX_MOBILE_STACK_DEPTH = 3;

/** Matches the breakpoint where the bottom navigation shows and `Toast.css` stacks the toasts behind the front toast. */
const MOBILE_STACK_QUERY = '(max-width: 767px)';

interface ToastItemProps {
    data: ToastData;
    dismiss: (id: string) => void;
    stackDepth: number;
}

function ToastItem({ data, dismiss, stackDepth }: ToastItemProps) {
    const { id, duration } = data;
    const isMobileStack = useMediaQuery(MOBILE_STACK_QUERY);
    const isConcealed = stackDepth > 0 && isMobileStack;
    const remainingRef = useRef(duration ?? 0);
    const startedAtRef = useRef(0);
    const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

    const clearTimer = useCallback(() => {
        if (timerRef.current !== null) {
            clearTimeout(timerRef.current);
            timerRef.current = null;
        }
    }, []);

    const armTimer = useCallback(
        (ms: number) => {
            clearTimer();
            if (!ms || ms === Infinity) {
                return;
            }
            startedAtRef.current = Date.now();
            timerRef.current = setTimeout(() => dismiss(id), ms);
        },
        [clearTimer, dismiss, id],
    );

    const handlePause = useCallback(() => {
        if (timerRef.current === null) {
            return;
        }
        remainingRef.current = Math.max(0, remainingRef.current - (Date.now() - startedAtRef.current));
        clearTimer();
    }, [clearTimer]);

    const handleResume = useCallback(() => {
        if (!isConcealed) {
            armTimer(remainingRef.current);
        }
    }, [armTimer, isConcealed]);

    useEffect(() => {
        if (isConcealed) {
            handlePause();
        } else {
            handleResume();
        }
        return handlePause;
    }, [isConcealed, handlePause, handleResume]);

    return (
        <Toast
            open
            role={getToastRole(data.variant)}
            variant={data.variant}
            duration={Infinity}
            onPause={handlePause}
            onResume={handleResume}
            onOpenChange={(open) => !open && dismiss(id)}
            inert={isConcealed}
            data-stack-depth={stackDepth}
            data-stack-hidden={stackDepth >= MAX_MOBILE_STACK_DEPTH}
        >
            <div className="toast-list__content">
                {data.variant === 'success' && (
                    <CheckCircleIcon
                        weight="fill"
                        className="toast-list__icon toast-list__icon--success"
                        aria-hidden="true"
                    />
                )}
                {data.variant === 'error' && (
                    <XCircleIcon
                        weight="fill"
                        className="toast-list__icon toast-list__icon--error"
                        aria-hidden="true"
                    />
                )}
                {data.variant === 'warning' && (
                    <WarningIcon
                        weight="fill"
                        className="toast-list__icon toast-list__icon--warning"
                        aria-hidden="true"
                    />
                )}
                {data.variant === 'info' && (
                    <InfoIcon weight="fill" className="toast-list__icon toast-list__icon--info" aria-hidden="true" />
                )}
                <div>
                    {data.title && <ToastTitle>{data.title}</ToastTitle>}
                    {data.description && <ToastDescription>{data.description}</ToastDescription>}
                </div>
            </div>
            {data.action && (
                <ToastAction altText={data.action.altText} onClick={data.action.onClick}>
                    {data.action.label}
                </ToastAction>
            )}
            <ToastClose />
        </Toast>
    );
}

export function ToastList() {
    const { toasts, dismiss } = useToast();

    return (
        <>
            {toasts.map((t, index) => (
                <ToastItem key={t.id} data={t} dismiss={dismiss} stackDepth={toasts.length - 1 - index} />
            ))}
        </>
    );
}
