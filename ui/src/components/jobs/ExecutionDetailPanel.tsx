import { useState } from 'react';
import {
    CheckCircleIcon,
    XCircleIcon,
    ListBulletsIcon,
    WarningIcon,
    InfoIcon,
    BugIcon,
    TreeStructureIcon,
} from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { RelatedEventsChain } from '@/components/activity/RelatedEventsModal';
import type { ApiJobExecution } from '@/hooks/useJobs';
import { mapStatusToDisplay, formatLogTime, type JobExecutionStatusDisplay } from '@/lib/jobExecution';

import './ExecutionDetailPanel.css';

interface ExecutionDetailPanelProps {
    execution: ApiJobExecution;

    active?: boolean;
}

export function ExecutionDetailPanel({ execution, active = true }: ExecutionDetailPanelProps) {
    const [detailTab, setDetailTab] = useState('details');

    const displayStatus = mapStatusToDisplay(execution.status, execution.notBeforeAt);

    return (
        <Tabs value={detailTab} onValueChange={setDetailTab} className="execution-row__detail-tabs">
            <TabsList className="execution-row__detail-tabs-list">
                <TabsTrigger value="details" className="execution-row__detail-tab">
                    <ListBulletsIcon weight="bold" aria-hidden="true" />
                    <span>Execution details</span>
                </TabsTrigger>
                <TabsTrigger value="audit" className="execution-row__detail-tab">
                    <TreeStructureIcon weight="bold" aria-hidden="true" />
                    <span>Execution trace</span>
                </TabsTrigger>
            </TabsList>

            <TabsContent value="details">
                <div
                    className="execution-row__logs-list"
                    role="log"
                    aria-label={`${execution.jobType} execution details`}
                    data-testid="execution-row__logs-list"
                >
                    <StaticExecutionDetail execution={execution} displayStatus={displayStatus} />
                </div>
            </TabsContent>

            <TabsContent value="audit" data-testid="execution-row__audit">
                <RelatedEventsChain executionId={execution.id} active={active && detailTab === 'audit'} />
            </TabsContent>
        </Tabs>
    );
}

ExecutionDetailPanel.displayName = 'ExecutionDetailPanel';

type DisplayStatus = JobExecutionStatusDisplay;

interface StaticExecutionDetailProps {
    execution: ApiJobExecution;
    displayStatus: DisplayStatus;
}

function StaticExecutionDetail({ execution, displayStatus }: StaticExecutionDetailProps) {
    return (
        <>
            <div className="execution-row__log-entry execution-row__log-entry--info">
                <span className="execution-row__log-time">{formatLogTime(execution.createdAt)}</span>
                <span className="execution-row__log-level">
                    <InfoIcon weight="bold" aria-hidden="true" />
                </span>
                <span className="execution-row__log-message">Created</span>
            </div>
            {execution.startedAt && (
                <div className="execution-row__log-entry execution-row__log-entry--info">
                    <span className="execution-row__log-time">{formatLogTime(execution.startedAt)}</span>
                    <span className="execution-row__log-level">
                        <InfoIcon weight="bold" aria-hidden="true" />
                    </span>
                    <span className="execution-row__log-message">Started</span>
                </div>
            )}
            {execution.statusMessage && (
                <div
                    className={`execution-row__log-entry execution-row__log-entry--${displayStatus === 'failed' ? 'error' : 'info'}`}
                >
                    <span className="execution-row__log-time">
                        {formatLogTime(execution.completedAt ?? execution.createdAt)}
                    </span>
                    <span className="execution-row__log-level">
                        {displayStatus === 'failed' ? (
                            <XCircleIcon weight="bold" aria-hidden="true" />
                        ) : displayStatus === 'running' ? (
                            <BugIcon weight="bold" aria-hidden="true" />
                        ) : (
                            <InfoIcon weight="bold" aria-hidden="true" />
                        )}
                    </span>
                    <span className="execution-row__log-message">{execution.statusMessage}</span>
                </div>
            )}
            {execution.completedAt && (
                <div
                    className={`execution-row__log-entry execution-row__log-entry--${displayStatus === 'completed' ? 'info' : displayStatus === 'failed' ? 'error' : 'warn'}`}
                >
                    <span className="execution-row__log-time">{formatLogTime(execution.completedAt)}</span>
                    <span className="execution-row__log-level">
                        {displayStatus === 'completed' ? (
                            <CheckCircleIcon weight="bold" aria-hidden="true" />
                        ) : displayStatus === 'failed' ? (
                            <XCircleIcon weight="bold" aria-hidden="true" />
                        ) : (
                            <WarningIcon weight="bold" aria-hidden="true" />
                        )}
                    </span>
                    <span className="execution-row__log-message">
                        {displayStatus === 'completed'
                            ? 'Completed successfully'
                            : displayStatus === 'failed'
                              ? 'Failed'
                              : displayStatus === 'cancelled'
                                ? 'Cancelled'
                                : 'Finished'}
                    </span>
                </div>
            )}
        </>
    );
}

export default ExecutionDetailPanel;
