import { useCallback, useEffect, useRef } from 'react';
import { CheckCircleIcon, InfoIcon, WarningIcon, XCircleIcon } from '@phosphor-icons/react';

import { Toast, ToastClose, ToastTitle, ToastDescription, type ToastVariant } from './Toast';
import { useToast, type ToastData } from '@/hooks/ui/useToast';

function getToastRole(variant?: ToastVariant): 'alert' | 'status' {
    return variant === 'error' || variant === 'warning' ? 'alert' : 'status';
}

function ToastItem({ data, dismiss }: { data: ToastData; dismiss: (id: string) => void }) {
    const { id, duration } = data;
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

    useEffect(() => {
        armTimer(remainingRef.current);
        return clearTimer;
    }, [armTimer, clearTimer]);

    const handlePause = useCallback(() => {
        if (timerRef.current === null) {
            return;
        }
        remainingRef.current = Math.max(0, remainingRef.current - (Date.now() - startedAtRef.current));
        clearTimer();
    }, [clearTimer]);

    const handleResume = useCallback(() => {
        armTimer(remainingRef.current);
    }, [armTimer]);

    return (
        <Toast
            open
            role={getToastRole(data.variant)}
            variant={data.variant}
            duration={Infinity}
            onPause={handlePause}
            onResume={handleResume}
            onOpenChange={(open) => !open && dismiss(id)}
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
            <ToastClose />
        </Toast>
    );
}

export function ToastList() {
    const { toasts, dismiss } = useToast();

    return (
        <>
            {toasts.map((t) => (
                <ToastItem key={t.id} data={t} dismiss={dismiss} />
            ))}
        </>
    );
}
