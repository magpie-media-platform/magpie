import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

import { ContentSearch } from './ContentSearch';

describe('ContentSearch', () => {
    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="content-search"]')).toBeInTheDocument();
        });

        it('renders search input with correct aria-label', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(screen.getByRole('searchbox', { name: 'Search content by title' })).toBeInTheDocument();
        });

        it('renders with default placeholder', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(screen.getByPlaceholderText('Search by title...')).toBeInTheDocument();
        });

        it('renders with custom placeholder', () => {
            render(<ContentSearch value="" onChange={mockOnChange} placeholder="Find content..." />);

            expect(screen.getByPlaceholderText('Find content...')).toBeInTheDocument();
        });

        it('renders with value', () => {
            render(<ContentSearch value="breaking" onChange={mockOnChange} />);

            expect(screen.getByRole('searchbox')).toHaveValue('breaking');
        });

        it('renders search icon', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(document.querySelector('.input__icon--leading')).toBeInTheDocument();
        });

        it('renders data-search attribute on input', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(document.querySelector('[data-search="input"]')).toBeInTheDocument();
        });
    });

    describe('clear button', () => {
        it('does not render clear button when value is empty', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(document.querySelector('[data-action="clear-search"]')).not.toBeInTheDocument();
        });

        it('renders clear button when value is present', () => {
            render(<ContentSearch value="test" onChange={mockOnChange} />);

            const clearButton = document.querySelector('[data-action="clear-search"]');
            expect(clearButton).toBeInTheDocument();
        });

        it('has data-action attribute on clear button', () => {
            render(<ContentSearch value="test" onChange={mockOnChange} />);

            expect(document.querySelector('[data-action="clear-search"]')).toBeInTheDocument();
        });

        it('clears value immediately when clear button is clicked', async () => {
            const user = userEvent.setup();
            render(<ContentSearch value="test" onChange={mockOnChange} />);

            const clearButton = document.querySelector('[data-action="clear-search"]') as HTMLElement;
            await user.click(clearButton);

            expect(mockOnChange).toHaveBeenCalledWith('');
        });
    });

    describe('debounced input', () => {
        it('updates internal value immediately on input', async () => {
            const user = userEvent.setup();
            render(<ContentSearch value="" onChange={mockOnChange} />);

            const input = screen.getByRole('searchbox');
            await user.type(input, 'test');

            expect(input).toHaveValue('test');
        });

        it('calls onChange after debounce delay', async () => {
            const user = userEvent.setup();
            render(<ContentSearch value="" onChange={mockOnChange} debounceMs={50} />);

            const input = screen.getByRole('searchbox');
            await user.type(input, 'test');

            await waitFor(() => {
                expect(mockOnChange).toHaveBeenCalledWith('test');
            });
        });

        it('debounces rapid typing', async () => {
            const user = userEvent.setup();
            render(<ContentSearch value="" onChange={mockOnChange} debounceMs={100} />);

            const input = screen.getByRole('searchbox');

            await user.type(input, 'test');

            await waitFor(() => {
                expect(mockOnChange).toHaveBeenCalledWith('test');
            });

            expect(mockOnChange).toHaveBeenCalledTimes(1);
        });

        it('uses custom debounce delay', async () => {
            const user = userEvent.setup();
            render(<ContentSearch value="" onChange={mockOnChange} debounceMs={50} />);

            const input = screen.getByRole('searchbox');
            await user.type(input, 'test');

            await waitFor(() => {
                expect(mockOnChange).toHaveBeenCalledWith('test');
            });
        });
    });

    describe('controlled value', () => {
        it('syncs internal value when external value changes', () => {
            const { rerender } = render(<ContentSearch value="initial" onChange={mockOnChange} />);

            expect(screen.getByRole('searchbox')).toHaveValue('initial');

            rerender(<ContentSearch value="updated" onChange={mockOnChange} />);

            expect(screen.getByRole('searchbox')).toHaveValue('updated');
        });
    });

    describe('loading state', () => {
        it('disables input when loading', () => {
            render(<ContentSearch value="" onChange={mockOnChange} loading={true} />);

            expect(screen.getByRole('searchbox')).toBeDisabled();
        });

        it('enables input when not loading', () => {
            render(<ContentSearch value="" onChange={mockOnChange} loading={false} />);

            expect(screen.getByRole('searchbox')).not.toBeDisabled();
        });
    });

    describe('accessibility', () => {
        it('has searchbox role', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(screen.getByRole('searchbox')).toBeInTheDocument();
        });

        it('has aria-label on input', () => {
            render(<ContentSearch value="" onChange={mockOnChange} />);

            expect(screen.getByRole('searchbox')).toHaveAttribute('aria-label', 'Search content by title');
        });

        it('has aria-label on clear button', () => {
            render(<ContentSearch value="test" onChange={mockOnChange} />);

            const clearButton = document.querySelector('[data-action="clear-search"]');
            expect(clearButton).toHaveAttribute('aria-label', 'Clear search');
        });
    });

    describe('className prop', () => {
        it('appends custom className', () => {
            render(<ContentSearch value="" onChange={mockOnChange} className="custom-class" />);

            const container = document.querySelector('[data-component="content-search"]');
            expect(container).toHaveClass('content-search');
            expect(container).toHaveClass('custom-class');
        });
    });
});
