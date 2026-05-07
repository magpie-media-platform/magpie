export function formatShortDate(dateStr: string | null): string {
    if (!dateStr) {
        return 'Never';
    }
    try {
        const date = new Date(dateStr);
        if (isNaN(date.getTime())) {
            return 'Unknown';
        }
        return date.toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            year: 'numeric',
        });
    } catch {
        return 'Unknown';
    }
}

export function formatDateTime(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        if (isNaN(date.getTime())) {
            return dateStr;
        }
        return date.toLocaleDateString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
        });
    } catch {
        return dateStr;
    }
}
