import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, MemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { TooltipProvider } from '@/components/ui/Tooltip';
import { apiGet } from '@/lib/api';

import { ChildrenTab, type ChildItem } from './ChildrenTab';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
}));

const ITEMS: ChildItem[] = Array.from({ length: 51 }, (_, index) => ({
    id: `child-${index + 1}`,
    title: `Episode ${index + 1}`,
    number: index + 1,
    effectiveMonitoring: 'Monitored',
    fulfillment: 'fulfilled',
}));
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });

beforeEach(() => {
    localStorage.clear();
    queryClient.clear();
    vi.mocked(apiGet).mockResolvedValue([]);
});

afterEach(() => {
    vi.unstubAllGlobals();
});

describe('ChildrenTab pagination', () => {
    it('UrlState_PreservesTheOriginalOrder_WhenClearingSorting', async () => {
        // Arrange
        vi.stubGlobal('innerWidth', 1440);
        const user = userEvent.setup();
        const router = createMemoryRouter(
            [{ path: '/', element: <ChildrenTab items={[ITEMS[1], ITEMS[0]]} parentId="content-1" /> }],
            { initialEntries: ['/?childrenSort=title:desc'] },
        );
        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <RouterProvider router={router} />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Sort by Title' }));

        // Assert
        expect(new URLSearchParams(router.state.location.search).get('childrenSort')).toBe('none');
        expect(document.querySelector('tbody tr')).toHaveTextContent('Episode 2');

        // Act
        await act(() => router.navigate('/?childrenSort=number:asc'));

        // Assert
        expect(document.querySelector('tbody tr')).toHaveTextContent('Episode 1');

        // Act
        await act(() => router.navigate(-1));

        // Assert
        expect(document.querySelector('tbody tr')).toHaveTextContent('Episode 2');
    });

    it.each([390, 1440])('UrlState_RestoresSortAndPage_WhenNavigatingAtWidth%i', async (width) => {
        // Arrange
        vi.stubGlobal('innerWidth', width);
        const user = userEvent.setup();
        const router = createMemoryRouter(
            [{ path: '/', element: <ChildrenTab items={ITEMS} parentId="content-1" /> }],
            { initialEntries: ['/?childrenPage=2&childrenSort=number:desc&releasesPage=3'] },
        );
        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <RouterProvider router={router} />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 1')).toBeInTheDocument();
        expect(screen.queryByText('Episode 51')).not.toBeInTheDocument();

        // Act
        await act(() => router.navigate('/?childrenPage=2&childrenSort=number:asc&releasesPage=3'));

        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 51')).toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: 'Go to previous page' }));

        // Assert
        expect(new URLSearchParams(router.state.location.search).has('childrenPage')).toBe(false);
        expect(new URLSearchParams(router.state.location.search).get('releasesPage')).toBe('3');

        // Act
        await act(() => router.navigate(-1));

        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 51')).toBeInTheDocument();

        // Act
        await act(() => router.navigate(-1));

        // Assert
        expect(screen.getByText('Episode 1')).toBeInTheDocument();
    });

    it.each([390, 1440])('Pagination_UsesList_WhenGalleryWasSelectedAtWidth%i', async (width) => {
        // Arrange
        vi.stubGlobal('innerWidth', width);
        localStorage.setItem('children-view', '"gallery"');
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={queryClient}>
                <TooltipProvider>
                    <MemoryRouter initialEntries={['/?view=gallery&childrenPage=2']}>
                        <ChildrenTab items={ITEMS} parentId="content-1" />
                    </MemoryRouter>
                </TooltipProvider>
            </QueryClientProvider>,
        );
        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 51')).toBeInTheDocument();
        expect(screen.queryByText('Episode 1')).not.toBeInTheDocument();
        expect(screen.queryByRole('tab', { name: 'Gallery' })).not.toBeInTheDocument();
        expect(screen.queryByRole('list', { name: 'Content gallery' })).not.toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: 'Go to previous page' }));

        // Assert
        expect(screen.getByText('Page 1 of 2')).toBeInTheDocument();
        expect(screen.getByText('Episode 1')).toBeInTheDocument();
        expect(screen.queryByText('Episode 51')).not.toBeInTheDocument();
    });
});
