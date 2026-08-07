import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useBlocker } from 'react-router-dom';

const MESSAGE = 'You have unsaved changes. Leave this page and discard them?';

interface UnsavedChangesRegistry {
    register: (key: symbol) => void;
    unregister: (key: symbol) => void;
}

const UnsavedChangesGuardContext = createContext<UnsavedChangesRegistry>({
    register: () => {},
    unregister: () => {},
});

const UnsavedChangesStateContext = createContext<boolean>(false);

export function UnsavedChangesGuardProvider({ children }: { children: ReactNode }) {
    const keysRef = useRef<Set<symbol>>(new Set());
    const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);

    const registry = useMemo<UnsavedChangesRegistry>(
        () => ({
            register: (key) => {
                keysRef.current.add(key);
                setHasUnsavedChanges(keysRef.current.size > 0);
            },
            unregister: (key) => {
                keysRef.current.delete(key);
                setHasUnsavedChanges(keysRef.current.size > 0);
            },
        }),
        [],
    );

    return (
        <UnsavedChangesGuardContext.Provider value={registry}>
            <UnsavedChangesStateContext.Provider value={hasUnsavedChanges}>
                {children}
            </UnsavedChangesStateContext.Provider>
        </UnsavedChangesGuardContext.Provider>
    );
}

export function UnsavedChangesBlocker() {
    const hasUnsavedChanges = useContext(UnsavedChangesStateContext);
    const blocker = useBlocker(hasUnsavedChanges);

    useEffect(() => {
        if (blocker.state !== 'blocked') {
            return;
        }

        if (window.confirm(MESSAGE)) {
            blocker.proceed();
        } else {
            blocker.reset();
        }
    }, [blocker]);

    return null;
}

export function useUnsavedChangesGuard(active: boolean): void {
    const { register, unregister } = useContext(UnsavedChangesGuardContext);

    useEffect(() => {
        if (!active) {
            return;
        }

        const key = Symbol('unsaved-changes-guard');
        register(key);

        const handleBeforeUnload = (e: BeforeUnloadEvent) => {
            e.preventDefault();
        };

        window.addEventListener('beforeunload', handleBeforeUnload);

        return () => {
            window.removeEventListener('beforeunload', handleBeforeUnload);
            unregister(key);
        };
    }, [active, register, unregister]);
}
