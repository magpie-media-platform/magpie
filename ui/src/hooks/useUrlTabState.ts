import { useCallback, useEffect, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';

interface UrlTabStateConfig<TTab extends string> {
    defaultTab: TTab;
    allowedTabs: readonly TTab[];
    paramName?: string;
}

interface SetUrlTabStateOptions<TTab extends string> {
    replace?: boolean;
    update?: (params: URLSearchParams, tab: TTab) => void;
}

export function useUrlTabState<TTab extends string>({
    defaultTab,
    allowedTabs,
    paramName = 'tab',
}: UrlTabStateConfig<TTab>): [TTab, (tab: string, options?: SetUrlTabStateOptions<TTab>) => void] {
    const [searchParams, setSearchParams] = useSearchParams();

    const allowed = useMemo(() => new Set<string>(allowedTabs), [allowedTabs]);
    const requestedTab = searchParams.get(paramName);
    const activeTab = requestedTab && allowed.has(requestedTab) ? (requestedTab as TTab) : defaultTab;

    useEffect(() => {
        if (requestedTab === null || (requestedTab === activeTab && activeTab !== defaultTab)) {
            return;
        }

        setSearchParams(
            (prev) => {
                const next = new URLSearchParams(prev);
                if (activeTab === defaultTab) {
                    next.delete(paramName);
                } else {
                    next.set(paramName, activeTab);
                }
                return next;
            },
            { replace: true },
        );
    }, [activeTab, defaultTab, paramName, requestedTab, setSearchParams]);

    const setActiveTab = useCallback(
        (tab: string, options?: SetUrlTabStateOptions<TTab>) => {
            const nextTab = allowed.has(tab) ? (tab as TTab) : defaultTab;
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    options?.update?.(next, nextTab);
                    if (nextTab === defaultTab) {
                        next.delete(paramName);
                    } else {
                        next.set(paramName, nextTab);
                    }
                    return next;
                },
                { replace: options?.replace ?? false },
            );
        },
        [allowed, defaultTab, paramName, setSearchParams],
    );

    return [activeTab, setActiveTab];
}
