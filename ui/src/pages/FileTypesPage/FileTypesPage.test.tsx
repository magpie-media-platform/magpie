import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';
import { TooltipProvider } from '@/components/ui/Tooltip';

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(),
    apiPost: vi.fn(),
    apiPut: vi.fn(),
    apiDelete: vi.fn(),
    isApiError: vi.fn((value: unknown) => {
        return typeof value === 'object' && value !== null && 'status' in value && 'message' in value;
    }),
}));

import { apiGet, apiPost, apiPut, apiDelete } from '@/lib/api';
import type { BlockedFileType, MimeMapping } from '@/hooks/useFileTypes';

import { FileTypesPage } from './FileTypesPage';

const mockDenyList: BlockedFileType[] = [
    {
        id: 'deny-1',
        pattern: '.iso',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        id: 'deny-2',
        pattern: 'application/x-msdownload',
        createdAt: '2024-02-01T00:00:00Z',
    },
];

const mockMimeMappings: MimeMapping[] = [
    {
        extension: '.mkv',
        mimeType: 'video/x-matroska',
        createdAt: '2024-01-01T00:00:00Z',
    },
    {
        extension: '.weirdo',
        mimeType: 'application/x-weirdo',
        createdAt: '2024-02-01T00:00:00Z',
    },
];

function createTestWrapper() {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
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
                                <ToastViewport />
                            </ToastStateProvider>
                        </ToastProvider>
                    </TooltipProvider>
                </MemoryRouter>
            </QueryClientProvider>
        );
    };
}

function setupApiMocks() {
    const mockApiGet = vi.mocked(apiGet);
    mockApiGet.mockImplementation((url: string) => {
        if (url === '/system/file-types/exclusions') {
            return Promise.resolve(mockDenyList);
        }
        if (url === '/system/file-types/mime') {
            return Promise.resolve(mockMimeMappings);
        }
        return Promise.reject(new Error(`Unexpected API call: ${url}`));
    });
}

describe('FileTypesPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        setupApiMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders with the correct data-page attribute', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-page="file-types"]')).toBeInTheDocument();
        });
    });

    it('renders the page title', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'File Types', level: 1 })).toBeInTheDocument();
        });
    });

    it('does not render a page-header description', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(screen.getByRole('heading', { name: 'File Types', level: 1 })).toBeInTheDocument();
        });

        expect(screen.queryByTestId('page-header__description')).not.toBeInTheDocument();
    });
});

describe('FileTypesPage hard-gate retirement', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        setupApiMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('no longer renders the hard-gate section', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="deny-list"]')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-section="hard-gate"]')).not.toBeInTheDocument();
        expect(screen.queryByTestId('hard-gate__extensions')).not.toBeInTheDocument();
        expect(screen.queryByTestId('hard-gate__chip')).not.toBeInTheDocument();
    });

    it('does not fetch the retired hard-gate endpoint', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="deny-list"]')).toBeInTheDocument();
        });

        const calledUrls = vi.mocked(apiGet).mock.calls.map((call) => call[0]);
        expect(calledUrls).not.toContain('/system/file-types/hard-gate');
    });

    it('shows configured deny-list rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="deny-1"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id="deny-1"]')!;
        expect(within(row as HTMLElement).getByText('.iso')).toBeInTheDocument();
        expect(row.querySelector('[data-action="delete"]')).not.toBeDisabled();
    });
});

describe('FileTypesPage tabs', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        setupApiMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders both deny-list and MIME-registry tab triggers', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-tab="deny-list"]')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-tab="mime-registry"]')).toBeInTheDocument();
    });

    it('defaults to the deny-list tab and switches to the MIME registry on click', async () => {
        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-tab="deny-list"]')).toHaveAttribute('data-state', 'active');
        });
        expect(document.querySelector('[data-tab="mime-registry"]')).toHaveAttribute('data-state', 'inactive');

        await user.click(document.querySelector('[data-tab="mime-registry"]') as HTMLButtonElement);

        await waitFor(() => {
            expect(document.querySelector('[data-tab="mime-registry"]')).toHaveAttribute('data-state', 'active');
        });
        expect(document.querySelector('[data-tab="deny-list"]')).toHaveAttribute('data-state', 'inactive');
    });

    it('renders both sections through the shared data table', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="deny-list"]')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-table="deny-list"] [data-component="data-table"]')).toBeInTheDocument();
        expect(document.querySelector('[data-table="deny-list"] [role="grid"]')).toBeInTheDocument();
        expect(
            document.querySelector('[data-table="mime-registry"] [data-component="data-table"]'),
        ).toBeInTheDocument();
        expect(document.querySelector('[data-table="mime-registry"] [role="grid"]')).toBeInTheDocument();
    });

    it('does not box the deny list inside a card', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-section="deny-list"]')).toBeInTheDocument();
        });

        const section = document.querySelector('[data-section="deny-list"]')!;
        expect(section.querySelector('[data-component="card"]')).not.toBeInTheDocument();
    });
});

