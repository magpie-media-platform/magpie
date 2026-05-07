import { useState, useMemo, useCallback } from 'react';
import {
    ClockCountdown,
    Play,
    CircleNotch,
    ClockCounterClockwise,
    CalendarBlank,
    Lightning,
} from '@phosphor-icons/react';

import { Link, useSearchParams } from 'react-router-dom';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useToast } from '@/components/ui/Toast';
import { JobExecutionFilters, type JobExecutionFilterState } from '@/components/ui/JobExecutionFilters';
import { ExecutionRow } from '@/components/ui/ExecutionRow';
import { useAuth, Role } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useJobs } from '@/hooks/useJobs';
import { useScheduleTriggers, type ApiScheduleTrigger } from '@/hooks/useScheduleTriggers';
import { useEventTriggers, type ApiEventTrigger } from '@/hooks/useEventTriggers';

import { EmptyState } from '@/components/ui/EmptyState';
import { mapStatusToDisplay, statusFilterOptions, formatJobDateTime, type TemplateOption } from '@/lib/jobExecution';

import './MyJobsPage.css';

const USER_SCOPE = 3;

function describeCron(cronExpression: string): string {
    const presets: Record<string, string> = {
        '0 * * * *': 'Every hour',
        '0 */6 * * *': 'Every 6 hours',
        '0 */12 * * *': 'Every 12 hours',
        '0 0 * * *': 'Daily at midnight',
        '0 6 * * *': 'Daily at 6 AM',
        '0 2 * * *': 'Daily at 2 AM',
        '0 0 * * 0': 'Weekly on Sunday',
    };
    return presets[cronExpression] ?? cronExpression;
}

function describeEventPattern(trigger: ApiEventTrigger): string {
    const pattern = trigger.eventFilter?.eventName?.value;
    if (!pattern) {
        return 'All events';
    }
    const presets: Record<string, string> = {
        '.*': 'All events',
        'content\\.added': 'Content added',
        'content\\.updated': 'Content updated',
        'content\\.removed': 'Content removed',
        'download\\.completed': 'Download completed',
        'download\\.failed': 'Download failed',
        'file\\.imported': 'File imported',
        'metadata\\.refreshed': 'Metadata refreshed',
    };
    return presets[pattern] ?? pattern;
}

