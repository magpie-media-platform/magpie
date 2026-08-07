export function parseMappingRows(serialised: string): Record<string, string>[] {
    if (!serialised) {
        return [];
    }
    try {
        const parsed = JSON.parse(serialised);
        if (!Array.isArray(parsed)) {
            return [];
        }
        const rows: Record<string, string>[] = [];
        for (const entry of parsed) {
            if (entry === null || typeof entry !== 'object') {
                continue;
            }
            const row: Record<string, string> = {};
            for (const [key, value] of Object.entries(entry as Record<string, unknown>)) {
                if (value == null) {
                    row[key] = '';
                } else if (typeof value === 'string') {
                    row[key] = value;
                } else if (typeof value === 'number' || typeof value === 'boolean') {
                    row[key] = String(value);
                } else {
                    row[key] = JSON.stringify(value) ?? '';
                }
            }
            rows.push(row);
        }
        return rows;
    } catch {
        return [];
    }
}
