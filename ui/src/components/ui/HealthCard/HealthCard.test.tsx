import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { HealthCard, type HealthStatusCounts } from './HealthCard';

function renderWithRouter(ui: React.ReactElement) {
    return render(ui, { wrapper: MemoryRouter });
}

const defaultCounts: HealthStatusCounts = {
    healthy: 5,
    unhealthy: 2,
    degraded: 3,
    stabilizing: 1,
    unknown: 1,
    stale: 0,
};

describe('HealthCard', () => {
    it('renders with heading', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);
        expect(screen.getByRole('heading', { name: 'Indexers' })).toBeInTheDocument();
    });

    it('renders icon when provided', () => {
        const TestIcon = () => <span data-testid="test-icon">Icon</span>;
        renderWithRouter(<HealthCard heading="Indexers" icon={<TestIcon />} counts={defaultCounts} />);
        expect(screen.getByTestId('test-icon')).toBeInTheDocument();
    });

    it('displays total count in header', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);

        expect(screen.getByText('12')).toBeInTheDocument();
    });

    it('displays rows for non-zero statuses', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);

        expect(screen.getByText('Healthy')).toBeInTheDocument();
        expect(screen.getByText('Unhealthy')).toBeInTheDocument();
        expect(screen.getByText('Degraded')).toBeInTheDocument();
        expect(screen.getByText('Stabilizing')).toBeInTheDocument();
        expect(screen.getByText('Unknown')).toBeInTheDocument();

        expect(screen.queryByText('Stale')).not.toBeInTheDocument();

        const healthyRow = screen.getByText('Healthy').closest('.health-card__row');
        expect(healthyRow).toHaveTextContent('5');

        const unhealthyRow = screen.getByText('Unhealthy').closest('.health-card__row');
        expect(unhealthyRow).toHaveTextContent('2');

        const degradedRow = screen.getByText('Degraded').closest('.health-card__row');
        expect(degradedRow).toHaveTextContent('3');

        const stabilizingRow = screen.getByText('Stabilizing').closest('.health-card__row');
        expect(stabilizingRow).toHaveTextContent('1');

        const unknownRow = screen.getByText('Unknown').closest('.health-card__row');
        expect(unknownRow).toHaveTextContent('1');
    });

    it('hides zero-count status rows', () => {
        renderWithRouter(
            <HealthCard
                heading="Indexers"
                counts={{ healthy: 3, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 }}
            />,
        );

        expect(screen.getByText('Healthy')).toBeInTheDocument();
        expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument();
        expect(screen.queryByText('Degraded')).not.toBeInTheDocument();
        expect(screen.queryByText('Stabilizing')).not.toBeInTheDocument();
        expect(screen.queryByText('Unknown')).not.toBeInTheDocument();
        expect(screen.queryByText('Stale')).not.toBeInTheDocument();
    });

    it('shows empty state when all counts are zero', () => {
        renderWithRouter(
            <HealthCard
                heading="Indexers"
                counts={{ healthy: 0, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 }}
            />,
        );

        expect(screen.getByText('None configured')).toBeInTheDocument();
        expect(screen.getByText('0')).toBeInTheDocument();
    });

    it('applies data-component attribute', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} data-testid="card" />);
        expect(screen.getByTestId('card')).toHaveAttribute('data-component', 'health-card');
    });

    it('applies custom className', () => {
        renderWithRouter(
            <HealthCard heading="Indexers" counts={defaultCounts} className="custom-class" data-testid="card" />,
        );
        expect(screen.getByTestId('card')).toHaveClass('health-card', 'custom-class');
    });

    it('forwards ref', () => {
        const ref = vi.fn();
        renderWithRouter(<HealthCard ref={ref} heading="Indexers" counts={defaultCounts} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLDivElement));
    });

    it('passes through additional props', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} data-testid="my-card" />);
        expect(screen.getByTestId('my-card')).toBeInTheDocument();
    });
});

describe('HealthCard loading state', () => {
    it('shows skeleton rows when loading', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} loading />);

        const skeletonRows = document.querySelectorAll('.health-card__row--skeleton');
        expect(skeletonRows.length).toBe(2);
    });

    it('shows dash for total when loading', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} loading />);
        expect(screen.getByText('-')).toBeInTheDocument();
    });

    it('does not show status labels when loading', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} loading />);
        expect(screen.queryByText('Healthy')).not.toBeInTheDocument();
    });
});

