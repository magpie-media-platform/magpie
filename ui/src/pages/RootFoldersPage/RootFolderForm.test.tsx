import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { ToastProvider, ToastStateProvider, ToastViewport } from '@/components/ui/Toast';

import { RootFolderForm, type RootFolderFormValues } from './RootFolderForm';

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(),
}));

import { apiPost } from '@/lib/api';

function TestWrapper({ children }: { children: ReactNode }) {
    const queryClient = new QueryClient({
        defaultOptions: {
            queries: { retry: false, gcTime: 0 },
            mutations: { retry: false },
        },
    });

    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>
                <ToastProvider>
                    <ToastStateProvider>
                        {children}
                        <ToastViewport />
                    </ToastStateProvider>
                </ToastProvider>
            </MemoryRouter>
        </QueryClientProvider>
    );
}

const defaultContentTypes = [
    { id: 'movies', name: 'Movies', slug: 'movies' },
    { id: 'shows', name: 'TV Shows', slug: 'shows' },
    { id: 'music', name: 'Music', slug: 'music' },
];

describe('RootFolderForm', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiPost.mockResolvedValue([]);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });
    it('renders with correct data-component attribute when open', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="root-folder-form"]')).toBeInTheDocument();
    });

    it('does not render content when closed', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={false}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="root-folder-form"]')).not.toBeInTheDocument();
    });

    it('renders "Add Root Folder" title when adding new', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Root Folder')).toBeInTheDocument();
    });

    it('renders "Edit Root Folder" title when editing', () => {
        const initialValues: RootFolderFormValues = {
            id: 'folder-1',
            name: 'Movies',
            path: '/media/movies',
            contentTypeId: 'movies',
        };

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={initialValues}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByText('Edit Root Folder')).toBeInTheDocument();
    });
});

describe('RootFolderForm fields', () => {
    it('renders name field', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders path field', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/path/i)).toBeInTheDocument();
    });

    it('renders content type selector', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="content-type"]')).toBeInTheDocument();
    });

    it('populates fields with initial values when editing', () => {
        const initialValues: RootFolderFormValues = {
            id: 'folder-1',
            name: 'Movies',
            path: '/media/movies',
            contentTypeId: 'movies',
        };

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    initialValues={initialValues}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(screen.getAllByDisplayValue('Movies').length).toBeGreaterThanOrEqual(1);
        expect(screen.getByDisplayValue('/media/movies')).toBeInTheDocument();
    });
});

describe('RootFolderForm validation', () => {
    it('shows error when name is empty', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows error when path is empty', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Folder');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Path is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows error when content type is not selected', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Folder');

        const pathInput = screen.getByLabelText(/path/i);
        await user.type(pathInput, '/media/test');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Content type is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('clears error when field is corrected', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test');

        expect(screen.queryByText('Name is required')).not.toBeInTheDocument();
    });
});

describe('RootFolderForm submission', () => {
    it('calls onSubmit with form values when valid', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Folder');

        const pathInput = screen.getByLabelText(/path/i);
        await user.type(pathInput, '/media/test');

        const contentTypeTrigger = document.querySelector('[data-field="content-type"]') as HTMLElement;
        await user.click(contentTypeTrigger);

        const moviesOption = await screen.findByRole('option', { name: 'Movies' });
        await user.click(moviesOption);

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(onSubmit).toHaveBeenCalledWith({
            name: 'Test Folder',
            path: '/media/test',
            contentTypeId: 'movies',
        });
    });

    it('disables form fields when submitting', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const pathInput = screen.getByLabelText(/path/i);
        expect(pathInput).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });

    it('shows loading state when submitting', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                    isSubmitting={true}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });
});

describe('RootFolderForm cancel', () => {
    it('calls onOpenChange with false when cancel is clicked', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={onOpenChange}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const cancelButton = screen.getByRole('button', { name: /cancel/i });
        await user.click(cancelButton);

        await waitFor(() => {
            expect(onOpenChange).toHaveBeenCalledWith(false);
        });
    });
});

describe('RootFolderForm content type selector', () => {
    it('shows content types in dropdown', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const contentTypeTrigger = document.querySelector('[data-field="content-type"]') as HTMLElement;
        await user.click(contentTypeTrigger);

        expect(await screen.findByRole('option', { name: 'Movies' })).toBeInTheDocument();
        expect(await screen.findByRole('option', { name: 'TV Shows' })).toBeInTheDocument();
        expect(await screen.findByRole('option', { name: 'Music' })).toBeInTheDocument();
    });

    it('updates selected content type when option is clicked', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const contentTypeTrigger = document.querySelector('[data-field="content-type"]') as HTMLElement;
        await user.click(contentTypeTrigger);

        const moviesOption = await screen.findByRole('option', { name: 'TV Shows' });
        await user.click(moviesOption);

        await waitFor(() => {
            expect(contentTypeTrigger).toHaveTextContent('TV Shows');
        });
    });
});

describe('RootFolderForm accessibility', () => {
    it('has data-action attributes on buttons', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
    });

    it('has data-field attributes on form fields', () => {
        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="path"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="content-type"]')).toBeInTheDocument();
    });

    it('shows errors with role="alert"', async () => {
        const user = userEvent.setup();

        render(
            <TestWrapper>
                <RootFolderForm
                    open={true}
                    onOpenChange={() => {}}
                    contentTypes={defaultContentTypes}
                    onSubmit={() => {}}
                />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        const alerts = screen.getAllByRole('alert');
        expect(alerts.length).toBeGreaterThan(0);
    });
});
