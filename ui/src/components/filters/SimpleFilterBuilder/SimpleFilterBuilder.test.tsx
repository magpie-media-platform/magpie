import { startTransition, useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { type LibraryTypeDefinition } from '@/hooks/useLibraryTypes';

import { SimpleFilterBuilder } from './SimpleFilterBuilder';
import { isSimpleQuery } from '@/lib/simpleFilterQuery';

function ControlledBuilder({ initial = '', onChange }: { initial?: string; onChange?: (v: string) => void }) {
    const [query, setQuery] = useState(initial);
    return (
        <SimpleFilterBuilder
            value={query}
            onChange={(v) => {
                setQuery(v);
                onChange?.(v);
            }}
        />
    );
}

function DeferredControlledBuilder({ initial = '' }: { initial?: string }) {
    const [query, setQuery] = useState(initial);
    return (
        <SimpleFilterBuilder
            value={query}
            onChange={(v) => {
                startTransition(() => setQuery(v));
            }}
        />
    );
}

function ResettableControlledBuilder({ initial = '' }: { initial?: string }) {
    const [query, setQuery] = useState(initial);
    return (
        <>
            <button type="button" onClick={() => setQuery('')}>
                Reset
            </button>
            <SimpleFilterBuilder value={query} onChange={setQuery} />
        </>
    );
}

const mockContentTypes: LibraryTypeDefinition[] = [
    { id: 'movie', name: 'Movies', pluralName: 'Movies', slug: 'movie', levels: [] },
    { id: 'tv', name: 'TV Shows', pluralName: 'TV Shows', slug: 'tv', levels: [] },
    { id: 'music', name: 'Music', pluralName: 'Music', slug: 'music', levels: [] },
    { id: 'books', name: 'Books', pluralName: 'Books', slug: 'books', levels: [] },
];

const mockSubjectTypes: LibraryTypeDefinition[] = [
    { id: 'person', name: 'Person', pluralName: 'Person', slug: 'person', levels: [] },
    { id: 'studio', name: 'Studio', pluralName: 'Studio', slug: 'studio', levels: [] },
    { id: 'author', name: 'Author', pluralName: 'Author', slug: 'author', levels: [] },
];

const hierarchicalContentTypes: LibraryTypeDefinition[] = [
    {
        id: 'books',
        name: 'Books',
        pluralName: 'Books',
        slug: 'books',
        levels: [
            { slug: 'E-book_V1', displayName: 'E-book', parentSlug: null, standardFactFields: [] },
            { slug: 'audio.book', displayName: 'Audiobook', parentSlug: null, standardFactFields: [] },
            {
                slug: 'chapter',
                displayName: 'Chapter',
                parentSlug: 'E-book_V1',
                standardFactFields: [],
            },
        ],
    },
];

let mockLibraryTypes = {
    contentTypes: mockContentTypes,
    subjectTypes: mockSubjectTypes,
    isLoading: false,
    error: null,
    refetch: vi.fn(),
};

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const original = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...original,
        useLibraryTypes: () => mockLibraryTypes,
    };
});