describe('FileTypesPage deny list', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        setupApiMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders all deny-list rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const table = document.querySelector('[data-table="deny-list"]');
            expect(table).toBeInTheDocument();
            expect(table!.querySelector('[data-row-id="deny-1"]')).toBeInTheDocument();
            expect(table!.querySelector('[data-row-id="deny-2"]')).toBeInTheDocument();
        });
    });

    it('does not render a source column or badge', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="deny-1"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id="deny-1"]')!;
        expect(row.querySelector('[data-label="Source"]')).not.toBeInTheDocument();
        expect(within(row as HTMLElement).queryByText('Bundled')).not.toBeInTheDocument();
    });

    it('enables the delete button on extension rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="deny-1"]')).toBeInTheDocument();
        });

        const extensionRow = document.querySelector('[data-row-id="deny-1"]')!;
        const deleteButton = extensionRow.querySelector('[data-action="delete"]');
        expect(deleteButton).not.toBeDisabled();
    });

    it('enables the delete button on MIME-pattern rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="deny-2"]')).toBeInTheDocument();
        });

        const mimePatternRow = document.querySelector('[data-row-id="deny-2"]')!;
        const deleteButton = mimePatternRow.querySelector('[data-action="delete"]');
        expect(deleteButton).not.toBeDisabled();
    });

    it('deletes an extension deny-list row through the delete API', async () => {
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="deny-1"]')).toBeInTheDocument();
        });

        const extensionRow = document.querySelector('[data-row-id="deny-1"]')!;
        await user.click(extensionRow.querySelector('[data-action="delete"]') as HTMLButtonElement);

        const confirmButton = await screen.findByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/system/file-types/exclusions/deny-1');
        });
    });

    it('adds an entry through a modal and apiPost', async () => {
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockResolvedValue({
            id: 'deny-new',
            pattern: '.dmg',
            createdAt: '2024-03-01T00:00:00Z',
        });

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-action="add-deny-list-entry"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="add-deny-list-entry"]') as HTMLButtonElement);

        const form = await screen.findByRole('dialog');
        const patternInput = within(form).getByLabelText('Pattern');
        await user.type(patternInput, '.dmg');

        await user.click(within(form).getByRole('button', { name: /^add$/i }));

        await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/system/file-types/exclusions', JSON.stringify('.dmg'));
        });
    });

    it('shows a modal error when the pattern is empty', async () => {
        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-action="add-deny-list-entry"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="add-deny-list-entry"]') as HTMLButtonElement);

        const form = await screen.findByRole('dialog');
        await user.click(within(form).getByRole('button', { name: /^add$/i }));

        await waitFor(() => {
            expect(within(form).getByRole('alert')).toHaveTextContent(/pattern must not be empty/i);
        });
    });

    it('surfaces backend error responses on add failure', async () => {
        const mockApiPost = vi.mocked(apiPost);
        mockApiPost.mockRejectedValue({
            status: 400,
            message: 'Pattern already exists.',
        });

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-action="add-deny-list-entry"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="add-deny-list-entry"]') as HTMLButtonElement);

        const form = await screen.findByRole('dialog');
        await user.type(within(form).getByLabelText('Pattern'), '.dmg');
        await user.click(within(form).getByRole('button', { name: /^add$/i }));

        await waitFor(() => {
            expect(within(form).getByText(/pattern already exists/i)).toBeInTheDocument();
        });
    });

    it('calls delete API when confirming a deletion', async () => {
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id="deny-2"]')).toBeInTheDocument();
        });

        const mimePatternRow = document.querySelector('[data-row-id="deny-2"]')!;
        const deleteButton = mimePatternRow.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        const confirmButton = await screen.findByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/system/file-types/exclusions/deny-2');
        });
    });
});

