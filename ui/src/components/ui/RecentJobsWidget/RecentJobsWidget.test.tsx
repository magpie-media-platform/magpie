import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { RecentJobsWidget, type JobExecution } from './RecentJobsWidget';

const renderWithRouter = (ui: React.ReactElement) => {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
};

const mockJobs: JobExecution[] = [
    { id: '1', name: 'Content metadata refresh item', status: 'completed', duration: '2m 15s', timestamp: '5 min ago' },
    { id: '2', name: 'Search Missing', status: 'running', duration: '45s', timestamp: '10 min ago' },
    { id: '3', name: 'Download import item', status: 'failed', duration: '1m 30s', timestamp: '15 min ago' },
    { id: '4', name: 'Sync Libraries', status: 'cancelled', timestamp: '1h ago' },
    { id: '5', name: 'Clean Up Files', status: 'completed', duration: '30s', timestamp: '2h ago' },
];

describe('RecentJobsWidget', () => {
    it('renders with default heading', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);
        expect(screen.getByRole('heading', { name: 'Recent Jobs' })).toBeInTheDocument();
    });

    it('renders with custom heading', () => {
        renderWithRouter(<RecentJobsWidget heading="Job History" jobs={mockJobs} />);
        expect(screen.getByRole('heading', { name: 'Job History' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        renderWithRouter(<RecentJobsWidget icon={<TestIcon />} jobs={mockJobs} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays job names', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        expect(screen.getByText('Content metadata refresh item')).toBeInTheDocument();
        expect(screen.getByText('Search Missing')).toBeInTheDocument();
        expect(screen.getByText('Download import item')).toBeInTheDocument();
    });

    it('displays job statuses', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        expect(screen.getAllByText('Completed').length).toBeGreaterThanOrEqual(1);
        expect(screen.getByText('Running')).toBeInTheDocument();
        expect(screen.getByText('Failed')).toBeInTheDocument();
        expect(screen.getByText('Cancelled')).toBeInTheDocument();
    });

    it('displays job durations when provided', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        expect(screen.getByText('2m 15s')).toBeInTheDocument();
        expect(screen.getByText('45s')).toBeInTheDocument();
        expect(screen.getByText('1m 30s')).toBeInTheDocument();
    });

    it('displays job timestamps', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        expect(screen.getByText('5 min ago')).toBeInTheDocument();
        expect(screen.getByText('10 min ago')).toBeInTheDocument();
        expect(screen.getByText('15 min ago')).toBeInTheDocument();
    });

    it('applies data-component attribute', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveAttribute('data-component', 'recent-jobs-widget');
    });

    it('applies custom className', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} className="custom-class" data-testid="widget" />);
        expect(screen.getByTestId('widget')).toHaveClass('recent-jobs-widget', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<RecentJobsWidget ref={ref} jobs={mockJobs} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} data-testid="my-widget" />);
        expect(screen.getByTestId('my-widget')).toBeInTheDocument();
    });
});

describe('RecentJobsWidget failed badge', () => {
    it('shows failed badge when there are failed jobs', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);
        expect(screen.getByText('1 failed')).toBeInTheDocument();
    });

    it('shows correct count when multiple jobs failed', () => {
        const jobs: JobExecution[] = [
            { id: '1', name: 'Job 1', status: 'failed', timestamp: '5m ago' },
            { id: '2', name: 'Job 2', status: 'failed', timestamp: '10m ago' },
            { id: '3', name: 'Job 3', status: 'completed', timestamp: '15m ago' },
        ];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);
        expect(screen.getByText('2 failed')).toBeInTheDocument();
    });

    it('does not show failed badge when no jobs failed', () => {
        const jobs: JobExecution[] = [
            { id: '1', name: 'Job 1', status: 'completed', timestamp: '5m ago' },
            { id: '2', name: 'Job 2', status: 'running', timestamp: '10m ago' },
        ];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);
        expect(screen.queryByText(/failed/)).not.toBeInTheDocument();
    });

    it('does not show failed badge when loading', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} loading />);
        expect(screen.queryByText('1 failed')).not.toBeInTheDocument();
    });

    it('has aria-label for failed badge', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);
        const badge = screen.getByText('1 failed');
        expect(badge).toHaveAttribute('aria-label', '1 failed job');
    });

    it('has plural aria-label for multiple failed jobs', () => {
        const jobs: JobExecution[] = [
            { id: '1', name: 'Job 1', status: 'failed', timestamp: '5m ago' },
            { id: '2', name: 'Job 2', status: 'failed', timestamp: '10m ago' },
        ];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);
        const badge = screen.getByText('2 failed');
        expect(badge).toHaveAttribute('aria-label', '2 failed jobs');
    });
});