describe('HealthCard interactions', () => {
    const getStatusUrl = (status: string) => `/indexers?status=${status}`;

    it('renders status rows as links when getStatusUrl is provided', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        const healthyRow = screen.getByText('Healthy').closest('.health-card__row');
        expect(healthyRow?.tagName).toBe('A');
        expect(healthyRow).toHaveAttribute('href', '/indexers?status=healthy');
    });

    it('renders links with correct URLs for each status', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        expect(screen.getByText('Healthy').closest('a')).toHaveAttribute('href', '/indexers?status=healthy');
        expect(screen.getByText('Unhealthy').closest('a')).toHaveAttribute('href', '/indexers?status=unhealthy');
        expect(screen.getByText('Degraded').closest('a')).toHaveAttribute('href', '/indexers?status=degraded');
        expect(screen.getByText('Stabilizing').closest('a')).toHaveAttribute('href', '/indexers?status=stabilizing');
        expect(screen.getByText('Unknown').closest('a')).toHaveAttribute('href', '/indexers?status=unknown');
    });

    it('does not render rows with zero count', () => {
        renderWithRouter(
            <HealthCard
                heading="Indexers"
                counts={{ healthy: 1, unhealthy: 0, degraded: 0, stabilizing: 0, unknown: 0, stale: 0 }}
                getStatusUrl={getStatusUrl}
            />,
        );

        expect(screen.queryByText('Stale')).not.toBeInTheDocument();
        expect(screen.queryByText('Unhealthy')).not.toBeInTheDocument();
        expect(screen.queryByText('Degraded')).not.toBeInTheDocument();
        expect(screen.queryByText('Stabilizing')).not.toBeInTheDocument();
        expect(screen.queryByText('Unknown')).not.toBeInTheDocument();
    });

    it('makes row a link when count is greater than 0', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        const healthyRow = screen.getByText('Healthy').closest('.health-card__row');
        expect(healthyRow?.tagName).toBe('A');
        expect(healthyRow).toHaveAttribute('data-clickable');
    });

    it('renders status rows as divs without getStatusUrl', async () => {
        const user = userEvent.setup();

        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);

        const healthyRow = screen.getByText('Healthy').closest('.health-card__row');
        expect(healthyRow?.tagName).toBe('DIV');
        expect(healthyRow).not.toHaveAttribute('data-clickable');

        await user.click(healthyRow!);
    });
});

describe('HealthCard data attributes', () => {
    it('applies data-status to each rendered status row', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);

        expect(screen.getByText('Healthy').closest('.health-card__row')).toHaveAttribute('data-status', 'healthy');
        expect(screen.getByText('Unhealthy').closest('.health-card__row')).toHaveAttribute('data-status', 'unhealthy');
        expect(screen.getByText('Degraded').closest('.health-card__row')).toHaveAttribute('data-status', 'degraded');
        expect(screen.getByText('Stabilizing').closest('.health-card__row')).toHaveAttribute(
            'data-status',
            'stabilizing',
        );
        expect(screen.getByText('Unknown').closest('.health-card__row')).toHaveAttribute('data-status', 'unknown');
    });
});

describe('HealthCard accessibility', () => {
    const getStatusUrl = (status: string) => `/indexers?status=${status}`;

    it('has accessible heading structure', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);
        expect(screen.getByRole('heading', { level: 2, name: 'Indexers' })).toBeInTheDocument();
    });

    it('supports custom heading level', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} headingLevel={3} />);
        expect(screen.getByRole('heading', { level: 3, name: 'Indexers' })).toBeInTheDocument();
    });

    it('clickable rows have aria-label', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} getStatusUrl={getStatusUrl} />);

        const healthyRow = screen.getByText('Healthy').closest('.health-card__row');
        expect(healthyRow).toHaveAttribute('aria-label', 'View 5 healthy items');
    });

    it('icon is hidden from screen readers', () => {
        const TestIcon = () => <span>Icon</span>;
        renderWithRouter(<HealthCard heading="Indexers" icon={<TestIcon />} counts={defaultCounts} />);

        const iconWrapper = document.querySelector('.health-card__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('status indicators are hidden from screen readers', () => {
        renderWithRouter(<HealthCard heading="Indexers" counts={defaultCounts} />);

        const indicators = document.querySelectorAll('.health-card__indicator');
        indicators.forEach((indicator) => {
            expect(indicator).toHaveAttribute('aria-hidden', 'true');
        });
    });
});
