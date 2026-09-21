import { Suspense } from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { LibraryTypeDefinition } from '@/hooks/useLibraryTypes';

import { LibraryContentRoute } from './LibraryContentRoute';

const mockUseLibraryTypes = vi.fn();

const registryContentTypes: LibraryTypeDefinition[] = [
    { id: 'widget-type', name: 'Widget', pluralName: 'Widgets', slug: 'widget', levels: [] },
    { id: 'music-type', name: 'Music', pluralName: 'Music', slug: 'music', levels: [] },
    { id: 'book-type', name: 'Book', pluralName: 'Books', slug: 'book', levels: [] },
    { id: 'movie-type', name: 'Movie', pluralName: 'Movies', slug: 'movie', levels: [] },
    { id: 'tv-type', name: 'TV Show', pluralName: 'TV Shows', slug: 'tv', levels: [] },
];

vi.mock('@/hooks/useLibraryTypes', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/hooks/useLibraryTypes')>();
    return {
        ...actual,
        useLibraryTypes: () => mockUseLibraryTypes(),
    };
});

vi.mock('@/pages/ContentDetailPage/ContentDetailPage', () => ({
    default: () => <div>Content detail page</div>,
}));

function RouteLocation() {
    const location = useLocation();
    return <div data-testid="route-location">{`${location.pathname}${location.search}`}</div>;
}

RouteLocation.displayName = 'RouteLocation';

describe('routes', () => {
    beforeEach(() => {
        mockUseLibraryTypes.mockReturnValue({
            contentTypes: registryContentTypes,
            isLoading: false,
        });
    });

    it.each([
        { segment: 'widget', slug: 'widget' },
        { segment: 'music', slug: 'music' },
        { segment: 'books', slug: 'book' },
        { segment: 'movies', slug: 'movie' },
        { segment: 'tv-shows', slug: 'tv' },
    ])('routes /library/$segment to the registry slug $slug', async ({ segment, slug }) => {
        render(
            <MemoryRouter initialEntries={[`/library/${segment}`]}>
                <Routes>
                    <Route path="/library/:contentId" element={<LibraryContentRoute />} />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByTestId('route-location')).toHaveTextContent(`/library/content/${slug}`);
    });

    it('matches route segments case-insensitively', async () => {
        render(
            <MemoryRouter initialEntries={['/library/Movies']}>
                <Routes>
                    <Route path="/library/:contentId" element={<LibraryContentRoute />} />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByTestId('route-location')).toHaveTextContent('/library/content/movie');
    });

    it('normalizes underscores in route segments', async () => {
        render(
            <MemoryRouter initialEntries={['/library/TV_Shows']}>
                <Routes>
                    <Route path="/library/:contentId" element={<LibraryContentRoute />} />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByTestId('route-location')).toHaveTextContent('/library/content/tv');
    });

    it('preserves the query string across the redirect', async () => {
        render(
            <MemoryRouter initialEntries={['/library/movies?filter=missing&sort=title']}>
                <Routes>
                    <Route path="/library/:contentId" element={<LibraryContentRoute />} />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByTestId('route-location')).toHaveTextContent(
            '/library/content/movie?filter=missing&sort=title',
        );
    });

    it.each([
        { label: 'a content id', segment: '550e8400-e29b-41d4-a716-446655440000' },
        { label: 'an unmatched segment', segment: 'not-a-content-type' },
    ])('renders the detail page for $label', async ({ segment }) => {
        render(
            <MemoryRouter initialEntries={[`/library/${segment}`]}>
                <Routes>
                    <Route
                        path="/library/:contentId"
                        element={
                            <Suspense fallback={<div>Loading</div>}>
                                <LibraryContentRoute />
                            </Suspense>
                        }
                    />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByText('Content detail page')).toBeInTheDocument();
        expect(screen.queryByTestId('route-location')).not.toBeInTheDocument();
    });

    it('prefers an exact slug over a colliding plural name', async () => {
        mockUseLibraryTypes.mockReturnValue({
            contentTypes: [
                { id: 'catalog-type', name: 'Catalog', pluralName: 'Collections', slug: 'catalog', levels: [] },
                { id: 'collection-type', name: 'Collection', pluralName: 'Catalog', slug: 'collection', levels: [] },
            ],
            isLoading: false,
        });

        render(
            <MemoryRouter initialEntries={['/library/Catalog']}>
                <Routes>
                    <Route path="/library/:contentId" element={<LibraryContentRoute />} />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByTestId('route-location')).toHaveTextContent('/library/content/catalog');
    });

    it('prefers a unique normalized plural over a normalized slug collision', async () => {
        mockUseLibraryTypes.mockReturnValue({
            contentTypes: [
                {
                    id: 'underscored-type',
                    name: 'Underscored',
                    pluralName: 'Underscored Items',
                    slug: 'shared_value',
                    levels: [],
                },
                { id: 'plural-type', name: 'Plural', pluralName: 'Shared Value', slug: 'plural', levels: [] },
            ],
            isLoading: false,
        });

        render(
            <MemoryRouter initialEntries={['/library/shared-value']}>
                <Routes>
                    <Route path="/library/:contentId" element={<LibraryContentRoute />} />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByTestId('route-location')).toHaveTextContent('/library/content/plural');
    });

    it('falls through when normalized plural matches remain ambiguous', async () => {
        mockUseLibraryTypes.mockReturnValue({
            contentTypes: [
                { id: 'first-type', name: 'First', pluralName: 'Shared Name', slug: 'first', levels: [] },
                { id: 'second-type', name: 'Second', pluralName: 'Shared_Name', slug: 'second', levels: [] },
            ],
            isLoading: false,
        });

        render(
            <MemoryRouter initialEntries={['/library/shared-name']}>
                <Routes>
                    <Route
                        path="/library/:contentId"
                        element={
                            <Suspense fallback={<div>Loading</div>}>
                                <LibraryContentRoute />
                            </Suspense>
                        }
                    />
                    <Route path="/library/content/:contentType" element={<RouteLocation />} />
                </Routes>
            </MemoryRouter>,
        );

        expect(await screen.findByText('Content detail page')).toBeInTheDocument();
        expect(screen.queryByTestId('route-location')).not.toBeInTheDocument();
    });
});
