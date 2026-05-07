import { forwardRef, memo, type HTMLAttributes, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { ArrowCounterClockwise, Timer, WarningCircle } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

import './RecentJobsWidget.css';

export type JobStatus = 'running' | 'completed' | 'failed' | 'cancelled';

export interface JobExecution {
    id: string;
    name: string;
    status: JobStatus;

    duration?: string;

    timestamp: string;
}

export interface RecentJobsWidgetProps extends HTMLAttributes<HTMLDivElement> {
    heading?: string;

    icon?: ReactNode;

    jobs: JobExecution[];

    maxItems?: number;

    loading?: boolean;

    error?: Error | null;

    onRetry?: () => void;

    getJobUrl?: (job: JobExecution) => string;

    viewAllUrl?: string;

    headingLevel?: 2 | 3;
}

const statusVariantMap: Record<JobStatus, string> = {
    running: 'running',
    completed: 'completed',
    failed: 'failed',
    cancelled: 'cancelled',
};

const statusLabelMap: Record<JobStatus, string> = {
    running: 'Running',
    completed: 'Completed',
    failed: 'Failed',
    cancelled: 'Cancelled',
};

interface JobItemProps {
    job: JobExecution;
    url?: string;
}

const JobItem = ({ job, url }: JobItemProps) => {
    const isClickable = url !== undefined;
    const variant = statusVariantMap[job.status];
    const statusLabel = statusLabelMap[job.status];

    const content = (
        <>
            <div className="recent-jobs-widget__item-main">
                <span className="recent-jobs-widget__indicator" aria-hidden="true" />
                <span className="recent-jobs-widget__item-name">{job.name}</span>
                <span className="recent-jobs-widget__item-status" aria-label={`Status: ${statusLabel}`}>
                    {statusLabel}
                </span>
            </div>
            <div className="recent-jobs-widget__item-meta">
                {job.duration && <span className="recent-jobs-widget__item-duration">{job.duration}</span>}
                <span className="recent-jobs-widget__item-timestamp">{job.timestamp}</span>
            </div>
        </>
    );

    if (isClickable && url) {
        return (
            <Link
                to={url}
                className={`recent-jobs-widget__item recent-jobs-widget__item--${variant} recent-jobs-widget__item--clickable`}
                aria-label={`View ${job.name} job details`}
                data-job-id={job.id}
                data-status={job.status}
                data-variant={variant}
                data-clickable
            >
                {content}
            </Link>
        );
    }

    return (
        <div
            className={`recent-jobs-widget__item recent-jobs-widget__item--${variant}`}
            data-job-id={job.id}
            data-status={job.status}
            data-variant={variant}
        >
            {content}
        </div>
    );
};

const SkeletonItem = () => (
    <div
        className="recent-jobs-widget__item recent-jobs-widget__item--skeleton"
        data-testid="recent-jobs-widget__item--skeleton"
    >
        <div className="recent-jobs-widget__item-main">
            <span className="recent-jobs-widget__indicator recent-jobs-widget__indicator--skeleton" />
            <span className="recent-jobs-widget__item-name recent-jobs-widget__item-name--skeleton" />
            <span className="recent-jobs-widget__item-status recent-jobs-widget__item-status--skeleton" />
        </div>
        <div className="recent-jobs-widget__item-meta">
            <span className="recent-jobs-widget__item-duration recent-jobs-widget__item-duration--skeleton" />
            <span className="recent-jobs-widget__item-timestamp recent-jobs-widget__item-timestamp--skeleton" />
        </div>
    </div>
);

const RecentJobsWidgetBase = forwardRef<HTMLDivElement, RecentJobsWidgetProps>(
    (
        {
            heading = 'Recent Jobs',
            icon,
            jobs,
            maxItems = 5,
            loading = false,
            error = null,
            onRetry,
            getJobUrl,
            viewAllUrl,
            headingLevel = 2,
            className,
            ...props
        },
        ref,
    ) => {
        const classes = ['recent-jobs-widget', className].filter(Boolean).join(' ');
        const displayedJobs = jobs.slice(0, maxItems);
        const failedCount = jobs.filter((j) => j.status === 'failed').length;
        const HeadingTag = `h${headingLevel}` as const;

        return (
            <div ref={ref} className={classes} data-component="recent-jobs-widget" {...props}>
                <div className="recent-jobs-widget__header">
                    {icon && (
                        <span className="recent-jobs-widget__icon" aria-hidden="true">
                            {icon}
                        </span>
                    )}
                    <HeadingTag className="recent-jobs-widget__title">{heading}</HeadingTag>
                    {failedCount > 0 && !loading && (
                        <span
                            className="recent-jobs-widget__failed-badge"
                            aria-label={`${failedCount} failed job${failedCount !== 1 ? 's' : ''}`}
                        >
                            {failedCount} failed
                        </span>
                    )}
                </div>

                <div className="recent-jobs-widget__body">
                    {loading ? (
                        <>
                            <SkeletonItem />
                            <SkeletonItem />
                            <SkeletonItem />
                        </>
                    ) : error ? (
                        <Alert variant="error" icon={<WarningCircle weight="duotone" />} title="Failed to load">
                            {onRetry && (
                                <Button variant="ghost" size="sm" onClick={onRetry}>
                                    <ArrowCounterClockwise weight="bold" aria-hidden="true" />
                                    Try again
                                </Button>
                            )}
                        </Alert>
                    ) : displayedJobs.length > 0 ? (
                        displayedJobs.map((job) => <JobItem key={job.id} job={job} url={getJobUrl?.(job)} />)
                    ) : (
                        <EmptyState
                            icon={<Timer weight="duotone" />}
                            title="No recent jobs"
                            description="Job executions will appear here"
                            size="sm"
                        />
                    )}
                </div>

                {viewAllUrl && !loading && (
                    <div className="recent-jobs-widget__footer">
                        <Link to={viewAllUrl} className="recent-jobs-widget__view-all" data-action="view-all">
                            View all jobs
                        </Link>
                    </div>
                )}
            </div>
        );
    },
);

RecentJobsWidgetBase.displayName = 'RecentJobsWidget';

export const RecentJobsWidget = memo(RecentJobsWidgetBase);

export default RecentJobsWidget;
