import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', async (importOriginal) => {
    const actual = await importOriginal<typeof import('@/lib/api')>();
    return {
        ...actual,
        apiGet: vi.fn(),
    };
});

import { apiGet } from '@/lib/api';
import { FileBrowser } from './FileBrowser';
import type { FileSystemEntry } from '@/hooks/useFileBrowser';

const mockFileSystem: Record<string, FileSystemEntry[]> = {
    '/': [
        { name: 'home', path: '/home', type: 'folder' },
        { name: 'media', path: '/media', type: 'folder' },
        { name: 'mnt', path: '/mnt', type: 'folder' },
        { name: 'opt', path: '/opt', type: 'folder' },
    ],
    '/home': [{ name: 'user', path: '/home/user', type: 'folder' }],
    '/home/user': [
        { name: 'Documents', path: '/home/user/Documents', type: 'folder' },
        { name: 'Downloads', path: '/home/user/Downloads', type: 'folder' },
        { name: 'notes.txt', path: '/home/user/notes.txt', type: 'file', size: 1024 },
    ],
    '/media/storage': [
        { name: 'Movies', path: '/media/storage/Movies', type: 'folder' },
        { name: 'TV', path: '/media/storage/TV', type: 'folder' },
    ],
    '/opt': [],
    '/srv': [{ name: 'movie.mp4', path: '/srv/movie.mp4', type: 'file', size: 18768 }],
};

function createWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: React.ReactNode }) =>
        createElement(QueryClientProvider, { client: queryClient }, children);
}

