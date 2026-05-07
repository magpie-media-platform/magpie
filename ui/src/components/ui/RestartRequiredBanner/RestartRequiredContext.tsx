/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

interface RestartRequiredContextValue {
    /** True when the host needs a restart for already-applied extension changes to take effect. */
    restartRequired: boolean;
    /** Mark the host as needing a restart. Persists in-memory until the page reloads. */
    setRestartRequired: (required: boolean) => void;
    /** Hide the banner without clearing the underlying flag. */
    dismiss: () => void;
    dismissed: boolean;
}

const RestartRequiredContext = createContext<RestartRequiredContextValue | undefined>(undefined);

export function RestartRequiredProvider({ children }: { children: ReactNode }) {
    const [restartRequired, setRestartRequiredState] = useState(false);
    const [dismissed, setDismissed] = useState(false);

    const setRestartRequired = useCallback((required: boolean) => {
        setRestartRequiredState(required);
        if (required) {
            // Fresh signal overrides a prior dismissal so subsequent events are seen.
            setDismissed(false);
        }
    }, []);

    const dismiss = useCallback(() => {
        setDismissed(true);
    }, []);

    const value = useMemo(
        () => ({ restartRequired, setRestartRequired, dismiss, dismissed }),
        [restartRequired, setRestartRequired, dismiss, dismissed],
    );

    return <RestartRequiredContext.Provider value={value}>{children}</RestartRequiredContext.Provider>;
}

export function useRestartRequired(): RestartRequiredContextValue {
    const ctx = useContext(RestartRequiredContext);
    if (!ctx) {
        throw new Error('useRestartRequired must be used within a RestartRequiredProvider');
    }
    return ctx;
}