describe('SimpleFilterBuilder', () => {
    const mockOnChange = vi.fn();

    beforeEach(() => {
        mockOnChange.mockClear();
        mockLibraryTypes = {
            contentTypes: mockContentTypes,
            subjectTypes: mockSubjectTypes,
            isLoading: false,
            error: null,
            refetch: vi.fn(),
        };
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
            expect(screen.getByLabelText('Release year')).toBeInTheDocument();
            expect(screen.getByLabelText('Title')).toBeInTheDocument();
            expect(screen.getByLabelText('Genre')).toBeInTheDocument();
            expect(screen.getByLabelText('Tag')).toBeInTheDocument();
            expect(screen.getByRole('combobox', { name: 'Related kind' })).toBeInTheDocument();
            expect(screen.getByLabelText('Related')).toBeInTheDocument();
        });

        it('groups the relationship kind and name into a single side-by-side Related control', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const row = document.querySelector('.simple-filter-builder__subject');
            expect(row).toBeInTheDocument();
            expect(row).toContainElement(screen.getByRole('combobox', { name: 'Related kind' }));
            expect(row).toContainElement(screen.getByLabelText('Related'));

            expect(screen.queryByText('Related kind')).not.toBeInTheDocument();
            expect(screen.queryByText('Name')).not.toBeInTheDocument();
        });

        it('renders the relationship name input before the kind dropdown', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            const nameInput = screen.getByLabelText('Related');
            const kindTrigger = screen.getByRole('combobox', { name: 'Related kind' });

            expect(nameInput.compareDocumentPosition(kindTrigger)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
        });

        it.each([
            ['', 'Any'],
            ['Relationships has (Type = "person")', 'Person'],
            ['Relationships has (Type = "very-long-subject-kind")', 'A very long subject kind label that overflows'],
        ])('left-aligns and truncates the relationship kind value for query %s', (value, label) => {
            mockLibraryTypes = {
                ...mockLibraryTypes,
                subjectTypes: [
                    ...mockSubjectTypes,
                    {
                        id: 'very-long-subject-kind',
                        name: 'A very long subject kind label that overflows',
                        pluralName: 'A very long subject kind label that overflows',
                        slug: 'very-long-subject-kind',
                        levels: [],
                    },
                ],
            };

            render(<SimpleFilterBuilder value={value} onChange={mockOnChange} />);

            const trigger = screen.getByRole('combobox', { name: 'Related kind' });
            const triggerValue = trigger.querySelector('.simple-filter-builder__subject-kind');

            expect(triggerValue).toHaveTextContent(label);
            expect(triggerValue).toHaveClass('simple-filter-builder__subject-kind');
            expect(triggerValue).not.toHaveAttribute('style');
            expect(trigger.querySelector('.select-trigger__icon')).toBeInTheDocument();
        });

        it('no longer renders the monitoring select control', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(document.querySelector('#sfb-monitoring')).not.toBeInTheDocument();
            expect(screen.queryByRole('combobox', { name: 'Monitoring' })).not.toBeInTheDocument();
        });

        it('keeps the radix form-integration native selects out of the accessibility tree in content context', () => {
            render(
                <form>
                    <SimpleFilterBuilder value="" onChange={mockOnChange} context="content" />
                </form>,
            );

            const nativeSelects = document.querySelectorAll('select');
            expect(nativeSelects.length).toBeGreaterThan(0);
            for (const nativeSelect of nativeSelects) {
                expect(nativeSelect).toHaveAttribute('aria-hidden', 'true');
            }

            const comboboxes = screen.getAllByRole('combobox');
            expect(comboboxes).toHaveLength(2);
            for (const combobox of comboboxes) {
                expect(combobox).toHaveAccessibleName();
            }
            expect(screen.getByRole('combobox', { name: 'Content Type' })).toBeInTheDocument();
            expect(screen.getByRole('combobox', { name: 'Related kind' })).toBeInTheDocument();
        });

        it('keeps the radix form-integration native select out of the accessibility tree in subject context', () => {
            render(
                <form>
                    <SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />
                </form>,
            );

            const nativeSelects = document.querySelectorAll('select');
            expect(nativeSelects.length).toBeGreaterThan(0);
            for (const nativeSelect of nativeSelects) {
                expect(nativeSelect).toHaveAttribute('aria-hidden', 'true');
            }

            const comboboxes = screen.getAllByRole('combobox');
            expect(comboboxes).toHaveLength(1);
            expect(comboboxes[0]).toHaveAccessibleName();
            expect(screen.getByRole('combobox', { name: 'Kind' })).toBeInTheDocument();
        });

        it('defaults the release-year comparison to "in"', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'false');
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'false');
        });

        it('renders the curated subject context fields when context is subject', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />);

            expect(screen.getByLabelText('Kind')).toBeInTheDocument();
            expect(screen.getByLabelText('Related')).toBeInTheDocument();
            expect(screen.getByLabelText('Tag')).toBeInTheDocument();

            expect(screen.queryByLabelText('Genre')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Content Type')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Monitoring')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Title')).not.toBeInTheDocument();
        });

        it('groups the subject kind and name into a single side-by-side control', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />);

            const row = document.querySelector('.simple-filter-builder__subject');
            expect(row).toBeInTheDocument();
            expect(row).toContainElement(screen.getByRole('combobox', { name: 'Kind' }));
            expect(row).toContainElement(screen.getByLabelText('Related'));
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
            const movieOption = await screen.findByRole('option', { name: 'Movies' });
            await user.click(movieOption);

            expect(mockOnChange).toHaveBeenCalledWith('contentType = "movie"');

            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value='contentType = "movie"' onChange={mockOnChange} />);

            const trigger = screen.getByRole('combobox', { name: 'Content Type' });
            expect(trigger).toHaveTextContent('Movies');
        });

        it('renders one option per installed content type', async () => {
            const user = userEvent.setup();
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));

            expect(await screen.findByRole('option', { name: 'Movies' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'TV Shows' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Music' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Books' })).toBeInTheDocument();

            expect(screen.queryByRole('option', { name: 'season' })).not.toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'episode' })).not.toBeInTheDocument();
        });

        it('renders root levels as flat sibling options and excludes descendants', async () => {
            const user = userEvent.setup();
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));

            expect(await screen.findByRole('option', { name: 'Books' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Books > E-book' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Books > Audiobook' })).toBeInTheDocument();
            expect(screen.queryByRole('option', { name: /Chapter/ })).not.toBeInTheDocument();
        });

        it('selects and reads a composite root-level option with the exact registered slug', async () => {
            const user = userEvent.setup();
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));
            await user.click(await screen.findByRole('option', { name: 'Books > E-book' }));

            expect(mockOnChange).toHaveBeenLastCalledWith('contentType = "books" and level = "E-book_V1"');

            rerender(
                <SimpleFilterBuilder value='contentType = "books" and level = "E-book_V1"' onChange={mockOnChange} />,
            );
            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Books > E-book');
        });

        it('clears both composite predicates without disturbing another simple predicate', async () => {
            const user = userEvent.setup();
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(
                <SimpleFilterBuilder
                    value='contentType = "books" and level = "E-book_V1" and tag has "favorite"'
                    onChange={mockOnChange}
                />,
            );

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));
            await user.click(await screen.findByRole('option', { name: 'Any' }));

            expect(mockOnChange).toHaveBeenLastCalledWith('tag has "favorite"');
        });

        it('keeps an earlier edit when the next edit lands before the value prop catches up', async () => {
            const user = userEvent.setup();
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            await user.type(screen.getByLabelText('Title'), 'Journey');
            await user.type(screen.getByLabelText('Tag'), 'follow');

            expect(mockOnChange).toHaveBeenLastCalledWith('title contains "Journey" and tag has "follow"');
        });

        it('preserves the monitoring clause across edits even without a visible control', async () => {
            const user = userEvent.setup();
            render(<SimpleFilterBuilder value="monitoring = true" onChange={mockOnChange} />);

            await user.type(screen.getByLabelText('Title'), 'X');

            expect(mockOnChange).toHaveBeenLastCalledWith('monitoring = true and title contains "X"');
        });

        it('keeps a monitoring-only query simple and re-emits it unchanged', () => {
            expect(isSimpleQuery('monitoring = true', 'content')).toBe(true);

            render(<ControlledBuilder initial="monitoring = true" onChange={mockOnChange} />);

            fireEvent.change(screen.getByLabelText('Tag'), { target: { value: 'x' } });
            expect(mockOnChange).toHaveBeenLastCalledWith('monitoring = true and tag has "x"');

            fireEvent.change(screen.getByLabelText('Tag'), { target: { value: '' } });
            expect(mockOnChange).toHaveBeenLastCalledWith('monitoring = true');
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

        it('relationship name contributes nothing when no kind or name is set', async () => {
            const user = userEvent.setup();
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            await user.type(screen.getByLabelText('Genre'), 'A');
            expect(mockOnChange).toHaveBeenLastCalledWith('genre has "A"');
        });

        it('builds and round-trips a relationship subquery from the chosen kind and name', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.click(screen.getByRole('combobox', { name: 'Related kind' }));
            await user.click(await screen.findByRole('option', { name: 'Person' }));

            await user.type(screen.getByLabelText('Related'), 'Johansson');

            expect(mockOnChange).toHaveBeenLastCalledWith(
                'Relationships has (Name contains "Johansson" and Type = "person")',
            );
            expect(screen.getByLabelText('Related')).toHaveValue('Johansson');
            expect(screen.getByRole('combobox', { name: 'Related kind' })).toHaveTextContent('Person');
        });

        it('builds a relationship name query across any subject kind', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(screen.getByLabelText('Related'), 'A24');

            expect(mockOnChange).toHaveBeenLastCalledWith('Relationships has (Name contains "A24")');
        });

        it('clears the relationship subquery when both kind and name are cleared', async () => {
            const user = userEvent.setup();
            render(
                <ControlledBuilder
                    initial='Relationships has (Name contains "Johansson" and Type = "person")'
                    onChange={mockOnChange}
                />,
            );

            await user.clear(screen.getByLabelText('Related'));
            expect(mockOnChange).toHaveBeenLastCalledWith('Relationships has (Type = "person")');

            await user.click(screen.getByRole('combobox', { name: 'Related kind' }));
            await user.click(await screen.findByRole('option', { name: 'Any' }));
            expect(mockOnChange).toHaveBeenLastCalledWith('');
        });

        it('escapes a double-quote in the relationship name and round-trips it', () => {
            const { rerender } = render(
                <SimpleFilterBuilder
                    value='Relationships has (Name contains "" and Type = "person")'
                    onChange={mockOnChange}
                />,
            );

            fireEvent.change(screen.getByLabelText('Related'), { target: { value: 'Robert "Bob"' } });

            const emitted = mockOnChange.mock.calls.at(-1)?.[0] as string;
            expect(emitted).toBe('Relationships has (Name contains "Robert \\"Bob\\"" and Type = "person")');
            expect(isSimpleQuery(emitted, 'content')).toBe(true);

            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value={emitted} onChange={mockOnChange} />);

            expect(screen.getByLabelText('Related')).toHaveValue('Robert "Bob"');
        });

        it('round-trips the relationship clause alongside scalar fields', () => {
            const query = 'contentType = "movie" and Relationships has (Name contains "Johansson" and Type = "person")';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');
            expect(screen.getByRole('combobox', { name: 'Related kind' })).toHaveTextContent('Person');
            expect(screen.getByLabelText('Related')).toHaveValue('Johansson');
            expect(screen.getByLabelText('Title')).toHaveValue('');
        });

        it('round-trips the relationship clause when it sits between scalar fields', () => {
            const query =
                'contentType = "movie" and Relationships has (Name contains "Johansson" and Type = "person") and tag has "fav"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');
            expect(screen.getByRole('combobox', { name: 'Related kind' })).toHaveTextContent('Person');
            expect(screen.getByLabelText('Related')).toHaveValue('Johansson');
            expect(screen.getByLabelText('Tag')).toHaveValue('fav');
        });

        it('omits the relationship row entirely when no subject kinds are registered', () => {
            mockLibraryTypes = {
                contentTypes: mockContentTypes,
                subjectTypes: [],
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            };

            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(screen.queryByLabelText('Related kind')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Related')).not.toBeInTheDocument();
        });

        it('hides metadata fields unsupported by the selected content type', () => {
            mockLibraryTypes = {
                contentTypes: [
                    {
                        id: 'title-only',
                        name: 'Title Only',
                        pluralName: 'Title Only',
                        slug: 'title-only',
                        levels: [],
                        standardMetadataFields: [
                            {
                                field: 'Title',
                                key: 'title',
                                displayName: 'title',
                                dataType: 'string',
                                valueKind: 'text',
                                editable: true,
                            },
                        ],
                    },
                ],
                subjectTypes: mockSubjectTypes,
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            };

            render(<SimpleFilterBuilder value='contentType = "title-only"' onChange={mockOnChange} />);

            expect(screen.getByLabelText('Title')).toBeInTheDocument();
            expect(screen.getByLabelText('Tag')).toBeInTheDocument();
            expect(screen.queryByLabelText('Release year')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Genre')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Related kind')).not.toBeInTheDocument();
            expect(screen.queryByLabelText('Related')).not.toBeInTheDocument();
        });

        it('uses fact and custom metadata definitions when standard field definitions are absent', () => {
            mockLibraryTypes = {
                contentTypes: [
                    {
                        id: 'extension-type',
                        name: 'Extension Type',
                        pluralName: 'Extension Types',
                        slug: 'extension-type',
                        levels: [
                            {
                                slug: 'extension-type',
                                displayName: 'Extension Type',
                                parentSlug: null,
                                standardFactFields: [{ key: 'publishDate', displayName: 'Published' }],
                                customMetadataFields: [{ key: 'genre', displayName: 'Genre' }],
                            },
                        ],
                    },
                ],
                subjectTypes: mockSubjectTypes,
                isLoading: false,
                error: null,
                refetch: vi.fn(),
            };

            render(<SimpleFilterBuilder value='contentType = "extension-type"' onChange={mockOnChange} />);

            expect(screen.getByLabelText('Release year')).toBeInTheDocument();
            expect(screen.getByLabelText('Genre')).toBeInTheDocument();
            expect(screen.queryByLabelText('Title')).not.toBeInTheDocument();
        });

        it('round-trips multiple fields combined with AND', () => {
            const query = 'contentType = "movie" and title contains "Test"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            const trigger = screen.getByRole('combobox', { name: 'Content Type' });
            expect(trigger).toHaveTextContent('Movies');
            expect(screen.getByLabelText('Title')).toHaveValue('Test');
        });

        it('round-trips complex content query with all fields', () => {
            const query =
                'contentType = "tv" and monitoring = true and title contains "Drama" and genre has "Thriller" and tag has "must-watch"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('TV Shows');
            expect(screen.getByLabelText('Title')).toHaveValue('Drama');
            expect(screen.getByLabelText('Genre')).toHaveValue('Thriller');
            expect(screen.getByLabelText('Tag')).toHaveValue('must-watch');
        });
    });

    describe('release year control', () => {
        const yearInput = () => screen.getByLabelText('Release year');

        async function setOps(user: ReturnType<typeof userEvent.setup>, target: ('before' | 'in' | 'after')[]) {
            const all = ['before', 'in', 'after'] as const;
            const want = new Set(target);
            const isOn = (op: 'before' | 'in' | 'after') =>
                screen.getByRole('checkbox', { name: op }).getAttribute('aria-checked') === 'true';
            const current = () => new Set(all.filter(isOn));
            const yieldsClause = (s: Set<string>) => s.size === 1 || s.size === 2;
            const pending = () => all.filter((op) => isOn(op) !== want.has(op));

            let moves = pending();
            while (moves.length > 0) {
                const safe = moves.find((op) => {
                    const next = current();
                    if (isOn(op)) {
                        next.delete(op);
                    } else {
                        next.add(op);
                    }
                    return yieldsClause(next);
                });
                const move = safe ?? moves[0];
                await user.click(screen.getByRole('checkbox', { name: move }));
                moves = pending();
            }
        }

        it('emits no clause when the year is empty even though "in" is selected', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            expect(mockOnChange).toHaveBeenLastCalledWith('');
        });

        it('emits "in" as a canonical temporal equality', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate = "1999"');
        });

        it('emits "before" as less-than the year period', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            mockOnChange.mockClear();
            await setOps(user, ['before']);
            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate < "1999"');
        });

        it('emits "after" as greater-than the year period', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            mockOnChange.mockClear();
            await setOps(user, ['after']);
            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate > "1999"');
        });

        it('emits "before+in" as on-or-before the year period', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            mockOnChange.mockClear();
            await setOps(user, ['before', 'in']);
            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate <= "1999"');
        });

        it('emits "in+after" as on-or-after the year period', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            mockOnChange.mockClear();
            await setOps(user, ['in', 'after']);
            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate >= "1999"');
        });

        it('emits "before+after" as not equal to the year period', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            mockOnChange.mockClear();
            await setOps(user, ['before', 'after']);
            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate != "1999"');
        });

        it('emits no clause when no comparison is selected', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            mockOnChange.mockClear();
            await user.click(screen.getByRole('checkbox', { name: 'in' }));
            expect(mockOnChange).toHaveBeenLastCalledWith('');
        });

        it('retains the neutral all-selected draft after its controlled query rerender', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            mockOnChange.mockClear();
            await user.click(screen.getByRole('checkbox', { name: 'after' }));
            expect(mockOnChange).toHaveBeenLastCalledWith('');
            expect(yearInput()).toHaveValue(1999);
            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'true');
        });

        it('retains the neutral all-selected draft when the host defers the query round-trip', async () => {
            const user = userEvent.setup();
            render(<DeferredControlledBuilder />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            await user.click(screen.getByRole('checkbox', { name: 'after' }));

            expect(yearInput()).toHaveValue(1999);
            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'true');

            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            expect(yearInput()).toHaveValue(1999);
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'true');
        });

        it('emits the retained year immediately when one neutral choice is deselected', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            await user.click(screen.getByRole('checkbox', { name: 'after' }));
            mockOnChange.mockClear();

            await user.click(screen.getByRole('checkbox', { name: 'before' }));

            expect(mockOnChange).toHaveBeenLastCalledWith('publishDate >= "1999"');
        });

        it('preserves unrelated clauses while entering and leaving the neutral state', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder initial='contentType = "movie" and tag has "fav"' onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            mockOnChange.mockClear();
            await user.click(screen.getByRole('checkbox', { name: 'after' }));

            expect(mockOnChange).toHaveBeenLastCalledWith('contentType = "movie" and tag has "fav"');
            expect(yearInput()).toHaveValue(1999);

            await user.click(screen.getByRole('checkbox', { name: 'in' }));

            expect(mockOnChange).toHaveBeenLastCalledWith(
                'contentType = "movie" and tag has "fav" and publishDate != "1999"',
            );
        });

        it('retains the neutral draft when the content selection emits a controlled rerender', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder initial='contentType = "movie" and tag has "fav"' onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            await user.click(screen.getByRole('checkbox', { name: 'after' }));
            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));
            await user.click(await screen.findByRole('option', { name: 'TV Shows' }));

            expect(mockOnChange).toHaveBeenLastCalledWith('contentType = "tv" and tag has "fav"');
            expect(yearInput()).toHaveValue(1999);
            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'true');

            await user.click(screen.getByRole('checkbox', { name: 'before' }));

            expect(mockOnChange).toHaveBeenLastCalledWith(
                'contentType = "tv" and tag has "fav" and publishDate >= "1999"',
            );
        });

        it('clears the retained neutral draft when the year input is explicitly cleared', async () => {
            const user = userEvent.setup();
            render(<ControlledBuilder onChange={mockOnChange} />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            await user.click(screen.getByRole('checkbox', { name: 'after' }));
            await user.clear(yearInput());

            expect(yearInput()).toHaveValue(null);
            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'false');
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'false');
        });

        it('clears the retained neutral draft when the parent explicitly resets the query', async () => {
            const user = userEvent.setup();
            render(<ResettableControlledBuilder initial='tag has "fav"' />);

            await user.type(yearInput(), '1999');
            await user.click(screen.getByRole('checkbox', { name: 'before' }));
            await user.click(screen.getByRole('checkbox', { name: 'after' }));
            expect(yearInput()).toHaveValue(1999);

            await user.click(screen.getByRole('button', { name: 'Reset' }));

            expect(yearInput()).toHaveValue(null);
            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'false');
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'false');
        });

        it('round-trips each canonical publishDate form back into year + selected ops', () => {
            const cases: { query: string; year: number; on: ('before' | 'in' | 'after')[] }[] = [
                { query: 'publishDate = "1999"', year: 1999, on: ['in'] },
                { query: 'publishDate < "1999"', year: 1999, on: ['before'] },
                { query: 'publishDate > "1999"', year: 1999, on: ['after'] },
                { query: 'publishDate <= "1999"', year: 1999, on: ['before', 'in'] },
                { query: 'publishDate >= "1999"', year: 1999, on: ['in', 'after'] },
                { query: 'publishDate != "1999"', year: 1999, on: ['before', 'after'] },
            ];

            for (const { query, year, on } of cases) {
                const { unmount } = render(<SimpleFilterBuilder value={query} onChange={mockOnChange} />);

                expect(screen.getByLabelText('Release year')).toHaveValue(year);
                for (const op of ['before', 'in', 'after'] as const) {
                    expect(screen.getByRole('checkbox', { name: op })).toHaveAttribute(
                        'aria-checked',
                        on.includes(op) ? 'true' : 'false',
                    );
                }

                expect(isSimpleQuery(query, 'content')).toBe(true);
                unmount();
            }
        });

        it('parses a legacy range and normalizes it to canonical form on edit', async () => {
            const user = userEvent.setup();
            const query =
                'contentType = "movie" and (publishDate >= "2010-01-01" and publishDate <= "2010-12-31") and tag has "fav"';

            render(<ControlledBuilder initial={query} onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');
            expect(screen.getByLabelText('Release year')).toHaveValue(2010);
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'false');
            expect(screen.getByRole('checkbox', { name: 'before' })).toHaveAttribute('aria-checked', 'false');
            expect(screen.getByLabelText('Tag')).toHaveValue('fav');

            await user.type(screen.getByLabelText('Tag'), 'orite');

            expect(mockOnChange).toHaveBeenLastCalledWith(
                'contentType = "movie" and tag has "favorite" and publishDate = "2010"',
            );
        });
    });

    describe('build/parse round-trip - subject context', () => {
        it('round-trips kind field', async () => {
            const user = userEvent.setup();
            const { rerender } = render(<SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />);

            const kindTrigger = screen.getByRole('combobox');
            await user.click(kindTrigger);
            const personOption = await screen.findByRole('option', { name: 'Person' });
            await user.click(personOption);

            expect(mockOnChange).toHaveBeenCalledWith('kind = "person"');

            mockOnChange.mockClear();
            rerender(<SimpleFilterBuilder value='kind = "person"' onChange={mockOnChange} context="subject" />);

            const newTrigger = screen.getByRole('combobox');
            expect(newTrigger).toHaveTextContent('Person');
        });

        it('renders the real subject kinds from the registry and not the old hardcoded list', async () => {
            const user = userEvent.setup();
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" />);

            await user.click(screen.getByRole('combobox'));

            expect(await screen.findByRole('option', { name: 'Person' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Studio' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Author' })).toBeInTheDocument();

            expect(screen.queryByRole('option', { name: 'movie' })).not.toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'show' })).not.toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'album' })).not.toBeInTheDocument();
        });

        it('round-trips a subject query across the curated fields', () => {
            const query = 'kind = "studio" and title contains "Inception" and tag has "favorite"';

            render(<SimpleFilterBuilder value={query} onChange={mockOnChange} context="subject" />);

            const kindTrigger = screen.getByRole('combobox');
            expect(kindTrigger).toHaveTextContent('Studio');
            expect(screen.getByLabelText('Related')).toHaveValue('Inception');
            expect(screen.getByLabelText('Tag')).toHaveValue('favorite');

            expect(screen.queryByLabelText('Genre')).not.toBeInTheDocument();
        });
    });

    describe('parsing edge cases', () => {
        it('handles empty query string', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Any');
            expect(screen.getByLabelText('Release year')).toHaveValue(null);
            expect(screen.getByLabelText('Title')).toHaveValue('');
            expect(screen.getByLabelText('Genre')).toHaveValue('');
            expect(screen.getByLabelText('Tag')).toHaveValue('');
        });

        it('handles whitespace-only query string', () => {
            render(<SimpleFilterBuilder value="   " onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Any');
            expect(screen.getByLabelText('Release year')).toHaveValue(null);
        });

        it('handles case-insensitive field names with preserved monitoring', () => {
            render(<SimpleFilterBuilder value='CONTENTTYPE = "movie" AND MONITORING = true' onChange={mockOnChange} />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');
            expect(isSimpleQuery('CONTENTTYPE = "movie" AND MONITORING = true', 'content')).toBe(true);
        });

        it('parses a case-insensitive publishDate clause', () => {
            render(<SimpleFilterBuilder value='PUBLISHDATE >= "2010-01-01"' onChange={mockOnChange} />);

            expect(screen.getByLabelText('Release year')).toHaveValue(2010);
            expect(screen.getByRole('checkbox', { name: 'in' })).toHaveAttribute('aria-checked', 'true');
            expect(screen.getByRole('checkbox', { name: 'after' })).toHaveAttribute('aria-checked', 'true');
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

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');
            expect(screen.getByLabelText('Title')).toHaveValue('test');
        });
    });

    describe('defaultContentType (type-scoped prefill)', () => {
        it('shows the default content type pre-selected when the query has none', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} defaultContentType="movie" />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Movies');
        });

        it('does not write the default into the query (display only)', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} defaultContentType="movie" />);

            expect(mockOnChange).not.toHaveBeenCalled();
        });

        it('an explicit content-type clause in the query overrides the default', () => {
            render(
                <SimpleFilterBuilder value='contentType = "tv"' onChange={mockOnChange} defaultContentType="movie" />,
            );

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('TV Shows');
        });
    });

    describe('restrictContentType / lockContentLevel (route-scoped content filter)', () => {
        it('lists only the restricted content type and its roots and drops the Any option', async () => {
            const user = userEvent.setup();
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} restrictContentType="books" />);

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));

            expect(await screen.findByRole('option', { name: 'Books' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Books > E-book' })).toBeInTheDocument();
            expect(screen.getByRole('option', { name: 'Books > Audiobook' })).toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'Any' })).not.toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'Movies' })).not.toBeInTheDocument();
            expect(screen.queryByRole('option', { name: 'TV Shows' })).not.toBeInTheDocument();
        });

        it('pre-selects the restricted type without writing it into the query', () => {
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} restrictContentType="books" />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveTextContent('Books');
            expect(mockOnChange).not.toHaveBeenCalled();
        });

        it('filters in place by writing the level predicate when a root is chosen', async () => {
            const user = userEvent.setup();
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} restrictContentType="books" />);

            await user.click(screen.getByRole('combobox', { name: 'Content Type' }));
            await user.click(await screen.findByRole('option', { name: 'Books > E-book' }));

            expect(mockOnChange).toHaveBeenLastCalledWith('contentType = "books" and level = "E-book_V1"');
        });

        it('locks the control to the route level and disables it when a level is locked', () => {
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(
                <SimpleFilterBuilder
                    value=""
                    onChange={mockOnChange}
                    restrictContentType="books"
                    lockContentLevel="E-book_V1"
                />,
            );

            const trigger = screen.getByRole('combobox', { name: 'Content Type' });
            expect(trigger).toHaveTextContent('Books > E-book');
            expect(trigger).toHaveAttribute('data-disabled');
        });

        it('does not emit a content-type or level predicate when another field changes while locked', () => {
            mockLibraryTypes = { ...mockLibraryTypes, contentTypes: hierarchicalContentTypes };
            render(
                <SimpleFilterBuilder
                    value=""
                    onChange={mockOnChange}
                    restrictContentType="books"
                    lockContentLevel="E-book_V1"
                />,
            );

            fireEvent.change(screen.getByLabelText('Tag'), { target: { value: 'fav' } });

            expect(mockOnChange).toHaveBeenLastCalledWith('tag has "fav"');
        });
    });

    describe('defaultSubjectKind (route-scoped prefill)', () => {
        it('shows the default subject kind pre-selected when the query has none', () => {
            render(
                <SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" defaultSubjectKind="person" />,
            );

            expect(screen.getByRole('combobox', { name: 'Kind' })).toHaveTextContent('Person');
        });

        it('does not write the default into the query', () => {
            render(
                <SimpleFilterBuilder value="" onChange={mockOnChange} context="subject" defaultSubjectKind="person" />,
            );

            expect(mockOnChange).not.toHaveBeenCalled();
        });

        it('lets an explicit subject-kind clause override the default when unlocked', () => {
            render(
                <SimpleFilterBuilder
                    value='kind = "studio"'
                    onChange={mockOnChange}
                    context="subject"
                    defaultSubjectKind="person"
                />,
            );

            expect(screen.getByRole('combobox', { name: 'Kind' })).toHaveTextContent('Studio');
        });

        it('shows and disables the route kind when locked', () => {
            render(
                <SimpleFilterBuilder
                    value='kind = "studio"'
                    onChange={mockOnChange}
                    context="subject"
                    defaultSubjectKind="person"
                    lockSubjectKind
                />,
            );

            const kind = screen.getByRole('combobox', { name: 'Kind' });
            expect(kind).toHaveTextContent('Person');
            expect(kind).toHaveAttribute('data-disabled');
        });

        it('does not re-emit a conflicting kind when another field changes while locked', () => {
            render(
                <SimpleFilterBuilder
                    value='kind = "studio"'
                    onChange={mockOnChange}
                    context="subject"
                    defaultSubjectKind="person"
                    lockSubjectKind
                />,
            );

            fireEvent.change(screen.getByLabelText('Related'), { target: { value: 'A24' } });

            expect(mockOnChange).toHaveBeenLastCalledWith('title contains "A24"');
        });
    });

    describe('isSimpleQuery helper', () => {
        it('returns true for empty query', () => {
            expect(isSimpleQuery('', 'content')).toBe(true);
            expect(isSimpleQuery('   ', 'content')).toBe(true);
        });

        it('returns true for valid content queries', () => {
            expect(isSimpleQuery('contentType = "movie"', 'content')).toBe(true);
            expect(isSimpleQuery('monitoring = true', 'content')).toBe(true);
            expect(isSimpleQuery('title contains "test"', 'content')).toBe(true);
            expect(isSimpleQuery('genre has "Action"', 'content')).toBe(true);
            expect(isSimpleQuery('tag has "favorite"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate = "1999"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate < "1999"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate > "1999"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate <= "1999"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate >= "1999"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate != "1999"', 'content')).toBe(true);
            expect(isSimpleQuery('(publishDate >= "1999-01-01" and publishDate <= "1999-12-31")', 'content')).toBe(
                true,
            );
            expect(isSimpleQuery('publishDate < "1999-01-01"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate > "1999-12-31"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate <= "1999-12-31"', 'content')).toBe(true);
            expect(isSimpleQuery('publishDate >= "1999-01-01"', 'content')).toBe(true);
            expect(isSimpleQuery('(publishDate < "1999-01-01" or publishDate > "1999-12-31")', 'content')).toBe(true);
            expect(isSimpleQuery('contentType = "movie" and monitoring = true', 'content')).toBe(true);
            expect(isSimpleQuery('Relationships has (Name contains "Johansson" and Type = "person")', 'content')).toBe(
                true,
            );
            expect(
                isSimpleQuery(
                    'contentType = "movie" and Relationships has (Name contains "Johansson" and Type = "person")',
                    'content',
                ),
            ).toBe(true);
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
            expect(isSimpleQuery('(contentType = "movie" and monitoring = true)', 'content')).toBe(false);
        });

        it('returns false for NOT conditions', () => {
            expect(isSimpleQuery('not monitoring = true', 'content')).toBe(false);
        });
    });

    describe('disabled state', () => {
        it('disables all inputs when readOnly is true', () => {
            render(<SimpleFilterBuilder value="" onChange={mockOnChange} readOnly />);

            expect(screen.getByRole('combobox', { name: 'Content Type' })).toHaveAttribute('data-disabled');

            expect(screen.getByLabelText('Release year')).toBeDisabled();
            expect(screen.getByRole('checkbox', { name: 'in' })).toBeDisabled();
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
