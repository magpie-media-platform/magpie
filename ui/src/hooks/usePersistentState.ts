import { useCallback, useState } from 'react';

export function usePersistentState<T>(key: string, defaultValue: T): [T, (value: T) => void] {
    const [value, setValue] = useState<T>(() => {
        try {
            const stored = localStorage.getItem(key);
            return stored === null ? defaultValue : (JSON.parse(stored) as T);
        } catch {
            return defaultValue;
        }
    });

    const setPersistentValue = useCallback(
        (next: T) => {
            setValue(next);
            try {
                localStorage.setItem(key, JSON.stringify(next));
            } catch {
                void 0;
            }
        },
        [key],
    );

    return [value, setPersistentValue];
}
