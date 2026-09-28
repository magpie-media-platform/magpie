import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthProvider, Role } from '@/auth';
import { CommandPalette } from '@/components/ui/CommandPalette';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { apiGet } from '@/lib/api';
import { testEffectivePermissions } from '@/test/permissions';

import { LibraryQuickSearch } from './LibraryQuickSearch';

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => ({
        contentTypes: [
            {
                id: 'tv',
                slug: 'tv',
                name: 'Series',
                pluralName: 'Series',
                levels: [
                    { slug: 'show', displayName: 'Series' },
                    { slug: 'episode', displayName: 'Chapter' },
                ],
            },
        ],
        subjectTypes: [],
    }),
    useTypeIconName: () => () => undefined,
}));
vi.mock('@/lib/api', async (original) => ({
    ...(await original<typeof import('@/lib/api')>()),
    apiGet: vi.fn(),
}));

const SHOW = {
    id: 'show-1',
    parentId: null,
    contentType: 'tv',
    level: 'show',
    name: 'dark-folder',
    ordinal: null,
    metadataContainer: { contributions: [{ ordinal: 0, title: 'Dark', publishDate: '2017-12-01T00:00:00Z' }] },
};
const EPISODE = {
    id: 'episode-20',
    parentId: 'season-1',
    contentType: 'tv',
    level: 'episode',
    name: 'episode-20',
    ordinal: 20,
    metadataContainer: {
        contributions: [{ ordinal: 0, title: 'Darkness Falls', publishDate: '1994-04-15T00:00:00Z' }],
    },
};

type TitleSearch = (filter: string, signal: AbortSignal) => Promise<unknown[]>;

let titleSearch: TitleSearch;

function titleSearchCalls() {
    return vi
        .mocked(apiGet)
        .mock.calls.map(([path]) => path)
        .filter((path) => path.startsWith('/content?'));
}

function LocationProbe() {
    const location = useLocation();
    return (
        <output data-testid="location">
            {location.pathname}
            {location.search}|{JSON.stringify(location.state)}
        </output>
    );
}

function renderSearch(role: Role = Role.Admin) {
    return render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
            <MemoryRouter initialEntries={['/downloads']}>
                <AuthProvider
                    initialUser={{ id: 'user-1', username: 'user', role }}
                    initialPermissions={testEffectivePermissions(role)}
                >
                    <TooltipProvider>
                        <Routes>
                            <Route
                                path="*"
                                element={
                                    <>
                                        <LibraryQuickSearch />
                                        <CommandPalette />
                                        <LocationProbe />
                                    </>
                                }
                            />
                        </Routes>
                    </TooltipProvider>
                </AuthProvider>
            </MemoryRouter>
        </QueryClientProvider>,
    );
}

async function openAndType(text: string) {
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Search library' }));
    await user.type(screen.getByRole('combobox', { name: 'Library title' }), text);
    return user;
}