describe('FileTypesPage mime registry', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        setupApiMocks();
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('renders all mime mapping rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            const table = document.querySelector('[data-table="mime-registry"]');
            expect(table).toBeInTheDocument();
            expect(table!.querySelector('[data-row-id=".mkv"]')).toBeInTheDocument();
            expect(table!.querySelector('[data-row-id=".weirdo"]')).toBeInTheDocument();
        });
    });

    it('does not render a source column or badge', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id=".mkv"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id=".mkv"]')!;
        expect(row.querySelector('[data-label="Source"]')).not.toBeInTheDocument();
        expect(within(row as HTMLElement).queryByText('Bundled')).not.toBeInTheDocument();
        expect(document.querySelector('[data-column="source"]')).not.toBeInTheDocument();
    });

    it('enables the delete button on mapping rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id=".mkv"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id=".mkv"]')!;
        const deleteButton = row.querySelector('[data-action="delete"]');
        expect(deleteButton).not.toBeDisabled();
    });

    it('enables the delete button on additional mapping rows', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id=".weirdo"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id=".weirdo"]')!;
        const deleteButton = row.querySelector('[data-action="delete"]');
        expect(deleteButton).not.toBeDisabled();
    });

    it('adds a new MIME mapping through a modal and apiPut', async () => {
        const mockApiPut = vi.mocked(apiPut);
        mockApiPut.mockResolvedValue({
            extension: '.foo',
            mimeType: 'application/x-foo',
            createdAt: '2024-03-01T00:00:00Z',
        });

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-action="add-mime-mapping"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-action="add-mime-mapping"]') as HTMLButtonElement);

        const form = await screen.findByRole('dialog');
        await user.type(within(form).getByLabelText('Extension'), '.foo');
        await user.type(within(form).getByLabelText('MIME type'), 'application/x-foo');
        await user.click(within(form).getByRole('button', { name: /^add$/i }));

        await waitFor(() => {
            expect(mockApiPut).toHaveBeenCalledWith('/system/file-types/mime/.foo', {
                mimeType: 'application/x-foo',
            });
        });
    });

    it('shows a modal error when either field is empty', async () => {
        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-tab="mime-registry"]')).toBeInTheDocument();
        });
        await user.click(document.querySelector('[data-tab="mime-registry"]') as HTMLButtonElement);

        await user.click(document.querySelector('[data-action="add-mime-mapping"]') as HTMLButtonElement);

        const form = await screen.findByRole('dialog');
        await user.click(within(form).getByRole('button', { name: /^add$/i }));

        await waitFor(() => {
            expect(within(form).getByRole('alert')).toHaveTextContent(/extension and mime type are required/i);
        });
    });

    it('edits a mime mapping through a modal and apiPut', async () => {
        const mockApiPut = vi.mocked(apiPut);
        mockApiPut.mockResolvedValue({
            extension: '.weirdo',
            mimeType: 'application/x-weirdo-v2',
            createdAt: '2024-02-01T00:00:00Z',
        });

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id=".weirdo"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id=".weirdo"]')!;
        await user.click(row.querySelector('[data-action="edit"]') as HTMLButtonElement);

        const form = await screen.findByRole('dialog');
        const input = within(form).getByLabelText('MIME type') as HTMLInputElement;
        await user.clear(input);
        await user.type(input, 'application/x-weirdo-v2');

        await user.click(within(form).getByRole('button', { name: /^save$/i }));

        await waitFor(() => {
            expect(mockApiPut).toHaveBeenCalledWith('/system/file-types/mime/.weirdo', {
                mimeType: 'application/x-weirdo-v2',
            });
        });
    });

    it('does not edit inline (no inline edit input in the row)', async () => {
        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id=".weirdo"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id=".weirdo"]')!;
        await user.click(row.querySelector('[data-action="edit"]') as HTMLButtonElement);

        await screen.findByRole('dialog');
        expect(row.querySelector('[data-field="mime-edit-input"]')).not.toBeInTheDocument();
    });

    it('calls delete API when confirming a deletion', async () => {
        const mockApiDelete = vi.mocked(apiDelete);
        mockApiDelete.mockResolvedValue(undefined);

        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-row-id=".weirdo"]')).toBeInTheDocument();
        });

        const row = document.querySelector('[data-row-id=".weirdo"]')!;
        const deleteButton = row.querySelector('[data-action="delete"]') as HTMLButtonElement;
        await user.click(deleteButton);

        const confirmButton = await screen.findByRole('button', { name: /^delete$/i });
        await user.click(confirmButton);

        await waitFor(() => {
            expect(mockApiDelete).toHaveBeenCalledWith('/system/file-types/mime/.weirdo');
        });
    });
});

