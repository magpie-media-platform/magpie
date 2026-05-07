import { useState, useMemo, useCallback, useRef, useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
    Scroll,
    MagnifyingGlass,
    Bug,
    Info,
    Warning,
    XCircle,
    ArrowDown,
    ArrowsClockwise,
    X,
    Copy,
    Check,
    Play,
    Pause,
} from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Checkbox } from '@/components/ui/Checkbox';
import { Label } from '@/components/ui/Label';
import { Card } from '@/components/ui/Card';
import { Skeleton } from '@/components/ui/Skeleton';
import { SystemLogsSlot } from '@/components/ui/Slot/SystemSlots';
import { useLogs } from '@/hooks/useLogs';

import type { LogLevel, LogEntry } from '@/types/logs';

import './LogViewer.css';

export type { LogLevel, LogEntry };

export interface LogFilterState {
    levels: Set<LogLevel>;
    source: string;
    search: string;
}

const levelVariantMap: Record<LogLevel, 'unknown' | 'info' | 'warning' | 'unhealthy'> = {
    debug: 'unknown',
    info: 'info',
    warn: 'warning',
    error: 'unhealthy',
};

function getLevelIcon(level: LogLevel) {
    switch (level) {
        case 'debug':
            return Bug;
        case 'info':
            return Info;
        case 'warn':
            return Warning;
        case 'error':
            return XCircle;
    }
}

const levelLabels: Record<LogLevel, string> = {
    debug: 'Debug',
    info: 'Info',
    warn: 'Warn',
    error: 'Error',
};

function formatTimestamp(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        return date.toLocaleTimeString(undefined, {
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        });
    } catch {
        return dateStr;
    }
}

function formatFullTimestamp(dateStr: string): string {
    try {
        const date = new Date(dateStr);
        return date.toLocaleString(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false,
        });
    } catch {
        return dateStr;
    }
}

function getUniqueSources(logs: LogEntry[]): string[] {
    const sources = new Set(logs.map((l) => l.source));
    return Array.from(sources).sort();
}

const allLevels: LogLevel[] = ['debug', 'info', 'warn', 'error'];

const defaultFilters: LogFilterState = {
    levels: new Set(allLevels),
    source: '',
    search: '',
};

function readFiltersFromParams(searchParams: URLSearchParams): LogFilterState {
    const levelsParam = searchParams.get('levels');
    const levels = levelsParam
        ? new Set(levelsParam.split(',').filter((l): l is LogLevel => allLevels.includes(l as LogLevel)) as LogLevel[])
        : new Set(allLevels);
    return {
        levels,
        source: searchParams.get('source') ?? '',
        search: searchParams.get('search') ?? '',
    };
}

function writeFiltersToParams(setSearchParams: ReturnType<typeof useSearchParams>[1], filters: LogFilterState): void {
    setSearchParams(
        (prev) => {
            const next = new URLSearchParams(prev);

            if (filters.levels.size === allLevels.length) {
                next.delete('levels');
            } else {
                next.set('levels', Array.from(filters.levels).join(','));
            }

            if (filters.source) {
                next.set('source', filters.source);
            } else {
                next.delete('source');
            }

            if (filters.search) {
                next.set('search', filters.search);
            } else {
                next.delete('search');
            }
            return next;
        },
        { replace: true },
    );
}

function readLiveTailFromParams(searchParams: URLSearchParams): boolean {
    return searchParams.get('liveTail') === 'true';
}

function writeLiveTailToParams(setSearchParams: ReturnType<typeof useSearchParams>[1], liveTail: boolean): void {
    setSearchParams(
        (prev) => {
            const next = new URLSearchParams(prev);
            if (liveTail) {
                next.set('liveTail', 'true');
            } else {
                next.delete('liveTail');
            }
            return next;
        },
        { replace: true },
    );
}

export interface LogViewerProps {
    loading?: boolean;

    className?: string;

    persistFilters?: boolean;
}

