import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

interface RestartRequiredContextValue {
    restartRequired: boolean;
    setRestartRequired: (required: boolean) => void;
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
