export function queryErrorOrNull(query: { error: unknown }): Error | null {
    return query.error instanceof Error ? query.error : null;
}
