import { act, render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { MetadataInspector } from './MetadataInspector';

const mockUseInspector = vi.fn();
const mockUseSubjectInspector = vi.fn();
const mockPatchMutate = vi.fn();
const mockReorderContent = vi.fn();
const mockReorderSubject = vi.fn();
const mockToast = vi.fn();
let mockContentDetailData: unknown = null;

let mockHasRole = (_role: number) => true;

vi.mock('@/hooks/useContent', () => ({
    useMetadataInspector: (id: string | undefined) => mockUseInspector(id),
    useContentDetail: () => ({ data: mockContentDetailData, isLoading: false, error: null }),
    usePatchMetadataInspector: () => ({ mutate: mockPatchMutate, isPending: false }),
    useReorderMetadataContributions: () => ({ mutateAsync: mockReorderContent, isPending: false }),
}));

vi.mock('@/hooks/useSubjectMetadataInspector', () => ({
    useSubjectMetadataInspector: (id: string | undefined) => mockUseSubjectInspector(id),
    usePatchSubjectMetadataInspector: () => ({ mutate: vi.fn(), isPending: false }),
    useReorderSubjectMetadataContributions: () => ({ mutateAsync: mockReorderSubject, isPending: false }),
}));

vi.mock('@/auth', async () => {
    const actual = await vi.importActual<typeof import('@/auth')>('@/auth');
    return {
        ...actual,
        useAuth: () => ({
            user: { id: 'u1', username: 'mgr', role: 2 },
            isAuthenticated: true,
            hasPermission: () => true,
            hasRole: (role: number) => mockHasRole(role),
        }),
    };
});

const mockSubjects = vi.fn(() => ({ subjects: [] as unknown[], isLoading: false }));
let mockLibraryTypes: {
    contentTypes: unknown[];
    subjectTypes: unknown[];
} = {
    contentTypes: [],
    subjectTypes: [{ slug: 'person', name: 'Person', iconName: null, levels: [] }],
};
vi.mock('@/hooks/useSubjects', () => ({
    useSubjects: () => mockSubjects(),
    useSubject: () => ({ subject: null, isLoading: false }),
}));
vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...actual,
        useLibraryTypes: () => mockLibraryTypes,
    };
});

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: mockToast }),
}));

function userContribution(overrides: Record<string, unknown> = {}) {
    return {
        id: 'contrib-user',
        providerType: 'user',
        ordinal: -1,
        source: 'user',
        dateLastRefreshed: null,
        ...overrides,
    };
}

function providerContribution(overrides: Record<string, unknown> = {}) {
    return {
        id: 'contrib-a',
        providerType: 'provider-a',
        ordinal: 0,
        source: 'provider-a:42',
        dateLastRefreshed: null,
        ...overrides,
    };
}

const SCALAR_KEYS = [
    'title',
    'originalTitle',
    'sortTitle',
    'language',
    'overview',
    'publishDate',
    'duration',
    'sequenceNumber',
    'productionStatus',
    'established',
    'dissolved',
    'birthDate',
    'deathDate',
];

function materializedFrom(contributions: Array<Record<string, unknown>>): Record<string, unknown> {
    const sorted = [...contributions].sort((a, b) => ((a.ordinal as number) ?? 0) - ((b.ordinal as number) ?? 0));
    const materialized: Record<string, unknown> = { containerId: 'mc1' };
    for (const key of SCALAR_KEYS) {
        for (const contribution of sorted) {
            const value = contribution[key];
            if (value !== null && value !== undefined && value !== '') {
                materialized[key] = value;
                break;
            }
        }
    }
    return materialized;
}

function inspectorData(
    contributions: Array<Record<string, unknown>>,
    subjects: Record<string, { subjectId: string; name: string }> = {},
) {
    return {
        data: {
            materialized: materializedFrom(contributions),
            contributions,
            subjects,
        },
        isLoading: false,
        error: null,
    };
}

function saveChanges() {
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
}

function lastPatch(): Record<string, unknown> {
    return mockPatchMutate.mock.calls[0]?.[0] as Record<string, unknown>;
}

