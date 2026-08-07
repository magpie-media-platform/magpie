import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi } from 'vitest';
import type { ReactNode } from 'react';

import { ProviderTypesWrapper } from '@/test/providerTypesTestUtils';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

import { IndexerFeedForm } from './IndexerFeedForm';

const PROVIDER_TYPES = ['jackett', 'prowlarr'];

function TestWrapper({ children }: { children: ReactNode }) {
    return <ProviderTypesWrapper seed={{ indexerFeeds: PROVIDER_TYPES }}>{children}</ProviderTypesWrapper>;
}

async function selectIntegration(user: ReturnType<typeof userEvent.setup>, integrationName: string): Promise<void> {
    await user.click(screen.getByRole('combobox', { name: /integration/i }));
    await user.click(screen.getByRole('option', { name: integrationName }));
}

describe('IndexerFeedForm', () => {
    describe('add mode', () => {
        it('renders add form when no initial values', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            expect(screen.getByText('Add Feed')).toBeInTheDocument();
            expect(screen.getByText('Configure a new feed to sync indexer definitions.')).toBeInTheDocument();
        });

        it('shows name, description, and integration fields', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
            expect(screen.getByLabelText(/Description/)).toBeInTheDocument();
            expect(document.querySelector('[data-field="provider"]')).toBeInTheDocument();
        });

        it('shows enabled toggle defaulting to on', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            const toggle = screen.getByRole('switch');
            expect(toggle).toBeChecked();
        });

        it('shows settings section with add button', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            expect(screen.queryByText('Settings')).not.toBeInTheDocument();
            expect(screen.queryByRole('button', { name: /add setting/i })).not.toBeInTheDocument();
        });

        it('shows empty-state copy when no integrations are registered', () => {
            render(
                <ProviderTypesWrapper seed={{ indexerFeeds: [] }}>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </ProviderTypesWrapper>,
            );

            expect(
                screen.getByText(
                    'No feed integrations are available. An extension that provides one must be installed.',
                ),
            ).toBeInTheDocument();
        });
    });

    describe('edit mode', () => {
        const existingFeed = {
            id: 'feed-1',
            name: 'My Feed',
            description: 'Test feed description',
            provider: 'prowlarr',
            enabled: true,
            settings: { baseUrl: 'http://localhost:9696', apiKey: 'secret123' },
        };

        it('renders edit form with initial values', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingFeed}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(screen.getByText('Edit Feed')).toBeInTheDocument();
            expect(screen.getByText('Update the indexer feed configuration.')).toBeInTheDocument();
        });

        it('populates name from initial values', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingFeed}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(screen.getByDisplayValue('My Feed')).toBeInTheDocument();
        });

        it('populates description from initial values', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingFeed}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(screen.getByDisplayValue('Test feed description')).toBeInTheDocument();
        });

        it('populates integration from initial values', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingFeed}
                        onSubmit={vi.fn()}
                    />
                </TestWrapper>,
            );

            expect(screen.getByDisplayValue('prowlarr')).toBeInTheDocument();
        });
    });

    describe('validation', () => {
        it('shows error when name is empty on submit', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
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
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
                </TestWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'Test Feed');
            await user.click(screen.getByText('Save'));

            expect(screen.getByText('Integration is required')).toBeInTheDocument();
            expect(onSubmit).not.toHaveBeenCalled();
        });
    });

    describe('form submission', () => {
        it('calls onSubmit with form values when editing valid data', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();
            const existingFeed = {
                id: 'feed-1',
                name: 'My Feed',
                description: 'Test description',
                provider: 'prowlarr',
                enabled: true,
                settings: { baseUrl: 'http://localhost:9696', apiKey: 'secret123' },
            };

            render(
                <TestWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={vi.fn()}
                        initialValues={existingFeed}
                        onSubmit={onSubmit}
                    />
                </TestWrapper>,
            );

            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith(existingFeed);
        });

        it('submits with empty settings dict when adding a new feed', async () => {
            const user = userEvent.setup();
            const onSubmit = vi.fn();

            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={onSubmit} />
                </TestWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'New Feed');
            await selectIntegration(user, 'jackett');
            await user.click(screen.getByText('Save'));

            expect(onSubmit).toHaveBeenCalledWith({
                id: undefined,
                name: 'New Feed',
                description: '',
                provider: 'jackett',
                enabled: true,
                settings: {},
            });
        });

        it('disables buttons when submitting', () => {
            render(
                <TestWrapper>
                    <IndexerFeedForm
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
        it('calls onOpenChange with false when cancel clicked on clean form', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });
    });

    describe('unsaved changes guard', () => {
        it('shows unsaved changes dialog when cancelling a dirty form', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'Modified');
            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });

        it('closes the form when discard is clicked in unsaved dialog', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'Modified');
            await user.click(screen.getByText('Cancel'));
            await user.click(screen.getByText('Discard Changes'));

            expect(onOpenChange).toHaveBeenCalledWith(false);
        });

        it('returns to editing when keep editing is clicked in unsaved dialog', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'Modified');
            await user.click(screen.getByText('Cancel'));
            await user.click(screen.getByText('Keep Editing'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
        });

        it('detects dirty state from integration change', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();
            render(
                <TestWrapper>
                    <IndexerFeedForm open={true} onOpenChange={onOpenChange} initialValues={null} onSubmit={vi.fn()} />
                </TestWrapper>,
            );

            await selectIntegration(user, 'prowlarr');
            await user.click(screen.getByText('Cancel'));

            expect(onOpenChange).not.toHaveBeenCalled();
            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        });
    });

    describe('integration selection persists (MAGPIE-566)', () => {
        const PROVIDER_SCHEMA: IntegrationSettingDefinition[] = [
            { key: 'baseUrl', type: SchemaDataType.String, displayName: 'Base URL', required: true },
        ];

        function SchemaSelectWrapper({ children }: { children: ReactNode }) {
            return (
                <ProviderTypesWrapper
                    seed={{
                        indexerFeeds: PROVIDER_TYPES,
                        schemas: { indexerFeeds: { prowlarr: PROVIDER_SCHEMA } },
                    }}
                >
                    {children}
                </ProviderTypesWrapper>
            );
        }

        it('keeps the integration selected, preserves the name, and renders its settings', async () => {
            const user = userEvent.setup();

            render(
                <SchemaSelectWrapper>
                    <IndexerFeedForm open={true} onOpenChange={vi.fn()} initialValues={null} onSubmit={vi.fn()} />
                </SchemaSelectWrapper>,
            );

            await user.type(screen.getByLabelText(/Name/), 'My Feed');
            await selectIntegration(user, 'prowlarr');

            expect(screen.getByRole('combobox', { name: /integration/i })).toHaveTextContent('prowlarr');
            expect(screen.getByDisplayValue('My Feed')).toBeInTheDocument();
            expect(await screen.findByLabelText(/Base URL/)).toBeInTheDocument();
        });
    });

    describe('schema-default dirty baseline', () => {
        const SCHEMA_WITH_DEFAULTS: IntegrationSettingDefinition[] = [
            { key: 'baseUrl', type: SchemaDataType.String, displayName: 'Base URL', required: true },
            { key: 'syncInterval', type: SchemaDataType.String, displayName: 'Sync interval', defaultValue: '60' },
        ];

        function SchemaWrapper({ children }: { children: ReactNode }) {
            return (
                <ProviderTypesWrapper
                    seed={{
                        indexerFeeds: PROVIDER_TYPES,
                        schemas: { indexerFeeds: { prowlarr: SCHEMA_WITH_DEFAULTS } },
                    }}
                >
                    {children}
                </ProviderTypesWrapper>
            );
        }

        it('stays not-dirty after schema defaults seed with no user edits', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(
                <SchemaWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={{
                            name: 'My Feed',
                            description: '',
                            provider: 'prowlarr',
                            enabled: true,
                            settings: {},
                        }}
                        onSubmit={vi.fn()}
                    />
                </SchemaWrapper>,
            );

            await screen.findByDisplayValue('60');

            await user.click(screen.getByText('Cancel'));

            expect(screen.queryByText('Unsaved Changes')).not.toBeInTheDocument();
            expect(onOpenChange).toHaveBeenCalledWith(false);
        });

        it('becomes dirty after a real edit to a seeded-default form', async () => {
            const user = userEvent.setup();
            const onOpenChange = vi.fn();

            render(
                <SchemaWrapper>
                    <IndexerFeedForm
                        open={true}
                        onOpenChange={onOpenChange}
                        initialValues={{
                            name: 'My Feed',
                            description: '',
                            provider: 'prowlarr',
                            enabled: true,
                            settings: {},
                        }}
                        onSubmit={vi.fn()}
                    />
                </SchemaWrapper>,
            );

            const syncInput = await screen.findByDisplayValue('60');
            await user.clear(syncInput);
            await user.type(syncInput, '120');

            await user.click(screen.getByText('Cancel'));

            expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
            expect(onOpenChange).not.toHaveBeenCalled();
        });
    });
});
