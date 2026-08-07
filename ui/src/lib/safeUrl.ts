const safeProtocols = new Set(['http:', 'https:']);

export function safeHttpUrl(value: string | null | undefined): string | null {
    if (!value) {
        return null;
    }

    const trimmed = value.trim();
    if (!trimmed) {
        return null;
    }

    try {
        const parsed = new URL(trimmed);
        return safeProtocols.has(parsed.protocol) ? parsed.href : null;
    } catch {
        return null;
    }
}
