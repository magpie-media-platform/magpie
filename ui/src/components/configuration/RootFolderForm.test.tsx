import { render, screen, waitFor, within, cleanup } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, afterEach } from 'vitest';
import { QueryClient, QueryClientProvider, onlineManager } from '@tanstack/react-query';

vi.mock('@/hooks/useEventStream', () => ({
    useEventStream: () => {},
}));

vi.mock('@/lib/api', () => ({
    apiGet: vi.fn(async () => [
        {
            name: 'monitoring',
            extensionId: null,
            createdAt: '2026-01-01T00:00:00Z',
            settings: [
                {
                    id: 'setting-1',
                    settingsGroupId: 'group-1',
                    key: 'system-default',
                    value: 'false',
                    createdAt: '2026-01-01T00:00:00Z',
                },
            ],
        },
    ]),
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
import { apiGet, apiPost } from '@/lib/api';

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

afterEach(() => {
    cleanup();
    onlineManager.setOnline(true);
    vi.clearAllMocks();
});

describe('RootFolderForm default folder choice', () => {
    it('closes without a warning after reverting a level default that changes key order', async () => {
        // Arrange
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
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
                    />
                </TooltipProvider>
            </QueryClientProvider>,
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
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={() => {}}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        expect(screen.getByRole('radio', { name: 'Not the default' })).toBeDisabled();
        expect(screen.getByRole('radio', { name: 'Default for this content type' })).toBeChecked();
        expect(screen.getByText(/first folder for a content type must be the default/i)).toBeInTheDocument();
    });

    it('submits the first folder of a content type as the default', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={onSubmit}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ isDefault: true });
    });

    it('keeps Not the default when the content type already has a folder', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={onSubmit}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        expect(screen.getByRole('radio', { name: 'Not the default' })).toBeEnabled();

        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ isDefault: false });
    });

    it('starts auto-import off for a new folder', () => {
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={vi.fn()}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        expect(screen.getByRole('switch', { name: 'Auto-import confident matches' })).not.toBeChecked();
    });

    it('submits auto-import when the user turns it on', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={onSubmit}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        await user.click(screen.getByRole('switch', { name: 'Auto-import confident matches' }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ autoImport: true });
    });
});

describe('RootFolderForm default monitoring', () => {
    it('shows the system default in a disabled mode group when the folder uses the system default', async () => {
        // Arrange
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={vi.fn()}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Act
        const modeGroup = screen.getByRole('radiogroup', { name: 'Default monitoring' });
        const modes = within(modeGroup).getAllByRole('radio');

        // Assert
        expect(modeGroup).toHaveAccessibleName('Default monitoring');
        expect(screen.queryByRole('group', { name: 'Default monitoring' })).not.toBeInTheDocument();
        expect(screen.getByRole('switch', { name: 'Use system default' })).toBeChecked();
        expect(modes.map((mode) => mode.textContent)).toEqual(['Unmonitored', 'Monitored', 'Shotgun']);
        await waitFor(() => expect(modes[0]).toHaveAttribute('aria-checked', 'true'));
        expect(modes.map((mode) => (mode as HTMLButtonElement).disabled)).toEqual([true, true, true]);
    });

    it('selects no mode while the system default is loading', () => {
        // Arrange
        vi.mocked(apiGet).mockReturnValueOnce(new Promise<never>(() => {}));

        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={vi.fn()}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(
            within(screen.getByRole('radiogroup', { name: 'Default monitoring' }))
                .getAllByRole('radio')
                .map((mode) => mode.getAttribute('aria-checked')),
        ).toEqual(['false', 'false', 'false']);
    });

    it('selects no mode while the system default query is paused offline', () => {
        // Arrange
        onlineManager.setOnline(false);

        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={vi.fn()}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(apiGet).not.toHaveBeenCalled();
        expect(
            within(screen.getByRole('radiogroup', { name: 'Default monitoring' }))
                .getAllByRole('radio')
                .map((mode) => mode.getAttribute('aria-checked')),
        ).toEqual(['false', 'false', 'false']);
    });

    it('selects no mode, reports the failure and saves the system default when the system default fails to load', async () => {
        // Arrange
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        vi.mocked(apiGet).mockRejectedValueOnce(new Error('Settings request failed'));
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={onSubmit}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Act
        await screen.findByText('The system default could not be loaded.');
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        expect(screen.getByRole('switch', { name: 'Use system default' })).toHaveAccessibleDescription(
            'The system default could not be loaded.',
        );
        expect(
            within(screen.getByRole('radiogroup', { name: 'Default monitoring' }))
                .getAllByRole('radio')
                .map((mode) => mode.getAttribute('aria-checked')),
        ).toEqual(['false', 'false', 'false']);
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ defaultMonitoring: null });
    });

    it('shows Monitored as the system default when no system default is saved', async () => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValueOnce([]);

        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={vi.fn()}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        await waitFor(() =>
            expect(screen.getByRole('radio', { name: /^Monitored:/ })).toHaveAttribute('aria-checked', 'true'),
        );
    });

    it.each([
        ['Shotgun', /^Shotgun:/],
        ['Monitored', /^Monitored:/],
        ['true', /^Monitored:/],
    ])('SystemDefault_ShowsTheStoredMode_WhenTheSystemDefaultIs (%s)', async (stored, expected) => {
        // Arrange
        vi.mocked(apiGet).mockResolvedValueOnce([
            {
                name: 'monitoring',
                extensionId: null,
                createdAt: '2026-01-01T00:00:00Z',
                settings: [
                    {
                        id: 'setting-1',
                        settingsGroupId: 'group-1',
                        key: 'system-default',
                        value: stored,
                        createdAt: '2026-01-01T00:00:00Z',
                    },
                ],
            },
        ]);

        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={vi.fn()}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        await waitFor(() =>
            expect(screen.getByRole('radio', { name: expected })).toHaveAttribute('aria-checked', 'true'),
        );
    });

    it('saves Shotgun chosen by keyboard after the system default switch is turned off', async () => {
        // Arrange
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={notDefaultValues}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={onSubmit}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );
        await waitFor(() =>
            expect(screen.getByRole('radio', { name: /^Unmonitored:/ })).toHaveAttribute('aria-checked', 'true'),
        );

        // Act
        screen.getByRole('switch', { name: 'Use system default' }).focus();
        await user.keyboard(' ');
        await user.tab();
        await user.keyboard('{ArrowRight}{ArrowRight}');
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ defaultMonitoring: 'Shotgun' });
    });

    it('returns an explicit Shotgun default to the system default with the same switch', async () => {
        // Arrange
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={{ ...notDefaultValues, defaultMonitoring: 'Shotgun' }}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={onSubmit}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );
        const useSystemDefault = screen.getByRole('switch', { name: 'Use system default' });
        const shotgun = screen.getByRole('radio', { name: /^Shotgun:/ });
        expect(useSystemDefault).not.toBeChecked();
        expect(shotgun).toBeEnabled();
        expect(shotgun).toHaveAttribute('aria-checked', 'true');
        expect(screen.queryByRole('button', { name: 'Use system default' })).not.toBeInTheDocument();

        // Act
        await user.click(useSystemDefault);
        await user.click(screen.getByRole('button', { name: /save/i }));

        // Assert
        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        expect(onSubmit.mock.calls[0][0]).toMatchObject({ defaultMonitoring: null });
    });
});

