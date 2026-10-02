import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { useState } from 'react';
vi.mock('@/hooks/useLocalContentPreview', () => ({
    useLocalContentPreview: () => ({ data: undefined, error: null, isPending: false }),
}));

vi.mock('@/hooks/usePreviewByExternalId', () => ({
    usePreviewByExternalId: vi.fn(),
}));

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => ({
        subjectTypes: [
            { id: 'person', name: 'Person', pluralName: 'People', slug: 'person', levels: [] },
            {
                id: 'production-company',
                name: 'Studio',
                pluralName: 'Studios',
                slug: 'production-company',
                levels: [],
            },
        ],
    }),
}));

import { usePreviewByExternalId, type ContentPreview } from '@/hooks/usePreviewByExternalId';
import { ApiError } from '@/lib/api';
import { PreviewDrawer, type PreviewDrawerTarget } from './PreviewDrawer';

const mockUsePreview = vi.mocked(usePreviewByExternalId);

const target: PreviewDrawerTarget = {
    providerType: 'provider-a',
    externalIdKey: 'provider-a.movie',
    externalId: '27205',
    contentType: 'movies',
    fallbackTitle: 'Inception',
    fallbackYear: '2010',
};

const preview: ContentPreview = {
    providerType: 'provider-a',
    contentType: 'movies',
    levelSlug: 'movie',
    title: 'Inception',
    year: 2010,
    overview: 'A thief who steals corporate secrets.',
    genres: ['Action', 'Sci-Fi'],
    contentRatings: [{ body: 'MPAA', rating: 'PG-13' }],
    images: [{ type: 'poster', url: 'https://img/poster.jpg' }],
    previews: [{ kind: 'trailer', url: 'https://www.youtube.com/watch?v=YoHD9XEInc0', title: 'Official Trailer' }],
    externalIds: [{ source: 'provider-a.movie', value: '27205' }],
    relatedSubjects: [
        {
            name: 'Leonardo DiCaprio',
            kind: 'person',
            relationshipKind: 'actor',
            relationshipQualifier: 'Cobb',
            ordinal: 0,
            imageUrl: 'https://img/leo.jpg',
        },
    ],
};

const sparsePreview: ContentPreview = {
    providerType: 'provider-a',
    contentType: 'movies',
    levelSlug: 'movie',
    title: 'Inception',
    genres: [],
    contentRatings: [],
    images: [],
    previews: [],
    externalIds: [{ source: 'provider-a.movie', value: '27205' }],
    relatedSubjects: [],
};

