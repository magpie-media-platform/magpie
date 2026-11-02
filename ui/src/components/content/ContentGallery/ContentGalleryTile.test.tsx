import { apiPost } from '@/lib/api';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import type { ContentListItem } from '@/types';

import { ContentGalleryTile } from './ContentGalleryTile';
import { TooltipProvider } from '@/components/ui/Tooltip';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();
const mockUseLibraryTypes = vi.fn();
const openPreview = vi.fn();
vi.mock('@/components/content/LocalContentPreview', () => ({ useOpenLocalContentPreview: () => openPreview }));

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiPost: vi.fn(),
}));

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/hooks/useLibraryTypes')>()),
    useLibraryTypes: () => mockUseLibraryTypes(),
}));

const PLAYABLE_TYPE: LibraryTypeDefinition = {
    id: 'videos',
    name: 'Video',
    pluralName: 'Videos',
    slug: 'videos',
    iconName: null,
    levels: [
        {
            slug: 'episode',
            displayName: 'Episode',
            parentSlug: null,
            iconName: null,
            supportedMetadataFields: 0x10000,
            customMetadataFields: [],
            standardMetadataFields: [
                {
                    field: 'Previews',
                    key: 'previews',
                    displayName: 'Previews',
                    dataType: 'collection',
                    valueKind: 'collection',
                    editable: false,
                },
            ],
            standardFactFields: [],
            acceptedFileTypes: [],
            acceptedSidecarTypes: [],
            subfolder: null,
        },
    ],
    supportedMetadataFields: 0x10000,
    customMetadataFields: [],
    standardMetadataFields: [],
};

const PLAYABLE_ITEM: ContentListItem = {
    id: 'episode-1',
    title: 'The Long Night',
    posterUrl: 'https://example.com/poster.jpg',
    level: 'episode',
    levelLabel: 'Episode',
    contentType: 'videos',
    monitoring: 'Monitored',
    effectiveMonitoring: 'Monitored',
    fulfillment: 'fulfilled',
    previews: [{ url: 'https://example.com/clip.mp4', kind: 'trailer', title: 'Trailer' }],
};

const REPLACING_EXTENSION = {
    id: 'ext-1',
    extensionId: 'playable-posters',
    name: 'Playable Posters',
    slot: 'content.gallery.poster',
    modifier: 'Replace',
    tagName: 'magpie-playable-poster',
    priority: 100,
    attributes: null,
};

function Wrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <TooltipProvider>{children}</TooltipProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