export function LogViewer({ loading = false, className, persistFilters = false }: LogViewerProps) {
    const [searchParams, setSearchParams] = useSearchParams();

    const [localFilters, setLocalFilters] = useState<LogFilterState>(defaultFilters);
    const [localLiveTail, setLocalLiveTail] = useState(false);

    const filters: LogFilterState = persistFilters ? readFiltersFromParams(searchParams) : localFilters;
    const filtersRef = useRef(filters);
    filtersRef.current = filters;

    const setFilters = useCallback(
        (updater: LogFilterState | ((prev: LogFilterState) => LogFilterState)) => {
            if (persistFilters) {
                const next = typeof updater === 'function' ? updater(filtersRef.current) : updater;
                writeFiltersToParams(setSearchParams, next);
            } else {
                setLocalFilters(updater);
            }
        },
        [persistFilters, setSearchParams],
    );

    const liveTail = persistFilters ? readLiveTailFromParams(searchParams) : localLiveTail;
    const liveTailRef = useRef(liveTail);
    liveTailRef.current = liveTail;

    const setLiveTailValue = useCallback(
        (value: boolean | ((prev: boolean) => boolean)) => {
            if (persistFilters) {
                const next = typeof value === 'function' ? value(liveTailRef.current) : value;
                writeLiveTailToParams(setSearchParams, next);
            } else {
                setLocalLiveTail(value);
            }
        },
        [persistFilters, setSearchParams],
    );

    const [expandedLogs, setExpandedLogs] = useState<Set<string>>(new Set());
    const [copiedId, setCopiedId] = useState<string | null>(null);
    const logsContainerRef = useRef<HTMLDivElement>(null);
    const copyTimeoutRef = useRef<ReturnType<typeof setTimeout>>(null);

    useEffect(() => {
        return () => {
            if (copyTimeoutRef.current) {
                clearTimeout(copyTimeoutRef.current);
            }
        };
    }, []);

    const { logs: logEntries, isLoading, refetch } = useLogs(liveTail);

    const sources = useMemo(() => getUniqueSources(logEntries), [logEntries]);
    const showLoading = loading || (isLoading && logEntries.length === 0);

    const filteredLogs = useMemo(() => {
        return logEntries.filter((log) => {
            if (!filters.levels.has(log.level)) {
                return false;
            }

            if (filters.source && log.source !== filters.source) {
                return false;
            }

            if (filters.search) {
                const search = filters.search.toLowerCase();
                const matchesMessage = log.message.toLowerCase().includes(search);
                const matchesSource = log.source.toLowerCase().includes(search);
                const matchesException = log.exception?.toLowerCase().includes(search);
                if (!matchesMessage && !matchesSource && !matchesException) {
                    return false;
                }
            }
            return true;
        });
    }, [logEntries, filters]);

    const levelCounts = useMemo(() => {
        const counts: Record<LogLevel, number> = { debug: 0, info: 0, warn: 0, error: 0 };
        filteredLogs.forEach((log) => {
            counts[log.level]++;
        });
        return counts;
    }, [filteredLogs]);

    useEffect(() => {
        if (liveTail && logsContainerRef.current) {
            logsContainerRef.current.scrollTop = logsContainerRef.current.scrollHeight;
        }
    }, [liveTail, filteredLogs]);

    const handleLevelToggle = useCallback(
        (level: LogLevel) => {
            setFilters((prev) => {
                const newLevels = new Set(prev.levels);
                if (newLevels.has(level)) {
                    newLevels.delete(level);
                } else {
                    newLevels.add(level);
                }
                return { ...prev, levels: newLevels };
            });
        },
        [setFilters],
    );

    const handleSourceChange = useCallback(
        (e: React.ChangeEvent<HTMLSelectElement>) => {
            setFilters((prev) => ({ ...prev, source: e.target.value }));
        },
        [setFilters],
    );

    const handleSearchChange = useCallback(
        (e: React.ChangeEvent<HTMLInputElement>) => {
            setFilters((prev) => ({ ...prev, search: e.target.value }));
        },
        [setFilters],
    );

    const handleClearSearch = useCallback(() => {
        setFilters((prev) => ({ ...prev, search: '' }));
    }, [setFilters]);

    const handleClearFilters = useCallback(() => {
        setFilters(defaultFilters);
    }, [setFilters]);

    const handleToggleExpand = useCallback((id: string) => {
        setExpandedLogs((prev) => {
            const next = new Set(prev);
            if (next.has(id)) {
                next.delete(id);
            } else {
                next.add(id);
            }
            return next;
        });
    }, []);

    const handleCopyLog = useCallback(async (log: LogEntry) => {
        const text = log.exception
            ? `[${log.timestamp}] [${log.level.toUpperCase()}] [${log.source}] ${log.message}\n${log.exception}`
            : `[${log.timestamp}] [${log.level.toUpperCase()}] [${log.source}] ${log.message}`;
        try {
            await navigator.clipboard.writeText(text);
            setCopiedId(log.id);
            if (copyTimeoutRef.current) {
                clearTimeout(copyTimeoutRef.current);
            }
            copyTimeoutRef.current = setTimeout(() => setCopiedId(null), 2000);
        } catch {
            /* empty */
        }
    }, []);

    const handleToggleLiveTail = useCallback(() => {
        setLiveTailValue((prev) => !prev);
    }, [setLiveTailValue]);

    const handleRefresh = useCallback(() => {
        void refetch();
    }, [refetch]);

    const handleScrollToBottom = useCallback(() => {
        if (logsContainerRef.current) {
            logsContainerRef.current.scrollTop = logsContainerRef.current.scrollHeight;
        }
    }, []);

    const hasActiveFilters = filters.search !== '' || filters.source !== '' || filters.levels.size !== allLevels.length;

    const classes = ['log-viewer', className].filter(Boolean).join(' ');

    return (
        <SystemLogsSlot>
            <div className={classes} data-component="log-viewer">
                <div className="log-viewer__header">
                    <div className="log-viewer__header-left">
                        <Scroll weight="duotone" className="log-viewer__icon" aria-hidden="true" />
                        <h2 className="log-viewer__title">System Logs</h2>
                        <span className="log-viewer__count">{filteredLogs.length}</span>
                    </div>
                    <div className="log-viewer__header-actions">
                        <Button
                            type="button"
                            variant={liveTail ? 'primary' : 'ghost'}
                            size="sm"
                            onClick={handleToggleLiveTail}
                            aria-label={liveTail ? 'Pause live tail' : 'Start live tail'}
                            aria-pressed={liveTail}
                            data-action="toggle-live-tail"
                        >
                            {liveTail ? <Pause weight="bold" /> : <Play weight="bold" />}
                            <span className="log-viewer__btn-label">{liveTail ? 'Pause' : 'Live'}</span>
                        </Button>
                        <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={handleRefresh}
                            disabled={liveTail || isLoading}
                            aria-label="Refresh logs"
                            data-action="refresh-logs"
                        >
                            <ArrowsClockwise weight="bold" />
                        </Button>
                        <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={handleScrollToBottom}
                            aria-label="Scroll to bottom"
                            data-action="scroll-to-bottom"
                        >
                            <ArrowDown weight="bold" />
                        </Button>
                    </div>
                </div>

                <div className="log-viewer__filters" data-component="log-filters">
                    <div className="log-viewer__level-filters">
                        {allLevels.map((level) => {
                            const Icon = getLevelIcon(level);
                            const isChecked = filters.levels.has(level);
                            return (
                                <Label
                                    key={level}
                                    className={`log-viewer__level-toggle log-viewer__level-toggle--${level} ${isChecked ? 'log-viewer__level-toggle--active' : ''}`}
                                    data-level={level}
                                    data-active={isChecked}
                                >
                                    <Checkbox
                                        checked={isChecked}
                                        onCheckedChange={() => handleLevelToggle(level)}
                                        aria-label={`Show ${levelLabels[level]} logs`}
                                        data-filter={`level-${level}`}
                                    />
                                    <Icon weight="bold" className="log-viewer__level-icon" aria-hidden="true" />
                                    <span className="log-viewer__level-label">{levelLabels[level]}</span>
                                    <span className="log-viewer__level-count">{levelCounts[level]}</span>
                                </Label>
                            );
                        })}
                    </div>

                    <div className="log-viewer__filter-row">
                        <div className="log-viewer__search">
                            <Input
                                type="text"
                                value={filters.search}
                                onChange={handleSearchChange}
                                placeholder="Search logs..."
                                size="sm"
                                leadingIcon={<MagnifyingGlass weight="regular" />}
                                trailingIcon={
                                    filters.search ? (
                                        <button
                                            type="button"
                                            className="log-viewer__clear-btn"
                                            onClick={handleClearSearch}
                                            aria-label="Clear search"
                                            data-action="clear-search"
                                        >
                                            <X weight="bold" />
                                        </button>
                                    ) : undefined
                                }
                                aria-label="Search log messages"
                                data-filter="search"
                            />
                        </div>
                        <select
                            className="log-viewer__source-select"
                            value={filters.source}
                            onChange={handleSourceChange}
                            aria-label="Filter by source"
                            data-filter="source"
                        >
                            <option value="">All Sources</option>
                            {sources.map((source) => (
                                <option key={source} value={source}>
                                    {source}
                                </option>
                            ))}
                        </select>
                        {hasActiveFilters && (
                            <Button
                                type="button"
                                variant="ghost"
                                size="sm"
                                onClick={handleClearFilters}
                                className="log-viewer__clear-filters"
                                aria-label="Clear all filters"
                                data-action="clear-filters"
                            >
                                <X weight="bold" />
                                <span>Clear</span>
                            </Button>
                        )}
                    </div>
                </div>

                <Card className="log-viewer__card">
                    <div
                        className="log-viewer__logs"
                        ref={logsContainerRef}
                        role="log"
                        aria-label="System log entries"
                        aria-live={liveTail ? 'polite' : 'off'}
                    >
                        {showLoading ? (
                            <div className="log-viewer__loading">
                                {Array.from({ length: 5 }).map((_, i) => (
                                    <div key={i} className="log-viewer__skeleton-row">
                                        <Skeleton width={60} height={14} />
                                        <Skeleton width={40} height={14} />
                                        <Skeleton width={150} height={14} />
                                        <Skeleton width="60%" height={14} />
                                    </div>
                                ))}
                            </div>
                        ) : filteredLogs.length === 0 ? (
                            <div className="log-viewer__empty">
                                <Scroll weight="duotone" className="log-viewer__empty-icon" aria-hidden="true" />
                                <p className="log-viewer__empty-text">No log entries</p>
                                <p className="log-viewer__empty-hint">
                                    {hasActiveFilters
                                        ? 'Try adjusting your filters'
                                        : 'Logs will appear here as events occur'}
                                </p>
                            </div>
                        ) : (
                            filteredLogs.map((log) => (
                                <LogEntryRow
                                    key={log.id}
                                    log={log}
                                    expanded={expandedLogs.has(log.id)}
                                    copied={copiedId === log.id}
                                    onToggleExpand={handleToggleExpand}
                                    onCopy={handleCopyLog}
                                />
                            ))
                        )}
                    </div>
                </Card>
            </div>
        </SystemLogsSlot>
    );
}

