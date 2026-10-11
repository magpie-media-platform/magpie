import { fireEvent, render, screen } from '@testing-library/react';
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
            effectiveMonitoring: 'Monitored',
            fulfillment: 'fulfilled',
        },
    ];

    beforeEach(() => {
        sortingProps.length = 0;
        vi.stubGlobal('matchMedia', createMatchMedia(1024));
    });

    it('replaces failed child artwork in the card layout', () => {
        // Arrange
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={[{ ...items[0], posterUrl: '/broken.jpg' }]} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        // Act
        fireEvent.error(screen.getByAltText('Season 1 artwork'));

        // Assert
        expect(screen.queryByAltText('Season 1 artwork')).not.toBeInTheDocument();
        expect(document.querySelector('.children-tab__poster--placeholder')).toBeInTheDocument();
    });

    it('ChildrenTab_ShowsNoPosterPlaceholder_WhenChildHasNoArtworkInCardLayout', () => {
        // Arrange
        vi.stubGlobal('matchMedia', createMatchMedia(390));

        // Act
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={items} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        // Assert
        expect(screen.getByRole('link', { name: /Season 1/ })).toBeInTheDocument();
        expect(document.querySelector('.children-tab__poster')).not.toBeInTheDocument();
    });

    it('ChildrenTab_KeepsTitlesAligned_WhenSomeChildrenHaveArtworkInCardLayout', () => {
        // Arrange
        vi.stubGlobal('matchMedia', createMatchMedia(390));
        const children = [
            { ...items[0], posterUrl: '/season-1.jpg' },
            { ...items[0], id: 'child-2', title: 'Season 2', number: 2 },
        ];

        // Act
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={children} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        // Assert
        expect(screen.getAllByAltText('Season 1 artwork')).toHaveLength(1);
        expect(document.querySelectorAll('.children-tab__poster--spacer')).toHaveLength(1);
        expect(document.querySelector('.children-tab__poster--placeholder')).not.toBeInTheDocument();
    });

    it('uses a singular primary-media file label in the card layout', () => {
        // Arrange
        vi.stubGlobal('matchMedia', createMatchMedia(390));

        // Act
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab
                        items={[{ ...items[0], primaryMediaFileCount: 1, primaryMediaSize: 1024 }]}
                        parentId="content-1"
                    />
                </MemoryRouter>
            </TooltipProvider>,
        );

        // Assert
        expect(screen.getByText('Media: 1 file · 1 KB')).toBeInTheDocument();
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
        effectiveMonitoring: 'Monitored',
        fulfillment: 'fulfilled' as const,
    }));

    beforeEach(() => {
        vi.stubGlobal('matchMedia', createMatchMedia(390));
    });

    it('Pagination_ShowsExactResultCount_WhenChildrenUseCardLayout', () => {
        // Arrange
        render(
            <TooltipProvider>
                <MemoryRouter>
                    <ChildrenTab items={manyItems} parentId="content-1" />
                </MemoryRouter>
            </TooltipProvider>,
        );

        // Act
        const pager = screen.getByRole('navigation', { name: 'Pagination' });

        // Assert
        expect(pager).toHaveTextContent('Page 1 of 2');
        expect(pager).toHaveTextContent('51 results');
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

        await user.click(screen.getByRole('button', { name: 'Sort descending' }));

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
