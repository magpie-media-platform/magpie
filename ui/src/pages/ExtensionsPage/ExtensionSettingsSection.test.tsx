import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ApiSettingsGroup } from '@/hooks/useSettings';

import { ExtensionSettingsSection } from './ExtensionSettingsSection';

const mocks = vi.hoisted(() => ({
    groups: [] as unknown[],
    updateSettings: vi.fn(),
    testConnection: vi.fn(),
    toast: vi.fn(),
    updatedAt: 1,
}));

vi.mock('@/hooks/useSettings', () => ({
    useExtensionSettingsGroups: () => ({
        groups: mocks.groups,
        isLoading: false,
        error: null,
        dataUpdatedAt: mocks.updatedAt,
    }),
    useUpdateExtensionSettingsGroup: () => ({
        updateSettings: mocks.updateSettings,
        isUpdating: false,
        error: null,
    }),
    useTestExtensionSettingsGroup: () => ({ test: mocks.testConnection, isTesting: false, error: null }),
    settingsToRecord: (settings: Array<{ key: string; value: string }>) =>
        Object.fromEntries(settings.map((setting) => [setting.key, setting.value])),
}));

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: mocks.toast }),
}));

function makeGroup(value: string, canTest = true): ApiSettingsGroup {
    return {
        id: 'settings-group-1',
        name: 'provider',
        extensionId: 'extension-1',
        createdAt: '2026-01-01T00:00:00Z',
        settings: [
            {
                id: 'setting-1',
                settingsGroupId: 'settings-group-1',
                key: 'endpoint',
                value,
                createdAt: '2026-01-01T00:00:00Z',
            },
        ],
        schema: [{ key: 'endpoint', type: 'String', displayName: 'Endpoint' }],
        canTest,
    };
}

function makeSecretGroup(value: string, isSet: boolean): ApiSettingsGroup {
    return {
        ...makeGroup(value),
        secretSettings: { token: { isSet } },
        schema: [
            { key: 'endpoint', type: 'String', displayName: 'Endpoint' },
            { key: 'token', type: 'String', displayName: 'API Token', secret: true },
        ],
    };
}

function deferredPromise(): {
    promise: Promise<void>;
    resolve: () => void;
} {
    let resolve!: () => void;
    const promise = new Promise<void>((resolvePromise) => {
        resolve = resolvePromise;
    });
    return { promise, resolve };
}

function endpointInput(): HTMLInputElement {
    return screen.getByRole('textbox', { name: 'Endpoint' });
}