describe('RecentJobsWidget maxItems', () => {
    it('limits displayed jobs to maxItems', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} maxItems={3} />);

        expect(screen.getByText('Content metadata refresh item')).toBeInTheDocument();
        expect(screen.getByText('Search Missing')).toBeInTheDocument();
        expect(screen.getByText('Download import item')).toBeInTheDocument();
        expect(screen.queryByText('Sync Libraries')).not.toBeInTheDocument();
        expect(screen.queryByText('Clean Up Files')).not.toBeInTheDocument();
    });

    it('defaults to 5 items', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        expect(screen.getByText('Content metadata refresh item')).toBeInTheDocument();
        expect(screen.getByText('Clean Up Files')).toBeInTheDocument();
    });

    it('shows all jobs when fewer than maxItems', () => {
        const fewJobs: JobExecution[] = [
            { id: '1', name: 'Job 1', status: 'completed', timestamp: '5m ago' },
            { id: '2', name: 'Job 2', status: 'running', timestamp: '10m ago' },
        ];
        renderWithRouter(<RecentJobsWidget jobs={fewJobs} maxItems={5} />);

        expect(screen.getByText('Job 1')).toBeInTheDocument();
        expect(screen.getByText('Job 2')).toBeInTheDocument();
    });
});

describe('RecentJobsWidget loading state', () => {
    it('shows skeleton items when loading', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} loading />);

        const skeletonItems = document.querySelectorAll('.recent-jobs-widget__item--skeleton');
        expect(skeletonItems.length).toBe(3);
    });

    it('does not show job names when loading', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} loading />);
        expect(screen.queryByText('Content metadata refresh item')).not.toBeInTheDocument();
    });

    it('does not show view all link when loading', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} loading viewAllUrl="/jobs" />);
        expect(screen.queryByText('View all jobs')).not.toBeInTheDocument();
    });
});

describe('RecentJobsWidget empty state', () => {
    it('shows empty state with icon and title when no jobs', () => {
        renderWithRouter(<RecentJobsWidget jobs={[]} />);
        expect(screen.getByText('No recent jobs')).toBeInTheDocument();
        expect(screen.getByText('Job executions will appear here')).toBeInTheDocument();
        expect(document.querySelector('[data-component="empty-state"]')).toBeInTheDocument();
    });

    it('shows view all link when empty and viewAllUrl is provided', () => {
        renderWithRouter(<RecentJobsWidget jobs={[]} viewAllUrl="/jobs" />);
        expect(screen.getByText('View all jobs')).toBeInTheDocument();
    });

    it('does not show view all link when empty and no viewAllUrl', () => {
        renderWithRouter(<RecentJobsWidget jobs={[]} />);
        expect(screen.queryByText('View all jobs')).not.toBeInTheDocument();
    });
});

describe('RecentJobsWidget navigation', () => {
    it('renders job items as links when getJobUrl is provided', () => {
        const getJobUrl = (job: JobExecution) => `/jobs/${job.id}`;

        renderWithRouter(<RecentJobsWidget jobs={mockJobs} getJobUrl={getJobUrl} />);

        const link = screen.getByText('Content metadata refresh item').closest('a');
        expect(link).toBeInTheDocument();
        expect(link).toHaveAttribute('href', '/jobs/1');
    });

    it('renders job items as divs when getJobUrl is not provided', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        const item = screen.getByText('Content metadata refresh item').closest('.recent-jobs-widget__item');
        expect(item?.tagName).toBe('DIV');
    });

    it('makes job item clickable when getJobUrl is provided', () => {
        const getJobUrl = (job: JobExecution) => `/jobs/${job.id}`;

        renderWithRouter(<RecentJobsWidget jobs={mockJobs} getJobUrl={getJobUrl} />);

        const item = screen.getByText('Content metadata refresh item').closest('.recent-jobs-widget__item');
        expect(item).toHaveAttribute('data-clickable');
    });

    it('renders view all as link when viewAllUrl is provided', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} viewAllUrl="/jobs" />);

        const link = screen.getByText('View all jobs');
        expect(link.tagName).toBe('A');
        expect(link).toHaveAttribute('href', '/jobs');
    });

    it('links have correct URLs for each job', () => {
        const getJobUrl = (job: JobExecution) => `/jobs/${job.id}`;

        renderWithRouter(<RecentJobsWidget jobs={mockJobs} getJobUrl={getJobUrl} />);

        const refreshLink = screen.getByText('Content metadata refresh item').closest('a');
        const searchLink = screen.getByText('Search Missing').closest('a');
        const importLink = screen.getByText('Download import item').closest('a');

        expect(refreshLink).toHaveAttribute('href', '/jobs/1');
        expect(searchLink).toHaveAttribute('href', '/jobs/2');
        expect(importLink).toHaveAttribute('href', '/jobs/3');
    });
});