describe('LibraryQuickSearch', () => {
    beforeEach(() => {
        vi.mocked(apiGet).mockReset();
        titleSearch = () => Promise.resolve([SHOW, EPISODE]);
        vi.mocked(apiGet).mockImplementation((path: string, options?: { signal?: AbortSignal | null }) => {
            if (path.startsWith('/content?')) {
                const filter = new URLSearchParams(path.split('?')[1]).get('filter') ?? '';
                return titleSearch(filter, options?.signal ?? new AbortController().signal).then((items) => ({
                    items,
                    pageNumber: 1,
                    pageSize: 8,
                    totalItems: items.length,
                }));
            }
            if (path.endsWith('/metadata')) return Promise.resolve({ title: 'Preview of Darkness Falls' });
            return Promise.resolve({ id: 'episode-20', name: 'Darkness Falls', contentType: 'tv', level: 'episode' });
        });
    });

    it('shows the empty-query state and sends no request when search opens', async () => {
        // Arrange
        renderSearch();
        const user = userEvent.setup();

        // Act
        await user.click(screen.getByRole('button', { name: 'Search library' }));

        // Assert
        expect(screen.getByRole('dialog', { name: 'Search library' })).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Library title' })).toHaveFocus();
        expect(screen.getByText('Type a title to search the library.')).toBeInTheDocument();
        expect(apiGet).not.toHaveBeenCalled();
    });

    it('sends one bounded title-sorted content query with the title filter at every level', async () => {
        // Arrange
        renderSearch();

        // Act
        await openAndType('dark');

        // Assert
        expect(screen.getByRole('status')).toHaveTextContent('Searching the library…');
        expect(await screen.findByRole('option', { name: /Darkness Falls/ })).toBeInTheDocument();
        expect(titleSearchCalls()).toEqual([
            '/content?filter=title+contains+%22dark%22&pageSize=8&sortBy=Name&sortDirection=Ascending',
        ]);
    });

    it('shows the level, the child ordinal and the year that identify each result', async () => {
        // Arrange
        renderSearch();

        // Act
        await openAndType('dark');

        // Assert
        const options = await screen.findAllByRole('option');
        expect(options.map((option) => option.textContent)).toEqual([
            'DarkSeries · 2017',
            'Darkness FallsChapter 20 · 1994',
        ]);
    });

    it('aborts the obsolete request and ignores its late response', async () => {
        // Arrange
        const pending = new Map<string, { resolve: (value: unknown[]) => void; signal: AbortSignal }>();
        titleSearch = (query, signal) =>
            new Promise((resolve, reject) => {
                pending.set(query, { resolve, signal });
                signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
            });
        renderSearch();
        const user = await openAndType('dark');
        await waitFor(() => expect(pending.has('title contains "dark"')).toBe(true));

        // Act
        await user.type(screen.getByRole('combobox', { name: 'Library title' }), 'ness');
        await waitFor(() => expect(pending.has('title contains "darkness"')).toBe(true));
        pending.get('title contains "dark"')!.resolve([SHOW]);
        pending.get('title contains "darkness"')!.resolve([EPISODE]);

        // Assert
        expect(await screen.findByRole('option', { name: /Darkness Falls/ })).toBeInTheDocument();
        expect(pending.get('title contains "dark"')!.signal.aborted).toBe(true);
        expect(screen.getAllByRole('option')).toHaveLength(1);
    });

    it('aborts the pending request and restores focus to the header action when search closes', async () => {
        // Arrange
        let signal: AbortSignal | undefined;
        titleSearch = (_query, requestSignal) => {
            signal = requestSignal;
            return new Promise(() => {});
        };
        renderSearch();
        const user = await openAndType('dark');
        await waitFor(() => expect(signal).toBeDefined());

        // Act
        await user.keyboard('{Escape}');

        // Assert
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(signal!.aborted).toBe(true);
        expect(screen.getByRole('button', { name: 'Search library' })).toHaveFocus();
        expect(screen.getByTestId('location')).toHaveTextContent('/downloads|null');
    });

    it('clears the results on the first Escape and closes search on the second', async () => {
        // Arrange
        renderSearch();
        const user = await openAndType('dark');
        const input = screen.getByRole('combobox', { name: 'Library title' });
        await screen.findByRole('option', { name: /Darkness Falls/ });
        expect(input).toHaveAttribute('aria-expanded', 'true');

        // Act
        await user.keyboard('{Escape}');

        // Assert
        expect(screen.getByRole('dialog', { name: 'Search library' })).toBeInTheDocument();
        expect(input).toHaveValue('');
        expect(input).toHaveAttribute('aria-expanded', 'false');

        // Act
        await user.keyboard('{Escape}');

        // Assert
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
        expect(screen.getByRole('button', { name: 'Search library' })).toHaveFocus();
    });

    it('shows a failed search as an error, not as an empty result', async () => {
        // Arrange
        titleSearch = () => Promise.reject(new Error('boom'));
        renderSearch();

        // Act
        await openAndType('dark');

        // Assert
        expect(await screen.findByText('The library search failed')).toBeInTheDocument();
        expect(screen.queryByText(/No library titles match/)).not.toBeInTheDocument();
        expect(apiGet).not.toHaveBeenCalledWith(expect.stringContaining('/content-search'), expect.anything());
    });

    it('shows the no-match state without searching providers', async () => {
        // Arrange
        titleSearch = () => Promise.resolve([]);
        renderSearch();

        // Act
        await openAndType('zzz');

        // Assert
        expect(await screen.findByText('No library titles match “zzz”.')).toBeInTheDocument();
        expect(vi.mocked(apiGet).mock.calls.some(([path]) => path.includes('content-search'))).toBe(false);
    });

    it('opens the shared preview from the keyboard and returns to the same results when it closes', async () => {
        // Arrange
        renderSearch();
        const user = await openAndType('dark');
        await screen.findByRole('option', { name: /Darkness Falls/ });

        // Act
        await user.keyboard('{ArrowDown}{Enter}');
        expect(await screen.findByText('Preview of Darkness Falls')).toBeInTheDocument();
        await user.keyboard('{Escape}');

        // Assert
        await waitFor(() => expect(screen.queryByText('Preview of Darkness Falls')).not.toBeInTheDocument());
        const input = screen.getByRole('combobox', { name: 'Library title' });
        expect(input).toHaveValue('dark');
        expect(input).toHaveFocus();
        expect(screen.getByRole('option', { name: /Darkness Falls/ })).toHaveAttribute('aria-selected', 'true');
        expect(screen.getByTestId('location')).toHaveTextContent('/downloads|null');
        expect(apiGet).toHaveBeenCalledWith('/content/episode-20', expect.anything());
        expect(titleSearchCalls()).toHaveLength(1);
    });

    it('does not open an earlier result when Enter is pressed before the changed title settles', async () => {
        // Arrange
        renderSearch();
        const user = await openAndType('dark');
        await screen.findByRole('option', { name: /Darkness Falls/ });

        // Act
        await user.keyboard('x{Enter}');

        // Assert
        expect(apiGet).not.toHaveBeenCalledWith('/content/show-1', expect.anything());
        expect(screen.queryByText('Preview of Darkness Falls')).not.toBeInTheDocument();
    });

    it('returns focus to the clicked result when the preview closes', async () => {
        // Arrange
        renderSearch();
        const user = await openAndType('dark');
        const result = await screen.findByRole('option', { name: /Darkness Falls/ });

        // Act
        await user.click(result);
        await screen.findByText('Preview of Darkness Falls');
        await user.click(screen.getAllByRole('button', { name: 'Close' }).at(-1)!);

        // Assert
        await waitFor(() => expect(result).toHaveFocus());
        expect(screen.getByRole('dialog', { name: 'Search library' })).toBeInTheDocument();
    });

    it('links all library results to the same title scope at every level', async () => {
        // Arrange
        renderSearch();

        // Act
        await openAndType('dark "city"');

        // Assert
        expect(screen.getByRole('link', { name: 'All library results' })).toHaveAttribute(
            'href',
            '/library/all?filter=title+contains+%22dark+%5C%22city%5C%22%22&levels=all',
        );
    });

    it.each([
        [Role.Viewer, true],
        [Role.Requester, false],
        [Role.Manager, false],
    ])('gates the provider action on content.add for role %s', async (role, disabled) => {
        // Arrange
        renderSearch(role);

        // Act
        await openAndType('dark');

        // Assert
        expect(screen.getByRole('button', { name: 'Search providers' }).hasAttribute('disabled')).toBe(disabled);
    });

    it('passes the title to Add Content only when the provider action is chosen', async () => {
        // Arrange
        renderSearch(Role.Requester);
        const user = await openAndType('dark');
        await screen.findByRole('option', { name: /Darkness Falls/ });
        expect(vi.mocked(apiGet).mock.calls.some(([path]) => path.includes('content-search'))).toBe(false);

        // Act
        await user.click(screen.getByRole('button', { name: 'Search providers' }));

        // Assert
        expect(screen.getByTestId('location')).toHaveTextContent(
            '/library/add|{"filterQuery":"title contains \\"dark\\""}',
        );
        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('keeps the input and the close button together in the block that stays pinned', async () => {
        // Arrange
        renderSearch();

        // Act
        await openAndType('dark');

        // Assert
        const top = screen.getByRole('combobox', { name: 'Library title' }).closest('.library-quick-search__top');
        expect(top).not.toBeNull();
        expect(within(top as HTMLElement).getByRole('button', { name: 'Close' })).toBeInTheDocument();
        expect(within(top as HTMLElement).getByText('Search library')).toBeInTheDocument();
    });

    it('pins the input and scrolls the results and actions on a short viewport', () => {
        // Arrange
        const css = readFileSync(
            resolve(process.cwd(), 'src/components/content/LibraryQuickSearch/LibraryQuickSearch.css'),
            'utf8',
        );

        // Act
        const shortViewport = css.slice(css.indexOf('@media (max-height: 500px)'));

        // Assert
        expect(shortViewport).toMatch(
            /\.library-quick-search\.dialog-content \{[^}]*height: 100%;[^}]*overflow-y: auto;/,
        );
        expect(shortViewport).toMatch(/\.library-quick-search__top \{\s*position: sticky;\s*top: 0;/);
        expect(shortViewport).toMatch(/\.library-quick-search__results \{\s*flex-shrink: 0;\s*overflow: visible;/);
    });

    it('places the close button inside the title row without counting the safe area twice', () => {
        // Arrange
        const css = readFileSync(
            resolve(process.cwd(), 'src/components/content/LibraryQuickSearch/LibraryQuickSearch.css'),
            'utf8',
        );

        // Act
        const regular = css.slice(0, css.indexOf('@media'));

        // Assert
        expect(regular).toMatch(/\.library-quick-search__top \{[^}]*--overlay-close-top: var\(--space-md\);/);
        expect(regular).toMatch(/\.library-quick-search__top \{[^}]*padding: var\(--space-md\) /);
        expect(regular).toMatch(/\.library-quick-search__title \{[^}]*min-height: var\(--touch-target-min\);/);
    });

    it('keeps the pinned block and the rows inside the safe area on a short viewport', () => {
        // Arrange
        const css = readFileSync(
            resolve(process.cwd(), 'src/components/content/LibraryQuickSearch/LibraryQuickSearch.css'),
            'utf8',
        );

        // Act
        const shortViewport = css.slice(css.indexOf('@media (max-height: 500px)'));

        // Assert
        expect(shortViewport).toMatch(
            /\.library-quick-search\.dialog-content \{[^}]*padding: 0 env\(safe-area-inset-right, 0\) env\(safe-area-inset-bottom, 0\) env\(safe-area-inset-left, 0\);/,
        );
        expect(shortViewport).toMatch(
            /\.library-quick-search__top \{[^}]*padding: calc\(var\(--space-sm\) \+ env\(safe-area-inset-top, 0\)\)[^}]*--overlay-close-top: calc\(var\(--space-sm\) \+ env\(safe-area-inset-top, 0\)\);/,
        );
    });

    it('keeps the phone header action a 44px square', () => {
        // Arrange
        const css = readFileSync(
            resolve(process.cwd(), 'src/components/content/LibraryQuickSearch/LibraryQuickSearch.css'),
            'utf8',
        );

        // Act
        const phone = css.slice(css.indexOf('@media (max-width: 767px)'));

        // Assert
        expect(phone).toMatch(
            /\.library-quick-search__trigger \{\s*width: var\(--touch-target-min\);\s*padding: 0;\s*\}/,
        );
    });

    it('keeps the command palette shortcut', async () => {
        // Arrange
        renderSearch();
        const user = userEvent.setup();

        // Act
        await user.keyboard('{Control>}k{/Control}');

        // Assert
        expect(await screen.findByRole('dialog', { name: 'Command palette' })).toBeInTheDocument();
        expect(within(document.body).queryByRole('dialog', { name: 'Search library' })).not.toBeInTheDocument();
    });
});
