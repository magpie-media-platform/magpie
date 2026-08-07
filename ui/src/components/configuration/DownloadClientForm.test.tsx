import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';

import { ProviderTypesWrapper } from '@/test/providerTypesTestUtils';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';
import type { IntegrationSettingsValidationResult } from '@/hooks/useValidateSettings';

vi.mock('@/lib/api', async () => {
    const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
    return {
        ...actual,
        apiGet: vi.fn().mockResolvedValue([]),
        apiPost: vi.fn().mockResolvedValue({ isValid: true, issues: [] }),
    };
});

import { apiGet, apiPost } from '@/lib/api';
import { DownloadClientForm } from './DownloadClientForm';

const PROVIDER_TYPES = ['nzbget', 'qbittorrent', 'sabnzbd', 'transmission'];

function TestWrapper({ children }: { children: ReactNode }) {
    return (
        <MemoryRouter>
            <ProviderTypesWrapper seed={{ downloadClients: PROVIDER_TYPES }}>{children}</ProviderTypesWrapper>
        </MemoryRouter>
    );
}

const SAMPLE_SCHEMA: IntegrationSettingDefinition[] = [
    { key: 'host', type: SchemaDataType.String, displayName: 'Host', required: true },
    { key: 'port', type: SchemaDataType.Number, displayName: 'Port' },
    { key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true },
];

describe('DownloadClientForm', () => {
    it('renders nothing when closed', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={false} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByText('Add Download Client')).not.toBeInTheDocument();
    });

    it('renders the form when open', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByText('Add Download Client')).toBeInTheDocument();
    });

    it('shows "Edit Download Client" when editing', async () => {
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Client');
        expect(screen.getByText('Edit Download Client')).toBeInTheDocument();
    });

    it('pre-fills form fields when editing', async () => {
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Test Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        expect(await screen.findByDisplayValue('My Test Client')).toBeInTheDocument();
        expect(screen.getByDisplayValue('sabnzbd')).toBeInTheDocument();
    });

    it('renders name input field', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    });

    it('renders integration dropdown populated from available integrations', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        const providerField = document.querySelector('[data-field="provider"]');
        expect(providerField).toBeInTheDocument();
        expect(providerField).toHaveAttribute('aria-label', 'Integration');
    });

    it('shows empty-state copy when no provider integrations are registered', () => {
        render(
            <MemoryRouter>
                <ProviderTypesWrapper seed={{ downloadClients: [] }}>
                    <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        expect(
            screen.getByText('No download client integrations are registered. Install an extension that provides one.'),
        ).toBeInTheDocument();
    });

    it('renders enabled toggle', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.getByLabelText(/enabled/i)).toBeInTheDocument();
    });

    it('renders Save and Cancel buttons', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
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
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
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
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        await user.type(nameInput, 'Test Client');

        const saveButton = screen.getByRole('button', { name: /save/i });
        await user.click(saveButton);

        expect(screen.getByText('Integration is required')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows the provider value as a disabled input when editing', async () => {
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Client');
        const providerInput = document.querySelector('[data-field="provider"]');
        expect(providerInput).toBeDisabled();
        expect(providerInput).toHaveValue('sabnzbd');
    });

    it('shows loading state when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toHaveAttribute('aria-busy', 'true');
        expect(saveButton).toBeDisabled();
    });

    it('disables form fields when isSubmitting is true', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting={true} />
            </TestWrapper>,
        );

        const nameInput = screen.getByLabelText(/name/i);
        expect(nameInput).toBeDisabled();

        const providerField = document.querySelector('[data-field="provider"]');
        expect(providerField).toBeDisabled();

        const saveButton = screen.getByRole('button', { name: /save/i });
        expect(saveButton).toBeDisabled();
    });

    it('calls onTest with the current form values when Test is clicked', async () => {
        const user = userEvent.setup();
        const onTest = vi.fn();
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: { host: 'localhost' },
                    }}
                    onSubmit={vi.fn()}
                    onTest={onTest}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Client');
        await user.click(screen.getByRole('button', { name: /test/i }));

        expect(onTest).toHaveBeenCalledWith({
            providerType: 'sabnzbd',
            id: 'client-1',
            settings: { host: 'localhost' },
        });
    });

    it('hides the Test button until a provider is selected in add mode', () => {
        render(
            <TestWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} onSubmit={vi.fn()} onTest={vi.fn()} />
            </TestWrapper>,
        );

        expect(screen.queryByRole('button', { name: /test/i })).not.toBeInTheDocument();
    });

    it('marks the form dirty after editing a scalar field', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        const nameInput = await screen.findByDisplayValue('My Client');
        await user.type(nameInput, '!');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('stays clean when no field is edited', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </TestWrapper>,
        );

        await screen.findByDisplayValue('My Client');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });
});

