import { useMemo, useCallback, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ClockCountdownIcon, ClockCounterClockwiseIcon, CalendarBlankIcon, LightningIcon } from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { Button } from '@/components/ui/Button';
import { Pager } from '@/components/ui/Pager';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { Switch } from '@/components/ui/Switch';
import { JobExecutionFilters, type JobExecutionFilterState } from '@/components/ui/JobExecutionFilters';
import { ExecutionRow } from '@/components/jobs/ExecutionRow';
import { useAuth } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlTableState } from '@/hooks/useUrlTableState';
import {
    useJobExecution,
    useJobs,
    useJobSpecs,
    displayStatusToWireStatuses,
    type JobExecutionQueryParams,
} from '@/hooks/useJobs';
import { EventTriggersTab } from '@/components/jobs/EventTriggersTab';
import { RunJobDialog } from '@/components/jobs/RunJobDialog';
import { ScheduleTriggersTab } from '@/components/jobs/ScheduleTriggersTab';
import { useEventTriggerCrud } from '@/components/jobs/useEventTriggerCrud';
import { useScheduleTriggerCrud } from '@/components/jobs/useScheduleTriggerCrud';

import { EmptyState } from '@/components/ui/EmptyState';
import { canRunJobOnDemand, statusFilterOptions, type TemplateOption } from '@/lib/jobExecution';

import './MyJobsPage.css';

const USER_SCOPE = 'User' as const;
const SCHEDULE_TRIGGER_MODE = 'Schedule' as const;
const EVENT_TRIGGER_MODE = 'Event' as const;
const EXECUTIONS_PAGE_SIZE = 50;

type MyJobsFilters = JobExecutionFilterState & {
    tab: string;
    rootsOnly: string;
    [key: string]: string;
};

const DEFAULT_FILTERS: MyJobsFilters = { tab: 'executions', template: '', status: '', rootsOnly: '1' };

function specsToTemplateOptions(specs: ReturnType<typeof useJobSpecs>['specs']): TemplateOption[] {
    return specs
        .map((spec) => ({
            id: spec.jobType,
            name: spec.displayName || spec.jobType,
            enabled: spec.enabled,
            onDemand: spec.onDemand,
            scope: spec.scope,
            supportedTriggerModes: spec.supportedTriggerModes,
            requesterTriggerModes: spec.requesterTriggerModes,
            defaultSchedule: spec.defaultSchedule,
            parameters: spec.parameters,
        }))
        .sort((a, b) => a.name.localeCompare(b.name));
}

