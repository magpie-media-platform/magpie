import type { ReactNode } from 'react';

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { DatabaseIcon } from '@phosphor-icons/react';

import { IntegrationEmptyState } from './IntegrationEmptyState';

function renderState(hasActiveFilters: boolean, onClearFilters = vi.fn(), addAction?: ReactNode) {
    render(
        <IntegrationEmptyState
            icon={<DatabaseIcon data-testid="icon" />}
            entity="indexers"
            hasActiveFilters={hasActiveFilters}
            addDescription="Add an indexer to start searching."
            addAction={addAction}
            onClearFilters={onClearFilters}
        />,
    );
}

describe('IntegrationEmptyState', () => {
    it('shows the nothing-configured prompt without an add button by default', () => {
        renderState(false);

        expect(screen.getByRole('heading', { name: 'No indexers configured' })).toBeInTheDocument();
        expect(screen.getByText('Add an indexer to start searching.')).toBeInTheDocument();

        expect(screen.queryByRole('button')).not.toBeInTheDocument();

        expect(screen.queryByText(/match the active filters/i)).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
    });

    it('renders an add action only when one is supplied', () => {
        renderState(false, vi.fn(), <button type="button">Browse catalog</button>);

        expect(screen.getByRole('button', { name: 'Browse catalog' })).toBeInTheDocument();
    });

    it('shows only the filtered-to-none state when a filter is active', () => {
        renderState(true);

        expect(screen.getByRole('heading', { name: 'No indexers match the active filters' })).toBeInTheDocument();
        expect(screen.getByText('Adjust or clear the filters to see more.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Clear filters' })).toBeInTheDocument();

        expect(screen.queryByRole('heading', { name: 'No indexers configured' })).not.toBeInTheDocument();
    });

    it('uses the supplied entity noun, never "instances"', () => {
        renderState(true);

        expect(screen.queryByText(/instances/i)).not.toBeInTheDocument();
    });

    it('clears filters via the clear-filters action', async () => {
        const user = userEvent.setup();
        const onClearFilters = vi.fn();
        renderState(true, onClearFilters);

        await user.click(screen.getByRole('button', { name: 'Clear filters' }));

        expect(onClearFilters).toHaveBeenCalledOnce();
    });
});
