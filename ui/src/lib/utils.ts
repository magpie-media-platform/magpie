export function cn(...classes: (string | undefined | null | false)[]): string {
    return classes.filter(Boolean).join(' ');
}

export function queryErrorOrNull(query: { error: unknown }): Error | null {
    return query.error instanceof Error ? query.error : null;
}
