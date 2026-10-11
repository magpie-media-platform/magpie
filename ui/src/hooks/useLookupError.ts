import { useState } from 'react';

interface LookupQueryState {
    data: unknown;
    error: Error | null;
    errorUpdateCount?: number;
}

/** Returns the error of a lookup query that has no data, and keeps that error while a retry of the query runs. */
export function useLookupError({ data, error, errorUpdateCount = 0 }: LookupQueryState): Error | null {
    const [lastError, setLastError] = useState<Error | null>(null);
    if (error && error !== lastError) {
        setLastError(error);
    }
    if (data !== undefined) {
        return null;
    }
    return error ?? (errorUpdateCount > 0 ? lastError : null);
}