describe('ExtensionSettingsSection', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mocks.groups = [makeGroup('https://initial.example')];
        mocks.updateSettings.mockResolvedValue(undefined);
        mocks.testConnection.mockResolvedValue({ success: true, message: 'Connected' });
        mocks.updatedAt = 1;
    });

    it('disables save and sends no patch when settings are unchanged', async () => {
        const user = userEvent.setup();
        render(<ExtensionSettingsSection extensionId="extension-1" />);

        const saveButton = screen.getByRole('button', { name: 'Save' });
        expect(saveButton).toBeDisabled();

        await user.click(saveButton);

        expect(mocks.updateSettings).not.toHaveBeenCalled();
        expect(mocks.toast).not.toHaveBeenCalled();
    });

    it('updates a clean form when refetched settings change', async () => {
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);

        expect(endpointInput()).toHaveValue('https://initial.example');
        mocks.groups = [makeGroup('https://refetched.example')];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);

        await waitFor(() => expect(endpointInput()).toHaveValue('https://refetched.example'));
        expect(screen.queryByText('Configuration changed elsewhere')).not.toBeInTheDocument();
    });

    it('preserves a dirty draft and confirms before overwriting refetched settings', async () => {
        const user = userEvent.setup();
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);

        await user.clear(endpointInput());
        await user.type(endpointInput(), 'https://draft.example');
        mocks.groups = [makeGroup('https://external.example')];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);

        expect(await screen.findByText('Configuration changed elsewhere')).toBeInTheDocument();
        expect(endpointInput()).toHaveValue('https://draft.example');

        await user.click(screen.getByRole('button', { name: 'Save' }));
        expect(mocks.updateSettings).not.toHaveBeenCalled();
        expect(screen.getByRole('dialog', { name: 'Save over newer changes?' })).toBeInTheDocument();

        await user.click(screen.getByRole('button', { name: 'Save anyway' }));
        await waitFor(() =>
            expect(mocks.updateSettings).toHaveBeenCalledWith(
                expect.objectContaining({ values: expect.objectContaining({ endpoint: 'https://draft.example' }) }),
            ),
        );
    });

    it('keeps edits made while saving dirty and visible after the submitted snapshot succeeds', async () => {
        const user = userEvent.setup();
        const pendingSave = deferredPromise();
        mocks.updateSettings.mockReturnValueOnce(pendingSave.promise);
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);

        await user.clear(endpointInput());
        await user.type(endpointInput(), 'https://submitted.example');
        await user.click(screen.getByRole('button', { name: 'Save' }));
        await user.clear(endpointInput());
        await user.type(endpointInput(), 'https://new-draft.example');
        pendingSave.resolve();
        await waitFor(() =>
            expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ title: 'Settings Saved' })),
        );

        mocks.groups = [makeGroup('https://submitted.example')];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);
        await waitFor(() => expect(endpointInput()).toHaveValue('https://new-draft.example'));
        expect(screen.queryByText('Configuration changed elsewhere')).not.toBeInTheDocument();

        mocks.groups = [makeGroup('https://later-change.example')];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);
        expect(await screen.findByText('Configuration changed elsewhere')).toBeInTheDocument();
        expect(endpointInput()).toHaveValue('https://new-draft.example');
    });

    it('processes a completed refetch even when the server returns the previously observed values', async () => {
        const user = userEvent.setup();
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);

        await user.clear(endpointInput());
        await user.type(endpointInput(), 'https://submitted.example');
        await user.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ title: 'Settings Saved' })),
        );

        mocks.updatedAt += 1;
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);

        await waitFor(() => expect(endpointInput()).toHaveValue('https://initial.example'));
        expect(screen.queryByText('Configuration changed elsewhere')).not.toBeInTheDocument();
    });

    it('retains the dirty draft and stale warning when an overwrite fails', async () => {
        const user = userEvent.setup();
        mocks.updateSettings.mockRejectedValueOnce(new Error('Save rejected'));
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);

        await user.clear(endpointInput());
        await user.type(endpointInput(), 'https://draft.example');
        mocks.groups = [makeGroup('https://external.example')];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);
        await screen.findByText('Configuration changed elsewhere');

        await user.click(screen.getByRole('button', { name: 'Save' }));
        await user.click(screen.getByRole('button', { name: 'Save anyway' }));
        await waitFor(() =>
            expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ title: 'Save Failed' })),
        );

        expect(screen.getByDisplayValue('https://draft.example')).toBeInTheDocument();
        expect(screen.getByText('Configuration changed elsewhere')).toBeInTheDocument();
        expect(screen.getByRole('dialog', { name: 'Save over newer changes?' })).toBeInTheDocument();
    });

    it('returns to clean after saving a secret and adopts the confirmed refetch', async () => {
        const user = userEvent.setup();
        mocks.groups = [makeSecretGroup('https://initial.example', false)];
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);

        await user.type(screen.getByLabelText('API Token'), 'new-secret');
        await user.click(screen.getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            expect(mocks.toast).toHaveBeenCalledWith(expect.objectContaining({ title: 'Settings Saved' })),
        );

        mocks.groups = [makeSecretGroup('https://refetched.example', true)];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);

        await waitFor(() => expect(endpointInput()).toHaveValue('https://refetched.example'));
        expect(screen.getByLabelText('API Token')).toHaveValue('');
        expect(screen.queryByText('Configuration changed elsewhere')).not.toBeInTheDocument();
    });

    it('renders the Test action only when the settings group can be tested', () => {
        mocks.groups = [makeGroup('https://initial.example', true)];
        const rendered = render(<ExtensionSettingsSection extensionId="extension-1" />);
        expect(screen.getByRole('button', { name: 'Test' })).toBeInTheDocument();

        mocks.groups = [makeGroup('https://initial.example', false)];
        rendered.rerender(<ExtensionSettingsSection extensionId="extension-1" />);
        expect(screen.queryByRole('button', { name: 'Test' })).not.toBeInTheDocument();
    });
});
