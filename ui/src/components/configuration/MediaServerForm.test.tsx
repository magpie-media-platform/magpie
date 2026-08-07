import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import type { QueryClient } from '@tanstack/react-query';

import { ProviderTypesWrapper } from '@/test/providerTypesTestUtils';
import { createTestQueryClient } from '@/test/providerTypesTestQueryClient';
import { ExtensionContext, type ExtensionContextValue } from '@/extensions/ExtensionProvider';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

vi.mock('@/lib/api', async () => {
    const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
    return {
        ...actual,
        apiGet: vi.fn().mockResolvedValue([]),
        apiPost: vi.fn().mockResolvedValue({ isValid: true, issues: [] }),
    };
});

import { apiGet, apiPost } from '@/lib/api';
import { MediaServerForm, type MediaServerFormValues } from './MediaServerForm';

const PROVIDER_TYPES = ['emby', 'jellyfin', 'plex'];

function TestWrapper({ children }: { children: ReactNode }) {
    return (
        <MemoryRouter>
            <ProviderTypesWrapper seed={{ mediaServers: PROVIDER_TYPES }}>{children}</ProviderTypesWrapper>
        </MemoryRouter>
    );
}

const SAMPLE_SCHEMA: IntegrationSettingDefinition[] = [
    { key: 'baseUrl', type: SchemaDataType.String, displayName: 'Base URL', required: true },
    { key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true },
];

const JELLYFIN_SCHEMA: IntegrationSettingDefinition[] = [
    { key: 'host', type: SchemaDataType.String, displayName: 'Host URL', required: true },
    { key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true, required: true },
    {
        key: 'libraryMappings',
        type: SchemaDataType.MappingsList,
        displayName: 'Library mappings',
        rowSchema: [
            {
                key: 'rootFolderPath',
                type: SchemaDataType.String,
                displayName: 'Root folder',
                required: true,
                optionsSource: { kind: 'RootFolders' },
            },
            {
                key: 'contentType',
                type: SchemaDataType.String,
                displayName: 'Content type',
                required: true,
                optionsSource: { kind: 'ContentTypes' },
            },
            {
                key: 'serverLibraryId',
                type: SchemaDataType.String,
                displayName: 'Jellyfin library',
                required: true,
                optionsSource: {
                    kind: 'JSFunction',
                    functionName: 'magpieJellyfin.listLibraries',
                },
            },
        ],
    },
];

const READY_EXTENSION_CONTEXT: ExtensionContextValue = {
    state: { state: 'ready', error: null, manifest: { assets: [], extensions: [] } },
    context: {
        apiBaseUrl: '/api',
        userId: 'user-1',
        username: 'admin',
        role: 'Admin',
        theme: null,
    },
    getExtensionsForSlot: () => [],
    retry: () => Promise.resolve(),
};

describe('MediaServerForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={false} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Media Server')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Media Server')).toBeInTheDocument();
    });

    it('shows "Edit Media Server" when editing', async () => {
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'plex',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Server');
        expect(screen.getByText('Edit Media Server')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', async () => {
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Test Server',
                        provider: 'plex',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(await screen.findByDisplayValue('My Test Server')).toBeInTheDocument();
        expect(screen.getByDisplayValue('plex')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders integration dropdown populated from available integrations', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const providerField = document.querySelector('[data-field="provider"]');
        expect(providerField).toBeInTheDocument();
        expect(providerField).toHaveAttribute('aria-label', 'Integration');
    });

    it('shows empty-state copy when no provider integrations are registered', () => {
        render(
            <MemoryRouter>
                <ProviderTypesWrapper seed={{ mediaServers: [] }}>
                    <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        expect(
            screen.getByText('No media server integrations are registered. Install an extension that provides one.'),
        ).toBeInTheDocument();
    });

    it('renders enabled toggle', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/enabled/i)).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
    });

    it('shows validation error when name is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows validation error when integration is empty on submit', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Server');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Integration is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows the provider value as a disabled input when editing', async () => {
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'plex',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Server');
        const providerInput = document.querySelector('[data-field="provider"]');
        expect(providerInput).toBeDisabled();
        expect(providerInput).toHaveValue('plex');
    });

    it('shows loading state when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const providerField = document.querySelector('[data-field="provider"]');
        expect(providerField).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });

    it('resets form when opened with new values', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();

        const { rerender } = render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Name');

        rerender(
            <TestWrapper>
                <MediaServerForm open={false} onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        rerender(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'server-2',
                        name: 'Different Server',
                        provider: 'jellyfin',
                        enabled: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(screen.getByDisplayValue('Different Server')).toBeInTheDocument();
    });

    it('calls onTest with the current form values when Test is clicked', async () => {
        const user = userEvent.setup();
        const onTest = vi.fn();
        render(
            <TestWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'plex',
                        enabled: true,
                        settings: { host: 'localhost' },
                    }}
                    onSubmit={vi.fn()}
                    onTest={onTest}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Server');
        await user.click(screen.getByRole('button', { name: /test/i }));

        expect(onTest).toHaveBeenCalledWith({
            providerType: 'plex',
            id: 'server-1',
            settings: { host: 'localhost' },
        });
    });

    it('hides the Test button until a provider is selected in add mode', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} onTest={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByRole('button', { name: /test/i })).not.toBeInTheDocument();
    });
});

