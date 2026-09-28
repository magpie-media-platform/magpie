import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';
import type { ContentListItem } from '@/types';

import { LibraryGalleryTile } from './LibraryGalleryTile';
import { TooltipProvider } from '@/components/ui/Tooltip';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();
const mockUseLibraryTypes = vi.fn();
const openPreview = vi.fn();
vi.mock('@/components/content/LocalContentPreview', () => ({ useOpenLocalContentPreview: () => openPreview }));

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
            effectiveAcceptedMimeTypes: [],
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
            <MemoryRouter>{children}</MemoryRouter>
        </QueryClientProvider>
    );
}

describe('LibraryGalleryTile', () => {
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

    it('shows explicit unmonitored and fulfillment states separately', () => {
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
                <LibraryGalleryTile item={item} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByLabelText('Unmonitored')).toBeInTheDocument();
        expect(screen.getByText('Partial')).toBeInTheDocument();
    });

    it('marks an inherited unmonitored item as inherited instead of an explicit choice', () => {
        // Arrange
        const item: ContentListItem = {
            id: 'movie-3',
            title: 'A Third Movie',
            level: 'movie',
            levelLabel: 'Movie',
            contentType: 'movie',
            monitoring: null,
            effectiveMonitoring: 'Unmonitored',
            fulfillment: 'partial',
        };

        // Act
        render(
            <Wrapper>
                <LibraryGalleryTile item={item} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(screen.getByLabelText('Unmonitored (inherited)')).toBeInTheDocument();
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
                <LibraryGalleryTile item={item} selectionMode={false} />
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
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(container.querySelector('[data-slot-extension]')).toBeNull();
        expect(
            container.querySelector('.library-gallery-tile__poster > .slot > .library-gallery-tile__img'),
        ).not.toBeNull();
    });

    it('mounts one slot inside the poster region of the tile', () => {
        // Act
        const { container } = render(
            <Wrapper>
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        const slots = container.querySelectorAll('[data-slot="content.gallery.poster"]');
        expect(slots).toHaveLength(1);
        expect(slots[0].parentElement).toHaveClass('library-gallery-tile__poster');
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
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
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
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
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
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );

        // Assert
        expect(container.querySelector('magpie-playable-poster')).not.toBeNull();
        expect(container.querySelector('.library-gallery-tile__img')).toBeNull();
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
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={true} selected={false} />
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
                <LibraryGalleryTile item={{ ...PLAYABLE_ITEM, year: 2026 }} selectionMode selected onToggle={toggle} />
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
        fireEvent.click(container.querySelector('.library-gallery-tile__poster-target') as HTMLElement);
        fireEvent.click(screen.getByRole('button', { name: 'Select The Long Night' }));
        // Assert
        expect(toggle).toHaveBeenCalledTimes(2);
        expect(container.querySelector('.library-gallery-tile__poster-target')).toHaveAttribute('tabindex', '-1');
    });

    it('selects with the keyboard through the selection circle toggle', async () => {
        // Arrange
        const user = userEvent.setup();
        const toggle = vi.fn();
        render(
            <Wrapper>
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode selected={false} onToggle={toggle} />
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

    it('keeps title navigation outside selection mode', () => {
        // Arrange
        render(
            <Wrapper>
                <LibraryGalleryTile item={PLAYABLE_ITEM} selectionMode={false} />
            </Wrapper>,
            { wrapper: TooltipProvider },
        );
        // Assert
        expect(screen.getByRole('link', { name: 'Monitored The Long Night Fulfilled' })).toHaveAttribute(
            'href',
            '/library/episode-1',
        );
    });
});
