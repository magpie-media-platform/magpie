import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { HouseIcon } from '@phosphor-icons/react';

import { PageHeader } from './PageHeader';

describe('PageHeader', () => {
    it('renders the title as a level-1 heading', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." />);
        expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeInTheDocument();
    });

    it('renders the description text', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." />);
        expect(screen.getByText('At-a-glance system view.')).toBeInTheDocument();
    });

    it('associates the title to the description via aria-describedby', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." />);
        const heading = screen.getByRole('heading', { level: 1, name: 'Dashboard' });
        const descriptionId = heading.getAttribute('aria-describedby');
        expect(descriptionId).toBeTruthy();

        const description = screen.getByTestId('page-header__description');
        expect(description).toHaveAttribute('id', descriptionId);
    });

    it('renders icon as decorative when provided', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." icon={<HouseIcon />} />);
        const iconWrapper = screen.getByTestId('page-header__icon');
        expect(iconWrapper).toHaveAttribute('aria-hidden', 'true');
    });

    it('does not render an icon wrapper when no icon is provided', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." />);
        expect(screen.queryByTestId('page-header__icon')).not.toBeInTheDocument();
    });

    it('renders titleAdornment when provided', () => {
        render(
            <PageHeader
                title="Indexers"
                description="Configured indexers."
                titleAdornment={<span data-testid="adornment">4</span>}
            />,
        );
        expect(screen.getByTestId('adornment')).toBeInTheDocument();
    });

    it('renders actions when provided', () => {
        render(
            <PageHeader
                title="Indexers"
                description="Configured indexers."
                actions={<button type="button">Add</button>}
            />,
        );
        expect(screen.getByRole('button', { name: 'Add' })).toBeInTheDocument();
    });

    it('uses the header element with data-component="page-header"', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." />);
        const header = document.querySelector('[data-component="page-header"]');
        expect(header).toBeInTheDocument();
        expect(header?.tagName).toBe('HEADER');
    });

    it('applies custom className', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." className="custom-class" />);
        const header = document.querySelector('[data-component="page-header"]');
        expect(header).toHaveClass('page-header');
        expect(header).toHaveClass('custom-class');
    });

    it('forwards ref to the header element', () => {
        const ref = vi.fn();
        render(<PageHeader ref={ref} title="Dashboard" description="At-a-glance system view." />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('passes through additional props', () => {
        render(<PageHeader title="Dashboard" description="At-a-glance system view." data-page="dashboard" />);
        expect(document.querySelector('[data-page="dashboard"]')).toBeInTheDocument();
    });
});