describe('MetadataInspector', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockContentDetailData = null;
        mockLibraryTypes = {
            contentTypes: [],
            subjectTypes: [{ slug: 'person', name: 'Person', iconName: null, levels: [] }],
        };
        mockHasRole = () => true;
        mockUseSubjectInspector.mockReturnValue({ data: undefined, isLoading: false, error: null });
        mockReorderContent.mockResolvedValue(undefined);
        mockReorderSubject.mockResolvedValue(undefined);
        mockSubjects.mockReturnValue({ subjects: [], isLoading: false });
    });

    it('renders a skeleton while loading', () => {
        mockUseInspector.mockReturnValue({ data: undefined, isLoading: true, error: null });

        const { container } = render(<MetadataInspector contentId="c1" />);

        expect(container.querySelector('.metadata-inspector__skeleton')).not.toBeNull();
    });

    it('renders the EmptyState on error', () => {
        mockUseInspector.mockReturnValue({ data: undefined, isLoading: false, error: new Error('boom') });

        render(<MetadataInspector contentId="c1" />);

        expect(screen.getByText('Metadata unavailable')).toBeInTheDocument();
    });

    it('resolves winners from contribution field values and renders the stack', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution({ title: 'Primary Title' }),
                providerContribution({
                    id: 'contrib-b',
                    providerType: 'provider-j',
                    ordinal: 1,
                    source: null,
                    overview: 'Provider Overview',
                }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        expect(screen.getByText('Primary Title')).toBeInTheDocument();
        expect(screen.getByText('Provider Overview')).toBeInTheDocument();
        const title = document.querySelector('[data-field="Title"]') as HTMLElement;
        expect(within(title).getByText('provider-a')).toBeInTheDocument();
        expect(screen.getByText('Contribution stack')).toBeInTheDocument();
    });

    it('marks an edited row unsaved with its overridden value and source', async () => {
        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ title: 'Primary Title' })]));

        render(<MetadataInspector contentId="c1" />);
        fireEvent.click(screen.getByRole('button', { name: 'Edit Title' }));
        await waitFor(() => expect(screen.getByText('Override Title')).toBeInTheDocument());
        fireEvent.change(screen.getByLabelText('Value'), { target: { value: 'Operator Title' } });
        fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

        const title = document.querySelector('[data-field="Title"]') as HTMLElement;
        expect(title).toHaveAttribute('data-pending', 'true');
        expect(within(title).getByText('Operator Title')).toBeInTheDocument();
        expect(within(title).getByText('Primary Title')).toBeInTheDocument();
        expect(within(title).getByText('provider-a')).toBeInTheDocument();
        expect(within(title).getByText('unsaved')).toBeInTheDocument();
        expect(screen.getByTestId('edited-field-count')).toHaveTextContent('1 edited');
        expect(screen.getByTestId('unsaved-changes')).toHaveTextContent('1 unsaved change');
    });

    it('leaves an unedited row quiet when providers disagree', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution({ title: 'Primary Title' }),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1, title: 'Other Title' }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const title = document.querySelector('[data-field="Title"]') as HTMLElement;
        expect(title).not.toHaveAttribute('data-pending');
        expect(within(title).queryByText('Other Title')).not.toBeInTheDocument();
        expect(within(title).queryByText('unsaved')).not.toBeInTheDocument();
        expect(screen.queryByTestId('edited-field-count')).not.toBeInTheDocument();
    });

    it('pins the user contribution above every provider contribution', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
                userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }),
                providerContribution(),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const rows = document.querySelectorAll('.metadata-inspector__stack-item');
        expect(rows[0]).toHaveAttribute('data-user-contribution', 'true');
        expect(rows[1]).toHaveAttribute('data-provider-type', 'provider-a');
        expect(rows[2]).toHaveAttribute('data-provider-type', 'provider-b');
    });

    it('renders no ordinal for the user contribution', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }), providerContribution()]),
        );

        render(<MetadataInspector contentId="c1" />);

        const userRow = document.querySelector('[data-user-contribution]') as HTMLElement;
        const providerRow = document.querySelector('[data-provider-type="provider-a"]') as HTMLElement;
        expect(userRow.querySelector('.metadata-inspector__stack-ordinal')).toBeNull();
        expect(within(providerRow).getByText('#0')).toBeInTheDocument();
    });

    it('renders no drag handle for the user contribution', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }), providerContribution()]),
        );

        render(<MetadataInspector contentId="c1" />);

        const userRow = document.querySelector('[data-user-contribution]') as HTMLElement;
        const providerRow = document.querySelector('[data-provider-type="provider-a"]') as HTMLElement;
        expect(within(userRow).queryByTestId('metadata-inspector__stack-drag-handle')).toBeNull();
        expect(within(providerRow).getByTestId('metadata-inspector__stack-drag-handle')).toBeInTheDocument();
    });

    it('does not make the user contribution a drop target', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }), providerContribution()]),
        );

        render(<MetadataInspector contentId="c1" />);

        const userRow = document.querySelector('[data-user-contribution]') as HTMLElement;
        expect(userRow).not.toHaveAttribute('data-sortable-list-item-id');
    });

    it('does not allow a provider contribution to cross the pinned user contribution', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }),
                providerContribution(),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
            ]),
        );
        const transferred = new Map<string, string>();
        const dataTransfer = {
            dropEffect: 'none',
            effectAllowed: 'none',
            setData: vi.fn((type: string, value: string) => transferred.set(type, value)),
            getData: vi.fn((type: string) => transferred.get(type) ?? ''),
            setDragImage: vi.fn(),
        };

        render(<MetadataInspector contentId="c1" />);

        const userRow = document.querySelector('[data-user-contribution]') as HTMLElement;
        const providerB = document.querySelector('[data-provider-type="provider-b"]') as HTMLElement;
        fireEvent.dragStart(within(providerB).getByTestId('metadata-inspector__stack-drag-handle'), {
            dataTransfer,
        });
        fireEvent.dragOver(userRow, { dataTransfer, clientY: 0 });
        fireEvent.drop(userRow, { dataTransfer });

        const rows = document.querySelectorAll('.metadata-inspector__stack-item');
        expect(mockReorderContent).not.toHaveBeenCalled();
        expect(rows[0]).toHaveAttribute('data-user-contribution', 'true');
    });

    it('reorders provider contributions by dragging the dedicated handle', async () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution(),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
            ]),
        );
        const transferred = new Map<string, string>();
        const dataTransfer = {
            dropEffect: 'none',
            effectAllowed: 'none',
            setData: vi.fn((type: string, value: string) => transferred.set(type, value)),
            getData: vi.fn((type: string) => transferred.get(type) ?? ''),
            setDragImage: vi.fn(),
        };

        render(<MetadataInspector contentId="c1" />);

        const providerA = document.querySelector('[data-provider-type="provider-a"]') as HTMLElement;
        const providerB = document.querySelector('[data-provider-type="provider-b"]') as HTMLElement;
        vi.spyOn(providerB, 'getBoundingClientRect').mockReturnValue({
            x: 0,
            y: 0,
            top: 0,
            right: 100,
            bottom: 100,
            left: 0,
            width: 100,
            height: 100,
            toJSON: () => ({}),
        });
        const handle = within(providerA).getByTestId('metadata-inspector__stack-drag-handle');
        expect(providerA).not.toHaveAttribute('draggable');
        expect(handle).toHaveAttribute('draggable', 'true');

        fireEvent.dragStart(handle, { dataTransfer });
        expect(dataTransfer.setData).toHaveBeenCalledWith('text/plain', 'contrib-a');
        fireEvent.dragOver(providerB, { dataTransfer });
        expect(providerB).toHaveClass('sortable-list__item--drop-after');
        fireEvent.drop(providerB, { dataTransfer });

        await waitFor(() => expect(mockReorderContent).toHaveBeenCalledWith(['contrib-b', 'contrib-a']));
    });

    it('reorders provider contributions with a touch drag on the handle', async () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution(),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const providerA = document.querySelector('[data-provider-type="provider-a"]') as HTMLElement;
        const providerB = document.querySelector('[data-provider-type="provider-b"]') as HTMLElement;
        vi.spyOn(providerA, 'getBoundingClientRect').mockReturnValue({
            x: 0,
            y: 0,
            top: 0,
            right: 100,
            bottom: 100,
            left: 0,
            width: 100,
            height: 100,
            toJSON: () => ({}),
        });
        Object.defineProperty(document, 'elementFromPoint', {
            configurable: true,
            value: vi.fn(() => providerA),
        });
        const handle = within(providerB).getByTestId('metadata-inspector__stack-drag-handle');

        fireEvent.touchStart(handle, { touches: [{ clientX: 1, clientY: 1 }] });
        fireEvent.touchMove(handle, { touches: [{ clientX: 1, clientY: 1 }] });
        fireEvent.touchEnd(handle, { changedTouches: [{ clientX: 1, clientY: 1 }] });

        await waitFor(() => expect(mockReorderContent).toHaveBeenCalledWith(['contrib-b', 'contrib-a']));
    });

    it('updates provider ordinals and the resolved winner during an optimistic keyboard move', async () => {
        let resolveReorder: (() => void) | undefined;
        mockReorderContent.mockReturnValue(
            new Promise<void>((resolve) => {
                resolveReorder = resolve;
            }),
        );
        const user = userContribution({ genres: [{ id: 'g-user', name: 'Action' }] });
        const providerA = providerContribution({ title: 'Primary title' });
        const providerB = providerContribution({
            id: 'contrib-b',
            providerType: 'provider-b',
            ordinal: 1,
            title: 'Secondary title',
        });
        mockUseInspector.mockReturnValue(inspectorData([user, providerA, providerB]));

        const { rerender } = render(<MetadataInspector contentId="c1" />);
        fireEvent.click(screen.getByRole('button', { name: 'Expand provider-b contribution' }));

        fireEvent.click(screen.getByRole('button', { name: 'Move provider-b contribution up' }));

        expect(mockReorderContent).toHaveBeenCalledWith(['contrib-b', 'contrib-a']);
        const optimisticRows = document.querySelectorAll('.metadata-inspector__stack-item');
        expect(optimisticRows[0]).toHaveAttribute('data-user-contribution', 'true');
        expect(optimisticRows[1]).toHaveAttribute('data-provider-type', 'provider-b');
        expect(within(optimisticRows[1] as HTMLElement).getByText('#0')).toBeInTheDocument();
        expect(within(optimisticRows[2] as HTMLElement).getByText('#1')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Collapse provider-b contribution' })).toHaveAttribute(
            'aria-expanded',
            'true',
        );
        const resolvedTitle = document.querySelector('tr[data-field="Title"]') as HTMLElement;
        expect(within(resolvedTitle).getByText('Secondary title')).toBeInTheDocument();
        expect(within(resolvedTitle).getByText('provider-b')).toBeInTheDocument();

        mockUseInspector.mockReturnValue(
            inspectorData([user, { ...providerA, ordinal: 1 }, { ...providerB, ordinal: 0 }]),
        );
        rerender(<MetadataInspector contentId="c1" />);
        await act(async () => resolveReorder?.());

        await waitFor(() => {
            const persistedRows = document.querySelectorAll('.metadata-inspector__stack-item');
            expect(persistedRows[1]).toHaveAttribute('data-provider-type', 'provider-b');
        });
    });

    it('rolls back an optimistic provider move and shows an error when persistence fails', async () => {
        let rejectReorder: ((error: Error) => void) | undefined;
        mockReorderContent.mockReturnValue(
            new Promise<void>((_resolve, reject) => {
                rejectReorder = reject;
            }),
        );
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution(),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        fireEvent.click(screen.getByRole('button', { name: 'Move provider-b contribution up' }));
        let rows = document.querySelectorAll('.metadata-inspector__stack-item');
        expect(rows[0]).toHaveAttribute('data-provider-type', 'provider-b');

        await act(async () => rejectReorder?.(new Error('stale order')));

        await waitFor(() => {
            rows = document.querySelectorAll('.metadata-inspector__stack-item');
            expect(rows[0]).toHaveAttribute('data-provider-type', 'provider-a');
        });
        expect(mockToast).toHaveBeenCalledWith(
            expect.objectContaining({
                title: 'Failed to reorder contributions',
                description: 'stale order',
                variant: 'error',
            }),
        );
    });

    it.each(['Viewer', 'Requester'])('hides reorder controls from %s roles', () => {
        mockHasRole = () => false;
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution(),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        expect(screen.queryAllByTestId('metadata-inspector__stack-drag-handle')).toHaveLength(0);
        expect(screen.queryByRole('button', { name: 'Move provider-b contribution up' })).toBeNull();
    });

    it('uses the subject contribution reorder contract for a subject inspector', async () => {
        mockUseSubjectInspector.mockReturnValue(
            inspectorData([
                providerContribution(),
                providerContribution({ id: 'contrib-b', providerType: 'provider-b', ordinal: 1 }),
            ]),
        );

        render(<MetadataInspector owner={{ kind: 'subject', id: 's1' }} />);

        fireEvent.click(screen.getByRole('button', { name: 'Move provider-b contribution up' }));

        await waitFor(() => expect(mockReorderSubject).toHaveBeenCalledWith(['contrib-b', 'contrib-a']));
        expect(mockReorderContent).not.toHaveBeenCalled();
    });

    it('starts clean with Save disabled and no unsaved-changes hint', () => {
        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ title: 'Primary' })]));

        render(<MetadataInspector contentId="c1" />);

        expect(screen.getByTestId('no-changes')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled();
    });

    it('formats an ISO publish date instead of rendering the raw timestamp', () => {
        const isoDate = '2025-11-21T00:00:00.0000000';
        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ publishDate: isoDate })]));

        render(<MetadataInspector contentId="c1" />);

        const field = document.querySelector('[data-field="PublishDate"]') as HTMLElement;
        expect(within(field).getByText('Nov 21, 2025')).toBeInTheDocument();
        expect(screen.queryByText(isoDate)).not.toBeInTheDocument();
    });

    it('uses owner field definitions for scalar presence, labels, and stages an override', async () => {
        mockContentDetailData = { contentType: 'movie', level: 'movie' };
        mockLibraryTypes = {
            contentTypes: [
                {
                    id: 'movie',
                    name: 'Movie',
                    pluralName: 'Movies',
                    slug: 'movie',
                    levels: [
                        {
                            slug: 'movie',
                            displayName: 'Movie',
                            parentSlug: null,
                            standardMetadataFields: [
                                {
                                    field: 'Title',
                                    key: 'title',
                                    displayName: 'Title',
                                    dataType: 'string',
                                    valueKind: 'text',
                                    editable: true,
                                },
                                {
                                    field: 'PublishDate',
                                    key: 'publishDate',
                                    displayName: 'Publish date',
                                    dataType: 'date',
                                    valueKind: 'date',
                                    editable: true,
                                },
                            ],
                            customMetadataFields: [],
                            standardFactFields: [
                                { key: 'title', displayName: 'Name' },
                                { key: 'publishDate', displayName: 'Released' },
                            ],
                        },
                    ],
                },
            ],
            subjectTypes: [{ slug: 'person', name: 'Person', iconName: null, levels: [] }],
        };
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution({
                    title: 'Defined Title',
                    publishDate: '2025-11-21T00:00:00.0000000',
                    overview: 'Hidden Overview',
                }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const titleRow = document.querySelector('[data-field="Title"]') as HTMLElement;
        expect(within(titleRow).getByText('Name')).toBeInTheDocument();
        expect(within(titleRow).getByText('Defined Title')).toBeInTheDocument();
        expect(screen.getByText('Released')).toBeInTheDocument();
        expect(screen.queryByText('Hidden Overview')).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Edit Overview' })).not.toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Edit Name' }));
        await waitFor(() => expect(screen.getByText('Override Name')).toBeInTheDocument());
        fireEvent.change(screen.getByLabelText('Value'), { target: { value: 'Operator Title' } });
        fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().title).toEqual({ value: 'Operator Title' });
    });

    it('uses owner definitions for sub-item sections and custom value labels', () => {
        mockContentDetailData = { contentType: 'movie', level: 'movie' };
        mockLibraryTypes = {
            contentTypes: [
                {
                    id: 'movie',
                    name: 'Movie',
                    pluralName: 'Movies',
                    slug: 'movie',
                    levels: [
                        {
                            slug: 'movie',
                            displayName: 'Movie',
                            parentSlug: null,
                            standardMetadataFields: [
                                {
                                    field: 'Genres',
                                    key: 'genres',
                                    displayName: 'Genres',
                                    dataType: 'collection',
                                    valueKind: 'collection',
                                    editable: false,
                                },
                            ],
                            customMetadataFields: [{ key: 'mood', displayName: 'Mood' }],
                            standardFactFields: [],
                        },
                    ],
                },
            ],
            subjectTypes: [{ slug: 'person', name: 'Person', iconName: null, levels: [] }],
        };
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution({
                    genres: [{ id: 'g1', name: 'Action' }],
                    contentRatings: [{ id: 'cr1', body: 'MPAA', rating: 'PG-13' }],
                    customMetadata: [{ id: 'cv1', key: 'mood', value: 'tense' }],
                }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        expect(within(genres).getByText('Action')).toBeInTheDocument();
        expect(screen.getByText('Mood: tense')).toBeInTheDocument();
        expect(document.querySelector('[data-subsection="contentRatings"]')).not.toBeInTheDocument();
    });

    it('stages an override locally and issues a single patch on save', async () => {
        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ title: 'Primary Title' })]));

        render(<MetadataInspector contentId="c1" />);
        fireEvent.click(screen.getByRole('button', { name: 'Edit Title' }));

        await waitFor(() => expect(screen.getByText('Override Title')).toBeInTheDocument());

        fireEvent.change(screen.getByLabelText('Value'), { target: { value: 'Operator Title' } });
        fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

        expect(mockPatchMutate).not.toHaveBeenCalled();
        expect(screen.getByTestId('unsaved-changes')).toBeInTheDocument();

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().title).toEqual({ value: 'Operator Title' });
    });

    it('preserves a staged override when refreshed metadata arrives', async () => {
        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ title: 'Primary Title' })]));

        const { rerender } = render(<MetadataInspector contentId="c1" />);
        fireEvent.click(screen.getByRole('button', { name: 'Edit Title' }));
        await waitFor(() => expect(screen.getByText('Override Title')).toBeInTheDocument());
        fireEvent.change(screen.getByLabelText('Value'), { target: { value: 'Operator Title' } });
        fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ title: 'Refreshed Title' })]));
        rerender(<MetadataInspector contentId="c1" />);

        expect(screen.getByTestId('unsaved-changes')).toBeInTheDocument();
        saveChanges();
        expect(lastPatch().title).toEqual({ value: 'Operator Title' });
    });

    it('clears the user layer locally and saves a removal patch', async () => {
        mockUseInspector.mockReturnValue(
            inspectorData([userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }), providerContribution()]),
        );

        render(<MetadataInspector contentId="c1" />);

        fireEvent.click(screen.getByRole('button', { name: 'Clear user contribution' }));

        expect(mockPatchMutate).not.toHaveBeenCalled();
        expect(screen.getByTestId('unsaved-changes')).toBeInTheDocument();

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().genres).toEqual({ remove: ['Action'] });
        expect(lastPatch().title).toBeUndefined();
    });

    it('reverts staged changes without issuing a patch', async () => {
        mockUseInspector.mockReturnValue(inspectorData([providerContribution({ title: 'Primary Title' })]));

        render(<MetadataInspector contentId="c1" />);
        fireEvent.click(screen.getByRole('button', { name: 'Edit Title' }));
        await waitFor(() => expect(screen.getByText('Override Title')).toBeInTheDocument());
        fireEvent.change(screen.getByLabelText('Value'), { target: { value: 'Operator Title' } });
        fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

        expect(screen.getByTestId('unsaved-changes')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: 'Revert' }));

        expect(screen.getByTestId('no-changes')).toBeInTheDocument();
        expect(mockPatchMutate).not.toHaveBeenCalled();
    });

    it('renders read-only sub-item subsections aggregated across contributions', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                providerContribution({
                    genres: [{ id: 'g1', name: 'Action' }],
                    contentRatings: [{ id: 'cr1', body: 'MPAA', rating: 'PG-13' }],
                }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        expect(within(genres).getByText('Action')).toBeInTheDocument();

        const ratings = document.querySelector('[data-subsection="contentRatings"]') as HTMLElement;
        expect(within(ratings).getByText('MPAA: PG-13')).toBeInTheDocument();

        const references = document.querySelector('[data-subsection="references"]') as HTMLElement;
        expect(within(references).getByText(/no references/i)).toBeInTheDocument();
    });

    it('stages an added genre and includes it in the save patch', async () => {
        mockUseInspector.mockReturnValue(inspectorData([userContribution()]));

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        fireEvent.click(within(genres).getByRole('button', { name: /add genre/i }));

        await waitFor(() => expect(screen.getByText('Add genre')).toBeInTheDocument());

        fireEvent.click(screen.getByRole('button', { name: /^Add$/ }));
        expect(within(genres).queryByText('Thriller')).not.toBeInTheDocument();

        fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Thriller' } });
        fireEvent.click(screen.getByRole('button', { name: /^Add$/ }));

        expect(within(genres).getByText('Thriller')).toBeInTheDocument();

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().genres).toEqual({ add: ['Thriller'] });
    });

    it('stages a relationship via an existing subject', async () => {
        mockSubjects.mockReturnValue({
            subjects: [
                {
                    id: 'subj-1',
                    metadataContainerId: 'mc-s1',
                    kind: 'person',
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'mc-s1',
                        createdAt: '',
                        contributions: [{ ordinal: 0, title: 'Jane Doe' }],
                    },
                },
            ],
            isLoading: false,
        });
        mockUseInspector.mockReturnValue(inspectorData([userContribution()]));

        render(<MetadataInspector contentId="c1" />);

        const section = document.querySelector('[data-subsection="relationships"]') as HTMLElement;
        fireEvent.click(within(section).getByRole('button', { name: /add relationship/i }));

        await waitFor(() => expect(screen.getByText('Add relationship')).toBeInTheDocument());

        fireEvent.change(screen.getByLabelText(/Relationship kind/), { target: { value: 'cast' } });
        fireEvent.click(screen.getByRole('option', { name: /Jane Doe/ }));
        fireEvent.click(screen.getByRole('button', { name: /^Add$/ }));

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().relationships).toEqual({ add: [{ kind: { value: 'cast' }, subjectId: 'subj-1' }] });
    });

    it('stages a relationship via a newly created subject', async () => {
        mockUseInspector.mockReturnValue(inspectorData([userContribution()]));

        render(<MetadataInspector contentId="c1" />);

        const section = document.querySelector('[data-subsection="relationships"]') as HTMLElement;
        fireEvent.click(within(section).getByRole('button', { name: /add relationship/i }));

        await waitFor(() => expect(screen.getByText('Add relationship')).toBeInTheDocument());

        fireEvent.change(screen.getByLabelText(/Relationship kind/), { target: { value: 'director' } });
        fireEvent.click(screen.getByRole('radio', { name: /Create new/ }));
        fireEvent.change(screen.getByLabelText(/New subject name/), { target: { value: 'New Person' } });

        fireEvent.click(screen.getByLabelText(/New subject kind/));
        await waitFor(() => expect(screen.getByRole('option', { name: 'Person' })).toBeInTheDocument());
        fireEvent.click(screen.getByRole('option', { name: 'Person' }));

        fireEvent.click(screen.getByRole('button', { name: /^Add$/ }));

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().relationships).toEqual({
            add: [{ kind: { value: 'director' }, subjectName: 'New Person', subjectKind: 'person' }],
        });
    });

    it('stages an edited genre as a name swap on save', async () => {
        mockUseInspector.mockReturnValue(
            inspectorData([userContribution({ genres: [{ id: 'g-user', name: 'Action' }] })]),
        );

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        fireEvent.click(within(genres).getByRole('button', { name: /edit genre/i }));

        await waitFor(() => expect(screen.getByText('Edit genre')).toBeInTheDocument());

        const input = screen.getByLabelText(/Name/) as HTMLInputElement;
        expect(input.value).toBe('Action');
        fireEvent.change(input, { target: { value: 'Drama' } });
        fireEvent.click(screen.getByRole('button', { name: /^Save$/ }));

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().genres).toEqual({ add: ['Drama'], remove: ['Action'] });
    });

    it('stages a user sub-item deletion', async () => {
        mockUseInspector.mockReturnValue(
            inspectorData([userContribution({ genres: [{ id: 'g-user', name: 'Action' }] })]),
        );

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        fireEvent.click(within(genres).getByRole('button', { name: /delete genre/i }));

        expect(within(genres).queryByText('Action')).not.toBeInTheDocument();

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().genres).toEqual({ remove: ['Action'] });
    });

    it('prefills the current subject when editing a relationship', async () => {
        mockSubjects.mockReturnValue({
            subjects: [
                {
                    id: 'subj-1',
                    metadataContainerId: 'mc-s1',
                    kind: 'person',
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'mc-s1',
                        createdAt: '',
                        contributions: [{ ordinal: 0, title: 'Jane Doe' }],
                    },
                },
            ],
            isLoading: false,
        });
        mockUseInspector.mockReturnValue(
            inspectorData(
                [
                    userContribution({
                        relationships: [
                            {
                                id: 'rel-user',
                                kind: 'cast',
                                qualifier: 'Old Character',
                                relatedMetadataContainerId: 'mc-s1',
                            },
                        ],
                    }),
                ],
                { 'mc-s1': { subjectId: 'subj-1', name: 'Jane Doe' } },
            ),
        );

        render(<MetadataInspector contentId="c1" />);

        const section = document.querySelector('[data-subsection="relationships"]') as HTMLElement;
        fireEvent.click(within(section).getByRole('button', { name: /edit relationship/i }));

        await waitFor(() => expect(screen.getByText('Edit relationship')).toBeInTheDocument());

        expect(screen.getByTestId('subject-selected')).toHaveTextContent('Jane Doe');
        expect((screen.getByLabelText(/Qualifier/) as HTMLInputElement).value).toBe('Old Character');
    });

    it('stages a qualifier-only relationship edit without re-selecting the subject', async () => {
        mockSubjects.mockReturnValue({
            subjects: [
                {
                    id: 'subj-1',
                    metadataContainerId: 'mc-s1',
                    kind: 'person',
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'mc-s1',
                        createdAt: '',
                        contributions: [{ ordinal: 0, title: 'Jane Doe' }],
                    },
                },
            ],
            isLoading: false,
        });
        mockUseInspector.mockReturnValue(
            inspectorData(
                [
                    userContribution({
                        relationships: [
                            {
                                id: 'rel-user',
                                kind: 'cast',
                                qualifier: 'Old Character',
                                relatedMetadataContainerId: 'mc-s1',
                            },
                        ],
                    }),
                ],
                { 'mc-s1': { subjectId: 'subj-1', name: 'Jane Doe' } },
            ),
        );

        render(<MetadataInspector contentId="c1" />);

        const section = document.querySelector('[data-subsection="relationships"]') as HTMLElement;
        fireEvent.click(within(section).getByRole('button', { name: /edit relationship/i }));

        await waitFor(() => expect(screen.getByText('Edit relationship')).toBeInTheDocument());

        fireEvent.change(screen.getByLabelText(/Qualifier/), { target: { value: 'New Character' } });
        fireEvent.click(screen.getByRole('button', { name: /^Save$/ }));

        saveChanges();

        expect(mockPatchMutate).toHaveBeenCalledTimes(1);
        expect(lastPatch().relationships).toEqual({
            update: [{ id: 'rel-user', qualifier: { value: 'New Character' } }],
        });
    });

    it('renders a resolved relationship name in both the resolved view and the contribution stack', () => {
        mockUseInspector.mockReturnValue(
            inspectorData(
                [
                    userContribution({
                        relationships: [
                            {
                                id: 'rel-user',
                                kind: 'cast',
                                qualifier: 'Lead',
                                relatedMetadataContainerId: 'mc-s1',
                            },
                        ],
                    }),
                ],
                { 'mc-s1': { subjectId: 'subj-1', name: 'Jane Doe' } },
            ),
        );

        render(<MetadataInspector contentId="c1" />);

        const section = document.querySelector('[data-subsection="relationships"]') as HTMLElement;
        expect(within(section).getByText('Jane Doe (cast • Lead)')).toBeInTheDocument();

        fireEvent.click(screen.getByRole('button', { name: /Expand/ }));
        const stackGroup = document.querySelector('[data-subitem="relationships"]') as HTMLElement;
        expect(within(stackGroup).getByText('Jane Doe (cast • Lead)')).toBeInTheDocument();
    });

    it('renders the kind/qualifier detail for a relationship with no resolved name', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([
                userContribution({
                    relationships: [
                        {
                            id: 'rel-noname',
                            kind: 'director',
                            qualifier: 'Producer',
                        },
                    ],
                }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        const section = document.querySelector('[data-subsection="relationships"]') as HTMLElement;
        expect(within(section).getByText('director • Producer')).toBeInTheDocument();
    });

    it('disables the override pencil and clear-contribution controls for non-managers', () => {
        mockHasRole = () => false;
        mockUseInspector.mockReturnValue(
            inspectorData([
                userContribution({ genres: [{ id: 'g-user', name: 'Action' }] }),
                providerContribution({ title: 'Primary Title' }),
            ]),
        );

        render(<MetadataInspector contentId="c1" />);

        expect(screen.getByRole('button', { name: 'Edit Title' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Clear user contribution' })).toBeDisabled();
    });

    it('does not offer edit/delete on provider sub-items', () => {
        mockUseInspector.mockReturnValue(
            inspectorData([providerContribution({ genres: [{ id: 'g-prov', name: 'Action' }] })]),
        );

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        expect(within(genres).queryByRole('button', { name: /edit genre/i })).toBeNull();
        expect(within(genres).queryByRole('button', { name: /delete genre/i })).toBeNull();
        expect(within(genres).getByText('Action')).toBeInTheDocument();
    });

    it('disables the add control for non-managers with an explanation', () => {
        mockHasRole = () => false;
        mockUseInspector.mockReturnValue(inspectorData([userContribution()]));

        render(<MetadataInspector contentId="c1" />);

        const genres = document.querySelector('[data-subsection="genres"]') as HTMLElement;
        expect(within(genres).getByRole('button', { name: /add genre/i })).toBeDisabled();
    });
});
