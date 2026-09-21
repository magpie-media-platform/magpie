import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport, ToastList } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPatch: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiDelete, apiPost, apiPatch } from '@/lib/api';
import type { ApiRemotePathMapping } from '@/hooks/useRemotePathMappings';

import { RemotePathMappingsPage } from './RemotePathMappingsPage';

const mockApiMappings: ApiRemotePathMapping[] = [
    {
        id: 'mapping-1',
        host: 'downloadbox.example.test',
        remotePath: 'E:\\Transmission',
        localPath: '\\\\downloadbox.example.test\\share\\Downloads',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'mapping-2',
        host: 'seedbox.example.com',
        remotePath: '/downloads/complete',
        localPath: '/mnt/seedbox/complete',
        createdAt: '2024-01-02T00:00:00Z',
    },
    {
        id: 'mapping-3',
        host: 'nas.local',
        remotePath: '/volume1/torrents',
        localPath: '/media/torrents',
        createdAt: '2024-01-03T00:00:00Z',
    },
];

function createTestWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
                gcTime: 0,
            },
            mutations: {
                retry: false,
            },
        },
    });

    return function TestWrapper({ children }: { children: ReactNode }) {
        return (
            <QueryClientProvider client={queryClient}>
                <MemoryRouter>
                    <TooltipProvider>
                        <ToastProvider>
                            <ToastStateProvider>
                                {children}
                                <ToastList />
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

describe('RemotePathMappingsPage', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiMappings);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with correct data-page attribute', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-page="remote-path-mappings"]')).toBeInTheDocument();
        });
    });

    it('renders the page title', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'Remote Path Mappings', level: 1 })).toBeInTheDocument();
        });
    });

    it('renders the page icon as decorative', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const icon = screen.getByTestId('remote-path-mappings-page__icon');
            expect(icon).toHaveAttribute('aria-hidden', 'true');
        });
    });

    it('renders an Add Mapping button', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const addButtons = screen.getAllByRole('button', { name: /add mapping/i });
            expect(addButtons.length).toBeGreaterThanOrEqual(1);
        });
    });
});

describe('RemotePathMappingsPage list items', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiMappings);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders mapping items in the list', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const list = screen.getByRole('list', { name: /remote path mappings/i });
            const items = within(list).getAllByRole('listitem');
            expect(items.length).toBe(3);
        });
    });

    it('renders host, remote path and local path', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByText('downloadbox.example.test')).toBeInTheDocument();
            expect(screen.getByText('E:\\Transmission')).toBeInTheDocument();
            expect(screen.getByText('\\\\downloadbox.example.test\\share\\Downloads')).toBeInTheDocument();
        });
    });

    it('applies data-mapping-id to items', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const items = document.querySelectorAll('[data-mapping-id]');
            expect(items.length).toBe(3);
            expect(items[0]).toHaveAttribute('data-mapping-id', 'mapping-1');
        });
    });

    it('shows an empty state when there are no mappings', async () => {
        mockApiGet.mockResolvedValue([]);

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByText('No remote path mappings configured')).toBeInTheDocument();
        });
    });
});

