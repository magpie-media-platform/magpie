import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { SimpleFilterBuilder, isSimpleQuery } from './SimpleFilterBuilder';

describe('SimpleFilterBuilder', () => {
    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
    });

    describe('rendering', () => {
        it('renders with correct data-component attribute', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(document.querySelector('[data-component="simple-filter-builder"]')).toBeInTheDocument();
        });

        it('applies custom className', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} className="custom-class" />);

            const builder = document.querySelector('.simple-filter-builder');
            expect(builder).toHaveClass('custom-class');
        });

        it('applies readonly class when readOnly is true', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} readOnly />);

            const builder = document.querySelector('.simple-filter-builder');
            expect(builder).toHaveAttribute('data-readonly', 'true');
        });

        it('renders content context fields by default', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(screen.getByLabelText('Content Type')).toBeInTheDocument();
            expect(screen.getByLabelText('Monitoring')).toBeInTheDocument();
            expect(screen.getByLabelText('Title')).toBeInTheDocument();
            expect(screen.getByLabelText('Genre')).toBeInTheDocument();
            expect(screen.getByLabelText('Tag')).toBeInTheDocument();
        });

        it('renders subject context fields when context is subject', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />);

            expect(screen.getByLabelText('Kind')).toBeInTheDocument();
            expect(screen.getByLabelText('Title')).toBeInTheDocument();
            expect(screen.getByLabelText('Genre')).toBeInTheDocument();
            expect(screen.getByLabelText('Tag')).toBeInTheDocument();

            expect(screen.queryByLabelText('Content Type')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Monitoring')).not.toBeInTheDocument();
        });
    });

    describe('build/parse round-trip - content context', () => {
        it('builds empty query from empty fields', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(mockOnChange).not.toHaveBeenCalled();
        });

        it('round-trips contentType field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const contentTypeTrigger = screen.getByRole('combobox', { name: 'Content Type' });
            await user.click(contentTypeTrigger);
            const movieOption = await screen.findByRole('option', { name: 'movie' });
            await user.click(movieOption);

            expect(mockOnChange).toHaveBeenCalledWith('contentType = "movie"');

            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value='contentType = "movie"' onChange={mockOnChange} />);

            const trigger = screen.getByRole('combobox', { name: 'Content Type' });
            expect(trigger).toHaveTextContent('movie');
        });

        it('round-trips monitoring field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const monitoringTrigger = screen.getByRole('combobox', { name: 'Monitoring' });
            await user.click(monitoringTrigger);
            const activeOption = await screen.findByRole('option', { name: 'Active' });
            await user.click(activeOption);

            expect(mockOnChange).toHaveBeenCalledWith('monitoring = Active');

            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value="monitoring = Active" onChange={mockOnChange} />);

            const trigger = screen.getByRole('combobox', { name: 'Monitoring' });
            expect(trigger).toHaveTextContent('Active');
        });

        it('round-trips title field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const titleInput = screen.getByLabelText('Title');
            await user.type(titleInput, 'T');

            expect(mockOnChange).toHaveBeenLastCalledWith('title contains "T"');

            const query = 'title contains "Breaking Bad"';
            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            const newTitleInput = screen.getByLabelText('Title');
            expect(newTitleInput).toHaveValue('Breaking Bad');
        });

        it('round-trips genre field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const genreInput = screen.getByLabelText('Genre');
            await user.type(genreInput, 'A');

            expect(mockOnChange).toHaveBeenLastCalledWith('genre has "A"');

            const query = 'genre has "Action"';
            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            const newGenreInput = screen.getByLabelText('Genre');
            expect(newGenreInput).toHaveValue('Action');
        });

        it('round-trips tag field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const tagInput = screen.getByLabelText('Tag');
            await user.type(tagInput, 'f');

            expect(mockOnChange).toHaveBeenLastCalledWith('tag has "f"');

            const query = 'tag has "favorite"';
            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            const newTagInput = screen.getByLabelText('Tag');
            expect(newTagInput).toHaveValue('favorite');
        });

        it('round-trips multiple fields combined with AND', () => {
            const query = 'contentType = "movie" and title contains "Test"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            const trigger = screen.getByRole('combobox', { name: 'Content Type' });
            expect(trigger).toHaveTextContent('movie');
            expect(screen.getByLabelText('Title')).toHaveValue('Test');
        });

        it('round-trips complex content query with all fields', () => {
            const query =
                'contentType = "show" and monitoring = Active and title contains "Drama" and genre has "Thriller" and tag has "must-watch"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('show');
            expect(screen.getByRole('combobox', { name: 'Monitoring' })).toHaveTextContent('Active');
            expect(screen.getByLabelText('Title')).toHaveValue('Drama');
            expect(screen.getByLabelText('Genre')).toHaveValue('Thriller');
            expect(screen.getByLabelText('Tag')).toHaveValue('must-watch');
        });
    });

    describe('build/parse round-trip - subject context', () => {
        it('round-trips kind field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />);

            const kindTrigger = screen.getByRole('combobox');
            await user.click(kindTrigger);
            const personOption = await screen.findByRole('option', { name: 'person' });
            await user.click(personOption);

            expect(mockOnChange).toHaveBeenCalledWith('kind = "person"');

            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value='kind = "person"' onChange={mockOnChange} context="subject" />);

            const newTrigger = screen.getByRole('combobox');
            expect(newTrigger).toHaveTextContent('person');
        });

        it('round-trips complex subject query with all fields', () => {
            const query = 'kind = "movie" and title contains "Inception" and genre has "Sci-Fi" and tag has "favorite"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} context="subject" />);

            const kindTrigger = screen.getByRole('combobox');
            expect(kindTrigger).toHaveTextContent('movie');
            expect(screen.getByLabelText('Title')).toHaveValue('Inception');
            expect(screen.getByLabelText('Genre')).toHaveValue('Sci-Fi');
            expect(screen.getByLabelText('Tag')).toHaveValue('favorite');
        });
    });

    describe('parsing edge cases', () => {
        it('handles empty query string', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Any');
            expect(screen.getByRole('combobox', { name: 'Monitoring' })).toHaveTextContent('Any');
            expect(screen.getByLabelText('Title')).toHaveValue('');
            expect(screen.getByLabelText('Genre')).toHaveValue('');
            expect(screen.getByLabelText('Tag')).toHaveValue('');
        });

        it('handles whitespace-only query string', () => {
            render(<SimpleFilterBuilder value="   " onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Any');
            expect(screen.getByRole('combobox', { name: 'Monitoring' })).toHaveTextContent('Any');
        });

        it('handles case-insensitive field names', () => {
            render(
                <SimpleFilterBuilder value='CONTENTTYPE = "movie" AND MONITORING = Active' onChange={mockOnChange} />,
            );

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('movie');
            expect(screen.getByRole('combobox', { name: 'Monitoring' })).toHaveTextContent('Active');
        });

        it('handles case-insensitive operators', () => {
            render(<SimpleFilterBuilder value='title CONTAINS "test"' onChange={mockOnChange} />);

            expect(screen.getByLabelText('Title')).toHaveValue('test');
        });

        it('handles extra whitespace around operators', () => {
            render(
                <SimpleFilterBuilder
                    value='contentType   =   "movie"   and   title   contains   "test"'
                    onChange={mockOnChange}
                />,
            );

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('movie');
            expect(screen.getByLabelText('Title')).toHaveValue('test');
        });
    });

    describe('isSimpleQuery helper', () => {
        it('returns true for empty query', () => {
            expect(isSimpleQuery('', 'content')).toBe(true);
            expect(isSimpleQuery('   ', 'content')).toBe(true);
        });

        it('returns true for valid content queries', () => {
            expect(isSimpleQuery('contentType = "movie"', 'content')).toBe(true);
            expect(isSimpleQuery('monitoring = Active', 'content')).toBe(true);
            expect(isSimpleQuery('title contains "test"', 'content')).toBe(true);
            expect(isSimpleQuery('genre has "Action"', 'content')).toBe(true);
            expect(isSimpleQuery('tag has "favorite"', 'content')).toBe(true);
            expect(isSimpleQuery('contentType = "movie" and monitoring = Active', 'content')).toBe(true);
        });

        it('returns true for valid subject queries', () => {
            expect(isSimpleQuery('kind = "person"', 'subject')).toBe(true);
            expect(isSimpleQuery('kind = "movie" and title contains "test"', 'subject')).toBe(true);
        });

        it('returns false for unsupported operators', () => {
            expect(isSimpleQuery('title != "test"', 'content')).toBe(false);
            expect(isSimpleQuery('year > 2020', 'content')).toBe(false);
            expect(isSimpleQuery('rating >= 8', 'content')).toBe(false);
        });

        it('returns false for unsupported fields', () => {
            expect(isSimpleQuery('year = 2020', 'content')).toBe(false);
            expect(isSimpleQuery('studio = "Warner"', 'content')).toBe(false);
        });

        it('returns false for OR conditions', () => {
            expect(isSimpleQuery('contentType = "movie" or contentType = "show"', 'content')).toBe(false);
        });

        it('returns false for nested conditions', () => {
            expect(isSimpleQuery('(contentType = "movie" and monitoring = Active)', 'content')).toBe(false);
        });

        it('returns false for NOT conditions', () => {
            expect(isSimpleQuery('not monitoring = Active', 'content')).toBe(false);
        });
    });

    describe('disabled state', () => {
        it('disables all inputs when readOnly is true', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} readOnly />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveAttribute('data-disabled');
            expect(screen.getByRole('combobox', { name: 'Monitoring' })).toHaveAttribute('data-disabled');

            expect(screen.getByLabelText('Title')).toBeDisabled();
            expect(screen.getByLabelText('Genre')).toBeDisabled();
            expect(screen.getByLabelText('Tag')).toBeDisabled();
        });

        it('disables all inputs when loading is true', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} loading />);

            expect(screen.getByLabelText('Title')).toBeDisabled();
            expect(screen.getByLabelText('Genre')).toBeDisabled();
            expect(screen.getByLabelText('Tag')).toBeDisabled();
        });
    });

    describe('clearing fields', () => {
        it('removes field from query when cleared', async () => {
            const user = userEvent.setup();
            render(<SimpleFilterBuilder value='title contains "test"' onChange={mockOnChange} />);

            const titleInput = screen.getByLabelText('Title');
            await user.clear(titleInput);

            expect(mockOnChange).toHaveBeenLastCalledWith('');
        });

        it('updates query when one field is cleared from multi-field query', async () => {
            const user = userEvent.setup();
            render(
                <SimpleFilterBuilder value='contentType = "movie" and title contains "test"' onChange={mockOnChange} />,
            );

            const titleInput = screen.getByLabelText('Title');
            await user.clear(titleInput);

            expect(mockOnChange).toHaveBeenLastCalledWith('contentType = "movie"');
        });
    });
});
