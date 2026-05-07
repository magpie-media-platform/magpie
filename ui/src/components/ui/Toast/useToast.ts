import { useContext } from 'react';

import { ToastContext, type ToastData, type ToastOptions } from './ToastContext';

export type { ToastData, ToastOptions };

export interface UseToastReturn {
    toasts: ToastData[];
    toast: (options: ToastOptions) => string;
    dismiss: (id: string) => void;
    dismissAll: () => void;
}

export function useToast(): UseToastReturn {
    const context = useContext(ToastContext);

    if (!context) {
        throw new Error('useToast must be used within a ToastStateProvider');
    }

    return context;
}

export default useToast;
