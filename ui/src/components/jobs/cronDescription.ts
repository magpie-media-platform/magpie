import { formatClockTime } from '@/lib/formatTime';

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

const PRESET_LABELS: Record<string, string> = {
    '0 * * * *': 'Every hour',
    '0 */4 * * *': 'Every 4 hours',
    '0 */6 * * *': 'Every 6 hours',
    '0 */12 * * *': 'Every 12 hours',
    '0 0 * * *': 'Daily at midnight',
    '0 6 * * *': 'Daily at 6 AM',
    '0 2 * * *': 'Daily at 2 AM',
    '0 0 * * 0': 'Weekly on Sunday',
};

function isWildcard(field: string): boolean {
    return field === '*';
}

function parseStep(field: string): number | null {
    const match = /^\*\/(\d+)$/.exec(field);
    if (!match) {
        return null;
    }
    const step = Number(match[1]);
    return Number.isInteger(step) && step > 0 ? step : null;
}

function parseFixed(field: string, min: number, max: number): number | null {
    if (!/^\d+$/.test(field)) {
        return null;
    }
    const value = Number(field);
    return value >= min && value <= max ? value : null;
}

function pluralize(count: number, unit: string): string {
    return count === 1 ? `every ${unit}` : `every ${count} ${unit}s`;
}

function describeDayOfWeek(field: string): string | null {
    const day = parseFixed(field, 0, 7);
    if (day === null) {
        return null;
    }
    return DAY_NAMES[day === 7 ? 0 : day];
}

export function describeCron(cronExpression: string): string {
    const expression = cronExpression.trim();

    const preset = PRESET_LABELS[expression];
    if (preset !== undefined) {
        return preset;
    }

    const parts = expression.split(/\s+/);
    if (parts.length !== 5) {
        return expression;
    }

    const [minute, hour, dayOfMonth, month, dayOfWeek] = parts;

    if (!isWildcard(month) || !isWildcard(dayOfMonth)) {
        return expression;
    }

    const weekday = isWildcard(dayOfWeek) ? null : describeDayOfWeek(dayOfWeek);
    if (!isWildcard(dayOfWeek) && weekday === null) {
        return expression;
    }

    const minuteStep = parseStep(minute);
    const hourStep = parseStep(hour);
    const fixedMinute = parseFixed(minute, 0, 59);
    const fixedHour = parseFixed(hour, 0, 23);

    if (isWildcard(minute) && isWildcard(hour) && weekday === null) {
        return 'Every minute';
    }

    if (minuteStep !== null && isWildcard(hour) && weekday === null) {
        return capitalize(pluralize(minuteStep, 'minute'));
    }

    if (fixedMinute !== null && isWildcard(hour) && weekday === null) {
        return fixedMinute === 0 ? 'Every hour' : `Hourly at :${String(fixedMinute).padStart(2, '0')}`;
    }

    if (fixedMinute !== null && hourStep !== null && weekday === null) {
        const base = pluralize(hourStep, 'hour');
        return capitalize(fixedMinute === 0 ? base : `${base} at :${String(fixedMinute).padStart(2, '0')}`);
    }

    if (fixedMinute !== null && fixedHour !== null) {
        const time = formatClockTime(fixedHour, fixedMinute);
        return weekday === null ? `Daily at ${time}` : `Weekly on ${weekday} at ${time}`;
    }

    return expression;
}

export function describeCronUtc(cronExpression: string): string {
    return `${describeCron(cronExpression)} (UTC)`;
}

function capitalize(text: string): string {
    return text.length === 0 ? text : text.charAt(0).toUpperCase() + text.slice(1);
}
