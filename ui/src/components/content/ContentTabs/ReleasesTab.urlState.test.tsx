import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { useCardLayout } from '@/hooks/ui/useCardLayout';

import { ReleasesTab, type ReleaseRow } from './ReleasesTab';

vi.mock('@/hooks/ui/useCardLayout', () => ({ useCardLayout: vi.fn() }));

const RELEASES: ReleaseRow[] = Array.from({ length: 26 }, (_, index) => ({
    id: `release-${index + 1}`,
    title: `Release ${String(index + 1).padStart(2, '0')}`,
    protocol: 'torrent',
    publishDate: '2024-01-15T10:00:00Z',
}));

describe('ReleasesTab URL state', () => {
    it.each([false, true])('UrlState_RestoresSortAndPage_WhenCardLayoutIs%s', async (cardLayout) => {
        // Arrange
        vi.mocked(useCardLayout).mockReturnValue(cardLayout);
        const user = userEvent.setup();
        const router = createMemoryRouter(
            [{ path: '/', element: <ReleasesTab releases={RELEASES} onGrab={vi.fn()} /> }],
            { initialEntries: ['/?releasesPage=2&releasesSort=title:desc&childrenPage=3'] },
        );
        render(<RouterProvider router={router} />);

        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Release 01')).toBeInTheDocument();
        expect(screen.queryByText('Release 26')).not.toBeInTheDocument();

        // Act
        await act(() => router.navigate('/?releasesPage=2&releasesSort=title:asc&childrenPage=3'));

        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Release 26')).toBeInTheDocument();

        // Act
        await user.click(screen.getByRole('button', { name: 'Go to previous page' }));

        // Assert
        expect(new URLSearchParams(router.state.location.search).has('releasesPage')).toBe(false);
        expect(new URLSearchParams(router.state.location.search).get('childrenPage')).toBe('3');

        // Act
        await act(() => router.navigate(-1));

        // Assert
        expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
        expect(screen.getByText('Release 26')).toBeInTheDocument();

        // Act
        await act(() => router.navigate(-1));

        // Assert
        expect(screen.getByText('Release 01')).toBeInTheDocument();
    });
});