describe('MediaServerForm accessibility', () => {
    it('has data-component attribute', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="media-server-form"]')).toBeInTheDocument();
    });

    it('has data-action attributes on buttons', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-action="cancel"]')).toBeInTheDocument();
        expect(document.querySelector('[data-action="save"]')).toBeInTheDocument();
    });

    it('has data-field attributes on form inputs', () => {
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-field="name"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="provider"]')).toBeInTheDocument();
        expect(document.querySelector('[data-field="enabled"]')).toBeInTheDocument();
    });

    it('displays error messages with role="alert"', async () => {
        const user = userEvent.setup();
        render(
            <TestWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        const alerts = screen.getAllByRole('alert');
        expect(alerts.length).toBeGreaterThan(0);
    });
});

describe('MediaServerForm schema integration', () => {
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });
        delete (window as unknown as Record<string, unknown>).magpieJellyfin;
    });

    afterEach(() => {
        delete (window as unknown as Record<string, unknown>).magpieJellyfin;
        vi.restoreAllMocks();
    });

    function SchemaWrapper({ children }: { children: ReactNode }) {
        return (
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        mediaServers: PROVIDER_TYPES,
                        schemas: { mediaServers: { jellyfin: SAMPLE_SCHEMA } },
                    }}
                >
                    {children}
                </ProviderTypesWrapper>
            </MemoryRouter>
        );
    }

    function JellyfinSchemaWrapper({ children, client }: { children: ReactNode; client: QueryClient }) {
        return (
            <MemoryRouter>
                <ExtensionContext.Provider value={READY_EXTENSION_CONTEXT}>
                    <ProviderTypesWrapper
                        client={client}
                        seed={{
                            mediaServers: PROVIDER_TYPES,
                            schemas: { mediaServers: { jellyfin: JELLYFIN_SCHEMA } },
                        }}
                    >
                        {children}
                    </ProviderTypesWrapper>
                </ExtensionContext.Provider>
            </MemoryRouter>
        );
    }

    it('keeps the provider selected, preserves the name, and renders settings when adding a provider with a schema (MAGPIE-688)', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <SchemaWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </SchemaWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'My Server');
        await user.click(screen.getByRole('combobox', { name: /integration/i }));
        await user.click(screen.getByRole('option', { name: 'jellyfin' }));

        expect(screen.getByRole('combobox', { name: /integration/i })).toHaveTextContent('jellyfin');
        expect(screen.getByDisplayValue('My Server')).toBeInTheDocument();
        expect(await screen.findByLabelText(/Base URL/i)).toBeInTheDocument();
    });

    it('renders schema-driven inputs when editing a server with a known provider', async () => {
        render(
            <SchemaWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'jellyfin',
                        enabled: true,
                        settings: { baseUrl: 'http://localhost:8096' },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-setting-key="baseUrl"]')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-setting-key="apiKey"]')).toBeInTheDocument();
        expect(screen.getByDisplayValue('http://localhost:8096')).toBeInTheDocument();
    });

    it('discovers and persists a Jellyfin library while adding, then shows it selected while editing', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const listLibraries = vi.fn(() => [
            { value: 'lib-movies', label: 'Movies' },
            { value: 'lib-shows', label: 'Shows' },
        ]);
        vi.mocked(apiGet).mockImplementation((path) => {
            if (path === '/root-folders') {
                return Promise.resolve([{ name: 'Shows root', path: '/data/shows' }]);
            }
            if (path === '/content-types') {
                return Promise.resolve([{ slug: 'shows', displayName: 'Shows content' }]);
            }
            return Promise.reject(new Error(`Unexpected path: ${path}`));
        });
        (window as unknown as Record<string, unknown>).magpieJellyfin = { listLibraries };
        const client = createTestQueryClient({
            mediaServers: PROVIDER_TYPES,
            schemas: { mediaServers: { jellyfin: JELLYFIN_SCHEMA } },
        });
        const { rerender } = render(
            <JellyfinSchemaWrapper client={client}>
                <MediaServerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
            </JellyfinSchemaWrapper>,
        );

        await user.type(screen.getByLabelText(/Name/), 'Jellyfin');
        await user.click(screen.getByRole('combobox', { name: 'Integration' }));
        await user.click(screen.getByRole('option', { name: 'jellyfin' }));
        await user.type(await screen.findByLabelText(/Host URL/), 'http://jellyfin.local:8096');
        await user.type(screen.getByLabelText(/API Key/), 'new-secret');
        await user.click(screen.getByRole('button', { name: 'Add row' }));
        await user.click(await screen.findByLabelText('Root folder'));
        await user.click(screen.getByRole('option', { name: /Shows root/ }));
        await user.click(await screen.findByLabelText('Content type'));
        await user.click(screen.getByRole('option', { name: /Shows content/ }));

        await waitFor(() => {
            expect(listLibraries).toHaveBeenCalledWith(
                expect.objectContaining({
                    entityId: null,
                    state: expect.objectContaining({
                        host: 'http://jellyfin.local:8096',
                        apiKey: 'new-secret',
                    }),
                }),
            );
        });
        await waitFor(() => expect(screen.getByLabelText('Jellyfin library')).toBeEnabled());
        const picker = screen.getByLabelText('Jellyfin library');
        await user.click(picker);
        await user.click(await screen.findByRole('option', { name: 'Shows' }));
        await user.click(screen.getByRole('button', { name: /save/i }));

        await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
        const submitted = onSubmit.mock.calls[0]?.[0] as MediaServerFormValues;
        expect(JSON.parse(submitted.settings.libraryMappings as string)).toEqual([
            { rootFolderPath: '/data/shows', contentType: 'shows', serverLibraryId: 'lib-shows' },
        ]);

        rerender(
            <JellyfinSchemaWrapper client={client}>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'Jellyfin',
                        provider: 'jellyfin',
                        enabled: true,
                        settings: {
                            host: 'http://jellyfin.local:8096',
                            libraryMappings: submitted.settings.libraryMappings,
                        },
                    }}
                    onSubmit={vi.fn()}
                />
            </JellyfinSchemaWrapper>,
        );

        await waitFor(() => {
            expect(listLibraries).toHaveBeenCalledWith(
                expect.objectContaining({
                    entityId: 'server-1',
                    state: expect.not.objectContaining({ apiKey: expect.anything() }),
                }),
            );
        });
        await waitFor(() => expect(screen.getByLabelText('Jellyfin library')).toHaveTextContent('Shows'));
    });

    it('reloads saved settings when initial values arrive after the form opens', async () => {
        const { rerender } = render(
            <SchemaWrapper>
                <MediaServerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </SchemaWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toHaveValue('');

        rerender(
            <SchemaWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'Jellyfin',
                        provider: 'jellyfin',
                        enabled: true,
                        settings: { baseUrl: 'http://localhost:8096', apiKey: '********' },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        expect(await screen.findByDisplayValue('Jellyfin')).toBeInTheDocument();
        expect(screen.getByDisplayValue('jellyfin')).toBeInTheDocument();
        await waitFor(() => {
            expect(document.querySelector('[data-setting-key="baseUrl"]')).toBeInTheDocument();
        });
        expect(screen.getByDisplayValue('http://localhost:8096')).toBeInTheDocument();
        const apiKey = document.querySelector('[data-field="apiKey"]') as HTMLInputElement;
        expect(apiKey.value).toBe('');
        expect(apiKey).toHaveAttribute('placeholder', '********');
    });

    it('keeps Save enabled when editing name with a configured omitted API key', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <SchemaWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'Jellyfin',
                        provider: 'jellyfin',
                        enabled: true,
                        settings: { baseUrl: 'http://localhost:8096' },
                    }}
                    onSubmit={onSubmit}
                />
            </SchemaWrapper>,
        );

        await waitFor(() => expect(screen.getByRole('button', { name: /save/i })).toBeEnabled());

        const nameInput = screen.getByLabelText(/name/i);
        await user.clear(nameInput);
        await user.type(nameInput, 'Jellyfin Movies');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledTimes(1);
        const submitted = onSubmit.mock.calls[0][0];
        expect(submitted.name).toBe('Jellyfin Movies');
        expect(submitted.settings).toEqual({ baseUrl: 'http://localhost:8096' });
        expect(submitted.settings).not.toHaveProperty('apiKey');
    });

    it('seeds schema defaultValue into settings on add when value is absent', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
            { key: 'baseUrl', type: SchemaDataType.String, displayName: 'Base URL', required: true },
            {
                key: 'paused',
                type: SchemaDataType.Boolean,
                displayName: 'Add as paused',
                defaultValue: 'false',
            },
        ];
        const mockApiGet = vi.mocked(apiGet);
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULTS);

        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        mediaServers: PROVIDER_TYPES,
                        schemas: { mediaServers: { jellyfin: SCHEMA_WITH_DEFAULTS } },
                    }}
                >
                    <MediaServerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={{ name: '', provider: 'jellyfin', enabled: true, settings: {} }}
                        onSubmit={onSubmit}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        expect(await screen.findByRole('switch', { name: 'Add as paused' })).not.toBeChecked();
        await user.type(screen.getByLabelText(/name/i), 'Jellyfin');
        await user.type(document.querySelector('[data-field="baseUrl"]') as HTMLInputElement, 'http://localhost:8096');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({ settings: expect.objectContaining({ paused: 'false' }) }),
        );
    });

    it('does not warn on cancel after schema defaults seed with no user edits', async () => {
        const SCHEMA_WITH_DEFAULT: IntegrationSettingDefinition[] = [
            { key: 'baseUrl', type: SchemaDataType.String, displayName: 'Base URL', required: true },
            { key: 'libraryName', type: SchemaDataType.String, displayName: 'Library', defaultValue: 'Movies' },
        ];
        const mockApiGet = vi.mocked(apiGet);
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULT);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        mediaServers: PROVIDER_TYPES,
                        schemas: { mediaServers: { jellyfin: SCHEMA_WITH_DEFAULT } },
                    }}
                >
                    <MediaServerForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={{
                            id: 'server-1',
                            name: 'My Server',
                            provider: 'jellyfin',
                            enabled: true,
                            settings: { baseUrl: 'http://localhost:8096' },
                        }}
                        onSubmit={vi.fn()}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        await screen.findByDisplayValue('Movies');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('warns on cancel after a real settings edit', async () => {
        const mockApiGet = vi.mocked(apiGet);
        mockApiGet.mockResolvedValue(SAMPLE_SCHEMA);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <SchemaWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'jellyfin',
                        enabled: true,
                        settings: { baseUrl: 'http://localhost:8096' },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const baseUrlInput = await screen.findByDisplayValue('http://localhost:8096');
        await user.type(baseUrlInput, '/x');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('disables Save and surfaces field-level issues when local validation fails', async () => {
        render(
            <SchemaWrapper>
                <MediaServerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'server-1',
                        name: 'My Server',
                        provider: 'jellyfin',
                        enabled: true,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await waitFor(() => expect(saveButton).toBeDisabled(), { timeout: 2000 });

        expect(screen.queryByText('Base URL is required.')).not.toBeInTheDocument();

        const user = userEvent.setup();
        const baseUrlInput = document.querySelector('[data-field="baseUrl"]') as HTMLElement;
        await user.click(baseUrlInput);
        await user.tab();

        await waitFor(
            () => {
                expect(screen.getByText('Base URL is required.')).toBeInTheDocument();
            },
            { timeout: 2000 },
        );
    });
});
