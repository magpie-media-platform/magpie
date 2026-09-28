import { render, screen, waitFor, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('@/hooks/useEventStream', () => ({
    useEventStream: () => {},
}));

vi.mock('@/lib/api', () => ({
    apiPost: vi.fn(async () => ({
        tokens: [
            {
                name: 'studio',
                family: 'Relationship',
                description: 'The production studios, separated by spaces.',
                example: 'Example Pictures Other Films',
            },
        ],
        preview: { directory: 'Example Pictures Other Films', fileName: 'Example Title.mkv' },
    })),
}));

vi.mock('@/hooks/useQualityTierLists', () => ({
    useQualityTierLists: () => ({ tierLists: [], isLoading: false }),
}));

import { TooltipProvider } from '@/components/ui/Tooltip';
import { apiPost } from '@/lib/api';

import { RootFolderForm, type RootFolderFormValues } from './RootFolderForm';
import type { LibraryTypeDefinition } from '@/types/library';

const contentTypes: LibraryTypeDefinition[] = [
    {
        id: 'movies',
        name: 'Movies',
        pluralName: 'Movies',
        slug: 'movies',
        levels: [{ slug: 'movie', displayName: 'Movie', parentSlug: null, standardFactFields: [] }],
    },
];

const qualityTierLists = [{ id: 'tier-1', name: 'Standard' }];

const notDefaultValues: RootFolderFormValues = {
    name: 'Movies',
    path: '/media/movies',
    contentType: 'movies',
    isDefault: false,
    defaultMonitoring: null,
    renameTemplate: '',
    upgradeFileHandling: 'Replace',
    autoImport: false,
    defaults: { movie: { qualityTierListId: 'tier-1' } },
};

function renderForm(ui: ReactNode) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return render(
        <QueryClientProvider client={queryClient}>
            <TooltipProvider>{ui}</TooltipProvider>
        </QueryClientProvider>,
    );
}

afterEach(() => {
    cleanup();
    vi.clearAllMocks();
});

describe('RootFolderForm default folder choice', () => {
    it('closes without a warning after reverting a level default that changes key order', async () => {
        // Arrange
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        renderForm(
            <RootFolderForm
                open
                onOpenChange={onOpenChange}
                initialValues={{
                    name: 'Library',
                    path: '/library',
                    contentType: 'sample',
                    isDefault: false,
                    defaultMonitoring: null,
                    renameTemplate: '',
                    upgradeFileHandling: 'Replace',
                    autoImport: false,
                    defaults: { root: { qualityTierListId: 'tier-1' }, leaf: { qualityTierListId: 'tier-1' } },
                }}
                contentTypes={[
                    {
                        id: 'sample',
                        slug: 'sample',
                        name: 'Sample',
                        pluralName: 'Samples',
                        levels: [
                            { slug: 'root', displayName: 'Root', parentSlug: null, standardFactFields: [] },
                            { slug: 'leaf', displayName: 'Leaf', parentSlug: 'root', standardFactFields: [] },
                        ],
                    },
                ]}
                contentTypesWithFolders={['sample']}
                qualityTierLists={[
                    { id: 'tier-1', name: 'Standard' },
                    { id: 'tier-2', name: 'Alternate' },
                ]}
                onSubmit={vi.fn()}
            />,
        );

        // Act
        await user.click(screen.getByRole('combobox', { name: 'Default Quality Tiers' }));
        await user.click(screen.getByRole('option', { name: /Alternate/ }));
        await user.click(screen.getByRole('combobox', { name: 'Default Quality Tiers' }));
        await user.click(screen.getByRole('option', { name: /Standard/ }));
        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        // Assert
        expect(onOpenChange).toHaveBeenCalledWith(false);
        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
    });
    it('disables Not the default and explains why for the first folder of a content type', () => {
        renderForm(
            <RootFolderForm
                open
                onOpenChange={() => {}}
                initialValues={notDefaultValues}
                contentTypes={contentTypes}
                contentTypesWithFolders={[]}
                qualityTierLists={qualityTierLists}
                onSubmit={() => {}}
            />,
        );

        expect(screen.getByRole('radio', { name: 'Not the default' })).toBeDisabled();
        expect(screen.getByRole('radio', { name: 'Default for this content type' })).toBeChecked();
        expect(screen.getByText(/first folder for a content type must be the default/i)).toBeInTheDocument();
    });

    it('submits the first folder of a content type as the default', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm(
            <RootFolderForm
                open
                onOpenChange={() => {}}
                initialValues={notDefaultValues}
                contentTypes={contentTypes}
                contentTypesWithFolders={[]}
                qualityTierLists={qualityTierLists}
                onSubmit={onSubmit}
            />,
        );

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ isDefault: true });
    });

    it('keeps Not the default when the content type already has a folder', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm(
            <RootFolderForm
                open
                onOpenChange={() => {}}
                initialValues={notDefaultValues}
                contentTypes={contentTypes}
                contentTypesWithFolders={['movies']}
                qualityTierLists={qualityTierLists}
                onSubmit={onSubmit}
            />,
        );

        expect(screen.getByRole('radio', { name: 'Not the default' })).toBeEnabled();

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ isDefault: false });
    });

    it('starts auto-import off for a new folder', () => {
        renderForm(
            <RootFolderForm
                open
                onOpenChange={() => {}}
                contentTypes={contentTypes}
                contentTypesWithFolders={[]}
                qualityTierLists={qualityTierLists}
                onSubmit={vi.fn()}
            />,
        );

        expect(screen.getByRole('switch', { name: 'Auto-import confident matches' })).not.toBeChecked();
    });

    it('submits auto-import when the user turns it on', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        renderForm(
            <RootFolderForm
                open
                onOpenChange={() => {}}
                initialValues={notDefaultValues}
                contentTypes={contentTypes}
                contentTypesWithFolders={['movies']}
                qualityTierLists={qualityTierLists}
                onSubmit={onSubmit}
            />,
        );

        await user.click(screen.getByRole('switch', { name: 'Auto-import confident matches' }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ autoImport: true });
    });
});

describe('RootFolderForm rename mask', () => {
    it('previews the draft mask for the content type and inserts a token from the reference', async () => {
        // Arrange
        const user = userEvent.setup();
        renderForm(
            <RootFolderForm
                open
                onOpenChange={() => {}}
                initialValues={{ ...notDefaultValues, renameTemplate: '{Title}/' }}
                contentTypes={contentTypes}
                contentTypesWithFolders={['movies']}
                qualityTierLists={qualityTierLists}
                onSubmit={() => {}}
            />,
        );

        // Act
        await user.click(await screen.findByText('Available tokens'));
        await user.click(screen.getByRole('button', { name: 'Insert {studio}' }));

        // Assert
        expect(screen.getByRole('textbox', { name: /Rename template/ })).toHaveValue('{Title}/{studio}');
        expect(screen.getByText('Example Title.mkv')).toBeInTheDocument();
        await waitFor(() =>
            expect(apiPost).toHaveBeenLastCalledWith('/content/rename-tokens', {
                contentType: 'movies',
                contentHierarchyItemId: undefined,
                mediaFileId: undefined,
                template: '{Title}/{studio}',
            }),
        );
    });
});
