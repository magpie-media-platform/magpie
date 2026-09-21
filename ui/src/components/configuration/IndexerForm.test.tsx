import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';

import { ProviderTypesWrapper } from '@/test/providerTypesTestUtils';
import { SchemaDataType, type IntegrationSettingDefinition } from '@/hooks/useProviderSchema';

vi.mock('@/lib/api', async () => {
    const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
    return { ...actual, apiGet: vi.fn().mockResolvedValue([]), apiPost: vi.fn() };
});

import { IndexerForm } from './IndexerForm';

const schema: IntegrationSettingDefinition[] = [
    { key: 'url', type: SchemaDataType.String, displayName: 'URL', required: true },
    { key: 'apiKey', type: SchemaDataType.String, displayName: 'API Key', secret: true },
    { key: 'paused', type: SchemaDataType.Boolean, displayName: 'Add as paused', defaultValue: 'false' },
];

beforeEach(() => vi.clearAllMocks());

describe('IndexerForm creation', () => {
    it('shows required fields and reports missing values', async () => {
        // Arrange
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <ProviderTypesWrapper seed={{ indexers: ['sample'] }}>
                <IndexerForm open onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </ProviderTypesWrapper>,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Save' }));

        // Assert
        expect(screen.getByText('Add Indexer')).toBeInTheDocument();
        expect(screen.getByLabelText('Description')).toBeInTheDocument();
        expect(screen.getByText('Name is required')).toBeInTheDocument();
        expect(screen.getByText('Integration is required')).toBeInTheDocument();
        expect(screen.queryByRole('button', { name: 'Test' })).not.toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('shows an empty state when no integrations are registered', () => {
        render(
            <ProviderTypesWrapper seed={{ indexers: [] }}>
                <IndexerForm open onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </ProviderTypesWrapper>,
        );

        expect(
            screen.getByText('No indexer integrations are available. Install an extension that provides one.'),
        ).toBeInTheDocument();
        expect(screen.getByRole('combobox', { name: 'Integration' })).toBeDisabled();
    });

    it('submits new settings and schema defaults after selecting an integration', async () => {
        // Arrange
        const user = userEvent.setup();
        const onSubmit = vi.fn();
        render(
            <ProviderTypesWrapper seed={{ indexers: ['sample'], schemas: { indexers: { sample: schema } } }}>
                <IndexerForm open onOpenChange={vi.fn()} onSubmit={onSubmit} />
            </ProviderTypesWrapper>,
        );

        // Act
        await user.click(screen.getByRole('combobox', { name: 'Integration' }));
        await user.click(screen.getByRole('option', { name: 'sample' }));
        await user.type(screen.getByLabelText(/Name/), 'My Indexer');
        await user.type(screen.getByLabelText('Description'), 'Search service');
        const url = await screen.findByLabelText(/URL/);
        await user.click(url);
        await user.tab();
        expect(await screen.findByText('URL is required.')).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
        await user.type(url, 'https://indexer.test');
        await user.type(screen.getByLabelText('API Key'), 'new-secret');
        await user.click(screen.getByRole('button', { name: 'Save' }));

        // Assert
        await waitFor(() =>
            expect(onSubmit).toHaveBeenCalledWith({
                name: 'My Indexer',
                providerType: 'sample',
                description: 'Search service',
                settings: { url: 'https://indexer.test', apiKey: 'new-secret', paused: 'false' },
            }),
        );
    });

    it('closes a clean form and guards an edited form', async () => {
        // Arrange
        const user = userEvent.setup();
        const onOpenChange = vi.fn();
        render(
            <ProviderTypesWrapper seed={{ indexers: ['sample'] }}>
                <IndexerForm open onOpenChange={onOpenChange} onSubmit={vi.fn()} />
            </ProviderTypesWrapper>,
        );

        // Act
        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(onOpenChange).toHaveBeenCalledWith(false);
        onOpenChange.mockClear();
        await user.type(screen.getByLabelText(/Name/), 'Changed');
        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        // Assert
        expect(screen.getByText('Unsaved Changes')).toBeInTheDocument();
        expect(onOpenChange).not.toHaveBeenCalled();
    });

    it('presents an empty form when the sheet is reopened', async () => {
        // Arrange
        const user = userEvent.setup();
        const form = (
            <ProviderTypesWrapper seed={{ indexers: ['sample'] }}>
                <IndexerForm open onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </ProviderTypesWrapper>
        );
        const { rerender } = render(form);
        await user.type(screen.getByLabelText(/Name/), 'My Indexer');
        await user.click(screen.getByRole('button', { name: 'Save' }));
        expect(screen.getByText('Integration is required')).toBeInTheDocument();

        // Act
        rerender(
            <ProviderTypesWrapper seed={{ indexers: ['sample'] }}>
                <IndexerForm open={false} onOpenChange={vi.fn()} onSubmit={vi.fn()} />
            </ProviderTypesWrapper>,
        );
        rerender(form);

        // Assert
        expect(screen.getByLabelText(/Name/)).toHaveValue('');
        expect(screen.queryByText('Integration is required')).not.toBeInTheDocument();
    });

    it('disables actions while submitting', () => {
        render(
            <ProviderTypesWrapper seed={{ indexers: ['sample'] }}>
                <IndexerForm open onOpenChange={vi.fn()} onSubmit={vi.fn()} isSubmitting />
            </ProviderTypesWrapper>,
        );

        expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Save' })).toHaveAttribute('aria-busy', 'true');
        expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    });
});
