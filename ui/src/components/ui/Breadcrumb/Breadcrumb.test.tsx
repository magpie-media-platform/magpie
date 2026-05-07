import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { House } from '@phosphor-icons/react';

import { Breadcrumb, type BreadcrumbItem } from './Breadcrumb';

const renderWithRouter = (ui: React.ReactElement) => {
    return render(<MemoryRouter>{ui}</MemoryRouter>);
};

describe('Breadcrumb', () => {
    const sampleItems: BreadcrumbItem[] = [
        { id: 'library', label: 'Library', to: '/library' },
        { id: 'show', label: 'Breaking Bad', to: '/library/content-1' },
        { id: 'season', label: 'Season 1', to: '/library/content-1/season-1' },
    ];

    it('renders nothing when items array is empty', () => {
        const { container } = renderWithRouter(<Breadcrumb items={[]} />);
        expect(container.querySelector('.breadcrumb')).not.toBeInTheDocument();
    });

    it('renders breadcrumb navigation with proper aria-label', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);
        const nav = screen.getByRole('navigation', { name: 'Breadcrumb' });
        expect(nav).toBeInTheDocument();
    });

    it('renders all breadcrumb items', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);

        expect(screen.getAllByText('Library').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Breaking Bad').length).toBeGreaterThanOrEqual(1);
        expect(screen.getAllByText('Season 1').length).toBeGreaterThanOrEqual(1);
    });

    it('renders links for non-current items', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);

        const libraryLinks = screen.getAllByRole('link', { name: /Library/ });
        const showLinks = screen.getAllByRole('link', { name: /Breaking Bad/ });

        expect(libraryLinks.some((link) => link.getAttribute('href') === '/library')).toBe(true);
        expect(showLinks.some((link) => link.getAttribute('href') === '/library/content-1')).toBe(true);
    });

    it('marks the last item as current page', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);
        const currentItem = screen.getByText('Season 1').closest('[aria-current="page"]');
        expect(currentItem).toBeInTheDocument();
    });

    it('renders separators between items', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);
        const separators = document.querySelectorAll('.breadcrumb__separator');

        expect(separators.length).toBe(2);
    });

    it('renders custom separator when provided', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} separator={<span data-testid="custom-separator">/</span>} />);
        const customSeparators = screen.getAllByTestId('custom-separator');
        expect(customSeparators.length).toBe(2);
    });

    it('renders mobile back link with parent label', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);

        const backLink = document.querySelector('.breadcrumb__back-link');
        expect(backLink).toBeInTheDocument();
        expect(backLink).toHaveTextContent('Breaking Bad');
    });

    it('renders home icon when showHomeIcon is true', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} showHomeIcon />);
        const homeIcon = document.querySelector('.breadcrumb__icon');
        expect(homeIcon).toBeInTheDocument();
    });

    it('does not render home icon by default', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);

        const firstLink = screen.getByRole('link', { name: /Library/ });
        const homeIcon = firstLink.querySelector('.breadcrumb__icon');

        expect(homeIcon).not.toBeInTheDocument();
    });

    it('renders item icons when provided', () => {
        const itemsWithIcon: BreadcrumbItem[] = [
            { id: 'home', label: 'Home', to: '/', icon: <House data-testid="home-icon" /> },
            { id: 'page', label: 'Page', to: '/page' },
        ];
        renderWithRouter(<Breadcrumb items={itemsWithIcon} />);
        expect(screen.getByTestId('home-icon')).toBeInTheDocument();
    });

    it('collapses items when maxItems is set', () => {
        const manyItems: BreadcrumbItem[] = [
            { id: '1', label: 'Level 1', to: '/1' },
            { id: '2', label: 'Level 2', to: '/1/2' },
            { id: '3', label: 'Level 3', to: '/1/2/3' },
            { id: '4', label: 'Level 4', to: '/1/2/3/4' },
            { id: '5', label: 'Level 5', to: '/1/2/3/4/5' },
        ];
        renderWithRouter(<Breadcrumb items={manyItems} maxItems={3} />);

        const ellipsis = document.querySelector('.breadcrumb__ellipsis');
        expect(ellipsis).toBeInTheDocument();

        const breadcrumbList = document.querySelector('.breadcrumb__list');
        expect(breadcrumbList).toHaveTextContent('Level 3');
        expect(breadcrumbList).toHaveTextContent('Level 4');
        expect(breadcrumbList).toHaveTextContent('Level 5');
    });

    it('does not collapse when maxItems is 0', () => {
        const manyItems: BreadcrumbItem[] = [
            { id: '1', label: 'Level 1', to: '/1' },
            { id: '2', label: 'Level 2', to: '/1/2' },
            { id: '3', label: 'Level 3', to: '/1/2/3' },
        ];
        renderWithRouter(<Breadcrumb items={manyItems} maxItems={0} />);

        const breadcrumbList = document.querySelector('.breadcrumb__list');
        expect(breadcrumbList).toHaveTextContent('Level 1');
        expect(breadcrumbList).toHaveTextContent('Level 2');
        expect(breadcrumbList).toHaveTextContent('Level 3');

        const ellipsis = document.querySelector('.breadcrumb__ellipsis');
        expect(ellipsis).not.toBeInTheDocument();
    });

    it('forwards ref to nav element', () => {
        const ref = vi.fn();
        renderWithRouter(<Breadcrumb items={sampleItems} ref={ref} />);
        expect(ref).toHaveBeenCalledWith(expect.any(HTMLElement));
    });

    it('applies custom className', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} className="custom-breadcrumb" />);
        const nav = screen.getByRole('navigation', { name: 'Breadcrumb' });
        expect(nav).toHaveClass('breadcrumb');
        expect(nav).toHaveClass('custom-breadcrumb');
    });

    it('passes through additional props', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} data-testid="my-breadcrumb" />);
        expect(screen.getByTestId('my-breadcrumb')).toBeInTheDocument();
    });

    it('handles single item (no parent for back link)', () => {
        const singleItem: BreadcrumbItem[] = [{ id: 'library', label: 'Library', to: '/library' }];
        renderWithRouter(<Breadcrumb items={singleItem} />);

        expect(screen.getAllByText('Library').length).toBeGreaterThanOrEqual(1);

        const backLinkNoLink = document.querySelector('.breadcrumb__back-link--no-link');
        expect(backLinkNoLink).toBeInTheDocument();
    });

    it('renders skeleton placeholder when item is loading', () => {
        const loadingItems: BreadcrumbItem[] = [
            { id: 'library', label: 'Library', to: '/library' },
            { id: 'show', label: '', loading: true, to: '/library/content-1' },
            { id: 'season', label: '', loading: true, to: '/library/content-1/season-1' },
        ];
        renderWithRouter(<Breadcrumb items={loadingItems} />);
        const skeletons = document.querySelectorAll('[data-component="skeleton"]');

        expect(skeletons.length).toBe(3);
    });

    it('does not render skeleton when item is not loading', () => {
        renderWithRouter(<Breadcrumb items={sampleItems} />);
        const skeletons = document.querySelectorAll('[data-component="skeleton"]');
        expect(skeletons.length).toBe(0);
    });

    it('truncates long labels with ellipsis via CSS class', () => {
        const longLabelItems: BreadcrumbItem[] = [
            { id: 'long', label: 'A Very Long Label That Should Be Truncated', to: '/long' },
        ];
        renderWithRouter(<Breadcrumb items={longLabelItems} />);
        const label = document.querySelector('.breadcrumb__label');
        expect(label).toBeInTheDocument();
    });
});
