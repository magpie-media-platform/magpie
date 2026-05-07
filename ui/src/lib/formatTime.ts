import { formatShortDate } from './dateFormatting';

export function formatRelativeTime(dateStr?: string | null, fallback = 'Never'): string {
    if (!dateStr) {
        return fallback;
    }
    try {
        const date = new Date(dateStr);
        if (isNaN(date.getTime())) {
            return 'Unknown';
        }
        const now = new Date();
        const diffMs = now.getTime() - date.getTime();
        const diffMinutes = Math.floor(diffMs / (1000 * 60));
        if (diffMinutes < 1) {
            return 'Just now';
        }
        if (diffMinutes < 60) {
            return `${diffMinutes}m ago`;
        }
        const diffHours = Math.floor(diffMinutes / 60);
        if (diffHours < 24) {
            return `${diffHours}h ago`;
        }
        const diffDays = Math.floor(diffHours / 24);
        if (diffDays <= 30) {
            return `${diffDays}d ago`;
        }
        return formatShortDate(date.toISOString());
    } catch {
        return 'Unknown';
    }
}

export function isRelativeTimeString(value: string): boolean {
    return value.endsWith(' ago') || value === 'Just now';
}

export function formatUptime(startedAt?: string | null): string {
    if (!startedAt) {
        return '-';
    }
    try {
        const start = new Date(startedAt);
        if (isNaN(start.getTime())) {
            return '-';
        }
        const now = new Date();
        const diffMs = now.getTime() - start.getTime();
        if (diffMs < 0) {
            return '-';
        }
        const totalMinutes = Math.floor(diffMs / (1000 * 60));
        if (totalMinutes < 1) {
            return 'Just started';
        }
        const days = Math.floor(totalMinutes / (60 * 24));
        const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
        const minutes = totalMinutes % 60;
        const parts: string[] = [];
        if (days > 0) {
            parts.push(`${days}d`);
        }
        if (hours > 0) {
            parts.push(`${hours}h`);
        }
        if (minutes > 0) {
            parts.push(`${minutes}m`);
        }
        return parts.join(' ') || 'Just started';
    } catch {
        return '-';
    }
}