describe('RootFolderForm rename mask', () => {
    it('previews the draft mask for the content type and inserts a token from the reference', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={{ ...notDefaultValues, renameTemplate: '{Title}/' }}
                        contentTypes={contentTypes}
                        contentTypesWithFolders={['movies']}
                        qualityTierLists={qualityTierLists}
                        onSubmit={() => {}}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
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

describe('RootFolderForm level file types', () => {
    const publication: LibraryTypeDefinition = {
        id: 'publication',
        slug: 'publication',
        name: 'Publication',
        pluralName: 'Publications',
        levels: [
            {
                slug: 'ebook',
                displayName: 'E-book',
                parentSlug: null,
                standardFactFields: [],
                acceptedFileTypes: ['application/epub+zip', '.epub'],
            },
            {
                slug: 'audiobook',
                displayName: 'Audiobook',
                parentSlug: null,
                standardFactFields: [],
                acceptedFileTypes: ['audio/*'],
            },
        ],
    };

    const series: LibraryTypeDefinition = {
        id: 'series',
        slug: 'series',
        name: 'Series',
        pluralName: 'Series',
        levels: [
            { slug: 'show', displayName: 'Show', parentSlug: null, standardFactFields: [] },
            {
                slug: 'episode',
                displayName: 'Episode',
                parentSlug: 'show',
                standardFactFields: [],
                acceptedFileTypes: ['video/*'],
            },
        ],
    };

    const undeclared: LibraryTypeDefinition = {
        id: 'undeclared',
        slug: 'undeclared',
        name: 'Undeclared',
        pluralName: 'Undeclared',
        levels: [{ slug: 'item', displayName: 'Item', parentSlug: null, standardFactFields: [] }],
    };

    const values: RootFolderFormValues = { ...notDefaultValues, contentType: '', defaults: {} };

    it('shows the accepted file types of each root level', () => {
        // Arrange
        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={{ ...values, contentType: 'publication' }}
                        contentTypes={[publication]}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={() => {}}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(
            within(document.querySelector('[data-level="ebook"]') as HTMLElement).getByText(
                'Accepts: application/epub+zip, .epub',
            ),
        ).toBeInTheDocument();
        expect(
            within(document.querySelector('[data-level="audiobook"]') as HTMLElement).getByText('Accepts: audio/*'),
        ).toBeInTheDocument();
    });

    it('shows the file types of the levels below a container level', () => {
        // Arrange
        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={{ ...values, contentType: 'series' }}
                        contentTypes={[series]}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={() => {}}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(
            within(document.querySelector('[data-level="show"]') as HTMLElement).getByText('Accepts: video/*'),
        ).toBeInTheDocument();
    });

    it('does not present a level without declared file types as accepting any MIME type', () => {
        // Arrange
        // Act
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={{ ...values, contentType: 'undeclared' }}
                        contentTypes={[undeclared]}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={() => {}}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Assert
        expect(screen.getByText('Accepts: No file types declared')).toBeInTheDocument();
        expect(screen.queryByText(/Accepts: Any MIME type/)).not.toBeInTheDocument();
    });

    it('lists the tier lists without calling them compatible', async () => {
        // Arrange
        const user = userEvent.setup();
        render(
            <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
                <TooltipProvider>
                    <RootFolderForm
                        open
                        onOpenChange={() => {}}
                        initialValues={{ ...values, contentType: 'undeclared' }}
                        contentTypes={[undeclared]}
                        contentTypesWithFolders={[]}
                        qualityTierLists={qualityTierLists}
                        onSubmit={() => {}}
                    />
                </TooltipProvider>
            </QueryClientProvider>,
        );

        // Act
        await user.click(screen.getByRole('combobox', { name: 'Default Quality Tiers' }));

        // Assert
        expect(await screen.findByText('Tier lists')).toBeInTheDocument();
        expect(screen.queryByText('Compatible tier lists')).not.toBeInTheDocument();
    });
});
