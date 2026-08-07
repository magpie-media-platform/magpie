import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
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
import { IndexerForm } from './IndexerForm';

const PROVIDER_TYPES = ['newznab', 'torznab'];

const SAMPLE_SCHEMA: IntegrationSettingDefinition[] = [
    { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
    { key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true },
];

function TestWrapper({ children }: { children: ReactNode }) {
    return <ProviderTypesWrapper seed={{ indexers: PROVIDER_TYPES }}>{children}</ProviderTypesWrapper>;
}

describe('IndexerForm', () => {
    describe('add mode', () => {
        it('renders add form when no initial values', () => {
            render(
                <TestWrapper>
                    <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            expect(screen.getByText('Add Indexer')).toBeInTheDocument();
            expect(screen.getByText('Configure a new indexer to search for content.')).toBeInTheDocument();
        });

        it('shows name, integration, and description fields', () => {
            render(
                <TestWrapper>
                    <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
            expect(document.querySelector('[data-field="providerType"]')).toBeInTheDocument();
            expect(screen.getByLabelText(/Description/)).toBeInTheDocument();
        });

        it('shows empty-state copy when no provider integrations are registered', () => {
            render(
                <ProviderTypesWrapper seed={{ indexers: [] }}>
                    <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </ProviderTypesWrapper>,
            );

            expect(
                screen.getByText('No indexer integrations are available. Install an extension that provides one.'),
            ).toBeInTheDocument();
        });
    });

    describe('edit mode', () => {
        const existingIndexer = {
            id: 'idx-1',
            name: 'My Indexer',
            providerType: 'newznab',
            description: 'A test indexer',
            settings: {},
        };

        it('renders edit form with initial values', async () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            await screen.findByDisplayValue('My Indexer');
            expect(screen.getByText('Edit Indexer')).toBeInTheDocument();
            expect(screen.getByText('Update the indexer configuration.')).toBeInTheDocument();
        });

        it('populates name from initial values', async () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(await screen.findByDisplayValue('My Indexer')).toBeInTheDocument();
        });

        it('populates integration from initial values', async () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(await screen.findByDisplayValue('newznab')).toBeInTheDocument();
        });

        it('populates description from initial values', async () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(await screen.findByDisplayValue('A test indexer')).toBeInTheDocument();
        });
    });

    describe('validation', () => {
        it('shows error when name is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(
                <TestWrapper>
                    <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
                </TestWrapper>,
            );

            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Name is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });

        it('shows error when integration is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(
                <TestWrapper>
                    <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
                </TestWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'Test Indexer');
            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Integration is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });
    });

    describe('form submission', () => {
        it('calls onSubmit with form values when editing valid data', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            const existingIndexer = {
                id: 'idx-1',
                name: 'My Indexer',
                providerType: 'newznab',
                description: 'A test indexer',
                settings: {},
            };

            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={onSubmit}
                    />
                </TestWrapper>,
            );

            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith(existingIndexer);
        });

        it('disables buttons when submitting', () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={null}
                        onSubmit={vi.fn()}
                        isSubmitting={true}
                    />
                </TestWrapper>,
            );

            const saveButton = screen.getByRole('button', { name: /save/i });
            expect(saveButton).toHaveAttribute('aria-busy', 'true');
            expect(saveButton).toBeDisabled();
            expect(screen.getByRole('button', { name: /cancel/i })).toBeDisabled();
        });
    });

    describe('cancel', () => {
        it('calls onOpenChange with false when cancel clicked', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(
                <TestWrapper>
                    <IndexerForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });
    });

    describe('test button', () => {
        const existingIndexer = {
            id: 'idx-1',
            name: 'My Indexer',
            providerType: 'newznab',
            settings: {},
        };

        it('renders test button when editing and onTest provided', async () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                        onTest={vi.fn()}
                    />
                </TestWrapper>,
            );

            await screen.findByDisplayValue('My Indexer');
            expect(screen.getByRole('button', { name: /test/i })).toBeInTheDocument();
        });

        it('does not render test button in add mode', () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={null}
                        onSubmit={vi.fn()}
                        onTest={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(screen.queryByRole('button', { name: /test/i })).not.toBeInTheDocument();
        });

        it('calls onTest when clicked', async () => {
            const user = userEvent.setup();
            const onTest = vi.fn();
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                        onTest={onTest}
                    />
                </TestWrapper>,
            );

            await user.click(screen.getByRole('button', { name: /test/i }));

            expect(onTest).toHaveBeenCalledTimes(1);
        });

        it('disables test button while testing', async () => {
            render(
                <TestWrapper>
                    <IndexerForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingIndexer}
                        onSubmit={vi.fn()}
                        onTest={vi.fn()}
                        isTesting={true}
                    />
                </TestWrapper>,
            );

            await screen.findByDisplayValue('My Indexer');
            const testButton = screen.getByRole('button', { name: /test/i });
            expect(testButton).toBeDisabled();
            expect(testButton).toHaveAttribute('aria-busy', 'true');
        });
    });
});