export function MyJobsPage() {
    useDocumentTitle('My Jobs');
    const [searchParams, setSearchParams] = useSearchParams();
    const activeTab = searchParams.get('tab') || 'executions';
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === 'executions') {
                        next.delete('tab');
                    } else {
                        next.set('tab', tab);
                    }

                    if (tab !== 'executions') {
                        next.delete('template');
                        next.delete('status');
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const executionFilters: JobExecutionFilterState = useMemo(
        () => ({
            template: searchParams.get('template') || '',
            status: searchParams.get('status') || '',
        }),
        [searchParams],
    );

    const setExecutionFilters = useCallback(
        (filters: JobExecutionFilterState) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (filters.template) {
                        next.set('template', filters.template);
                    } else {
                        next.delete('template');
                    }
                    if (filters.status) {
                        next.set('status', filters.status);
                    } else {
                        next.delete('status');
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const [runningJobs, setRunningJobs] = useState<Set<string>>(new Set());
    const { toast } = useToast();
    const { hasRole } = useAuth();

    const canRunJobs = hasRole(Role.Viewer);

    const { jobs, runJob, isLoading: isLoadingJobs, refetch: refetchJobs } = useJobs();
    const { triggers: scheduleTriggers, isLoading: isLoadingSchedules } = useScheduleTriggers();
    const { triggers: eventTriggers, isLoading: isLoadingEvents } = useEventTriggers();

    const userJobs = useMemo(() => {
        return jobs.filter((job) => job.scope === USER_SCOPE);
    }, [jobs]);

    const userScheduleTriggers = useMemo(() => {
        return scheduleTriggers.filter((t) => t.scope === USER_SCOPE);
    }, [scheduleTriggers]);

    const userEventTriggers = useMemo(() => {
        return eventTriggers.filter((t) => t.scope === USER_SCOPE);
    }, [eventTriggers]);

    const templateOptions: TemplateOption[] = useMemo(() => {
        const seen = new Set<string>();
        const options: TemplateOption[] = [];
        for (const job of userJobs) {
            if (!seen.has(job.jobType)) {
                seen.add(job.jobType);
                options.push({ id: job.jobType, name: job.jobType });
            }
        }
        return options;
    }, [userJobs]);

    const executionStatusCounts = useMemo(() => {
        const counts = {
            running: 0,
            pending: 0,
            completed: 0,
            failed: 0,
            cancelled: 0,
        };
        for (const job of userJobs) {
            const displayStatus = mapStatusToDisplay(job.status);
            counts[displayStatus]++;
        }
        return counts;
    }, [userJobs]);

    const totalActiveExecutions = executionStatusCounts.running + executionStatusCounts.pending;
    const totalSchedules = userScheduleTriggers.length;
    const enabledSchedules = userScheduleTriggers.filter((t) => t.enabled).length;
    const totalEvents = userEventTriggers.length;
    const enabledEvents = userEventTriggers.filter((t) => t.enabled).length;

    const filteredExecutions = useMemo(() => {
        let items = [...userJobs];

        if (executionFilters.template) {
            items = items.filter((e) => e.jobType === executionFilters.template);
        }

        if (executionFilters.status) {
            items = items.filter((e) => mapStatusToDisplay(e.status) === executionFilters.status);
        }

        items.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());

        return items;
    }, [userJobs, executionFilters]);

    const hasActiveFilters = executionFilters.template !== '' || executionFilters.status !== '';

    const handleRunTrigger = useCallback(
        async (trigger: ApiScheduleTrigger) => {
            const triggerId = trigger.id;
            setRunningJobs((prev) => new Set([...prev, triggerId]));

            try {
                await runJob({
                    jobType: trigger.jobType,
                    extensionId: trigger.extensionId,
                    parameters: trigger.parameters,
                });

                toast({
                    title: trigger.name,
                    description: 'Job started successfully',
                    variant: 'success',
                });
                refetchJobs();
            } catch {
                toast({
                    title: trigger.name,
                    description: 'Failed to start job',
                    variant: 'error',
                });
            } finally {
                setRunningJobs((prev) => {
                    const next = new Set(prev);
                    next.delete(triggerId);
                    return next;
                });
            }
        },
        [runJob, toast, refetchJobs],
    );

    const handleRefresh = useCallback(async () => {
        await Promise.all([refetchJobs()]);
    }, [refetchJobs]);

    const isLoading = isLoadingJobs || isLoadingSchedules || isLoadingEvents;

    return (
        <div className="my-jobs-page" data-page="my-jobs">
            <PullToRefresh onRefresh={handleRefresh} className="my-jobs-page__pull-to-refresh">
                <header className="my-jobs-page__header">
                    <div className="my-jobs-page__header-left">
                        <ClockCountdown
                            weight="duotone"
                            className="my-jobs-page__icon"
                            aria-hidden="true"
                            data-testid="my-jobs-page__icon"
                        />
                        <h1 className="my-jobs-page__title">My Jobs</h1>
                        {!isLoading && (
                            <span className="page-count my-jobs-page__count" data-testid="my-jobs-page__count">
                                {userJobs.length}
                            </span>
                        )}
                    </div>
                </header>

                <Tabs value={activeTab} onValueChange={setActiveTab} className="my-jobs-page__tabs">
                    <TabsList>
                        <TabsTrigger value="executions">
                            <ClockCounterClockwise weight="bold" aria-hidden="true" />
                            <span>Executions</span>
                            {totalActiveExecutions > 0 && (
                                <span className="tab-count tab-count--active">{totalActiveExecutions}</span>
                            )}
                        </TabsTrigger>
                        <TabsTrigger value="schedules">
                            <CalendarBlank weight="bold" aria-hidden="true" />
                            <span>Schedules</span>
                            {totalSchedules > 0 && <span className="tab-count">{totalSchedules}</span>}
                        </TabsTrigger>
                        <TabsTrigger value="events">
                            <Lightning weight="bold" aria-hidden="true" />
                            <span>Events</span>
                            {totalEvents > 0 && <span className="tab-count">{totalEvents}</span>}
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="executions">
                        <div className="my-jobs-page__summary" aria-label="Executions by status">
                            <Badge
                                variant="running"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={executionStatusCounts.running}
                            >
                                Running
                            </Badge>
                            <Badge
                                variant="pending"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={executionStatusCounts.pending}
                            >
                                Pending
                            </Badge>
                            <Badge
                                variant="completed"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={executionStatusCounts.completed}
                            >
                                Completed
                            </Badge>
                            <Badge
                                variant="failed"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={executionStatusCounts.failed}
                            >
                                Failed
                            </Badge>
                            <Badge
                                variant="cancelled"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={executionStatusCounts.cancelled}
                            >
                                Cancelled
                            </Badge>
                        </div>

                        <JobExecutionFilters
                            filters={executionFilters}
                            templates={templateOptions}
                            statusOptions={statusFilterOptions}
                            onChange={setExecutionFilters}
                        />

                        <div className="my-jobs-page__list" role="list" aria-label="Job executions">
                            {filteredExecutions.length === 0 ? (
                                <EmptyState
                                    icon={<ClockCounterClockwise weight="duotone" />}
                                    title="No job executions found"
                                    description={
                                        hasActiveFilters
                                            ? 'Try adjusting your filters'
                                            : 'Run a job to see execution history here'
                                    }
                                />
                            ) : (
                                filteredExecutions.map((execution) => (
                                    <ExecutionRow key={execution.id} execution={execution} />
                                ))
                            )}
                        </div>
                    </TabsContent>

                    <TabsContent value="schedules">
                        <div className="my-jobs-page__summary" aria-label="Schedule triggers summary">
                            <Badge variant="completed" size="sm" badgeStyle="outline" dot count={enabledSchedules}>
                                Enabled
                            </Badge>
                            <Badge
                                variant="pending"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={totalSchedules - enabledSchedules}
                            >
                                Disabled
                            </Badge>
                        </div>
                        {userScheduleTriggers.length > 0 && (
                            <p className="my-jobs-page__tab-note">
                                Schedule triggers are configured by administrators and run jobs on your behalf.
                                {hasRole(Role.Admin) && (
                                    <>
                                        {' '}
                                        <Link to="/admin/jobs?tab=triggers">Manage in Admin &gt; Jobs</Link>.
                                    </>
                                )}
                            </p>
                        )}

                        <div className="my-jobs-page__list" role="list" aria-label="Schedule triggers">
                            {userScheduleTriggers.length === 0 ? (
                                <EmptyState
                                    icon={<CalendarBlank weight="duotone" />}
                                    title="No schedule triggers"
                                    description="Schedule triggers are managed by administrators."
                                    action={
                                        hasRole(Role.Admin) ? (
                                            <Link to="/admin/jobs">Manage in Admin &gt; Jobs</Link>
                                        ) : undefined
                                    }
                                />
                            ) : (
                                userScheduleTriggers.map((trigger) => (
                                    <ScheduleTriggerRow
                                        key={trigger.id}
                                        trigger={trigger}
                                        isRunning={runningJobs.has(trigger.id)}
                                        canRun={canRunJobs}
                                        onRunNow={() => handleRunTrigger(trigger)}
                                    />
                                ))
                            )}
                        </div>
                    </TabsContent>

                    <TabsContent value="events">
                        <div className="my-jobs-page__summary" aria-label="Event triggers summary">
                            <Badge variant="completed" size="sm" badgeStyle="outline" dot count={enabledEvents}>
                                Enabled
                            </Badge>
                            <Badge
                                variant="pending"
                                size="sm"
                                badgeStyle="outline"
                                dot
                                count={totalEvents - enabledEvents}
                            >
                                Disabled
                            </Badge>
                        </div>
                        {userEventTriggers.length > 0 && (
                            <p className="my-jobs-page__tab-note">
                                Event triggers are configured by administrators and run jobs in response to system
                                events.
                                {hasRole(Role.Admin) && (
                                    <>
                                        {' '}
                                        <Link to="/admin/jobs?tab=triggers">Manage in Admin &gt; Jobs</Link>.
                                    </>
                                )}
                            </p>
                        )}

                        <div className="my-jobs-page__list" role="list" aria-label="Event triggers">
                            {userEventTriggers.length === 0 ? (
                                <EmptyState
                                    icon={<Lightning weight="duotone" />}
                                    title="No event triggers"
                                    description="Event triggers are managed by administrators."
                                    action={
                                        hasRole(Role.Admin) ? (
                                            <Link to="/admin/jobs">Manage in Admin &gt; Jobs</Link>
                                        ) : undefined
                                    }
                                />
                            ) : (
                                userEventTriggers.map((trigger) => (
                                    <EventTriggerRow key={trigger.id} trigger={trigger} />
                                ))
                            )}
                        </div>
                    </TabsContent>
                </Tabs>
            </PullToRefresh>
        </div>
    );
}

MyJobsPage.displayName = 'MyJobsPage';

interface ScheduleTriggerRowProps {
    trigger: ApiScheduleTrigger;
    isRunning: boolean;
    canRun: boolean;
    onRunNow: () => void;
}

function ScheduleTriggerRow({ trigger, isRunning, canRun, onRunNow }: ScheduleTriggerRowProps) {
    return (
        <div
            className={`my-jobs-page__item ${!trigger.enabled ? 'my-jobs-page__item--disabled' : ''}`}
            role="listitem"
            data-trigger-id={trigger.id}
            data-enabled={trigger.enabled}
        >
            <div className="my-jobs-page__item-main">
                <div className="my-jobs-page__item-info">
                    <div className="my-jobs-page__item-header">
                        <span className="my-jobs-page__item-name">{trigger.name}</span>
                        <Badge variant={trigger.enabled ? 'fulfilled' : 'unknown'} size="sm">
                            {trigger.enabled ? 'Enabled' : 'Disabled'}
                        </Badge>
                    </div>
                    <p className="my-jobs-page__item-description">
                        <CalendarBlank
                            weight="regular"
                            aria-hidden="true"
                            className="my-jobs-page__item-description-icon"
                        />
                        {describeCron(trigger.schedule)}
                        {trigger.nextRunAt && trigger.enabled && (
                            <span className="my-jobs-page__item-description-secondary">
                                · Next: {formatJobDateTime(trigger.nextRunAt)}
                            </span>
                        )}
                    </p>
                </div>
                <div className="my-jobs-page__item-actions">
                    <Button
                        variant={isRunning ? 'secondary' : 'primary'}
                        size="sm"
                        disabled={!canRun || isRunning || !trigger.enabled}
                        onClick={onRunNow}
                        aria-label={isRunning ? `Running ${trigger.name}` : `Run ${trigger.name}`}
                        data-action="run"
                        data-target={trigger.id}
                        title={!trigger.enabled ? 'Trigger is disabled' : isRunning ? 'Running...' : 'Run now'}
                    >
                        {isRunning ? (
                            <>
                                <CircleNotch weight="bold" className="my-jobs-page__spinner" aria-hidden="true" />
                                <span className="my-jobs-page__run-label">Running</span>
                            </>
                        ) : (
                            <>
                                <Play weight="fill" aria-hidden="true" />
                                <span className="my-jobs-page__run-label">Run</span>
                            </>
                        )}
                    </Button>
                </div>
            </div>
        </div>
    );
}

interface EventTriggerRowProps {
    trigger: ApiEventTrigger;
}

function EventTriggerRow({ trigger }: EventTriggerRowProps) {
    const filterCount = trigger.eventFilter?.eventContext ? Object.keys(trigger.eventFilter.eventContext).length : 0;

    return (
        <div
            className={`my-jobs-page__item ${!trigger.enabled ? 'my-jobs-page__item--disabled' : ''}`}
            role="listitem"
            data-event-trigger-id={trigger.id}
            data-enabled={trigger.enabled}
        >
            <div className="my-jobs-page__item-main">
                <div className="my-jobs-page__item-info">
                    <div className="my-jobs-page__item-header">
                        <span className="my-jobs-page__item-name">{trigger.name}</span>
                        <Badge variant={trigger.enabled ? 'fulfilled' : 'unknown'} size="sm">
                            {trigger.enabled ? 'Enabled' : 'Disabled'}
                        </Badge>
                    </div>
                    <p className="my-jobs-page__item-description">
                        <Lightning
                            weight="regular"
                            aria-hidden="true"
                            className="my-jobs-page__item-description-icon"
                        />
                        {describeEventPattern(trigger)}
                        {filterCount > 0 && (
                            <span className="my-jobs-page__item-description-secondary">
                                · {filterCount} filter{filterCount !== 1 ? 's' : ''}
                            </span>
                        )}
                    </p>
                </div>
            </div>
        </div>
    );
}

export default MyJobsPage;
