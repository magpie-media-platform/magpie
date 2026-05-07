import { useMemo, useCallback } from 'react';
import { useUrlFilterState } from '@/hooks/useUrlFilterState';
import { ClockCountdown, ClockCounterClockwise, CalendarBlank, Lightning } from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { QuickActionsButton, type QuickAction } from '@/components/ui/QuickActionsButton';
import { JobExecutionFilters, type JobExecutionFilterState } from '@/components/ui/JobExecutionFilters';
import { ExecutionRow } from '@/components/ui/ExecutionRow';
import { EmptyState } from '@/components/ui/EmptyState';
import { useAuth, Role } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useJobs } from '@/hooks/useJobs';
import { mapStatusToDisplay, statusFilterOptions, type TemplateOption } from '@/lib/jobExecution';
import { useScheduleTriggerCrud } from './useScheduleTriggerCrud';
import { useEventTriggerCrud } from './useEventTriggerCrud';
import { ScheduleTriggersTab } from './ScheduleTriggersTab';
import { EventTriggersTab } from './EventTriggersTab';

import '@/styles/list-toolbar.css';
import './JobsPage.css';

export function JobsPage() {
    useDocumentTitle('Jobs');
    const [urlState, setUrlState] = useUrlFilterState({ tab: 'executions', template: '', status: '' });

    const activeTab = urlState.tab;
    const executionFilters: JobExecutionFilterState = useMemo(
        () => ({ template: urlState.template, status: urlState.status }),
        [urlState.template, urlState.status],
    );

    const setActiveTab = useCallback(
        (tab: string) => {
            setUrlState({
                tab,
                ...(tab !== 'executions' ? { template: '', status: '' } : {}),
            });
        },
        [setUrlState],
    );

    const setExecutionFilters = useCallback(
        (filters: JobExecutionFilterState) => {
            setUrlState(filters);
        },
        [setUrlState],
    );

    const { hasRole } = useAuth();
    const canRunJobs = hasRole(Role.Manager);
    const canManageTriggers = hasRole(Role.Admin);

    const { jobs, runJob, isLoading: isLoadingJobs, refetch: refetchJobs } = useJobs();

    const schedCrud = useScheduleTriggerCrud({ runJob, refetchJobs });
    const eventCrud = useEventTriggerCrud();

    const templateOptions: TemplateOption[] = useMemo(() => {
        const seen = new Set<string>();
        const options: TemplateOption[] = [];

        for (const job of jobs) {
            if (!seen.has(job.jobType)) {
                seen.add(job.jobType);
                options.push({ id: job.jobType, name: job.jobType });
            }
        }

        for (const trigger of schedCrud.triggers) {
            if (!seen.has(trigger.jobType)) {
                seen.add(trigger.jobType);
                options.push({ id: trigger.jobType, name: trigger.jobType });
            }
        }

        for (const trigger of eventCrud.triggers) {
            if (!seen.has(trigger.jobType)) {
                seen.add(trigger.jobType);
                options.push({ id: trigger.jobType, name: trigger.jobType });
            }
        }

        return options.sort((a, b) => a.name.localeCompare(b.name));
    }, [jobs, schedCrud.triggers, eventCrud.triggers]);

    const totalJobs = jobs.length;
    const totalScheduleTriggers = schedCrud.triggers.length;
    const enabledScheduleTriggers = schedCrud.triggers.filter((t) => t.enabled).length;
    const totalEventTriggers = eventCrud.triggers.length;
    const enabledEventTriggers = eventCrud.triggers.filter((t) => t.enabled).length;
    const totalAllTriggers = totalScheduleTriggers + totalEventTriggers;
    const enabledAllTriggers = enabledScheduleTriggers + enabledEventTriggers;

    const titleCount = activeTab === 'triggers' ? totalAllTriggers : totalJobs;

    const executionStatusCounts = useMemo(() => {
        const counts = {
            running: 0,
            pending: 0,
            completed: 0,
            failed: 0,
            cancelled: 0,
        };
        for (const job of jobs) {
            const displayStatus = mapStatusToDisplay(job.status);
            counts[displayStatus]++;
        }
        return counts;
    }, [jobs]);

    const totalActiveExecutions = executionStatusCounts.running + executionStatusCounts.pending;

    const filteredExecutions = useMemo(() => {
        let items = [...jobs];

        if (executionFilters.template) {
            items = items.filter((e) => e.jobType === executionFilters.template);
        }

        if (executionFilters.status) {
            items = items.filter((e) => mapStatusToDisplay(e.status) === executionFilters.status);
        }

        items.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());

        return items;
    }, [jobs, executionFilters]);

    const hasActiveFilters = executionFilters.template !== '' || executionFilters.status !== '';

    const isLoading = isLoadingJobs || schedCrud.isLoading || eventCrud.isLoading;

    const { refetch: refetchSched } = schedCrud;
    const { refetch: refetchEvent } = eventCrud;
    const handleRefresh = useCallback(async () => {
        await Promise.all([refetchJobs(), refetchSched(), refetchEvent()]);
    }, [refetchJobs, refetchSched, refetchEvent]);

    const triggerFabActions: QuickAction[] = useMemo(
        () => [
            {
                id: 'add-schedule',
                label: 'Add Schedule',
                icon: <CalendarBlank weight="bold" />,
                onClick: schedCrud.handleAdd,
            },
            {
                id: 'add-event-trigger',
                label: 'Add Event Trigger',
                icon: <Lightning weight="bold" />,
                onClick: eventCrud.handleAdd,
            },
        ],
        [schedCrud.handleAdd, eventCrud.handleAdd],
    );

    return (
        <div className="jobs-page" data-page="jobs">
            <PullToRefresh onRefresh={handleRefresh} className="jobs-page__pull-to-refresh">
                <header className="jobs-page__header">
                    <div className="jobs-page__header-left">
                        <ClockCountdown
                            weight="duotone"
                            className="jobs-page__icon"
                            aria-hidden="true"
                            data-testid="jobs-page__icon"
                        />
                        <h1 className="jobs-page__title">Jobs</h1>
                        {!isLoading && (
                            <span className="page-count jobs-page__count" data-testid="jobs-page__count">
                                {titleCount}
                            </span>
                        )}
                    </div>
                </header>

                <Tabs value={activeTab} onValueChange={setActiveTab} className="jobs-page__tabs">
                    <TabsList>
                        <TabsTrigger value="executions">
                            <ClockCounterClockwise weight="bold" aria-hidden="true" />
                            <span>Executions</span>
                            {totalActiveExecutions > 0 && (
                                <span className="tab-count tab-count--running">{totalActiveExecutions}</span>
                            )}
                        </TabsTrigger>
                        <TabsTrigger value="triggers">
                            <Lightning weight="bold" aria-hidden="true" />
                            <span>Triggers</span>
                            {totalAllTriggers > 0 && <span className="tab-count">{totalAllTriggers}</span>}
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="executions">
                        <div className="list-toolbar__summary" aria-label="Executions by status">
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

                        <div className="jobs-page__list" role="list" aria-label="Job executions">
                            {filteredExecutions.length === 0 ? (
                                <EmptyState
                                    icon={<ClockCounterClockwise weight="duotone" />}
                                    title="No job executions found"
                                    description={
                                        hasActiveFilters
                                            ? 'Try adjusting your filters'
                                            : 'Executions will appear here when jobs are run'
                                    }
                                />
                            ) : (
                                filteredExecutions.map((execution) => (
                                    <ExecutionRow key={execution.id} execution={execution} />
                                ))
                            )}
                        </div>
                    </TabsContent>

                    <TabsContent value="triggers">
                        <div className="jobs-page__section-header">
                            <div className="list-toolbar__summary" aria-label="Triggers summary">
                                <Badge
                                    variant="completed"
                                    size="sm"
                                    badgeStyle="outline"
                                    dot
                                    count={enabledAllTriggers}
                                >
                                    Enabled
                                </Badge>
                                <Badge
                                    variant="pending"
                                    size="sm"
                                    badgeStyle="outline"
                                    dot
                                    count={totalAllTriggers - enabledAllTriggers}
                                >
                                    Disabled
                                </Badge>
                            </div>
                            {canManageTriggers && (
                                <div className="jobs-page__trigger-desktop-actions">
                                    <Button
                                        variant="primary"
                                        size="sm"
                                        onClick={schedCrud.handleAdd}
                                        data-action="add-schedule"
                                    >
                                        <CalendarBlank weight="bold" aria-hidden="true" />
                                        <span>Add Schedule</span>
                                    </Button>
                                    <Button
                                        variant="secondary"
                                        size="sm"
                                        onClick={eventCrud.handleAdd}
                                        data-action="add-event-trigger"
                                    >
                                        <Lightning weight="bold" aria-hidden="true" />
                                        <span>Add Event Trigger</span>
                                    </Button>
                                </div>
                            )}
                        </div>

                        <div className="jobs-page__list" role="list" aria-label="Triggers">
                            {totalAllTriggers === 0 ? (
                                <EmptyState
                                    icon={<Lightning weight="duotone" />}
                                    title="No triggers found"
                                    description={
                                        canManageTriggers
                                            ? 'Create a schedule or event trigger to run jobs automatically'
                                            : 'Triggers will appear here when configured'
                                    }
                                />
                            ) : (
                                <>
                                    <ScheduleTriggersTab
                                        crud={schedCrud}
                                        templates={templateOptions}
                                        canManage={canManageTriggers}
                                        canRun={canRunJobs}
                                    />
                                    <EventTriggersTab
                                        crud={eventCrud}
                                        templates={templateOptions}
                                        canManage={canManageTriggers}
                                    />
                                </>
                            )}
                        </div>
                    </TabsContent>
                </Tabs>
            </PullToRefresh>

            {canManageTriggers && activeTab === 'triggers' && (
                <QuickActionsButton actions={triggerFabActions} title="Add Trigger" className="jobs-page__fab" />
            )}
        </div>
    );
}

JobsPage.displayName = 'JobsPage';

export default JobsPage;
