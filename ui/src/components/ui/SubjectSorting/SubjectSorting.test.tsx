import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { SubjectSorting, type SubjectSortState } from './SubjectSorting';

describe('SubjectSorting', () => {
    const defaultSort: SubjectSortState = {
        field: 'title',
        direction: 'asc',
    };

    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="subject-sorting"]')).toBeInTheDocument();
        });

        it('renders sort label', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(screen.getByText('Sort')).toBeInTheDocument();
        });

        it('renders field dropdown', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(document.querySelector('[data-sort="field"]')).toBeInTheDocument();
        });

        it('renders direction toggle button', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(document.querySelector('[data-sort="direction"]')).toBeInTheDocument();
        });
    });

    describe('sort field', () => {
        it('displays "Title" when title field is selected', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveTextContent('Title');
        });

        it('displays "Date Added" when dateAdded field is selected', () => {
            render(<SubjectSorting sort={{ ...defaultSort, field: 'dateAdded' }} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveTextContent('Date Added');
        });

        it('calls onChange when field is changed to dateAdded', async () => {
            const user = userEvent.setup();
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const dateAddedOption = await screen.findByRole('option', { name: 'Date Added' });
            await user.click(dateAddedOption);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'dateAdded',
                direction: 'asc',
            });
        });

        it('calls onChange when field is changed to title', async () => {
            const user = userEvent.setup();
            render(<SubjectSorting sort={{ field: 'dateAdded', direction: 'asc' }} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]') as HTMLElement;
            await user.click(fieldTrigger);

            const titleOption = await screen.findByRole('option', { name: 'Title' });
            await user.click(titleOption);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'title',
                direction: 'asc',
            });
        });
    });

    describe('sort direction', () => {
        it('displays "Ascending" when direction is asc', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            expect(screen.getByText('Ascending')).toBeInTheDocument();
        });

        it('displays "Descending" when direction is desc', () => {
            render(<SubjectSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            expect(screen.getByText('Descending')).toBeInTheDocument();
        });

        it('has correct data-direction attribute when asc', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('data-direction', 'asc');
        });

        it('has correct data-direction attribute when desc', () => {
            render(<SubjectSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('data-direction', 'desc');
        });

        it('calls onChange to toggle direction from asc to desc', async () => {
            const user = userEvent.setup();
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'title',
                direction: 'desc',
            });
        });

        it('calls onChange to toggle direction from desc to asc', async () => {
            const user = userEvent.setup();
            render(<SubjectSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

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
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} disabled />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveAttribute('data-disabled');
        });

        it('disables direction button when disabled', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} disabled />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toBeDisabled();
        });
    });

    describe('accessibility', () => {
        it('has aria-label on field dropdown', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            const fieldTrigger = document.querySelector('[data-sort="field"]');
            expect(fieldTrigger).toHaveAttribute('aria-label', 'Sort by field');
        });

        it('has aria-label on direction button for ascending', () => {
            render(<SubjectSorting sort={defaultSort} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('aria-label', 'Sort descending');
        });

        it('has aria-label on direction button for descending', () => {
            render(<SubjectSorting sort={{ ...defaultSort, direction: 'desc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]');
            expect(directionButton).toHaveAttribute('aria-label', 'Sort ascending');
        });
    });

    describe('preserves other sort state', () => {
        it('preserves direction when changing field', async () => {
            const user = userEvent.setup();
            render(<SubjectSorting sort={{ field: 'title', direction: 'desc' }} onChange={mockOnChange} />);

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
            render(<SubjectSorting sort={{ field: 'dateAdded', direction: 'asc' }} onChange={mockOnChange} />);

            const directionButton = document.querySelector('[data-sort="direction"]') as HTMLElement;
            await user.click(directionButton);

            expect(mockOnChange).toHaveBeenCalledWith({
                field: 'dateAdded',
                direction: 'desc',
            });
        });
    });
});
