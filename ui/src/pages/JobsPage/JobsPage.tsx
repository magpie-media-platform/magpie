import { useMemo, useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ClockCountdownIcon, ClockCounterClockwiseIcon, CalendarBlankIcon, LightningIcon } from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Switch';
import { Pager } from '@/components/ui/Pager';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { JobExecutionFilters, type JobExecutionFilterState } from '@/components/ui/JobExecutionFilters';
import { ExecutionRow } from '@/components/jobs/ExecutionRow';
import { EmptyState } from '@/components/ui/EmptyState';
import { Skeleton } from '@/components/ui/Skeleton';
import { DisabledTooltip, useAuth, Role } from '@/auth';
import { EventTriggersTab } from '@/components/jobs/EventTriggersTab';
import { RunJobDialog } from '@/components/jobs/RunJobDialog';
import { ScheduleTriggersTab } from '@/components/jobs/ScheduleTriggersTab';
import { useEventTriggerCrud } from '@/components/jobs/useEventTriggerCrud';
import { useScheduleTriggerCrud } from '@/components/jobs/useScheduleTriggerCrud';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import { useUrlTabState } from '@/hooks/useUrlTabState';
import {
    useJobExecution,
    useJobSpecs,
    useJobs,
    displayStatusToWireStatuses,
    type JobExecutionQueryParams,
} from '@/hooks/useJobs';
import { canRunJobOnDemand, statusFilterOptions, type TemplateOption } from '@/lib/jobExecution';
import './JobsPage.css';

const SCHEDULE_TRIGGER_MODE = 'Schedule';
const EVENT_TRIGGER_MODE = 'Event';
const SYSTEM_SCOPE = 'System';

const EXECUTIONS_PAGE_SIZE = 50;
const JOB_TABS = ['executions', 'schedules', 'events'] as const;
type JobTab = (typeof JOB_TABS)[number];

type JobsFilters = JobExecutionFilterState & {
    rootsOnly: string;
    [key: string]: string;
};

const DEFAULT_FILTERS: JobsFilters = { template: '', status: '', rootsOnly: '1' };

function isJobTab(tab: string): tab is JobTab {
    return JOB_TABS.includes(tab as JobTab);
}

