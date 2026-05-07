import { useCallback, useState } from 'react';

export type UseToggleReturn = [boolean, () => void, () => void, () => void];

export function useToggle(initialValue = false): UseToggleReturn {
    const [value, setValue] = useState(initialValue);
    const toggle = useCallback(() => setValue((v) => !v), []);
    const on = useCallback(() => setValue(true), []);
    const off = useCallback(() => setValue(false), []);
    return [value, toggle, on, off];
}
