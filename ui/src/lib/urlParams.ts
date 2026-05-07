export function mapStatusParam(param: string | null): string {
    if (!param) {
        return '';
    }
    const map: Record<string, string> = {
        unknown: '0',
        healthy: '1',
        unhealthy: '2',
        stale: '3',
    };
    return map[param.toLowerCase()] ?? '';
}