export function JobsPage() {
    useDocumentTitle('System Jobs');
    const [searchParams] = useSearchParams();
    const linkedJobId = searchParams.get('job');

    const { filters, page, setFilters, setPage } = useUrlTableState<JobsFilters>({
        defaultSort: [],
        sortKeys: [],
        defaultFilters: DEFAULT_FILTERS,
    });

    const [activeTab, setUrlActiveTab] = useUrlTabState({ defaultTab: 'executions', allowedTabs: JOB_TABS });
    const rootsOnly = filters.rootsOnly !== '0';
    const executionFilters: JobExecutionFilterState = useMemo(
        () => ({ template: filters.template, status: filters.status }),
        [filters.template, filters.status],
    );

    const setActiveTab = useCallback(
        (tab: string) => {
            if (!isJobTab(tab)) {
                return;
            }

            setUrlActiveTab(tab, {
                update: (params, nextTab) => {
                    if (activeTab === 'executions' && nextTab !== 'executions') {
                        params.delete('template');
                        params.delete('status');
                        params.delete('rootsOnly');
                        params.delete('page');
                    }
                },
            });
        },
        [activeTab, setUrlActiveTab],
    );

    const setExecutionFilters = useCallback(
        (next: JobExecutionFilterState) => {
            setFilters({ template: next.template, status: next.status });
        },
        [setFilters],
    );

    const setRootsOnly = useCallback(
        (next: boolean) => {
            setFilters({ rootsOnly: next ? '1' : '0' });
        },
        [setFilters],
    );

    const { hasRole, user } = useAuth();
    const canRunJobs = hasRole(Role.Manager);
    const canManageTriggers = hasRole(Role.Admin);

    const [runDialogOpen, setRunDialogOpen] = useState(false);

    const { specs } = useJobSpecs();

    const executionQueryParams: JobExecutionQueryParams = useMemo(() => {
        const params: JobExecutionQueryParams = {
            rootsOnly,
            pageNumber: page,
            pageSize: EXECUTIONS_PAGE_SIZE,
        };
        if (executionFilters.template) {
            params.jobType = executionFilters.template;
        }
        const wireStatuses = executionFilters.status ? displayStatusToWireStatuses[executionFilters.status] : undefined;
        if (wireStatuses && wireStatuses.length > 0) {
            params.status = wireStatuses;
        }
        return params;
    }, [rootsOnly, page, executionFilters.template, executionFilters.status]);

    const { jobs, pagination, isLoading: isLoadingJobs, runJob, refetch: refetchJobs } = useJobs(executionQueryParams);
    const linkedJobIsListed = linkedJobId !== null && jobs.some((execution) => execution.id === linkedJobId);
    const { job: linkedJob } = useJobExecution(linkedJobId ?? '', linkedJobId !== null && !linkedJobIsListed);
    const displayedJobs = linkedJob && !linkedJobIsListed ? [linkedJob, ...jobs] : jobs;

    const schedCrud = useScheduleTriggerCrud({ runJob, refetchJobs });
    const eventCrud = useEventTriggerCrud();

    const templateOptions: TemplateOption[] = useMemo(() => {
        const seen = new Set<string>();
        const options: TemplateOption[] = [];
        const historicalUsage = new Map<string, { execution: boolean; schedule: boolean; event: boolean }>();

        const ensureUsage = (jobType: string) => {
            const usage = historicalUsage.get(jobType) ?? { execution: false, schedule: false, event: false };
            historicalUsage.set(jobType, usage);
            return usage;
        };

        const getHistoricalTriggerModes = (jobType: string) => {
            const usage = historicalUsage.get(jobType);
            if (!usage) {
                return undefined;
            }

            const modes: NonNullable<TemplateOption['supportedTriggerModes']> = [];
            if (usage.schedule) {
                modes.push(SCHEDULE_TRIGGER_MODE);
            }
            if (usage.event) {
                modes.push(EVENT_TRIGGER_MODE);
            }

            return modes.length > 0 ? modes : undefined;
        };

        for (const job of jobs) {
            ensureUsage(job.jobType).execution = true;
        }

        for (const trigger of schedCrud.triggers) {
            ensureUsage(trigger.jobType).schedule = true;
        }

        for (const trigger of eventCrud.triggers) {
            ensureUsage(trigger.jobType).event = true;
        }

        for (const spec of specs) {
            const historicalTriggerModes = getHistoricalTriggerModes(spec.jobType);
            const supportedTriggerModes = [...spec.supportedTriggerModes];

            for (const mode of historicalTriggerModes ?? []) {
                if (!supportedTriggerModes.includes(mode)) {
                    supportedTriggerModes.push(mode);
                }
            }

            seen.add(spec.jobType);
            options.push({
                id: spec.jobType,
                name: spec.displayName || spec.jobType,
                enabled: spec.enabled,
                onDemand: spec.onDemand,
                scope: spec.scope,
                supportedTriggerModes,
                defaultSchedule: spec.defaultSchedule,
                parameters: spec.parameters,
            });
        }

        for (const job of jobs) {
            if (!seen.has(job.jobType)) {
                seen.add(job.jobType);
                options.push({
                    id: job.jobType,
                    name: job.jobType,
                    supportedTriggerModes: getHistoricalTriggerModes(job.jobType),
                });
            }
        }

        for (const trigger of schedCrud.triggers) {
            if (!seen.has(trigger.jobType)) {
                seen.add(trigger.jobType);
                options.push({
                    id: trigger.jobType,
                    name: trigger.jobType,
                    supportedTriggerModes: getHistoricalTriggerModes(trigger.jobType),
                });
            }
        }

        for (const trigger of eventCrud.triggers) {
            if (!seen.has(trigger.jobType)) {
                seen.add(trigger.jobType);
                options.push({
                    id: trigger.jobType,
                    name: trigger.jobType,
                    supportedTriggerModes: getHistoricalTriggerModes(trigger.jobType),
                });
            }
        }

        return options.sort((a, b) => a.name.localeCompare(b.name));
    }, [specs, jobs, schedCrud.triggers, eventCrud.triggers]);

    const systemJobTypes = useMemo(() => {
        const result = new Set<string>();
        for (const option of templateOptions) {
            if (option.scope === SYSTEM_SCOPE) {
                result.add(option.id);
            }
        }
        return result;
    }, [templateOptions]);

    const scheduleTemplateOptions = useMemo(
        () =>
            templateOptions.filter(
                (option) =>
                    option.scope !== SYSTEM_SCOPE &&
                    (!option.supportedTriggerModes || option.supportedTriggerModes.includes(SCHEDULE_TRIGGER_MODE)),
            ),
        [templateOptions],
    );

    const eventTemplateOptions = useMemo(
        () =>
            templateOptions.filter(
                (option) =>
                    option.scope !== SYSTEM_SCOPE &&
                    (!option.supportedTriggerModes || option.supportedTriggerModes.includes(EVENT_TRIGGER_MODE)),
            ),
        [templateOptions],
    );

    const runnableJobOptions = useMemo(
        () => templateOptions.filter((option) => canRunJobOnDemand(option, user?.role)),
        [templateOptions, user?.role],
    );
    const hasRunnableJobs = runnableJobOptions.length > 0;

    const totalSchedules = schedCrud.triggers.length;
    const totalEvents = eventCrud.triggers.length;

    const totalExecutions = pagination?.totalItems ?? 0;
    const totalPages = Math.max(1, Math.ceil(totalExecutions / EXECUTIONS_PAGE_SIZE));
    const hasActiveFilters = executionFilters.template !== '' || executionFilters.status !== '';

    const { refetch: refetchSched } = schedCrud;
    const { refetch: refetchEvent } = eventCrud;
    const handleRefresh = useCallback(async () => {
        await Promise.all([refetchJobs(), refetchSched(), refetchEvent()]);
    }, [refetchJobs, refetchSched, refetchEvent]);

    const runButtonDisabled = !canRunJobs || !hasRunnableJobs;
    const runButtonMessage = !canRunJobs
        ? 'Requires Manager role'
        : !hasRunnableJobs
          ? 'No on-demand jobs are available to run'
          : undefined;

    return (
        <div className="jobs-page" data-page="jobs">
            <PullToRefresh onRefresh={handleRefresh} className="jobs-page__pull-to-refresh">
                <PageHeader
                    className="jobs-page__header"
                    title="System Jobs"
                    description="View job history and configure automatic triggers."
                    icon={<ClockCountdownIcon weight="duotone" aria-hidden="true" data-testid="jobs-page__icon" />}
                />

                <Tabs value={activeTab} onValueChange={setActiveTab} className="jobs-page__tabs">
                    <TabsList>
                        <TabsTrigger value="executions">
                            <ClockCounterClockwiseIcon weight="bold" aria-hidden="true" />
                            <span>History</span>
                        </TabsTrigger>
                        <TabsTrigger value="schedules">
                            <CalendarBlankIcon weight="bold" aria-hidden="true" />
                            <span>Scheduled Jobs</span>
                        </TabsTrigger>
                        <TabsTrigger value="events">
                            <LightningIcon weight="bold" aria-hidden="true" />
                            <span>Event Triggers</span>
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="executions">
                        <div className="jobs-page__section-header">
                            <DisabledTooltip minRole={Role.Manager} message={runButtonMessage}>
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() => setRunDialogOpen(true)}
                                    disabled={runButtonDisabled}
                                    data-action="run-job"
                                >
                                    <LightningIcon weight="bold" aria-hidden="true" />
                                    <span>Run a job</span>
                                </Button>
                            </DisabledTooltip>
                        </div>

                        <div className="jobs-page__executions-toolbar">
                            <JobExecutionFilters
                                filters={executionFilters}
                                templates={templateOptions}
                                statusOptions={statusFilterOptions}
                                onChange={setExecutionFilters}
                            />
                            <label
                                htmlFor="jobs-page-roots-only"
                                className="jobs-page__roots-toggle"
                                data-control="roots-only"
                            >
                                <Switch
                                    id="jobs-page-roots-only"
                                    size="sm"
                                    checked={rootsOnly}
                                    onCheckedChange={setRootsOnly}
                                    aria-label="Show top level only"
                                    data-action="toggle-roots-only"
                                />
                                <span>Show top level only</span>
                            </label>
                        </div>

                        <div className="jobs-page__list" role="list" aria-label="Job history">
                            {isLoadingJobs ? (
                                <div className="jobs-page__loading" data-testid="jobs-page__loading">
                                    <Skeleton className="jobs-page__skeleton" />
                                    <Skeleton className="jobs-page__skeleton" />
                                    <Skeleton className="jobs-page__skeleton" />
                                </div>
                            ) : displayedJobs.length === 0 ? (
                                <EmptyState
                                    icon={<ClockCounterClockwiseIcon weight="duotone" />}
                                    title="No job history found"
                                    description={
                                        hasActiveFilters
                                            ? 'Try adjusting your filters'
                                            : 'History will appear here when jobs are run'
                                    }
                                />
                            ) : (
                                displayedJobs.map((execution) => (
                                    <ExecutionRow
                                        key={execution.id}
                                        execution={execution}
                                        autoExpand={execution.id === linkedJobId}
                                    />
                                ))
                            )}
                        </div>

                        {totalExecutions > EXECUTIONS_PAGE_SIZE && (
                            <Pager
                                className="jobs-page__pager"
                                data-component="executions-pager"
                                page={page}
                                pageCount={totalPages}
                                onFirst={() => setPage(1)}
                                onPrevious={() => setPage(page - 1)}
                                onNext={() => setPage(page + 1)}
                                onLast={() => setPage(totalPages)}
                            />
                        )}
                    </TabsContent>

                    <TabsContent value="schedules">
                        {canManageTriggers && (
                            <div className="jobs-page__section-header">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={schedCrud.handleAdd}
                                    data-action="add-schedule"
                                >
                                    <CalendarBlankIcon weight="bold" aria-hidden="true" />
                                    <span>Add Schedule</span>
                                </Button>
                            </div>
                        )}

                        <div className="jobs-page__list" role="list" aria-label="Schedule triggers">
                            {totalSchedules === 0 && (
                                <EmptyState
                                    icon={<CalendarBlankIcon weight="duotone" />}
                                    title="No schedule triggers"
                                    description={
                                        canManageTriggers
                                            ? 'Create a schedule to run a job automatically.'
                                            : 'Schedule triggers will appear here when configured.'
                                    }
                                />
                            )}
                            <ScheduleTriggersTab
                                crud={schedCrud}
                                templates={scheduleTemplateOptions}
                                systemJobTypes={systemJobTypes}
                                canManage={canManageTriggers}
                                canRun={canRunJobs}
                            />
                        </div>
                    </TabsContent>

                    <TabsContent value="events">
                        {canManageTriggers && (
                            <div className="jobs-page__section-header">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={eventCrud.handleAdd}
                                    data-action="add-event-trigger"
                                >
                                    <LightningIcon weight="bold" aria-hidden="true" />
                                    <span>Add Event Trigger</span>
                                </Button>
                            </div>
                        )}

                        <div className="jobs-page__list" role="list" aria-label="Event triggers">
                            {totalEvents === 0 && (
                                <EmptyState
                                    icon={<LightningIcon weight="duotone" />}
                                    title="No event triggers"
                                    description={
                                        canManageTriggers
                                            ? 'Create an event trigger to run a job when events occur.'
                                            : 'Event triggers will appear here when configured.'
                                    }
                                />
                            )}
                            <EventTriggersTab
                                crud={eventCrud}
                                templates={eventTemplateOptions}
                                systemJobTypes={systemJobTypes}
                                canManage={canManageTriggers}
                            />
                        </div>
                    </TabsContent>
                </Tabs>
            </PullToRefresh>

            <RunJobDialog
                open={runDialogOpen}
                onOpenChange={setRunDialogOpen}
                runnableJobs={runnableJobOptions}
                runJob={runJob}
                onRan={refetchJobs}
            />
        </div>
    );
}

JobsPage.displayName = 'JobsPage';

export default JobsPage;