describe('IndexerForm schema integration', () => {
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
            <ProviderTypesWrapper
                seed={{
                    indexers: PROVIDER_TYPES,
                    schemas: { indexers: { newznab: SAMPLE_SCHEMA } },
                }}
            >
                {children}
            </ProviderTypesWrapper>
        );
    }

    it('renders schema-driven inputs when editing an indexer with a known provider', async () => {
        const validResult: IntegrationSettingsValidationResult = { isValid: true, issues: [] };
        mockApiPost.mockResolvedValue(validResult);

        render(
            <SchemaWrapper>
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'idx-1',
                        name: 'My Indexer',
                        providerType: 'newznab',
                        settings: { url: 'https://indexer.test' },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-setting-key="url"]')).toBeInTheDocument();
        });
        expect(document.querySelector('[data-setting-key="apiKey"]')).toBeInTheDocument();
        expect(screen.getByDisplayValue('https://indexer.test')).toBeInTheDocument();
    });

    it('seeds existing settings from initialValues into schema-driven inputs', async () => {
        const validResult: IntegrationSettingsValidationResult = { isValid: true, issues: [] };
        mockApiPost.mockResolvedValue(validResult);

        render(
            <SchemaWrapper>
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'idx-1',
                        name: 'My Indexer',
                        providerType: 'newznab',
                        settings: {
                            url: 'https://indexer.test',
                            apiKey: 'stored-secret',
                        },
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByDisplayValue('https://indexer.test')).toBeInTheDocument();
        });
        const apiKey = document.querySelector('[data-field="apiKey"]') as HTMLInputElement;
        expect(apiKey.value).toBe('');
        expect(apiKey).toHaveAttribute('placeholder', '********');
        expect(screen.queryByDisplayValue('stored-secret')).not.toBeInTheDocument();
    });

    it('round-trips settings to onSubmit when saving without changes', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const validResult: IntegrationSettingsValidationResult = { isValid: true, issues: [] };
        mockApiPost.mockResolvedValue(validResult);

        const initial = {
            id: 'idx-1',
            name: 'My Indexer',
            providerType: 'newznab',
            settings: {
                url: 'https://indexer.test',
                apiKey: 'stored-secret',
            },
        };

        render(
            <SchemaWrapper>
                <IndexerForm open={true} onOpenChange={vi.fn()} initialValues={initial} onSubmit={onSubmit} />
            </SchemaWrapper>,
        );

        await waitFor(() => {
            expect(screen.getByDisplayValue('https://indexer.test')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledTimes(1);
        const submitted = onSubmit.mock.calls[0][0];
        expect(submitted.settings).toEqual({
            url: 'https://indexer.test',
            apiKey: 'stored-secret',
        });
    });

    it('validates settings locally when settings change', async () => {
        const user = userEvent.setup();
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <SchemaWrapper>
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'idx-1',
                        name: 'My Indexer',
                        providerType: 'newznab',
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const urlInput = (await screen.findByDisplayValue('My Indexer'))
            .closest('form')
            ?.querySelector('[data-field="url"]') as HTMLInputElement;
        expect(urlInput).toBeInTheDocument();
        await user.type(urlInput, 'https://indexer.test');

        await waitFor(() => expect(screen.getByRole('button', { name: /save/i })).toBeEnabled());
        expect(mockApiPost).not.toHaveBeenCalled();
    });

    it('seeds schema defaultValue into settings on add when value is absent', async () => {
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
            { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
            {
                key: 'paused',
                type: SchemaDataType.Boolean,
                displayName: 'Add as paused',
                defaultValue: 'false',
            },
        ];
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULTS);

        render(
            <ProviderTypesWrapper
                seed={{
                    indexers: PROVIDER_TYPES,
                    schemas: { indexers: { newznab: SCHEMA_WITH_DEFAULTS } },
                }}
            >
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{ name: '', providerType: 'newznab', settings: {} }}
                    onSubmit={onSubmit}
                />
            </ProviderTypesWrapper>,
        );

        expect(await screen.findByRole('switch', { name: 'Add as paused' })).not.toBeChecked();
        await user.type(screen.getByLabelText(/name/i), 'My Indexer');
        await user.type(document.querySelector('[data-field="url"]') as HTMLInputElement, 'https://indexer.test');
        await user.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
            expect.objectContaining({ settings: expect.objectContaining({ paused: 'false' }) }),
        );
    });

    it('stays not-dirty after schema defaults seed with no user edits', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
            { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
            { key: 'paused', type: SchemaDataType.Boolean, displayName: 'Add as paused', defaultValue: 'false' },
        ];
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULTS);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <ProviderTypesWrapper
                seed={{ indexers: PROVIDER_TYPES, schemas: { indexers: { newznab: SCHEMA_WITH_DEFAULTS } } }}
            >
                <IndexerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{ name: 'My Indexer', providerType: 'newznab', settings: {} }}
                    onSubmit={vi.fn()}
                />
            </ProviderTypesWrapper>,
        );

        await waitFor(() => {
            expect(document.querySelector('[data-setting-key="paused"]')).toBeInTheDocument();
        });

        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('becomes dirty after a real edit to a seeded-default form', async () => {
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
            { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
            { key: 'paused', type: SchemaDataType.Boolean, displayName: 'Add as paused', defaultValue: 'false' },
        ];
        mockApiGet.mockResolvedValue(SCHEMA_WITH_DEFAULTS);
        mockApiPost.mockResolvedValue({ isValid: true, issues: [] });

        render(
            <ProviderTypesWrapper
                seed={{ indexers: PROVIDER_TYPES, schemas: { indexers: { newznab: SCHEMA_WITH_DEFAULTS } } }}
            >
                <IndexerForm
                    open={true}
                    onOpenChange={onOpenChange}
                    initialValues={{ name: 'My Indexer', providerType: 'newznab', settings: {} }}
                    onSubmit={vi.fn()}
                />
            </ProviderTypesWrapper>,
        );

        const urlInput = (await waitFor(() => {
            const el = document.querySelector('[data-field="url"]');
            expect(el).toBeInTheDocument();
            return el as HTMLInputElement;
        })) as HTMLInputElement;
        await user.type(urlInput, 'https://indexer.test');

        await user.click(screen.getByRole('button', { name: /cancel/i }));

        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('disables Save and surfaces field-level issues when local validation fails', async () => {
        render(
            <SchemaWrapper>
                <IndexerForm
                    open={true}
                    onOpenChange={vi.fn()}
                    initialValues={{
                        id: 'idx-1',
                        name: 'My Indexer',
                        providerType: 'newznab',
                        settings: {},
                    }}
                    onSubmit={vi.fn()}
                />
            </SchemaWrapper>,
        );

        const saveButton = screen.getByRole('button', { name: /save/i });
        await waitFor(() => expect(saveButton).toBeDisabled(), { timeout: 2000 });

        expect(screen.queryByText('URL is required.')).not.toBeInTheDocument();

        const user = userEvent.setup();
        const urlInput = document.querySelector('[data-field="url"]') as HTMLElement;
        await user.click(urlInput);
        await user.tab();

        await waitFor(
            () => {
                expect(screen.getByText('URL is required.')).toBeInTheDocument();
            },
            { timeout: 2000 },
        );
    });
});
