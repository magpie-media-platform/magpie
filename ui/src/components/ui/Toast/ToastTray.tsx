import { useToast } from '@/hooks/ui/useToast';

export function ToastTray() {
    const { toasts, dismissAll } = useToast();

    if (toasts.length < 2) {
        return null;
    }

    return (
        <li className="toast-tray">
            <button
                type="button"
                className="toast-tray__clear"
                aria-label="Clear all notifications"
                onClick={dismissAll}
            >
                Clear all
            </button>
        </li>
    );
}