describe('DownloadClientForm schema integration', () => {
    const mockApiGet = vi.mocked(apiGet);
    const mockApiPost = vi.mocked(apiPost);

    beforeEach(() => {
        vi.clearAllMocks();
        mockApiGet.mockResolvedValue(SAMPLE_SCHEMA);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    function SchemaWrapper({ children }: { children: ReactNode }) {
        return (
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        downloadClients: PROVIDER_TYPES,
                        schemas: { downloadClients: { sabnzbd: SAMPLE_SCHEMA } },
                    }}
                >
                    {children}
                </ProviderTypesWrapper>
            </MemoryRouter>
        );
    }

    it('keeps the provider selected, preserves the name, and renders settings when adding a provider with a schema (MAGPIE-688)', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <SchemaWrapper>
                <DownloadClientForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
            </SchemaWrapper>,
        );

        await user.type(screen.getByLabelText(/name/i), 'My Client');
        await user.click(screen.getByRole('combobox', { name: /integration/i }));
        await user.click(screen.getByRole('option', { name: 'sabnzbd' }));

        expect(screen.getByRole('combobox', { name: /integration/i })).toHaveTextContent('sabnzbd');
        expect(screen.getByDisplayValue('My Client')).toBeInTheDocument();
        expect(await screen.findByLabelText(/Host/i)).toBeInTheDocument();
    });

    it('renders schema-driven inputs when editing a client with a known provider', async () => {
        const validResult: IntegrationSettingsValidationResult = { isValid: true, issues: [] };
        mockApiPost.mockResolvedValue(validResult);

        render(
            <SchemaWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: { host: 'localhost', port: '8080' },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-setting-key="host"]')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-setting-key="port"]')).toBeInTheDocument();
        expect(document.querySelector('[data-setting-key="apiKey"]')).toBeInTheDocument();
        expect(screen.getByDisplayValue('localhost')).toBeInTheDocument();
    });

    it('validates settings locally when settings change', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <SchemaWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const hostInput = (await screen.findByDisplayValue('My Client'))
            .closest('form')
            ?.querySelector('[data-field="host"]') as HTMLInputElement;
        expect(hostInput).toBeInTheDocument();
        await user.type(hostInput, 'host.example.com');

        await waitFor(() => expect(screen.getByRole('button', { name: /save/i })).toBeEnabled());
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('keeps Save enabled when editing category with a configured blank password', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const schema: IntegrationSettingDefinition[] = [
            { key: 'host', type: SchemaDataType.String, displayName: 'Host', required: true },
            { key: 'username', type: SchemaDataType.String, displayName: 'Username' },
            { key: 'password', type: SchemaDataType.String, displayName: 'Password', required: true, secret: true },
            { key: 'category', type: SchemaDataType.String, displayName: 'Category' },
        ];

        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        downloadClients: PROVIDER_TYPES,
                        schemas: { downloadClients: { qbittorrent: schema } },
                    }}
                >
                    <DownloadClientForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={{
                            id: 'client-1',
                            name: 'qBittorrent',
                            provider: 'qbittorrent',
                            enabled: true,
                            cleanupDeleteFiles: true,
                            keepFailedOnClient: false,
                            settings: {
                                host: 'http://localhost:8080',
                                username: 'admin',
                                category: 'tv',
                            },
                        }}
                        onSubmit={onSubmit}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        const categoryInput = await screen.findByDisplayValue('tv');
        await user.clear(categoryInput);
        await user.type(categoryInput, 'movies');

        await waitFor(() => expect(screen.getByRole('button', { name: /save/i })).toBeEnabled());

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledTimes(1);
        const submitted = onSubmit.mock.calls[0][0];
        expect(submitted.settings).toMatchObject({ category: 'movies' });
        expect(submitted.settings).not.toHaveProperty('password');
    });

    it('dispatches onSubmit only once when Save is double-clicked while a save is in flight', async () => {
        const user = userEvent.setup();
        let resolveSubmit: (() => void) | undefined;
        const onSubmit = vi.fn(
            () =>
                new Promise<void>((resolve) => {
                    resolveSubmit = resolve;
                }),
        );

        render(
            <TestWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={onSubmit}
                />
            </TestWrapper>,
        );

        const saveButton = await screen.findByRole('button', { name: /save/i });
        await user.dblClick(saveButton);

        expect(onSubmit).toHaveBeenCalledTimes(1);

        await act(async () => {
            resolveSubmit?.();
        });
    });

    it('seeds schema defaultValue into settings on add when value is absent', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
            { key: 'host', type: SchemaDataType.String, displayName: 'Host', required: true },
            {
                key: 'paused',
                type: SchemaDataType.Boolean,
                displayName: 'Add as paused',
                defaultValue: 'false',
            },
        ];
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULTS);

        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        downloadClients: PROVIDER_TYPES,
                        schemas: { downloadClients: { sabnzbd: SCHEMA_WITH_DEFAULTS } },
                    }}
                >
                    <DownloadClientForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={{
                            name: '',
                            provider: 'sabnzbd',
                            enabled: true,
                            cleanupDeleteFiles: true,
                            keepFailedOnClient: false,
                            settings: {},
                        }}
                        onSubmit={onSubmit}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        expect(await screen.findByRole('switch', { name: 'Add as paused' })).not.toBeChecked();
        await user.type(screen.getByLabelText(/name/i), 'Sabnzbd');
        await user.type(document.querySelector('[data-field="host"]') as HTMLInputElement, 'host.example.com');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({ settings: expect.objectContaining({ paused: 'false' }) }),
        );
    });

    it('does not warn on cancel after schema defaults seed with no user edits', async () => {
        const SCHEMA_WITH_DEFAULT: IntegrationSettingDefinition[] = [
            { key: 'host', type: SchemaDataType.String, displayName: 'Host', required: true },
            { key: 'category', type: SchemaDataType.String, displayName: 'Category', defaultValue: 'tv' },
        ];
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULT);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        downloadClients: PROVIDER_TYPES,
                        schemas: { downloadClients: { sabnzbd: SCHEMA_WITH_DEFAULT } },
                    }}
                >
                    <DownloadClientForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={{
                            id: 'client-1',
                            name: 'My Client',
                            provider: 'sabnzbd',
                            enabled: true,
                            cleanupDeleteFiles: true,
                            keepFailedOnClient: false,
                            settings: { host: 'localhost' },
                        }}
                        onSubmit={vi.fn()}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        await screen.findByDisplayValue('tv');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('warns on cancel after a real settings edit', async () => {
        mockApiGet.mockResolvedValue(SAMPLE_SCHEMA);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <SchemaWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: { host: 'localhost' },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const hostInput = await screen.findByDisplayValue('localhost');
        await user.type(hostInput, '2');
        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('disables Save and surfaces field-level issues when local validation fails', async () => {
        render(
            <SchemaWrapper>
                <DownloadClientForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'client-1',
                        name: 'My Client',
                        provider: 'sabnzbd',
                        enabled: true,
                        cleanupDeleteFiles: true,
                        keepFailedOnClient: false,
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await waitFor(() => expect(saveButton).toBeDisabled(), { timeout: 2000 });

        expect(screen.queryByText('Host is required.')).not.toBeInTheDocument();

        const user = userEvent.setup();
        const hostInput = document.querySelector('[data-field="host"]') as HTMLElement;
        await user.click(hostInput);
        await user.tab();

        await waitFor(
            () => {
                expect(screen.getByText('Host is required.')).toBeInTheDocument();
            },
            { timeout: 2000 },
        );
    });

    it('omits a configured blank secret from the in-dialog Test so the stored secret is used', async () => {
        const user = userEvent.setup();
        const onTest = vi.fn();
        const schema: IntegrationSettingDefinition[] = [
            { key: 'host', type: SchemaDataType.String, displayName: 'Host', required: true },
            { key: 'password', type: SchemaDataType.String, displayName: 'Password', required: true, secret: true },
        ];
        mockApiGet.mockResolvedValue(schema);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        downloadClients: PROVIDER_TYPES,
                        schemas: { downloadClients: { qbittorrent: schema } },
                    }}
                >
                    <DownloadClientForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={{
                            id: 'client-1',
                            name: 'qBittorrent',
                            provider: 'qbittorrent',
                            enabled: true,
                            cleanupDeleteFiles: true,
                            keepFailedOnClient: false,
                            settings: { host: 'http://localhost:8080' },
                        }}
                        onSubmit={vi.fn()}
                        onTest={onTest}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        await screen.findByDisplayValue('qBittorrent');
        await user.click(screen.getByRole('button', { name: /test/i }));

        expect(onTest).toHaveBeenCalledTimes(1);
        const request = onTest.mock.calls[0]![0] as { id?: string; settings?: Record<string, string> };
        expect(request.id).toBe('client-1');
        expect(request.settings).not.toHaveProperty('password');
    });

    it('sends a newly entered secret with the in-dialog Test', async () => {
        const user = userEvent.setup();
        const onTest = vi.fn();
        const schema: IntegrationSettingDefinition[] = [
            { key: 'host', type: SchemaDataType.String, displayName: 'Host', required: true },
            { key: 'password', type: SchemaDataType.String, displayName: 'Password', required: true, secret: true },
        ];
        mockApiGet.mockResolvedValue(schema);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <MemoryRouter>
                <ProviderTypesWrapper
                    seed={{
                        downloadClients: PROVIDER_TYPES,
                        schemas: { downloadClients: { qbittorrent: schema } },
                    }}
                >
                    <DownloadClientForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={{
                            id: 'client-1',
                            name: 'qBittorrent',
                            provider: 'qbittorrent',
                            enabled: true,
                            cleanupDeleteFiles: true,
                            keepFailedOnClient: false,
                            settings: { host: 'http://localhost:8080' },
                        }}
                        onSubmit={vi.fn()}
                        onTest={onTest}
                    />
                </ProviderTypesWrapper>
            </MemoryRouter>,
        );

        await screen.findByDisplayValue('qBittorrent');
        await user.click(screen.getByRole('button', { name: 'Clear Password' }));
        const passwordInput = document.querySelector('[data-field="password"]') as HTMLInputElement;
        await user.type(passwordInput, 'new-secret');
        await user.click(screen.getByRole('button', { name: /test/i }));

        expect(onTest).toHaveBeenCalledTimes(1);
        const request = onTest.mock.calls[0]![0] as { id?: string; settings?: Record<string, string> };
        expect(request.id).toBe('client-1');
        expect(request.settings?.password).toBe('new-secret');
    });
});
