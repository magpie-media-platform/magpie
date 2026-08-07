import { useEffect, useState } from 'react';

export function useMediaQuery(query?: string): boolean {
    const [matches, setMatches] = useState(
        () =>
            query !== undefined &&
            typeof window !== 'undefined' &&
            typeof window.matchMedia === 'function' &&
            window.matchMedia(query).matches,
    );

    useEffect(() => {
        if (query === undefined || typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
            setMatches(false);
            return;
        }
        const mq = window.matchMedia(query);
        const handleChange = (e: MediaQueryListEvent) => setMatches(e.matches);
        setMatches(mq.matches);
        mq.addEventListener('change', handleChange);
        return () => mq.removeEventListener('change', handleChange);
    }, [query]);

    return matches;
}

export default useMediaQuery;
