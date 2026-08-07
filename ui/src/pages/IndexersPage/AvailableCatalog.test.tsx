import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastList, ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';
import type { ApiIndexer } from '@/hooks/useIndexers';

import { AvailableCatalog } from './AvailableCatalog';

const mockIndexers: ApiIndexer[] = [
    {
        id: 'idx-1',
        name: 'NZBgeek',
        providerType: 'newznab',
        availability: 'Public',
        categories: ['Movies', 'TV'],
        languages: ['en'],
        connectionCount: 0,
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'idx-2',
        name: 'IPTorrents',
        providerType: 'torznab',
        availability: 'Private',
        categories: ['Anime', 'TV'],
        languages: ['ja'],
        connectionCount: 0,
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'idx-3',
        name: 'BroadcasTheNet',
        providerType: 'indexer-a',
        availability: 'SemiPrivate',
        categories: ['TV'],
        languages: ['fr'],
        connectionCount: 0,
        createdAt: '2024-01-03T00:00:00Z',
    },
];

vi.mock('@/hooks/useIndexers', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useIndexers')>();
    return {
        ...actual,
        useIndexers: () => ({
            indexers: mockIndexers,
            isLoadingIndexers: false,
            refetch: vi.fn(),
        }),
    };
});

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } });
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <TooltipProvider>
                    <ToastProvider>
                        <ToastStateProvider>
                            {children}
                            <ToastList />
                            <ToastViewport />
                        </ToastStateProvider>
                    </ToastProvider>
                </TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

function dataRows() {
    const table = screen.getByRole('grid');
    return within(table)
        .getAllByRole('row')
        .filter((r) => within(r).queryAllByRole('cell').length > 0);
}

async function toggleFilter(user: ReturnType<typeof userEvent.setup>, columnLabel: RegExp, optionName: string) {
    await user.click(screen.getByRole('button', { name: columnLabel }));
    const listbox = await screen.findByRole('listbox');
    await user.click(within(listbox).getByRole('option', { name: optionName }));
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());
}

beforeEach(() => {
    vi.clearAllMocks();
});

describe('AvailableCatalog table', () => {
    it('renders every definition as a table row', () => {
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        expect(dataRows()).toHaveLength(3);
        expect(screen.getByText('NZBgeek')).toBeInTheDocument();
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
        expect(screen.getByText('BroadcasTheNet')).toBeInTheDocument();
    });

    it('renders the full "Availability" column header label', () => {
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        const availabilityHeader = screen
            .getAllByRole('columnheader')
            .find((cell) => cell.getAttribute('data-column') === 'availability');
        expect(availabilityHeader).toBeDefined();
        expect(availabilityHeader).toHaveTextContent('Availability');
    });

    it('exposes a Configure button per row that opens the configure dialog', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        const configureButtons = screen.getAllByRole('button', { name: /^configure$/i });
        expect(configureButtons).toHaveLength(3);

        await user.click(configureButtons[0]);
        await waitFor(() => {
            expect(document.querySelector('[data-component="configure-instance"]')).toBeInTheDocument();
        });
    });

    it('selecting two providers shows rows from either (multiselect)', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        await toggleFilter(user, /filter by provider/i, 'newznab');
        await toggleFilter(user, /provider filtered/i, 'torznab');

        expect(dataRows()).toHaveLength(2);
        expect(screen.getByText('NZBgeek')).toBeInTheDocument();
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
    });

    it('matches a category filter by overlap when the row has multiple categories', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        await toggleFilter(user, /filter by categories/i, 'Anime');
        expect(dataRows()).toHaveLength(1);
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();

        await toggleFilter(user, /categories filtered/i, 'Movies');
        expect(dataRows()).toHaveLength(2);
        expect(screen.getByText('NZBgeek')).toBeInTheDocument();
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
    });

    it('filters by language via the typeahead search', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /filter by languages/i }));
        const search = screen.getByRole('textbox', { name: /search languages/i });
        await user.type(search, 'ja');
        const listbox = screen.getByRole('listbox');
        await user.click(within(listbox).getByRole('option', { name: 'ja' }));
        await user.keyboard('{Escape}');

        expect(dataRows()).toHaveLength(1);
        expect(screen.getByText('IPTorrents')).toBeInTheDocument();
    });

    it('clear resets every filter', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        await toggleFilter(user, /filter by provider/i, 'newznab');
        await toggleFilter(user, /filter by categories/i, 'Anime');
        expect(screen.getByText('No matching indexers')).toBeInTheDocument();

        await user.click(document.querySelector('[data-action="clear-filters"]') as HTMLElement);
        expect(dataRows()).toHaveLength(3);
        expect(document.querySelector('[data-action="clear-filters"]')).not.toBeInTheDocument();
    });

    it('keeps the filter menu open across selections despite the url-driven re-render', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <AvailableCatalog />
            </TestWrapper>,
        );

        await user.click(screen.getByRole('button', { name: /filter by provider/i }));
        const listbox = await screen.findByRole('listbox');
        await user.click(within(listbox).getByRole('option', { name: 'newznab' }));
        expect(screen.getByRole('listbox')).toBeInTheDocument();

        await user.click(within(screen.getByRole('listbox')).getByRole('option', { name: 'torznab' }));
        expect(screen.getByRole('listbox')).toBeInTheDocument();
        expect(dataRows()).toHaveLength(2);

        await user.keyboard('{Escape}');
        await waitFor(() => expect(screen.queryByRole('listbox')).not.toBeInTheDocument());
    });
});
