export async function until<T>(
    what: string,
    probe: () => Promise<T | undefined>,
    timeoutMs: number,
    intervalMs = 1_000,
): Promise<T> {
    const deadline = Date.now() + timeoutMs;
    let lastError: unknown;
    while (Date.now() < deadline) {
        try {
            const value = await probe();
            if (value !== undefined && value !== false) {
                return value;
            }
        } catch (error) {
            lastError = error;
        }
        await new Promise((r) => setTimeout(r, intervalMs));
    }
    throw new Error(`Timed out after ${timeoutMs}ms waiting for ${what}${lastError ? `: ${String(lastError)}` : ''}`);
}