describe('FileBrowser', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockImplementation((url: string) => {
            const path = new URLSearchParams(url.split('?')[1]).get('path') ?? '/';
            const normalizedPath = path === '' ? '/' : path.replace(/\/+$/, '') || '/';
            return Promise.resolve(mockFileSystem[normalizedPath] ?? []);
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    const defaultProps = {
        open: true,
        onOpenChange: vi.fn(),
        onSelect: vi.fn(),
    };

    it('opens folders with the keyboard in folder mode and exposes selection on buttons', async () => {
        // Arrange
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} mode="folder" />, { wrapper: createWrapper() });
        const entry = await screen.findByRole('button', { name: 'home', pressed: false });

        // Act
        await user.click(entry);
        expect(entry).toHaveAttribute('aria-pressed', 'true');
        screen.getByRole('button', { name: 'Open home' }).focus();
        await user.keyboard('{Enter}');

        // Assert
        expect(await screen.findByRole('button', { name: 'Open user' })).toBeInTheDocument();
        expect(screen.getByRole('list', { name: 'Directory contents' })).toBeInTheDocument();
        expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    });

    it('renders dialog when open', async () => {
        render(<FileBrowser {...defaultProps} />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
    });

    it('does not render dialog when closed', () => {
        render(<FileBrowser {...defaultProps} open={false} />, { wrapper: createWrapper() });
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });

    it('displays correct title based on mode', async () => {
        const { rerender } = render(<FileBrowser {...defaultProps} mode="folder" />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByText('Select Folder')).toBeInTheDocument();

        rerender(<FileBrowser {...defaultProps} mode="file" />);
        expect(screen.getByText('Select File')).toBeInTheDocument();

        rerender(<FileBrowser {...defaultProps} mode="both" />);
        expect(screen.getByText('Select Path')).toBeInTheDocument();
    });

    it('uses custom title when provided', () => {
        render(<FileBrowser {...defaultProps} title="Choose Directory" />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByText('Choose Directory')).toBeInTheDocument();
    });

    it('displays navigation buttons', () => {
        render(<FileBrowser {...defaultProps} />, { wrapper: createWrapper() });
        expect(screen.getByRole('button', { name: /go to root/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /go to parent/i })).toBeInTheDocument();
    });

    it('displays breadcrumb navigation', () => {
        render(<FileBrowser {...defaultProps} />, { wrapper: createWrapper() });
        expect(screen.getByRole('navigation', { name: /current path/i })).toBeInTheDocument();
    });

    it('displays manual path input', () => {
        render(<FileBrowser {...defaultProps} />, { wrapper: createWrapper() });
        expect(screen.getByPlaceholderText('Enter path manually...')).toBeInTheDocument();
    });

    it('loads directory contents on open', async () => {
        render(<FileBrowser {...defaultProps} initialPath="/" />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        expect(screen.getByText('media')).toBeInTheDocument();
        expect(screen.getByText('mnt')).toBeInTheDocument();
    });

    it('normalizes empty initial path to root', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} initialPath="" mode="folder" onSelect={onSelect} />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%2F');
        expect(screen.getByPlaceholderText('Enter path manually...')).toHaveValue('/');
        expect(screen.getByRole('button', { name: /go to parent/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /go to root/i })).toBeDisabled();

        const selectionPath = document.querySelector('.file-browser__selection-path');
        expect(selectionPath).toHaveTextContent('/');

        await user.click(screen.getByRole('button', { name: /select$/i }));

        expect(onSelect).toHaveBeenCalledWith('/');
    });

    it('navigates to folder on click', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} mode="file" initialPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /open home/i }));

        await waitFor(() => {
            expect(screen.getByText('user')).toBeInTheDocument();
        });
    });

    it('navigates to parent on up button click', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('Documents')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /go to parent/i }));

        await waitFor(() => {
            expect(screen.getByText('user')).toBeInTheDocument();
        });
    });

    it('navigates to root on home button click', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('Documents')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /go to root/i }));

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });
    });

    it('disables parent button at root', async () => {
        render(<FileBrowser {...defaultProps} initialPath="/" />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /go to parent/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /go to root/i })).toBeDisabled();
    });

    it('uses the configured root as the navigation boundary', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/home/user/Documents" rootPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%2Fhome%2Fuser%2FDocuments');
        });

        expect(screen.queryByRole('button', { name: '/' })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'home' })).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: /go to root/i }));

        await waitFor(() => {
            expect(screen.getByPlaceholderText('Enter path manually...')).toHaveValue('/home/user');
        });
        expect(screen.getByRole('button', { name: /go to parent/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /go to root/i })).toBeDisabled();
    });

    it('rejects a manual path outside the configured root', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/home/user" rootPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('Documents')).toBeInTheDocument();
        });

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        await user.clear(pathInput);
        await user.type(pathInput, '/opt');
        await user.click(screen.getByRole('button', { name: 'Go' }));

        expect(await screen.findByText('The path must be in the configured root folder.')).toBeInTheDocument();
        expect(mockApiGet).not.toHaveBeenCalledWith('/system/files?path=%2Fopt');
    });

    it('rejects a case-variant manual path outside a POSIX root', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/media/storage/Movies" rootPath="/media/storage/Movies" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%2Fmedia%2Fstorage%2FMovies');
        });

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        await waitFor(() => expect(pathInput).toBeEnabled());
        await user.clear(pathInput);
        await user.type(pathInput, '/media/storage/movies');
        await user.click(screen.getByRole('button', { name: 'Go' }));

        expect(await screen.findByText('The path must be in the configured root folder.')).toBeInTheDocument();
        expect(mockApiGet).not.toHaveBeenCalledWith('/system/files?path=%2Fmedia%2Fstorage%2Fmovies');
    });

    it('rejects a case-variant manual path outside a UNC root', async () => {
        const user = userEvent.setup();
        render(
            <FileBrowser
                {...defaultProps}
                initialPath="\\\\Server\\Share\\Movies"
                rootPath="\\\\Server\\Share\\Movies"
            />,
            { wrapper: createWrapper() },
        );

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        await waitFor(() => expect(pathInput).toBeEnabled());
        await user.clear(pathInput);
        await user.type(pathInput, '\\\\server\\share\\movies-archive');
        await user.click(screen.getByRole('button', { name: 'Go' }));

        expect(await screen.findByText('The path must be in the configured root folder.')).toBeInTheDocument();
        expect(mockApiGet).not.toHaveBeenCalledWith('/system/files?path=%5C%5Cserver%5Cshare%5Cmovies-archive');
    });

    it('allows children of the configured POSIX file-system root', async () => {
        render(<FileBrowser {...defaultProps} initialPath="/movies/library" rootPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%2Fmovies%2Flibrary');
        });
        expect(screen.queryByText('The path must be in the configured root folder.')).not.toBeInTheDocument();
    });

    it('rejects a case-variant manual path outside a relative root', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="Media/Movies" rootPath="Media/Movies" />, {
            wrapper: createWrapper(),
        });

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        await waitFor(() => expect(pathInput).toBeEnabled());
        await user.clear(pathInput);
        await user.type(pathInput, 'media/movies');
        await user.click(screen.getByRole('button', { name: 'Go' }));

        expect(await screen.findByText('The path must be in the configured root folder.')).toBeInTheDocument();
    });

    it('navigates via breadcrumb', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('Documents')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: '/' }));

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });
    });

    it('navigates Windows drive paths by segment without leaving the drive', async () => {
        const user = userEvent.setup();
        const initialPath = 'C:\\Users\\Public';
        render(<FileBrowser {...defaultProps} initialPath={initialPath} />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=C%3A%5CUsers%5CPublic');
        });

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        expect(screen.getByRole('button', { name: 'C:\\' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Users' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Public' })).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: /go to parent/i }));

        await waitFor(() => {
            expect(pathInput).toHaveValue('C:\\Users');
        });

        await user.click(screen.getByRole('button', { name: /go to root/i }));

        await waitFor(() => {
            expect(pathInput).toHaveValue('C:\\');
        });
        expect(screen.getByRole('button', { name: /go to parent/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /go to root/i })).toBeDisabled();
    });

    it('treats a UNC share as the root for parent and breadcrumb navigation', async () => {
        const user = userEvent.setup();
        const initialPath = '\\\\nas.example\\Media\\TV\\Shows';
        render(<FileBrowser {...defaultProps} initialPath={initialPath} />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%5C%5Cnas.example%5CMedia%5CTV%5CShows');
        });

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        expect(screen.getByRole('button', { name: '\\\\nas.example\\Media' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'TV' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Shows' })).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'TV' }));

        await waitFor(() => {
            expect(pathInput).toHaveValue('\\\\nas.example\\Media\\TV');
        });

        await user.click(screen.getByRole('button', { name: /go to parent/i }));

        await waitFor(() => {
            expect(pathInput).toHaveValue('\\\\nas.example\\Media');
        });
        expect(screen.getByRole('button', { name: /go to parent/i })).toBeDisabled();
    });

    it('accepts a UNC initial path whose root differs only by case', async () => {
        const initialPath = '\\\\NAS.example\\Media\\TV\\Shows';
        render(<FileBrowser {...defaultProps} initialPath={initialPath} rootPath="\\\\nas.example\\media" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%5C%5CNAS.example%5CMedia%5CTV%5CShows');
        });
        expect(screen.queryByText('The path must be in the configured root folder.')).not.toBeInTheDocument();
    });

    it('treats a case-variant UNC configured root as the navigation boundary', async () => {
        const initialPath = '\\\\NAS.example\\Media\\TV';
        render(<FileBrowser {...defaultProps} initialPath={initialPath} rootPath="\\\\nas.example\\media\\tv" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(mockApiGet).toHaveBeenCalledWith('/system/files?path=%5C%5CNAS.example%5CMedia%5CTV');
        });
        expect(screen.getByRole('button', { name: /go to parent/i })).toBeDisabled();
        expect(screen.getByRole('button', { name: /go to root/i })).toBeDisabled();
        expect(screen.queryByRole('button', { name: '\\\\NAS.example\\Media' })).not.toBeInTheDocument();
    });

    it('navigates via manual path input', async () => {
        const user = userEvent.setup();
        render(<FileBrowser {...defaultProps} initialPath="/" />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        const pathInput = screen.getByPlaceholderText('Enter path manually...');
        await user.clear(pathInput);
        await user.type(pathInput, '/media/storage');
        await user.click(screen.getByRole('button', { name: 'Go' }));

        await waitFor(() => {
            expect(screen.getByText('Movies')).toBeInTheDocument();
        });
    });

    it('calls onSelect with current path in folder mode', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} mode="folder" onSelect={onSelect} />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /select$/i }));

        expect(onSelect).toHaveBeenCalledWith('/');
    });

    it('calls onOpenChange when cancel is clicked', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(<FileBrowser {...defaultProps} onOpenChange={onOpenChange} />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('has data-component attribute', async () => {
        render(<FileBrowser {...defaultProps} />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-component="file-browser"]')).toBeInTheDocument();
        });
    });

    it('has data-action attributes on action buttons', async () => {
        render(<FileBrowser {...defaultProps} />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByRole('button', { name: /go to root/i })).toHaveAttribute('data-action', 'navigate-home');
        expect(screen.getByRole('button', { name: /go to parent/i })).toHaveAttribute('data-action', 'navigate-up');
        expect(screen.getByRole('button', { name: /select$/i })).toHaveAttribute('data-action', 'select');
        expect(screen.getByRole('button', { name: /cancel/i })).toHaveAttribute('data-action', 'cancel');
    });

    it('shows loading state while fetching', async () => {
        let resolveFetch: (value: FileSystemEntry[]) => void;
        mockApiGet.mockReturnValue(
            new Promise((resolve) => {
                resolveFetch = resolve;
            }),
        );

        render(<FileBrowser {...defaultProps} initialPath="/" />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('Loading...')).toBeInTheDocument();
        });

        resolveFetch!(mockFileSystem['/']);

        await waitFor(() => {
            expect(screen.queryByText('Loading...')).not.toBeInTheDocument();
        });
    });

    it('shows empty state for empty directories', async () => {
        render(<FileBrowser {...defaultProps} initialPath="/opt" />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('This folder is empty')).toBeInTheDocument();
        });
    });

    it('shows no subfolders instead of empty for a folder with only files in folder mode', async () => {
        render(<FileBrowser {...defaultProps} mode="folder" initialPath="/srv" />, { wrapper: createWrapper() });

        await waitFor(() => {
            expect(screen.getByText('No subfolders')).toBeInTheDocument();
        });
        expect(screen.queryByText('This folder is empty')).not.toBeInTheDocument();
        expect(screen.queryByText('movie.mp4')).not.toBeInTheDocument();
    });

    it('displays selected path in folder mode', async () => {
        render(<FileBrowser {...defaultProps} mode="folder" initialPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        expect(screen.getByText('Selected:')).toBeInTheDocument();
        const selectionPath = document.querySelector('.file-browser__selection-path');
        expect(selectionPath).toHaveTextContent('/');
    });

    it('does not display selected path in file mode', async () => {
        render(<FileBrowser {...defaultProps} mode="file" initialPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        expect(screen.queryByText('Selected:')).not.toBeInTheDocument();
    });

    it('filters out files in folder mode', async () => {
        render(<FileBrowser {...defaultProps} mode="folder" initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('Documents')).toBeInTheDocument();
        });

        expect(screen.queryByText('notes.txt')).not.toBeInTheDocument();
    });

    it('shows files in file mode', async () => {
        render(<FileBrowser {...defaultProps} mode="file" initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('Documents')).toBeInTheDocument();
        });

        expect(screen.getByText('notes.txt')).toBeInTheDocument();
    });

    it('highlights folder on single click without navigating or committing', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} mode="folder" onSelect={onSelect} initialPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'home' }));

        expect(onSelect).not.toHaveBeenCalled();
        expect(screen.getByText('media')).toBeInTheDocument();
        expect(screen.queryByText('user')).not.toBeInTheDocument();

        const selectionPath = document.querySelector('.file-browser__selection-path');
        expect(selectionPath).toHaveTextContent('/home');
    });

    it('drills into folder on double click without committing', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} mode="folder" onSelect={onSelect} initialPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        await user.dblClick(screen.getByRole('button', { name: 'home' }));

        await waitFor(() => {
            expect(screen.getByText('user')).toBeInTheDocument();
        });

        expect(onSelect).not.toHaveBeenCalled();
    });

    it('commits highlighted folder via select button', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} mode="folder" onSelect={onSelect} initialPath="/" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('home')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'home' }));
        await user.click(screen.getByRole('button', { name: /select$/i }));

        expect(onSelect).toHaveBeenCalledWith('/home');
    });

    it('highlights file on single click without committing in file mode', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} mode="file" onSelect={onSelect} initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('notes.txt')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'notes.txt' }));

        expect(onSelect).not.toHaveBeenCalled();
    });

    it('commits file on double click in file mode', async () => {
        const user = userEvent.setup();
        const onSelect = vi.fn();
        render(<FileBrowser {...defaultProps} mode="file" onSelect={onSelect} initialPath="/home/user" />, {
            wrapper: createWrapper(),
        });

        await waitFor(() => {
            expect(screen.getByText('notes.txt')).toBeInTheDocument();
        });

        await user.dblClick(screen.getByRole('button', { name: 'notes.txt' }));

        expect(onSelect).toHaveBeenCalledWith('/home/user/notes.txt');
    });

    describe('on a short phone', () => {
        const deepPath = '/mnt/storage/media/library/movies';
        let innerWidth: number;

        beforeEach(() => {
            innerWidth = window.innerWidth;
            window.innerWidth = 320;
        });

        afterEach(() => {
            window.innerWidth = innerWidth;
        });

        it('collapses the middle crumbs into one crumb that keeps the first crumb and the last two', async () => {
            render(<FileBrowser {...defaultProps} initialPath={deepPath} />, { wrapper: createWrapper() });

            await screen.findByText('Selected:');

            const crumbs = within(screen.getByRole('navigation', { name: 'Current path' }))
                .getAllByRole('button')
                .map((crumb) => crumb.textContent);
            expect(crumbs).toEqual(['/', '…', 'library', 'movies']);
        });

        it('shows every crumb after a tap on the collapsed crumb', async () => {
            const user = userEvent.setup();
            render(<FileBrowser {...defaultProps} initialPath={deepPath} />, { wrapper: createWrapper() });

            await user.click(await screen.findByRole('button', { name: 'Show full path' }));

            const crumbs = within(screen.getByRole('navigation', { name: 'Current path' }))
                .getAllByRole('button')
                .map((crumb) => crumb.textContent);
            expect(crumbs).toEqual(['/', 'mnt', 'storage', 'media', 'library', 'movies']);
        });

        it('moves the focus to the first revealed crumb after a tap on the collapsed crumb', async () => {
            const user = userEvent.setup();
            render(<FileBrowser {...defaultProps} initialPath={deepPath} />, { wrapper: createWrapper() });

            await user.click(await screen.findByRole('button', { name: 'Show full path' }));

            expect(screen.getByRole('button', { name: 'mnt' })).toHaveFocus();
        });

        it('keeps a path of four crumbs whole', async () => {
            render(<FileBrowser {...defaultProps} initialPath="/mnt/storage/media" />, { wrapper: createWrapper() });

            await screen.findByText('Selected:');

            const crumbs = within(screen.getByRole('navigation', { name: 'Current path' }))
                .getAllByRole('button')
                .map((crumb) => crumb.textContent);
            expect(crumbs).toEqual(['/', 'mnt', 'storage', 'media']);
        });

        describe('when the crumbs are measured', () => {
            afterEach(() => {
                vi.restoreAllMocks();
            });

            it('Breadcrumb_ShowsTheParentCrumb_WhenTheParentCrumbFitsWhole', async () => {
                // Arrange
                vi.spyOn(Element.prototype, 'clientWidth', 'get').mockReturnValue(200);
                vi.spyOn(Element.prototype, 'scrollWidth', 'get').mockReturnValue(200);

                // Act
                render(<FileBrowser {...defaultProps} initialPath={deepPath} />, { wrapper: createWrapper() });
                await screen.findByText('Selected:');

                // Assert
                expect(screen.getByRole('navigation', { name: 'Current path' })).toHaveAttribute('data-fit', 'parent');
            });

            it('Breadcrumb_FoldsTheParentCrumb_WhenTheParentCrumbDoesNotFitWhole', async () => {
                // Arrange
                vi.spyOn(Element.prototype, 'clientWidth', 'get').mockReturnValue(200);
                vi.spyOn(Element.prototype, 'scrollWidth', 'get').mockImplementation(function (this: Element) {
                    return (this as HTMLElement).dataset.fit === 'parent' ? 260 : 200;
                });

                // Act
                render(<FileBrowser {...defaultProps} initialPath={deepPath} />, { wrapper: createWrapper() });
                await screen.findByText('Selected:');

                // Assert
                expect(screen.getByRole('navigation', { name: 'Current path' })).toHaveAttribute('data-fit', 'current');
            });
        });
    });
});
