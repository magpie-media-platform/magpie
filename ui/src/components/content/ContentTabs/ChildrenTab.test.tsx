import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { SortingState } from '@tanstack/react-table';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { ChildrenTab, type ChildItem } from './ChildrenTab';

const sortingProps: SortingState[] = [];

vi.mock('@/components/ui/DataTable', () => ({
    DataTable: ({ sorting }: { sorting?: SortingState }) => {
        if (sorting !== undefined) {
            sortingProps.push(sorting);
        }
        return <div data-testid="mock-data-table" />;
    },
}));

function createMatchMedia(width: number) {
    return vi.fn((query: string) => {
        const minWidth = query.match(/\(min-width:\s*(\d+)px\)/);
        const maxWidth = query.match(/\(max-width:\s*(\d+)px\)/);
        return {
            matches:
                (minWidth === null || width >= Number(minWidth[1])) &&
                (maxWidth === null || width <= Number(maxWidth[1])),
            media: query,
            onchange: null,
            addListener: () => {},
            removeListener: () => {},
            addEventListener: () => {},
            removeEventListener: () => {},
            dispatchEvent: () => false,
        };
    });
}

describe('ChildrenTab controlled sorting stability', () => {
    const items: ChildItem[] = [
        {
            id: 'child-1',
            title: 'Season 1',
            number: 1,
            monitoring: true,
            effectiveMonitoring: true,
            fulfillment: 'fulfilled',
        },
    ];

    beforeEach(() => {
        sortingProps.length = 0;
        vi.stubGlobal('matchMedia', createMatchMedia(1024));
    });

    it('keeps Number-sort controlled sorting referentially stable across re-renders', () => {
        const view = render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={items} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        expect(sortingProps.length).toBeGreaterThan(0);
        const firstSorting = sortingProps[sortingProps.length - 1];
        expect(firstSorting).toEqual([]);

        sortingProps.length = 0;
        view.rerender(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={[...items]} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        expect(sortingProps.length).toBeGreaterThan(0);
        const nextSorting = sortingProps[sortingProps.length - 1];
        expect(nextSorting).toEqual([]);
        expect(nextSorting).toBe(firstSorting);
    });
});

describe('ChildrenTab card-layout pagination', () => {
    const manyItems: ChildItem[] = Array.from({ length: 51 }, (_, index) => ({
        id: `child-${index + 1}`,
        title: `Episode ${index + 1}`,
        number: index + 1,
        monitoring: true,
        effectiveMonitoring: true,
        fulfillment: 'fulfilled' as const,
    }));

    beforeEach(() => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
    });

    it('pages the card list and reaches children beyond the first page', async () => {
        const user = userEvent.setup();
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={manyItems} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        expect(screen.getAllByRole('link')).toHaveLength(50);
        expect(screen.queryByText('Episode 51')).not.toBeInTheDocument();
        expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Go to next page' }));

        expect(screen.getAllByRole('link')).toHaveLength(1);
        expect(screen.getByText('Episode 51')).toBeInTheDocument();
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
    });

    it('returns to the first page when the parent changes', async () => {
        const user = userEvent.setup();
        const view = render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={manyItems} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Go to next page' }));
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();

        view.rerender(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={manyItems} parentId="content-2" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 1')).toBeInTheDocument();
    });

    it('returns to the first page when the sort direction changes', async () => {
        const user = userEvent.setup();
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={manyItems} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        await user.click(screen.getByRole('button', { name: 'Go to next page' }));
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Sort children by Number descending' }));

        expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 51')).toBeInTheDocument();
    });

    it('renders no pager when children fit on one page', () => {
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={manyItems.slice(0, 50)} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        expect(screen.getAllByRole('link')).toHaveLength(50);
        expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
    });
});
