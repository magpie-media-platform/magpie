import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';
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
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiPost.mockImplementation((url: string, body?: unknown) => {
            const request = body as { path?: string } | undefined;
            const path = request?.path ?? '/';
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
        mockApiPost.mockReturnValue(
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
});