LogViewer.displayName = 'LogViewer';

interface LogEntryRowProps {
    log: LogEntry;
    expanded: boolean;
    copied: boolean;
    onToggleExpand: (id: string) => void;
    onCopy: (log: LogEntry) => void;
}

function LogEntryRow({ log, expanded, copied, onToggleExpand, onCopy }: LogEntryRowProps) {
    const LevelIcon = getLevelIcon(log.level);
    const variant = levelVariantMap[log.level];
    const hasException = Boolean(log.exception);

    return (
        <div
            className={`log-viewer__entry log-viewer__entry--${log.level}`}
            data-log-id={log.id}
            data-level={log.level}
        >
            <div className="log-viewer__entry-main">
                <span className="log-viewer__entry-time" title={formatFullTimestamp(log.timestamp)}>
                    {formatTimestamp(log.timestamp)}
                </span>
                <Badge variant={variant} size="sm" className="log-viewer__entry-level">
                    <LevelIcon weight="fill" aria-hidden="true" />
                    <span className="log-viewer__entry-level-text">{log.level.toUpperCase()}</span>
                </Badge>
                <span className="log-viewer__entry-source" title={log.source}>
                    {log.source.split('.').pop()}
                </span>
                <span className="log-viewer__entry-message">{log.message}</span>
                <div className="log-viewer__entry-actions">
                    {hasException && (
                        <button
                            type="button"
                            className={`log-viewer__entry-btn ${expanded ? 'log-viewer__entry-btn--active' : ''}`}
                            onClick={() => onToggleExpand(log.id)}
                            aria-expanded={expanded}
                            aria-label={expanded ? 'Hide exception' : 'Show exception'}
                            data-action="toggle-exception"
                            data-target={log.id}
                        >
                            <Warning weight="bold" />
                        </button>
                    )}
                    <button
                        type="button"
                        className={`log-viewer__entry-btn ${copied ? 'log-viewer__entry-btn--copied' : ''}`}
                        onClick={() => onCopy(log)}
                        aria-label={copied ? 'Copied!' : 'Copy log entry'}
                        data-action="copy"
                        data-target={log.id}
                    >
                        {copied ? <Check weight="bold" /> : <Copy weight="regular" />}
                    </button>
                </div>
            </div>
            {expanded && hasException && (
                <div className="log-viewer__entry-exception">
                    <pre className="log-viewer__exception-text">{log.exception}</pre>
                </div>
            )}
        </div>
    );
}

export default LogViewer;