describe('ContentGalleryTile', () => {
    beforeAll(() => {
        if (!customElements.get('magpie-playable-poster')) {
            customElements.define('magpie-playable-poster', class extends HTMLElement {});
        }
    });

    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue({
            apiBaseUrl: '/api',
            userId: 'user-1',
            username: 'tester',
            role: 'Admin',
        });
        mockUseSlotExtensions.mockReturnValue({ extensions: [], isReady: true, isLoading: false });
        mockUseLibraryTypes.mockReturnValue({ contentTypes: [] });
    });

    it('shows unmonitored and fulfillment states separately', () => {
        // Arrange
        const item: ContentListItem = {
            id: 'movie-1',
            title: 'The Movie',
            level: 'movie',
            levelLabel: 'Movie',
            contentType: 'movie',
            monitoring: 'Unmonitored',
            effectiveMonitoring: 'Unmonitored',
            fulfillment: 'partial',
        };

        // Act
        render(
            <Wrapper>
                <ContentGalleryTile item={item} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
        expect(screen.getByText('Partial')).toBeInTheDocument();
    });

    it('shows monitored and fulfillment states separately', () => {
        // Arrange
        const item: ContentListItem = {
            id: 'movie-2',
            title: 'Another Movie',
            level: 'movie',
            levelLabel: 'Movie',
            contentType: 'movie',
            monitoring: 'Monitored',
            effectiveMonitoring: 'Monitored',
            fulfillment: 'partial',
        };

        // Act
        render(
            <Wrapper>
                <ContentGalleryTile item={item} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByLabelText('Monitored')).toBeInTheDocument();
        expect(screen.getByText('Partial')).toBeInTheDocument();
    });

    it('renders the poster image unchanged when no extension is registered for the slot', () => {
        // Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(container.querySelector('[data-slot-extension]')).toBeNull();
        expect(
            container.querySelector('.content-gallery-tile__poster > .slot > .content-gallery-tile__img'),
        ).not.toBeNull();
    });

    it('mounts one slot inside the poster region of the tile', () => {
        // Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        const slots = container.querySelectorAll('[data-slot="content.gallery.poster"]');
        expect(slots).toHaveLength(1);
        expect(slots[0].parentElement).toHaveClass('content-gallery-tile__poster');
    });

    it('gives the mounted extension the tile context when the level declares previews', () => {
        // Arrange
        mockUseLibraryTypes.mockReturnValue({ contentTypes: [PLAYABLE_TYPE] });
        mockUseSlotExtensions.mockReturnValue({
            extensions: [REPLACING_EXTENSION],
            isReady: true,
            isLoading: false,
        });

        // Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        const mounted = container.querySelector('magpie-playable-poster');
        expect(mounted).not.toBeNull();
        expect(mounted).toHaveAttribute('data-magpie-entity-id', 'episode-1');
        expect(mounted).toHaveAttribute('data-magpie-level', 'episode');
        expect(mounted).toHaveAttribute('data-magpie-poster-url', 'https://example.com/poster.jpg');
        expect(mounted).toHaveAttribute('data-magpie-declares-previews', 'true');
        expect(mounted).toHaveAttribute('data-magpie-selection-mode', 'false');
        expect(mounted).toHaveAttribute('data-magpie-preview-url', 'https://example.com/clip.mp4');
        expect(mounted).toHaveAttribute('data-magpie-preview-kind', 'trailer');
        expect(mounted).toHaveAttribute('data-magpie-preview-title', 'Trailer');
    });

    it('reports that the level does not declare previews when the declaration omits the field', () => {
        // Arrange
        mockUseSlotExtensions.mockReturnValue({
            extensions: [REPLACING_EXTENSION],
            isReady: true,
            isLoading: false,
        });

        // Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(container.querySelector('magpie-playable-poster')).toHaveAttribute(
            'data-magpie-declares-previews',
            'false',
        );
    });

    it('replaces the poster image with the extension element', () => {
        // Arrange
        mockUseSlotExtensions.mockReturnValue({
            extensions: [REPLACING_EXTENSION],
            isReady: true,
            isLoading: false,
        });

        // Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(container.querySelector('magpie-playable-poster')).not.toBeNull();
        expect(container.querySelector('.content-gallery-tile__img')).toBeNull();
    });

    it('keeps the poster extension mounted while selection mode is active', () => {
        // Arrange
        mockUseSlotExtensions.mockReturnValue({
            extensions: [REPLACING_EXTENSION],
            isReady: true,
            isLoading: false,
        });

        // Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={true} selected={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(container.querySelector('[data-slot="content.gallery.poster"]')).not.toBeNull();
        expect(container.querySelector('magpie-playable-poster')).toHaveAttribute('data-magpie-selection-mode', 'true');
        expect(screen.getByRole('button', { name: 'Select The Long Night', pressed: false })).toHaveClass(
            'selection-circle-toggle',
        );
    });

    it('opens the title preview without toggling selection and selects through the poster or the selection circle toggle', () => {
        // Arrange
        const toggle = vi.fn();
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile item={{ ...PLAYABLE_ITEM, year: 2026 }} selectionMode selected onToggle={toggle} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );
        // Act
        fireEvent.click(screen.getByText('(2026)'));
        // Assert
        expect(openPreview).toHaveBeenCalledWith(
            'episode-1',
            screen.getByRole('button', { name: 'Preview The Long Night' }),
        );
        expect(toggle).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Select The Long Night', pressed: true })).toHaveClass(
            'selection-circle-toggle--selected',
        );
        // Act
        fireEvent.click(container.querySelector('.content-gallery-tile__poster-target') as HTMLElement);
        fireEvent.click(screen.getByRole('button', { name: 'Select The Long Night' }));
        // Assert
        expect(toggle).toHaveBeenCalledTimes(2);
        expect(container.querySelector('.content-gallery-tile__poster-target')).toHaveAttribute('tabindex', '-1');
    });

    it('selects with the keyboard through the selection circle toggle', async () => {
        // Arrange
        const user = userEvent.setup();
        const toggle = vi.fn();
        render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode selected={false} onToggle={toggle} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Act
        await user.tab();
        await user.keyboard(' ');

        // Assert
        expect(screen.getByRole('button', { name: 'Select The Long Night' })).toHaveFocus();
        expect(toggle).toHaveBeenCalledTimes(1);
    });

    it('Poster_KeepsTheItemSelected_WhenALongPressEntersSelectionMode', () => {
        // Arrange
        vi.useFakeTimers();
        try {
            const onLongPress = vi.fn();
            const onToggle = vi.fn();
            const onOpen = vi.fn();
            const { container, rerender } = render(
                <Wrapper>
                    <ContentGalleryTile
                        item={PLAYABLE_ITEM}
                        onOpen={onOpen}
                        onLongPress={onLongPress}
                        onToggle={onToggle}
                    />
                </Wrapper>,
                { wrapper: TooltipProvider },
            );
            const poster = container.querySelector('.content-gallery-tile__poster-target') as HTMLElement;

            // Act
            fireEvent.pointerDown(poster, { clientX: 0, clientY: 0 });
            act(() => {
                vi.advanceTimersByTime(500);
            });
            rerender(
                <Wrapper>
                    <ContentGalleryTile
                        item={PLAYABLE_ITEM}
                        onOpen={onOpen}
                        onLongPress={onLongPress}
                        onToggle={onToggle}
                        selectionMode
                        selected
                    />
                </Wrapper>,
            );
            fireEvent.pointerUp(poster);
            fireEvent.click(poster);

            // Assert
            expect(onLongPress).toHaveBeenCalledTimes(1);
            expect(onToggle).not.toHaveBeenCalled();
            expect(onOpen).not.toHaveBeenCalled();
            expect(screen.getByRole('button', { name: 'Select The Long Night' })).toHaveAttribute(
                'aria-pressed',
                'true',
            );

            // Act
            fireEvent.click(poster);

            // Assert
            expect(onToggle).toHaveBeenCalledTimes(1);
        } finally {
            vi.useRealTimers();
        }
    });

    it('Title_NavigatesToDestination_WhenSelectionModeIsInactive', () => {
        // Arrange
        render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );
        // Assert
        expect(screen.getByRole('link', { name: 'Monitored The Long Night Fulfilled' })).toHaveAttribute(
            'href',
            '/library/episode-1',
        );
    });

    it('Poster_TogglesOnTheNextMouseClick_WhenTheLongPressReplacesTheNavigationLink', () => {
        // Arrange
        vi.useFakeTimers();
        try {
            const onLongPress = vi.fn();
            const onToggle = vi.fn();
            const { container, rerender } = render(
                <ContentGalleryTile item={PLAYABLE_ITEM} onLongPress={onLongPress} onToggle={onToggle} />,
                { wrapper: Wrapper },
            );
            const poster = screen.getByRole('link', { name: 'Open The Long Night' });

            // Act
            fireEvent.pointerDown(poster, { pointerType: 'mouse', button: 0 });
            act(() => {
                vi.advanceTimersByTime(500);
            });
            rerender(
                <ContentGalleryTile
                    item={PLAYABLE_ITEM}
                    onLongPress={onLongPress}
                    onToggle={onToggle}
                    selectionMode
                    selected
                />,
            );
            const selectionTarget = container.querySelector('.content-gallery-tile__poster-target') as HTMLElement;
            fireEvent.pointerUp(selectionTarget);
            fireEvent.pointerDown(selectionTarget, { pointerType: 'mouse', button: 0 });
            fireEvent.pointerUp(selectionTarget);
            fireEvent.click(selectionTarget);

            // Assert
            expect(onLongPress).toHaveBeenCalledOnce();
            expect(onToggle).toHaveBeenCalledOnce();
        } finally {
            vi.useRealTimers();
        }
    });
    it('Poster_LoadsExternalPreviewOnHover_WhenAnExtensionIsInstalled', async () => {
        // Arrange
        mockUseLibraryTypes.mockReturnValue({ contentTypes: [PLAYABLE_TYPE] });
        mockUseSlotExtensions.mockReturnValue({ extensions: [REPLACING_EXTENSION], isReady: true, isLoading: false });
        vi.mocked(apiPost).mockResolvedValue({ previews: PLAYABLE_ITEM.previews, levelSlug: 'episode' });
        const target = {
            providerType: 'provider',
            externalIdKey: 'provider.id',
            externalId: '123',
            contentType: 'videos',
            levelSlug: 'episode',
        };
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile
                    item={{ ...PLAYABLE_ITEM, previews: undefined }}
                    localContentId={null}
                    previewTarget={target}
                    onOpen={vi.fn()}
                />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );
        expect(apiPost).not.toHaveBeenCalled();

        // Act
        fireEvent.pointerEnter(container.querySelector('.content-gallery-tile__poster')!);

        // Assert
        await waitFor(() =>
            expect(container.querySelector('magpie-playable-poster')).toHaveAttribute(
                'data-magpie-preview-url',
                'https://example.com/clip.mp4',
            ),
        );
        expect(apiPost).toHaveBeenCalledWith('/content/preview-by-external-id', target);
        expect(container.querySelector('magpie-playable-poster')).not.toHaveAttribute('data-magpie-entity-id');
    });

    it('Poster_RetriesExternalPreview_WhenAnEarlierHoverRequestFailed', async () => {
        // Arrange
        const user = userEvent.setup();
        mockUseLibraryTypes.mockReturnValue({ contentTypes: [PLAYABLE_TYPE] });
        mockUseSlotExtensions.mockReturnValue({ extensions: [REPLACING_EXTENSION], isReady: true, isLoading: false });
        vi.mocked(apiPost)
            .mockRejectedValueOnce(new Error('Provider unavailable'))
            .mockResolvedValueOnce({ previews: PLAYABLE_ITEM.previews, levelSlug: 'episode' });
        const { container } = render(
            <ContentGalleryTile
                item={{ ...PLAYABLE_ITEM, previews: undefined }}
                localContentId={null}
                previewTarget={{
                    providerType: 'provider',
                    externalIdKey: 'provider.id',
                    externalId: '123',
                    contentType: 'videos',
                    levelSlug: 'episode',
                }}
                onOpen={vi.fn()}
            />,
            { wrapper: Wrapper },
        );
        const poster = container.querySelector('.content-gallery-tile__poster') as HTMLElement;
        await user.hover(poster);
        await user.unhover(poster);

        // Act
        await user.hover(poster);

        // Assert
        await waitFor(() =>
            expect(container.querySelector('magpie-playable-poster')).toHaveAttribute(
                'data-magpie-preview-url',
                'https://example.com/clip.mp4',
            ),
        );
        expect(apiPost).toHaveBeenCalledTimes(2);
    });

    it('OnOpen_RendersSinglePreviewButtonAndPreservesTextContent_WhenProvided', async () => {
        // Arrange
        const user = userEvent.setup();
        const onOpen = vi.fn();
        render(
            <Wrapper>
                <ContentGalleryTile
                    item={PLAYABLE_ITEM}
                    localContentId={null}
                    onOpen={onOpen}
                    detail={<span className="sr-only">In library</span>}
                />
            </Wrapper>,
        );

        // Act
        const textButton = screen.getByRole('button', { name: /In library/ });
        await user.click(textButton);

        // Assert
        expect(screen.getAllByRole('button', { name: 'Preview The Long Night' })).toHaveLength(1);
        expect(onOpen).toHaveBeenCalledExactlyOnceWith(textButton);
    });

    it('SelectionMode_UsesLinkWithoutOpeningPreview_WhenLocalContentIdIsNull', () => {
        // Arrange & Act
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile
                    item={{ id: 'show-1', title: 'The Show' }}
                    localContentId={null}
                    to="/wanted?parentId=show-1"
                    selectionMode
                />
            </Wrapper>,
        );

        // Assert
        expect(screen.getByRole('link', { name: 'The Show' })).toHaveAttribute('href', '/wanted?parentId=show-1');
        expect(openPreview).not.toHaveBeenCalled();
        expect(container.querySelector('.content-gallery-tile')).toHaveAttribute('data-interactive', 'true');
    });

    it('PreviewOnOpen_OpensLocalPreviewOnPrimaryClick_WhenLocalContentIdIsPresent', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <Wrapper>
                <ContentGalleryTile item={PLAYABLE_ITEM} previewOnOpen />
            </Wrapper>,
        );

        // Act
        const posterLink = screen.getByRole('link', { name: 'Open The Long Night' });
        await user.click(posterLink);

        // Assert
        expect(openPreview).toHaveBeenCalledExactlyOnceWith('episode-1', posterLink);
    });

    it('Poster_DoesNotRequestPreview_WhenSelectingExternalContent', () => {
        // Arrange
        mockUseLibraryTypes.mockReturnValue({ contentTypes: [PLAYABLE_TYPE] });
        mockUseSlotExtensions.mockReturnValue({ extensions: [REPLACING_EXTENSION], isReady: true, isLoading: false });
        const { container } = render(
            <Wrapper>
                <ContentGalleryTile
                    item={{ ...PLAYABLE_ITEM, previews: undefined }}
                    selectionMode
                    localContentId={null}
                    previewTarget={{
                        providerType: 'provider',
                        externalIdKey: 'provider.id',
                        externalId: '123',
                        contentType: 'videos',
                    }}
                    onOpen={vi.fn()}
                />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Act
        fireEvent.pointerEnter(container.querySelector('.content-gallery-tile__poster')!);

        // Assert
        expect(apiPost).not.toHaveBeenCalled();
    });
});
