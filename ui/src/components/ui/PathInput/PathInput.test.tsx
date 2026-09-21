import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('@/lib/api', async (importOriginal) => ({
    ...(await importOriginal<typeof import('@/lib/api')>()),
    apiPost: vi.fn(),
}));

import { AuthProvider } from '@/auth';
import { TooltipProvider } from '@/components/ui/Tooltip';
import { Role } from '@/auth/types';
import { apiPost } from '@/lib/api';
import { testEffectivePermissions } from '@/test/permissions';
import { PathInput } from './PathInput';

function createWrapper(user = { id: '1', username: 'admin', role: Role.Admin as Role }) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>
            <AuthProvider initialUser={user} skipSessionInit={true} initialPermissions={testEffectivePermissions()}>
                <TooltipProvider>{children}</TooltipProvider>
            </AuthProvider>
        </QueryClientProvider>
    );
}

describe('PathInput', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiPost.mockResolvedValue([]);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders an input element', () => {
        render(<PathInput aria-label="Path input" />, { wrapper: createWrapper() });
        expect(screen.getByRole('textbox')).toBeInTheDocument();
    });

    it('renders browse button by default', () => {
        render(<PathInput aria-label="Path input" />, { wrapper: createWrapper() });
        expect(screen.getByRole('button', { name: /browse/i })).toBeInTheDocument();
    });

    it('hides browse button when browseEnabled is false', () => {
        render(<PathInput aria-label="Path input" browseEnabled={false} />, {
            wrapper: createWrapper(),
        });
        expect(screen.queryByRole('button', { name: /browse/i })).not.toBeInTheDocument();
    });

    it('handles path input change', async () => {
        const user = userEvent.setup();
        const handleChange = vi.fn();
        render(<PathInput aria-label="Path" value="" onChange={handleChange} />, {
            wrapper: createWrapper(),
        });

        await user.type(screen.getByRole('textbox'), '/test');
        expect(handleChange).toHaveBeenCalledTimes(5);
    });

    it('displays provided value', () => {
        render(<PathInput aria-label="Path" value="/media/movies" onChange={() => {}} />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByRole('textbox')).toHaveValue('/media/movies');
    });

    it('applies custom placeholder based on mode', () => {
        const { rerender } = render(<PathInput aria-label="File path" mode="file" />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByPlaceholderText('/path/to/file')).toBeInTheDocument();

        rerender(<PathInput aria-label="Folder path" mode="folder" />);
        expect(screen.getByPlaceholderText('/path/to/folder')).toBeInTheDocument();

        rerender(<PathInput aria-label="Any path" mode="both" />);
        expect(screen.getByPlaceholderText('/path/to/file-or-folder')).toBeInTheDocument();
    });

    it('uses custom placeholder when provided', () => {
        render(<PathInput aria-label="Media folder" placeholder="/custom/placeholder" />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByPlaceholderText('/custom/placeholder')).toBeInTheDocument();
    });

    it('applies disabled state', () => {
        render(<PathInput aria-label="Disabled path" disabled />, { wrapper: createWrapper() });
        expect(screen.getByRole('textbox')).toBeDisabled();
        expect(screen.getByRole('button', { name: /browse/i })).toBeDisabled();
    });

    it('applies error state', () => {
        render(<PathInput aria-label="Error path" error errorMessage="Path is required" />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByRole('textbox')).toHaveAttribute('aria-invalid', 'true');
        expect(screen.getByRole('alert')).toHaveTextContent('Path is required');
    });

    it('opens file browser dialog when browse button is clicked', async () => {
        const user = userEvent.setup();
        render(<PathInput aria-label="Path" />, { wrapper: createWrapper() });

        await user.click(screen.getByRole('button', { name: /browse/i }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });
        expect(screen.getByText('Select Folder')).toBeInTheDocument();
    });

    it('selects root when browsing from an empty value', async () => {
        const user = userEvent.setup();
        const handleChange = vi.fn();
        render(<PathInput aria-label="Path" value="" onChange={handleChange} />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /browse/i }));

        await waitFor(() => {
            expect(screen.getByRole('dialog')).toBeInTheDocument();
        });

        expect(screen.getByPlaceholderText('Enter path manually...')).toHaveValue('/');

        await user.click(screen.getByRole('button', { name: /select$/i }));

        expect(handleChange).toHaveBeenCalledWith('/');
    });

    it('uses custom browse label', () => {
        render(<PathInput aria-label="Path" browseLabel="Open" />, { wrapper: createWrapper() });
        expect(screen.getByRole('button', { name: /open/i })).toBeInTheDocument();
    });

    it('opens the browser at a separate initial path', async () => {
        const user = userEvent.setup();
        render(<PathInput aria-label="Path" mode="file" value="/selected/movie.mkv" browserInitialPath="/library" />, {
            wrapper: createWrapper(),
        });

        await user.click(screen.getByRole('button', { name: /browse/i }));

        expect(await screen.findByPlaceholderText('Enter path manually...')).toHaveValue('/library');
    });

    it('has data-component attribute', () => {
        render(<PathInput aria-label="Path" />, { wrapper: createWrapper() });
        expect(document.querySelector('[data-component="path-input"]')).toBeInTheDocument();
    });

    it('has data-action attribute on browse button', () => {
        render(<PathInput aria-label="Path" />, { wrapper: createWrapper() });
        expect(screen.getByRole('button', { name: /browse/i })).toHaveAttribute('data-action', 'browse');
    });

    it('applies size variant', () => {
        const { rerender } = render(<PathInput aria-label="Small" size="sm" />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveClass('input-wrapper--sm');

        rerender(<PathInput aria-label="Large" size="lg" />);
        expect(screen.getByRole('textbox').closest('.input-wrapper')).toHaveClass('input-wrapper--lg');
    });

    it('renders leading icon when provided', () => {
        render(<PathInput aria-label="With icon" leadingIcon={<span data-testid="custom-icon">*</span>} />, {
            wrapper: createWrapper(),
        });
        expect(screen.getByTestId('custom-icon')).toBeInTheDocument();
    });

    describe('auth checks', () => {
        it('disables browse button when user lacks files.browse permission', () => {
            const viewer = { id: '1', username: 'viewer', role: Role.Viewer as Role };
            render(<PathInput aria-label="Path" />, { wrapper: createWrapper(viewer) });
            expect(screen.getByRole('button', { name: /browse/i })).toBeDisabled();
        });

        it('enables browse button for Admin', () => {
            const admin = { id: '1', username: 'admin', role: Role.Admin as Role };
            render(<PathInput aria-label="Path" />, { wrapper: createWrapper(admin) });
            expect(screen.getByRole('button', { name: /browse/i })).not.toBeDisabled();
        });

        it('enables unrestricted browse button for Manager', () => {
            const manager = { id: '1', username: 'manager', role: Role.Manager as Role };
            render(<PathInput aria-label="Path" />, { wrapper: createWrapper(manager) });
            expect(screen.getByRole('button', { name: /browse/i })).not.toBeDisabled();
        });

        it('enables scoped content browse button for Manager', () => {
            const manager = { id: '1', username: 'manager', role: Role.Manager as Role };
            render(<PathInput aria-label="Path" browsePermission="content.edit" />, {
                wrapper: createWrapper(manager),
            });
            expect(screen.getByRole('button', { name: /browse/i })).not.toBeDisabled();
        });
    });
});
