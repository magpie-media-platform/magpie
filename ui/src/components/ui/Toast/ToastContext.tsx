/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

import { LiveAnnouncerContext } from '../LiveAnnouncer/LiveAnnouncerContext';
import type { ToastVariant } from './Toast';

export interface ToastData {
    id: string;
    title?: string;
    description?: string;
    variant?: ToastVariant;
    action?: {
        label: string;
        altText: string;
        onClick: () => void;
    };
    duration?: number;
}

export interface ToastOptions {
    title?: string;
    description?: string;
    variant?: ToastVariant;
    action?: {
        label: string;
        altText: string;
        onClick: () => void;
    };
    duration?: number;
}

export interface ToastContextValue {
    toasts: ToastData[];
    toast: (options: ToastOptions) => string;
    dismiss: (id: string) => void;
    dismissAll: () => void;
}

const DEFAULT_DURATION = 5000;

const ERROR_DURATION = 10000;

let toastCount = 0;

function generateId(): string {
    toastCount += 1;
    return `toast-${toastCount}`;
}

function getDefaultDuration(variant?: ToastVariant, duration?: number): number {
    if (duration !== undefined) {
        return duration;
    }

    return variant === 'error' ? ERROR_DURATION : DEFAULT_DURATION;
}

function getPoliteness(variant?: ToastVariant): 'polite' | 'assertive' {
    return variant === 'error' ? 'assertive' : 'polite';
}

function buildAnnouncementMessage(options: ToastOptions): string {
    const parts: string[] = [];

    if (options.title) {
        parts.push(options.title);
    }

    if (options.description) {
        parts.push(options.description);
    }

    return parts.join('. ');
}

export const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastStateProvider({ children }: { children: ReactNode }) {
    const [toasts, setToasts] = useState<ToastData[]>([]);

    const announcerContext = useContext(LiveAnnouncerContext);

    const toast = useCallback(
        (options: ToastOptions): string => {
            const id = generateId();
            const newToast: ToastData = {
                id,
                ...options,
                duration: getDefaultDuration(options.variant, options.duration),
            };

            setToasts((prev) => [...prev, newToast]);

            if (announcerContext) {
                const message = buildAnnouncementMessage(options);
                const politeness = getPoliteness(options.variant);
                announcerContext.announce(message, politeness);
            }

            return id;
        },
        [announcerContext],
    );

    const dismiss = useCallback((id: string) => {
        setToasts((prev) => prev.filter((t) => t.id !== id));
    }, []);

    const dismissAll = useCallback(() => {
        setToasts([]);
    }, []);

    const value = useMemo(
        () => ({
            toasts,
            toast,
            dismiss,
            dismissAll,
        }),
        [toasts, toast, dismiss, dismissAll],
    );

    return <ToastContext.Provider value={value}>{children}</ToastContext.Provider>;
}