export function MyJobsPage() {
    useDocumentTitle('My Jobs');
    const [searchParams] = useSearchParams();
    const linkedJobId = searchParams.get('job');

    const { filters, page, setFilters, setPage } = useUrlTableState<MyJobsFilters>({
        defaultSort: [],
        sortKeys: [],
        defaultFilters: DEFAULT_FILTERS,
    });

    const activeTab = filters.tab;
    const rootsOnly = filters.rootsOnly !== '0';
    const executionFilters: JobExecutionFilterState = useMemo(
        () => ({ template: filters.template, status: filters.status }),
        [filters.template, filters.status],
    );

    const setActiveTab = useCallback(
        (tab: string) => {
            setFilters(tab !== 'executions' ? { tab, template: '', status: '' } : { tab });
        },
        [setFilters],
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

    const { hasPermission, user } = useAuth();
    const canManageTriggers = hasPermission('jobs.schedule');
    const canViewAdminJobs = hasPermission('jobs.view');

    const [runDialogOpen, setRunDialogOpen] = useState(false);

    const executionQueryParams: JobExecutionQueryParams = useMemo(() => {
        const params: JobExecutionQueryParams = {
            mine: true,
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

    const { jobs: userJobs, pagination, runJob, refetch: refetchJobs } = useJobs(executionQueryParams);
    const linkedJobIsListed = linkedJobId !== null && userJobs.some((execution) => execution.id === linkedJobId);
    const { job: linkedJob } = useJobExecution(linkedJobId ?? '', linkedJobId !== null && !linkedJobIsListed);
    const displayedUserJobs = linkedJob && !linkedJobIsListed ? [linkedJob, ...userJobs] : userJobs;

    const schedCrud = useScheduleTriggerCrud({
        runJob,
        refetchJobs,
        scope: USER_SCOPE,
        queryParams: { mine: true },
    });
    const eventCrud = useEventTriggerCrud({ scope: USER_SCOPE, queryParams: { mine: true } });

    const { specs: scheduleSpecs } = useJobSpecs('Schedule');
    const { specs: eventSpecs } = useJobSpecs('Event');
    const { specs: manualSpecs } = useJobSpecs();

    const scheduleTemplateOptions = useMemo(
        () =>
            specsToTemplateOptions(scheduleSpecs).filter(
                (option) =>
                    option.supportedTriggerModes?.includes(SCHEDULE_TRIGGER_MODE) === true &&
                    option.requesterTriggerModes?.includes(SCHEDULE_TRIGGER_MODE) === true,
            ),
        [scheduleSpecs],
    );
    const eventTemplateOptions = useMemo(
        () =>
            specsToTemplateOptions(eventSpecs).filter(
                (option) =>
                    option.supportedTriggerModes?.includes(EVENT_TRIGGER_MODE) === true &&
                    option.requesterTriggerModes?.includes(EVENT_TRIGGER_MODE) === true,
            ),
        [eventSpecs],
    );
    const runnableJobOptions = useMemo(
        () => specsToTemplateOptions(manualSpecs).filter((option) => canRunJobOnDemand(option, user?.role)),
        [manualSpecs, user?.role],
    );
    const hasRunnableJobs = runnableJobOptions.length > 0;
    const canRunJobs = hasRunnableJobs;

    const userScheduleTriggers = schedCrud.triggers;
    const userEventTriggers = eventCrud.triggers;

    const totalSchedules = userScheduleTriggers.length;
    const totalEvents = userEventTriggers.length;

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

    const totalExecutions = pagination?.totalItems ?? 0;
    const totalPages = Math.max(1, Math.ceil(totalExecutions / EXECUTIONS_PAGE_SIZE));
    const hasActiveFilters = executionFilters.template !== '' || executionFilters.status !== '';

    const handleRefresh = useCallback(async () => {
        await Promise.all([refetchJobs(), schedCrud.refetch(), eventCrud.refetch()]);
    }, [refetchJobs, schedCrud, eventCrud]);

    let executionsEmptyDescription = 'This list shows only jobs you ran.';
    if (canViewAdminJobs) {
        executionsEmptyDescription += ' System and scheduled jobs appear under Admin > System Jobs.';
    }
    if (canRunJobs) {
        executionsEmptyDescription += ' Run a job to see your execution history here.';
    }

    return (
        <div className="my-jobs-page" data-page="my-jobs">
            <PullToRefresh onRefresh={handleRefresh} className="my-jobs-page__pull-to-refresh">
                <PageHeader
                    className="my-jobs-page__header"
                    title="My Jobs"
                    description="Your jobs and job triggers."
                    icon={<ClockCountdownIcon weight="duotone" aria-hidden="true" data-testid="my-jobs-page__icon" />}
                />

                <Tabs value={activeTab} onValueChange={setActiveTab} className="my-jobs-page__tabs">
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
                        {canRunJobs && (
                            <div className="my-jobs-page__section-header">
                                <Button
                                    variant="primary"
                                    size="sm"
                                    onClick={() => setRunDialogOpen(true)}
                                    disabled={!hasRunnableJobs}
                                    data-action="run-job"
                                >
                                    <LightningIcon weight="bold" aria-hidden="true" />
                                    <span>Run a job</span>
                                </Button>
                            </div>
                        )}

                        <div className="my-jobs-page__executions-toolbar">
                            <JobExecutionFilters
                                filters={executionFilters}
                                templates={templateOptions}
                                statusOptions={statusFilterOptions}
                                onChange={setExecutionFilters}
                            />
                            <label
                                htmlFor="my-jobs-page-roots-only"
                                className="my-jobs-page__roots-toggle"
                                data-control="roots-only"
                            >
                                <Switch
                                    id="my-jobs-page-roots-only"
                                    size="sm"
                                    checked={rootsOnly}
                                    onCheckedChange={setRootsOnly}
                                    aria-label="Show top level only"
                                    data-action="toggle-roots-only"
                                />
                                <span>Show top level only</span>
                            </label>
                        </div>

                        <div className="my-jobs-page__list" role="list" aria-label="Job history">
                            {displayedUserJobs.length === 0 ? (
                                <EmptyState
                                    icon={<ClockCounterClockwiseIcon weight="duotone" />}
                                    title="No job history found"
                                    description={
                                        hasActiveFilters ? 'Try adjusting your filters' : executionsEmptyDescription
                                    }
                                />
                            ) : (
                                displayedUserJobs.map((execution) => (
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
                                className="my-jobs-page__pager"
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
                            <div className="my-jobs-page__section-header">
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

                        <div className="my-jobs-page__list" role="list" aria-label="Schedule triggers">
                            {totalSchedules === 0 && (
                                <EmptyState
                                    icon={<CalendarBlankIcon weight="duotone" />}
                                    title="No schedule triggers"
                                    description={
                                        canManageTriggers
                                            ? 'Create a schedule to run a job automatically on your behalf.'
                                            : 'Schedule triggers run a job automatically on your behalf.'
                                    }
                                />
                            )}
                            <ScheduleTriggersTab
                                crud={schedCrud}
                                templates={scheduleTemplateOptions}
                                canManage={canManageTriggers}
                                canRun={canRunJobs}
                            />
                        </div>
                    </TabsContent>

                    <TabsContent value="events">
                        {canManageTriggers && (
                            <div className="my-jobs-page__section-header">
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

                        <div className="my-jobs-page__list" role="list" aria-label="Event triggers">
                            {totalEvents === 0 && (
                                <EmptyState
                                    icon={<LightningIcon weight="duotone" />}
                                    title="No event triggers"
                                    description="Create an event trigger to run a job when events occur."
                                />
                            )}
                            <EventTriggersTab
                                crud={eventCrud}
                                templates={eventTemplateOptions}
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

MyJobsPage.displayName = 'MyJobsPage';

export default MyJobsPage;
