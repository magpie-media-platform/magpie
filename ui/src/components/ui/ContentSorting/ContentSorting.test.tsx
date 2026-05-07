import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { ContentSorting, type ContentSortState } from './ContentSorting';

describe('ContentSorting', () => {
    const defaultSort: ContentSortState = {
        field: 'title',
        direction: 'asc',
    };

    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="content-sorting"]')).toBeInTheDocument();
        });

        it('renders sort label', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(screen.getByText('Sort')).toBeInTheDocument();
        });

        it('renders field dropdown', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(document.querySelector('[data-sort="field"]')).toBeInTheDocument();
        });

        it('renders direction toggle button', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(document.querySelector('[data-sort="direction"]')).toBeInTheDocument();
        });
    });

    describe('sort field', () => {
        it('displays "Title" when title field is selected', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveTextContent('Title');
        });

        it('displays "Date Added" when dateAdded field is selected', () => {
            render(<ContentSorting sort={{ ...defaultSort, field: 'dateAdded' }} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveTextContent('Date Added');
        });

        it('displays "Last Searched" when lastSearched field is selected', () => {
            render(<ContentSorting sort={{ ...defaultSort, field: 'lastSearched' }} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveTextContent('Last Searched');
        });

        it('calls onChange when field is changed to dateAdded', async () => {
            const user = userEvent.setup();
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const dateAddedOption = await screen.findByRole('option', { name: 'Date Added' });
            await user.click(dateAddedOption);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'dateAdded',
                direction: 'asc',
            });
        });

        it('calls onChange when field is changed to lastSearched', async () => {
            const user = userEvent.setup();
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const lastSearchedOption = await screen.findByRole('option', { name: 'Last Searched' });
            await user.click(lastSearchedOption);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'lastSearched',
                direction: 'asc',
            });
        });
    });

    describe('sort direction', () => {
        it('displays "Ascending" when direction is asc', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(screen.getByText('Ascending')).toBeInTheDocument();
        });

        it('displays "Descending" when direction is desc', () => {
            render(<ContentSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            expect(screen.getByText('Descending')).toBeInTheDocument();
        });

        it('has correct data-direction attribute when asc', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('data-direction', 'asc');
        });

        it('has correct data-direction attribute when desc', () => {
            render(<ContentSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('data-direction', 'desc');
        });

        it('calls onChange to toggle direction from asc to desc', async () => {
            const user = userEvent.setup();
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'title',
                direction: 'desc',
            });
        });

        it('calls onChange to toggle direction from desc to asc', async () => {
            const user = userEvent.setup();
            render(<ContentSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'title',
                direction: 'asc',
            });
        });
    });

    describe('disabled state', () => {
        it('disables field dropdown when disabled', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} disabled />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveAttribute('data-disabled');
        });

        it('disables direction button when disabled', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} disabled />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toBeDisabled();
        });
    });

    describe('accessibility', () => {
        it('has aria-label on field dropdown', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveAttribute('aria-label', 'Sort by field');
        });

        it('has aria-label on direction button for ascending', () => {
            render(<ContentSorting sort={defaultSort} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('aria-label', 'Sort descending');
        });

        it('has aria-label on direction button for descending', () => {
            render(<ContentSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('aria-label', 'Sort ascending');
        });
    });

    describe('preserves other sort state', () => {
        it('preserves direction when changing field', async () => {
            const user = userEvent.setup();
            render(<ContentSorting sort={{ field: 'title', direction: 'desc' }} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const dateAddedOption = await screen.findByRole('option', { name: 'Date Added' });
            await user.click(dateAddedOption);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'dateAdded',
                direction: 'desc',
            });
        });

        it('preserves field when changing direction', async () => {
            const user = userEvent.setup();
            render(<ContentSorting sort={{ field: 'lastSearched', direction: 'asc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'lastSearched',
                direction: 'desc',
            });
        });
    });
});