describe('RemotePathMappingsPage actions', () => {
    const mockApiGet = vi.mocked(apiGet);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiMappings);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders edit button for each mapping', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const editButtons = document.querySelectorAll('[data-action="edit"]');
            expect(editButtons.length).toBe(3);
        });
    });

    it('renders delete button for each mapping', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const deleteButtons = document.querySelectorAll('[data-action="delete"]');
            expect(deleteButtons.length).toBe(3);
        });
    });

    it('renders test local path button for each mapping', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const testButtons = document.querySelectorAll('[data-action="test-local-path"]');
            expect(testButtons.length).toBe(3);
        });
    });

    it('edit button has correct aria-label and target', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const editButton = screen.getByRole('button', { name: /^edit mapping for downloadbox\.example\.test$/i });
            expect(editButton).toHaveAttribute('data-action', 'edit');
            expect(editButton).toHaveAttribute('data-target', 'mapping-1');
        });
    });

    it('opens add form when add button is clicked', async () => {
        const user = userEvent.setup();

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add mapping/i }).length).toBeGreaterThanOrEqual(1);
        });

        const addButton = screen.getAllByRole('button', { name: /add mapping/i })[0];
        await user.click(addButton);

        await waitFor(() => {
            expect(screen.getByText('Add Remote Path Mapping')).toBeInTheDocument();
        });
    });

    it('opens edit form prefilled when edit is clicked', async () => {
        const user = userEvent.setup();

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(
                screen.getByRole('button', { name: /^edit mapping for downloadbox\.example\.test$/i }),
            ).toBeInTheDocument();
        });

        const editButton = screen.getByRole('button', { name: /^edit mapping for downloadbox\.example\.test$/i });
        await user.click(editButton);

        await waitFor(() => {
            expect(screen.getByText('Edit Remote Path Mapping')).toBeInTheDocument();
            expect(screen.getByDisplayValue('downloadbox.example.test')).toBeInTheDocument();
            expect(screen.getByDisplayValue('E:\\Transmission')).toBeInTheDocument();
        });
    });

    it('creates a mapping via POST when the add form is submitted', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({
            id: 'mapping-4',
            host: 'new.local',
            remotePath: '/downloads',
            localPath: '/media/downloads',
            createdAt: '2024-01-04T00:00:00Z',
        });

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getAllByRole('button', { name: /add mapping/i }).length).toBeGreaterThanOrEqual(1);
        });

        await user.click(screen.getAllByRole('button', { name: /add mapping/i })[0]);

        await waitFor(() => {
            expect(screen.getByText('Add Remote Path Mapping')).toBeInTheDocument();
        });

        await user.type(document.querySelector('[data-field="host"]')!, 'new.local');
        await user.type(document.querySelector('[data-field="remote-path"]')!, '/downloads');
        await user.type(document.querySelector('[data-field="local-path"]')!, '/media/downloads');

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/remote-path-mappings', {
                host: 'new.local',
                remotePath: '/downloads',
                localPath: '/media/downloads',
            });
        });
    });

    it('deletes a mapping via DELETE after confirming', async () => {
        const user = userEvent.setup();
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(
                screen.getByRole('button', { name: /^delete mapping for downloadbox\.example\.test$/i }),
            ).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^delete mapping for downloadbox\.example\.test$/i }));

        const confirmButton = await screen.findByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/remote-path-mappings/mapping-1');
        });
    });

    it('tests a mapping local path from the row action', async () => {
        const user = userEvent.setup();
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({
            success: true,
            message: 'Local path is reachable and readable.',
        });

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(
                screen.getByRole('button', { name: /^test local path for downloadbox\.example\.test$/i }),
            ).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^test local path for downloadbox\.example\.test$/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/remote-path-mappings/test', {
                localPath: '\\\\downloadbox.example.test\\share\\Downloads',
            });
        });
        expect(await screen.findByText('Local path is reachable and readable.')).toBeInTheDocument();
    });

    it('updates a mapping via PATCH when the edit form is submitted', async () => {
        const user = userEvent.setup();
        const mockApiPatch = vi.mocked(apiPatch);
        mockApiPatch.mockResolvedValue(undefined);

        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(
                screen.getByRole('button', { name: /^edit mapping for downloadbox\.example\.test$/i }),
            ).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /^edit mapping for downloadbox\.example\.test$/i }));

        await waitFor(() => {
            expect(screen.getByDisplayValue('downloadbox.example.test')).toBeInTheDocument();
        });

        const localPathInput = document.querySelector('[data-field="local-path"]') as HTMLInputElement;
        await user.clear(localPathInput);
        await user.type(localPathInput, '/new/local/path');

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => {
            expect(mockApiPatch).toHaveBeenCalledWith('/remote-path-mappings/mapping-1', {
                host: { value: 'downloadbox.example.test' },
                remotePath: { value: 'E:\\Transmission' },
                localPath: { value: '/new/local/path' },
            });
        });
    });
});

describe('RemotePathMappingsPage selection', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiDelete = vi.mocked(apiDelete);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(mockApiMappings);
        mockApiDelete.mockResolvedValue(undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders no selection overlay outside select mode', async () => {
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByText('downloadbox.example.test')).toBeInTheDocument();
        });
        expect(screen.queryByLabelText('Select mapping for downloadbox.example.test')).not.toBeInTheDocument();
        expect(document.querySelectorAll('[data-action="select-row"]').length).toBe(0);
        expect(screen.getByRole('button', { name: 'Select' })).toBeInTheDocument();
    });

    it('shows an overlay circle per row in select mode and clears selection on exit', async () => {
        const user = userEvent.setup();
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByText('downloadbox.example.test')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Select' }));
        const toggle = screen.getByLabelText('Select mapping for downloadbox.example.test');
        await user.click(toggle);
        expect(toggle).toHaveAttribute('aria-pressed', 'true');

        await user.click(screen.getByRole('button', { name: 'Stop selecting' }));
        await user.click(screen.getByRole('button', { name: 'Select' }));
        expect(screen.getByLabelText('Select mapping for downloadbox.example.test')).toHaveAttribute(
            'aria-pressed',
            'false',
        );
    });

    it('bulk-deletes every selected mapping', async () => {
        const user = userEvent.setup();
        render(<RemotePathMappingsPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByText('downloadbox.example.test')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: 'Select' }));
        await user.click(screen.getByLabelText('Select mapping for downloadbox.example.test'));
        await user.click(screen.getByLabelText('Select mapping for seedbox.example.com'));
        const toolbar = screen.getByRole('region', { name: 'Remote path mapping selection actions' });
        await user.click(within(toolbar).getByRole('button', { name: 'Delete selected remote path mappings' }));
        await user.click(document.querySelector('[data-action="confirm-delete"]') as HTMLElement);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/remote-path-mappings/mapping-1');
            expect(mockApiDelete).toHaveBeenCalledWith('/remote-path-mappings/mapping-2');
        });
    });
});
