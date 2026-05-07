import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import {
    DownloadFilters,
    type DownloadFilterState,
    type DownloadClientOption,
    type StateFilterOption,
} from './DownloadFilters';

describe('DownloadFilters', () => {
    const defaultFilters: DownloadFilterState = {
        client: '',
        state: '',
    };

    const mockClients: DownloadClientOption[] = [
        { id: 'client-1', name: 'SABnzbd' },
        { id: 'client-2', name: 'qBittorrent' },
    ];

    const mockStateOptions: StateFilterOption[] = [
        { value: 'all', label: 'All States' },
        { value: 1, label: 'Downloading' },
        { value: 0, label: 'Queued' },
        { value: 2, label: 'Paused' },
        { value: 5, label: 'Warning' },
        { value: 4, label: 'Failed' },
    ];

    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            expect(document.querySelector('[data-component="download-filters"]')).toBeInTheDocument();
        });

        it('renders client filter dropdown', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            expect(document.querySelector('[data-filter="client"]')).toBeInTheDocument();
        });

        it('renders state filter dropdown', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            expect(document.querySelector('[data-filter="state"]')).toBeInTheDocument();
        });
    });

    describe('client filter', () => {
        it('displays "All Clients" when no client is selected', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const clientTrigger = document.querySelector('[data-filter="client"]');
            expect(clientTrigger).toHaveTextContent('All Clients');
        });

        it('displays selected client name', () => {
            render(
                <DownloadFilters
                    filters={{ ...defaultFilters, client: 'client-1' }}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const clientTrigger = document.querySelector('[data-filter="client"]');
            expect(clientTrigger).toHaveTextContent('SABnzbd');
        });

        it('calls onChange when client is selected', async () => {
            const user = userEvent.setup();
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const clientTrigger = document.querySelector('[data-filter="client"]') as HTMLElement;
            await user.click(clientTrigger);

            const sabnzbdOption = await screen.findByRole('option', { name: 'SABnzbd' });
            await user.click(sabnzbdOption);

            expect(mockOnChange).toHaveBeenCalledWith(expect.objectContaining({ client: 'client-1' }));
        });
    });

    describe('state filter', () => {
        it('displays "All States" when no state is selected', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const stateTrigger = document.querySelector('[data-filter="state"]');
            expect(stateTrigger).toHaveTextContent('All States');
        });

        it('displays selected state label', () => {
            render(
                <DownloadFilters
                    filters={{ ...defaultFilters, state: '1' }}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const stateTrigger = document.querySelector('[data-filter="state"]');
            expect(stateTrigger).toHaveTextContent('Downloading');
        });

        it('calls onChange when state is selected', async () => {
            const user = userEvent.setup();
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const stateTrigger = document.querySelector('[data-filter="state"]') as HTMLElement;
            await user.click(stateTrigger);

            const downloadingOption = await screen.findByRole('option', { name: 'Downloading' });
            await user.click(downloadingOption);

            expect(mockOnChange).toHaveBeenCalledWith(expect.objectContaining({ state: '1' }));
        });
    });

    describe('clear button', () => {
        it('does not show clear button when no filters are active', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            expect(screen.queryByText('Clear')).not.toBeInTheDocument();
        });

        it('shows clear button when filters are active', () => {
            render(
                <DownloadFilters
                    filters={{ client: 'client-1', state: '' }}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            expect(screen.getByText('Clear')).toBeInTheDocument();
        });

        it('calls onChange to clear all filters when clicked', async () => {
            const user = userEvent.setup();
            render(
                <DownloadFilters
                    filters={{ client: 'client-1', state: '1' }}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            await user.click(screen.getByText('Clear'));

            expect(mockOnChange).toHaveBeenCalledWith({
                client: '',
                state: '',
            });
        });
    });

    describe('loading state', () => {
        it('disables filter controls when loading', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                    loading={true}
                />,
            );

            const clientTrigger = document.querySelector('[data-filter="client"]');
            const stateTrigger = document.querySelector('[data-filter="state"]');

            expect(clientTrigger).toHaveAttribute('data-disabled');
            expect(stateTrigger).toHaveAttribute('data-disabled');
        });
    });

    describe('accessibility', () => {
        it('has aria-label on client filter', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const clientTrigger = document.querySelector('[data-filter="client"]');
            expect(clientTrigger).toHaveAttribute('aria-label', 'Filter by client');
        });

        it('has aria-label on state filter', () => {
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const stateTrigger = document.querySelector('[data-filter="state"]');
            expect(stateTrigger).toHaveAttribute('aria-label', 'Filter by state');
        });

        it('has data-action on clear button', () => {
            render(
                <DownloadFilters
                    filters={{ client: 'client-1', state: '' }}
                    clients={mockClients}
                    stateOptions={mockStateOptions}
                    onChange={mockOnChange}
                />,
            );

            expect(document.querySelector('[data-action="clear-filters"]')).toBeInTheDocument();
        });
    });

    describe('with history state options', () => {
        const historyStateOptions: StateFilterOption[] = [
            { value: 'all', label: 'All States' },
            { value: 3, label: 'Completed' },
            { value: 4, label: 'Failed' },
        ];

        it('renders only history-relevant state options', async () => {
            const user = userEvent.setup();
            render(
                <DownloadFilters
                    filters={defaultFilters}
                    clients={mockClients}
                    stateOptions={historyStateOptions}
                    onChange={mockOnChange}
                />,
            );

            const stateTrigger = document.querySelector('[data-filter="state"]') as HTMLElement;
            await user.click(stateTrigger);

            expect(await screen.findByRole('option', { name: 'All States' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Completed' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Failed' })).toBeInTheDocument();
        });
    });
});
