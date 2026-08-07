import { parseUtcInstant } from './dateParsing';

function formatRelativeFallbackDate(date: Date): string {
    return date.toLocaleDateString('en-US', {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
    });
}

export function formatRelativeTime(dateStr?: string | null, fallback = 'Never'): string {
    if (!dateStr) {
        return fallback;
    }
    try {
        const date = parseUtcInstant(dateStr);
        if (isNaN(date.getTime())) {
            return 'Unknown';
        }
        const now = new Date();
        const diffMs = now.getTime() - date.getTime();
        if (diffMs < 0) {
            return formatRelativeFallbackDate(date);
        }
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
        return formatRelativeFallbackDate(date);
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

export function formatPlaybackTime(seconds: number): string {
    if (!Number.isFinite(seconds) || seconds < 0) {
        return '0:00';
    }

    const total = Math.floor(seconds);
    const hrs = Math.floor(total / 3600);
    const mins = Math.floor((total % 3600) / 60);
    const secs = total % 60;
    const pad = (n: number) => n.toString().padStart(2, '0');

    return hrs > 0 ? `${hrs}:${pad(mins)}:${pad(secs)}` : `${mins}:${pad(secs)}`;
}

export function formatDurationFromSeconds(seconds: number): string {
    const totalMinutes = Math.round(seconds / 60);
    return formatDurationFromMinutes(totalMinutes);
}

export function formatDurationFromMinutes(totalMinutes: number): string {
    if (totalMinutes < 60) {
        return `${totalMinutes}m`;
    }

    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;
    return minutes === 0 ? `${hours}h` : `${hours}h ${minutes}m`;
}

export function formatTimeSpanDuration(duration: string | null | undefined): string | undefined {
    if (!duration) {
        return undefined;
    }

    const match = duration.match(/^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})/);
    if (!match) {
        return undefined;
    }

    const days = match[1] ? parseInt(match[1], 10) : 0;
    const hours = parseInt(match[2], 10) + days * 24;
    const minutes = parseInt(match[3], 10);

    if (hours === 0 && minutes === 0) {
        return undefined;
    }
    if (hours === 0) {
        return `${minutes}m`;
    }

    return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
}

export function formatElapsedDuration(startedAt?: string | null, completedAt?: string | null): string | undefined {
    if (!startedAt || !completedAt) {
        return undefined;
    }

    const start = new Date(startedAt).getTime();
    const end = new Date(completedAt).getTime();
    const ms = end - start;

    if (ms < 1000) {
        return `${ms}ms`;
    }
    if (ms < 60000) {
        return `${Math.round(ms / 1000)}s`;
    }

    const minutes = Math.floor(ms / 60000);
    const seconds = Math.round((ms % 60000) / 1000);
    return seconds > 0 ? `${minutes}m ${seconds}s` : `${minutes}m`;
}

export function formatTimeOfDay(dateStr: string): string {
    const date = new Date(dateStr);
    return date.toLocaleTimeString(undefined, {
        hour: '2-digit',
        minute: '2-digit',
    });
}

export function formatClockTime(hour: number, minute: number): string {
    const hh = String(hour).padStart(2, '0');
    const mm = String(minute).padStart(2, '0');
    return `${hh}:${mm}`;
}
