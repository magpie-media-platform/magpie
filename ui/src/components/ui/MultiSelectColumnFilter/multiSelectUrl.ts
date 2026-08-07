const SEPARATOR = ',';

export function encodeMultiValue(values: string[]): string {
    return values.map(encodeURIComponent).join(SEPARATOR);
}

export function decodeMultiValue(raw: string | undefined | null): string[] {
    if (!raw) {
        return [];
    }
    return raw
        .split(SEPARATOR)
        .filter((part) => part.length > 0)
        .map(decodeURIComponent);
}