describe('PreviewDrawer', () => {
    beforeEach(() => vi.clearAllMocks());

    it('fetches the preview with the target identifiers when opened', async () => {
        const mutate = vi.fn();
        mockUsePreview.mockReturnValue({
            mutate,
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        await waitFor(() => {
            expect(mutate).toHaveBeenCalledWith({
                providerType: 'provider-a',
                externalIdKey: 'provider-a.movie',
                externalId: '27205',
                contentType: 'movies',
            });
        });
    });

    it('returns focus to the opener after Escape', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);
        const user = userEvent.setup();
        function Opener() {
            const [open, setOpen] = useState(false);
            return (
                <>
                    <button onClick={() => setOpen(true)}>Open preview</button>
                    <PreviewDrawer open={open} onOpenChange={setOpen} target={target} />
                </>
            );
        }
        render(<Opener />);

        const opener = screen.getByRole('button', { name: 'Open preview' });
        opener.focus();
        await user.keyboard('{Enter}');
        await screen.findByTestId('preview-drawer');
        await user.keyboard('{Escape}');

        await waitFor(() => expect(screen.queryByTestId('preview-drawer')).not.toBeInTheDocument());
        expect(opener).toHaveFocus();
    });

    it('returns focus to the opener after Close', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);
        const user = userEvent.setup();
        function Opener() {
            const [open, setOpen] = useState(false);
            return (
                <>
                    <button onClick={() => setOpen(true)}>Open preview</button>
                    <PreviewDrawer open={open} onOpenChange={setOpen} target={target} />
                </>
            );
        }
        render(<Opener />);

        const opener = screen.getByRole('button', { name: 'Open preview' });
        opener.focus();
        await user.keyboard('{Enter}');
        const drawer = await screen.findByTestId('preview-drawer');
        await user.click(within(drawer).getByRole('button', { name: /^close$/i }));

        await waitFor(() => expect(screen.queryByTestId('preview-drawer')).not.toBeInTheDocument());
        expect(opener).toHaveFocus();
    });

    it('renders title, preview hero, and related subjects from the preview', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        await waitFor(() => {
            expect(screen.getByText('Inception')).toBeInTheDocument();
        });
        expect(screen.getByTestId('preview-player-facade')).toBeInTheDocument();
        expect(screen.getByTestId('preview-drawer-related-subjects')).toHaveTextContent('Leonardo DiCaprio');
        expect(screen.getByText('A thief who steals corporate secrets.')).toBeInTheDocument();
    });

    it('uses provider ordering without interpreting preview or image kinds', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: {
                ...preview,
                images: [
                    { type: 'cover', url: 'https://img/first.jpg' },
                    { type: 'poster', url: 'https://img/second.jpg' },
                ],
                previews: [
                    {
                        kind: 'sample',
                        url: 'https://www.youtube.com/watch?v=FIRST',
                        title: 'First Preview',
                    },
                    {
                        kind: 'trailer',
                        url: 'https://www.youtube.com/watch?v=SECOND',
                        title: 'Second Preview',
                    },
                ],
            },
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        const playButton = await screen.findByRole('button', { name: 'Play First Preview' });
        expect(playButton.querySelector('img')).toHaveAttribute('src', 'https://img/first.jpg');
    });

    it('groups related subjects by their generic subject kind', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: {
                ...preview,
                relatedSubjects: [
                    ...preview.relatedSubjects,
                    {
                        name: 'Legendary Pictures',
                        kind: 'production-company',
                        relationshipKind: 'studio',
                        ordinal: 1,
                    },
                ],
            },
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        const groups = await screen.findAllByTestId('preview-drawer-subject-group');
        expect(groups).toHaveLength(2);
        expect(within(groups[0]).getByRole('heading')).toHaveTextContent('Person');
        expect(within(groups[0]).getByText('Leonardo DiCaprio')).toBeInTheDocument();
        expect(within(groups[1]).getByRole('heading')).toHaveTextContent('Studio');
        expect(within(groups[1]).getByText('Legendary Pictures')).toBeInTheDocument();
    });

    it('caps each related-subject group after grouping', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: {
                ...preview,
                relatedSubjects: [
                    ...Array.from({ length: 13 }, (_, index) => ({
                        ...preview.relatedSubjects[0],
                        name: `Person ${index + 1}`,
                        ordinal: index,
                    })),
                    {
                        name: 'Legendary Pictures',
                        kind: 'production-company',
                        relationshipKind: 'studio',
                        ordinal: 13,
                    },
                ],
            },
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        const groups = await screen.findAllByTestId('preview-drawer-subject-group');
        expect(within(groups[0]).getAllByRole('listitem')).toHaveLength(12);
        expect(within(groups[1]).getByText('Legendary Pictures')).toBeInTheDocument();
    });

    it('hides the related-subject section when the preview has no related subjects', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: { ...preview, relatedSubjects: [] },
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        await waitFor(() => {
            expect(screen.getByText('Inception')).toBeInTheDocument();
        });
        expect(screen.queryByTestId('preview-drawer-related-subjects')).not.toBeInTheDocument();
    });

    it('renders artwork-only previews inside the bounded artwork frame', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: { ...preview, previews: [] },
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        const frame = await screen.findByTestId('preview-drawer-artwork-frame');
        const artwork = screen.getByTestId('preview-drawer-artwork');
        expect(frame).toHaveClass('preview-drawer__artwork-frame');
        expect(frame).toContainElement(artwork);
        expect(artwork).toHaveClass('preview-drawer__artwork');
        expect(artwork).toHaveAttribute('src', 'https://img/poster.jpg');
        fireEvent.error(artwork);
        expect(screen.queryByTestId('preview-drawer-artwork')).not.toBeInTheDocument();
        expect(frame.querySelector('.preview-drawer__artwork--placeholder')).toBeInTheDocument();
        expect(screen.queryByTestId('preview-player-facade')).not.toBeInTheDocument();
    });

    it('calls onAdd with the target when the Add button is clicked', async () => {
        const user = userEvent.setup();
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);
        const onAdd = vi.fn();

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={onAdd}
            />,
        );

        await waitFor(() => expect(screen.getByText('Inception')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /add to library/i }));
        expect(onAdd).toHaveBeenCalledWith(target, undefined);
    });

    it('adds straight to the sole destination when one addOption is given', async () => {
        const user = userEvent.setup();
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);
        const onAdd = vi.fn();

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                addOptions={[{ id: 'rf-1', label: 'Movies' }]}
                onAdd={onAdd}
            />,
        );

        await waitFor(() => expect(screen.getByText('Inception')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /add to library/i }));
        expect(onAdd).toHaveBeenCalledWith(target, 'rf-1');
    });

    it('opens a destination menu on Add when several addOptions are given', async () => {
        const user = userEvent.setup();
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);
        const onAdd = vi.fn();

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                addOptions={[
                    { id: 'rf-1', label: 'Movies A' },
                    { id: 'rf-2', label: 'Movies B' },
                ]}
                onAdd={onAdd}
            />,
        );

        await waitFor(() => expect(screen.getByText('Inception')).toBeInTheDocument());
        await user.click(screen.getByRole('button', { name: /add to library/i }));
        await user.click(await screen.findByRole('menuitem', { name: 'Movies B' }));
        expect(onAdd).toHaveBeenCalledWith(target, 'rf-2');
    });

    it('disables Add and shows the reason when addDisabledReason is set', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);
        const onAdd = vi.fn();

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                addDisabledReason="Configure a root folder for this content type before adding."
                onAdd={onAdd}
            />,
        );

        await waitFor(() => expect(screen.getByText('Inception')).toBeInTheDocument());
        const addButton = screen.getByRole('button', { name: /add to library/i });
        expect(addButton).toBeDisabled();
        expect(screen.getByTestId('preview-drawer__add-hint')).toHaveTextContent(/configure a root folder/i);
    });

    it('shows the empty state on a 404', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: undefined,
            error: new ApiError(404, 'Not found'),
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer
                open
                onOpenChange={() => {}}
                target={target}
                isAdded={false}
                isAdding={false}
                onAdd={() => {}}
            />,
        );

        await waitFor(() => {
            expect(screen.getByTestId('preview-drawer-not-found')).toBeInTheDocument();
        });
        expect(screen.getByTestId('preview-drawer-not-found')).toHaveTextContent(/no metadata found/i);
    });

    it('shows the no-details state when a successful preview has only its identity', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: sparsePreview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(<PreviewDrawer open onOpenChange={() => {}} target={target} />);

        const noDetails = await screen.findByTestId('preview-drawer-no-details');
        expect(within(noDetails).getByRole('heading', { name: 'No more details' })).toBeInTheDocument();
        expect(within(noDetails).getByText('This item has no more details.')).toBeInTheDocument();
        expect(screen.getByRole('heading', { name: 'Inception 2010' })).toBeInTheDocument();
        expect(screen.queryByTestId('preview-drawer-loading')).not.toBeInTheDocument();
        expect(screen.queryByTestId('preview-drawer-not-found')).not.toBeInTheDocument();
        expect(screen.queryByTestId('preview-drawer-error')).not.toBeInTheDocument();
    });

    it.each<[string, Partial<ContentPreview>]>([
        ['a preview clip', { previews: [{ kind: 'trailer', url: 'https://www.youtube.com/watch?v=YoHD9XEInc0' }] }],
        ['artwork', { images: [{ type: 'poster', url: 'https://img/poster.jpg' }] }],
        ['a runtime', { duration: '02:28:00' }],
        ['a language', { language: 'en' }],
        ['a content rating', { contentRatings: [{ body: 'MPAA', rating: 'PG-13' }] }],
        ['an overview', { overview: 'A thief who steals corporate secrets.' }],
        ['a genre', { genres: ['Action'] }],
        [
            'a related subject',
            {
                relatedSubjects: [{ name: 'Leonardo DiCaprio', kind: 'person', relationshipKind: 'actor', ordinal: 0 }],
            },
        ],
    ])('does not show the no-details state when the preview has only %s', async (_, details) => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: { ...sparsePreview, ...details },
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(<PreviewDrawer open onOpenChange={() => {}} target={target} />);

        expect(await screen.findByRole('heading', { name: 'Inception 2010' })).toBeInTheDocument();
        expect(screen.queryByTestId('preview-drawer-no-details')).not.toBeInTheDocument();
    });

    it('reflects an added candidate on the Add button', async () => {
        mockUsePreview.mockReturnValue({
            mutate: vi.fn(),
            reset: vi.fn(),
            data: preview,
            error: null,
            isPending: false,
        } as unknown as ReturnType<typeof usePreviewByExternalId>);

        render(
            <PreviewDrawer open onOpenChange={() => {}} target={target} isAdded isAdding={false} onAdd={() => {}} />,
        );

        await waitFor(() => expect(screen.getByText('Inception')).toBeInTheDocument());
        const addButton = screen.getByRole('button', { name: /added/i });
        expect(addButton).toBeDisabled();
    });
});
