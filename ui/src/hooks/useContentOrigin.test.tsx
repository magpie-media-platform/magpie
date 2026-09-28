import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, Link, useParams, useSearchParams } from 'react-router-dom';
import { describe, it, expect, vi, beforeEach } from 'vitest';

import { apiGet } from '@/lib/api';
import { ContentOriginProvider, useContentOrigin, resolveContentOrigin } from './useContentOrigin';

vi.mock('@/lib/api', () => ({ apiGet: vi.fn() }));

function Detail() {
    const { id } = useParams();
    const origin = useContentOrigin(id);
    const [, setSearch] = useSearchParams();
    return (
        <>
            <output>
                {origin?.pathname}
                {origin?.search}
            </output>
            <button onClick={() => setSearch({ tab: 'files' }, { replace: true })}>Files</button>
            <Link to="/library/child">Child</Link>
        </>
    );
}

describe('content return origin', () => {
    beforeEach(() => vi.resetAllMocks());
    it('retains the list across detail tabs and uses the surviving parent when opening a child', () => {
        // Arrange
        render(
            <MemoryRouter initialEntries={['/wanted?page=3&filter=missing']}>
                <ContentOriginProvider>
                    <Routes>
                        <Route path="/wanted" element={<Link to="/library/parent">Parent</Link>} />
                        <Route path="/library/:id" element={<Detail />} />
                    </Routes>
                </ContentOriginProvider>
            </MemoryRouter>,
        );
        // Act
        fireEvent.click(screen.getByRole('link', { name: 'Parent' }));
        fireEvent.click(screen.getByRole('button', { name: 'Files' }));
        // Assert
        expect(screen.getByRole('status')).toHaveTextContent('/wanted?page=3&filter=missing');
        // Act
        fireEvent.click(screen.getByRole('link', { name: 'Child' }));
        // Assert
        expect(screen.getByRole('status')).toHaveTextContent('/library/parent?tab=files');
    });
    it.each([
        null,
        { pathname: '//external.example', search: '', hash: '' },
        { pathname: '/library/deleted?tab=files', search: '', hash: '' },
        { pathname: '/library/deleted/child', search: '', hash: '' },
        { pathname: '/missing-route', search: '', hash: '' },
    ])('rejects an absent, unsafe, deleted, or unavailable destination %j', async (origin) => {
        // Act
        const result = await resolveContentOrigin(origin, 'deleted', true, true);
        // Assert
        expect(result.pathname).toBe('/library');
    });
    it('checks a parent destination after deletion and falls back if it no longer exists', async () => {
        // Arrange
        vi.mocked(apiGet).mockRejectedValue(new Error('Missing'));
        // Act
        const result = await resolveContentOrigin(
            { pathname: '/library/parent', search: '?tab=children', hash: '' },
            'child',
            true,
            true,
        );
        // Assert
        expect(apiGet).toHaveBeenCalledWith('/content/parent');
        expect(result.pathname).toBe('/library');
    });

    it('preserves all list state when its saved filter and content type remain available', async () => {
        // Arrange
        vi.mocked(apiGet)
            .mockResolvedValueOnce({ id: 'filter-1' })
            .mockResolvedValueOnce([{ slug: 'movie', levelDefinitions: [{ slug: 'movie' }] }]);
        const origin = {
            pathname: '/library/content/movie',
            search: '?savedFilter=filter-1&sort=title&page=4',
            hash: '',
            state: { view: 'gallery' },
        };
        // Act
        const result = await resolveContentOrigin(origin, 'deleted', true, false);
        // Assert
        expect(result).toEqual(origin);
    });

    it('falls back when the originating content type was removed', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValueOnce([]);
        // Act
        const result = await resolveContentOrigin(
            { pathname: '/library/content/removed', search: '', hash: '' },
            'deleted',
            true,
            false,
        );
        // Assert
        expect(result.pathname).toBe('/library');
    });
});
