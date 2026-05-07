export type LogLevel = 'debug' | 'info' | 'warn' | 'error';

export interface LogEntry {
    id: string;
    timestamp: string;
    level: LogLevel;
    source: string;
    message: string;
    exception?: string;
}