describe('FileTypesPage mime registry paging and sort', () => {
    const manyMappings: MimeMapping[] = Array.from({ length: 120 }, (_, i) => ({
        extension: `.ext${String(i).padStart(3, '0')}`,
        mimeType: `application/x-ext-${String(i).padStart(3, '0')}`,
        createdAt: '2024-01-01T00:00:00Z',
    }));

    beforeEach(() => {
        vi.clearAllMocks();
        const mockApiGet = vi.mocked(apiGet);
        mockApiGet.mockImplementation((url: string) => {
            if (url === '/system/file-types/exclusions') {
                return Promise.resolve(mockDenyList);
            }
            if (url === '/system/file-types/mime') {
                return Promise.resolve(manyMappings);
            }
            return Promise.reject(new Error(`Unexpected API call: ${url}`));
        });
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    function mimeRows(): Element[] {
        const table = document.querySelector('[data-table="mime-registry"]')!;
        return Array.from(table.querySelectorAll('[data-row-id]'));
    }

    it('renders only one page (50 rows) by default', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="mime-registry"]')).toBeInTheDocument();
        });

        expect(mimeRows()).toHaveLength(50);
        expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
    });

    it('defaults to Extension ascending sort', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="mime-registry"]')).toBeInTheDocument();
        });

        const first = mimeRows()[0];
        expect(first.getAttribute('data-row-id')).toBe('.ext000');
    });

    it('navigates to the next page', async () => {
        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="mime-registry"]')).toBeInTheDocument();
        });

        await user.click(
            document.querySelector('[data-component="mime-pagination"] [data-action="next-page"]') as HTMLButtonElement,
        );

        await waitFor(() => {
            expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
        });
        expect(mimeRows()[0].getAttribute('data-row-id')).toBe('.ext050');
    });

    it('reverses order when the Extension header is clicked', async () => {
        const user = userEvent.setup();
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="mime-registry"]')).toBeInTheDocument();
        });

        await user.click(document.querySelector('[data-column="extension"] [role="button"]') as HTMLElement);

        await waitFor(() => {
            expect(mimeRows()[0].getAttribute('data-row-id')).toBe('.ext119');
        });
    });

    it('persists sort and page in the URL via msort / mpage params', async () => {
        const user = userEvent.setup();

        let search = '';
        function LocationProbe() {
            search = useLocation().search;
            return null;
        }

        render(
            <>
                <FileTypesPage />
                <LocationProbe />
            </>,
            { wrapper: createTestWrapper() },
        );

        await waitFor(() => {
            expect(document.querySelector('[data-table="mime-registry"]')).toBeInTheDocument();
        });

        await user.click(
            document.querySelector('[data-component="mime-pagination"] [data-action="next-page"]') as HTMLButtonElement,
        );

        await waitFor(() => {
            expect(search).toContain('mpage=2');
        });

        await user.click(document.querySelector('[data-column="extension"] [role="button"]') as HTMLElement);

        await waitFor(() => {
            expect(search).toContain('msort=');
        });
    });

    it('no longer renders the filter input', async () => {
        render(<FileTypesPage />, { wrapper: createTestWrapper() });

        await waitFor(() => {
            expect(document.querySelector('[data-table="mime-registry"]')).toBeInTheDocument();
        });

        expect(document.querySelector('[data-field="mime-filter"]')).not.toBeInTheDocument();
    });
});