describe('RecentJobsWidget view all link', () => {
    it('shows view all link when there are more jobs than maxItems', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} maxItems={3} viewAllUrl="/jobs" />);
        expect(screen.getByText('View all jobs')).toBeInTheDocument();
    });

    it('shows view all link when jobs exist and URL provided', () => {
        const fewJobs: JobExecution[] = [{ id: '1', name: 'Job 1', status: 'completed', timestamp: '5m ago' }];
        renderWithRouter(<RecentJobsWidget jobs={fewJobs} viewAllUrl="/jobs" />);
        expect(screen.getByText('View all jobs')).toBeInTheDocument();
    });

    it('hides view all link when no URL provided', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);
        expect(screen.queryByText('View all jobs')).not.toBeInTheDocument();
    });

    it('applies data-action attribute to view all link', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} viewAllUrl="/jobs" />);
        expect(screen.getByText('View all jobs')).toHaveAttribute('data-action', 'view-all');
    });
});

describe('RecentJobsWidget data attributes', () => {
    it('applies data-job-id to each job item', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        const items = document.querySelectorAll('[data-job-id]');
        expect(items[0]).toHaveAttribute('data-job-id', '1');
        expect(items[1]).toHaveAttribute('data-job-id', '2');
        expect(items[2]).toHaveAttribute('data-job-id', '3');
    });

    it('applies data-status to each job item', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        const items = document.querySelectorAll('.recent-jobs-widget__item:not(.recent-jobs-widget__item--skeleton)');
        expect(items[0]).toHaveAttribute('data-status', 'completed');
        expect(items[1]).toHaveAttribute('data-status', 'running');
        expect(items[2]).toHaveAttribute('data-status', 'failed');
        expect(items[3]).toHaveAttribute('data-status', 'cancelled');
    });
});

describe('RecentJobsWidget accessibility', () => {
    it('has accessible heading structure', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Recent Jobs' })).toBeInTheDocument();
    });

    it('clickable items have aria-label', () => {
        const getJobUrl = (job: JobExecution) => `/jobs/${job.id}`;
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} getJobUrl={getJobUrl} />);

        const item = screen.getByText('Content metadata refresh item').closest('.recent-jobs-widget__item');
        expect(item).toHaveAttribute('aria-label', 'View Content metadata refresh item job details');
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        renderWithRouter(<RecentJobsWidget icon={<TestIcon />} jobs={mockJobs} />);

        const iconWrapper = document.querySelector('.recent-jobs-widget__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('status indicators are hidden from screen readers', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        const indicators = document.querySelectorAll(
            '.recent-jobs-widget__indicator:not(.recent-jobs-widget__indicator--skeleton)',
        );
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('status labels have aria-label', () => {
        renderWithRouter(<RecentJobsWidget jobs={mockJobs} />);

        const completedStatus = screen.getAllByText('Completed')[0];
        expect(completedStatus).toHaveAttribute('aria-label', 'Status: Completed');
    });
});

describe('RecentJobsWidget status variants', () => {
    it('applies running variant class', () => {
        const jobs: JobExecution[] = [{ id: '1', name: 'Job', status: 'running', timestamp: '5m ago' }];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);

        const item = screen.getByText('Job').closest('.recent-jobs-widget__item');
        expect(item).toHaveAttribute('data-variant', 'running');
    });

    it('applies completed variant class', () => {
        const jobs: JobExecution[] = [{ id: '1', name: 'Job', status: 'completed', timestamp: '5m ago' }];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);

        const item = screen.getByText('Job').closest('.recent-jobs-widget__item');
        expect(item).toHaveAttribute('data-variant', 'completed');
    });

    it('applies failed variant class', () => {
        const jobs: JobExecution[] = [{ id: '1', name: 'Job', status: 'failed', timestamp: '5m ago' }];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);

        const item = screen.getByText('Job').closest('.recent-jobs-widget__item');
        expect(item).toHaveAttribute('data-variant', 'failed');
    });

    it('applies cancelled variant class', () => {
        const jobs: JobExecution[] = [{ id: '1', name: 'Job', status: 'cancelled', timestamp: '5m ago' }];
        renderWithRouter(<RecentJobsWidget jobs={jobs} />);

        const item = screen.getByText('Job').closest('.recent-jobs-widget__item');
        expect(item).toHaveAttribute('data-variant', 'cancelled');
    });
});
