import { CheckCircle, Info, Warning, XCircle } from '@phosphor-icons/react';

import { Toast, ToastClose, ToastTitle, ToastDescription, type ToastVariant } from './Toast';
import { useToast } from './useToast';

function getToastRole(variant?: ToastVariant): 'alert' | 'status' {
    return variant === 'error' || variant === 'warning' ? 'alert' : 'status';
}

export function ToastList() {
    const { toasts, dismiss } = useToast();

    return (
        <>
            {toasts.map((t) => (
                <Toast
                    key={t.id}
                    open
                    role={getToastRole(t.variant)}
                    variant={t.variant}
                    duration={t.duration}
                    onOpenChange={(open) => !open && dismiss(t.id)}
                >
                    <div className="toast-list__content">
                        {t.variant === 'success' && (
                            <CheckCircle
                                weight="fill"
                                className="toast-list__icon toast-list__icon--success"
                                aria-hidden="true"
                            />
                        )}
                        {t.variant === 'error' && (
                            <XCircle
                                weight="fill"
                                className="toast-list__icon toast-list__icon--error"
                                aria-hidden="true"
                            />
                        )}
                        {t.variant === 'warning' && (
                            <Warning
                                weight="fill"
                                className="toast-list__icon toast-list__icon--warning"
                                aria-hidden="true"
                            />
                        )}
                        {t.variant === 'info' && (
                            <Info
                                weight="fill"
                                className="toast-list__icon toast-list__icon--info"
                                aria-hidden="true"
                            />
                        )}
                        <div>
                            {t.title && <ToastTitle>{t.title}</ToastTitle>}
                            {t.description && <ToastDescription>{t.description}</ToastDescription>}
                        </div>
                    </div>
                    <ToastClose />
                </Toast>
            ))}
        </>
    );
}
