import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { afterEach, describe, expect, it, vi } from 'vitest';

interface SettingsRuntime {
    register: (config: {
        tagName: string;
        title: string;
        groupName: string;
        fields: Array<{
            key: string;
            label: string;
            type: string;
        }>;
    }) => Promise<void>;
}

interface SettingsWindow extends Window {
    MagpieMetadataSettings?: SettingsRuntime;
}

describe('metadata settings runtime', () => {
    afterEach(() => {
        vi.restoreAllMocks();
        vi.unstubAllGlobals();
        document.body.replaceChildren();
        delete (window as SettingsWindow).MagpieMetadataSettings;
    });

    it('masks a configured secret behind an inline Clear action and pins every patch outcome', async () => {
        const runtimeSource = readFileSync(
            resolve(process.cwd(), '../src/Magpie.Extensions.Tmdb/Assets/metadata-settings.js'),
            'utf8',
        );
        const templateSource = readFileSync(
            resolve(process.cwd(), '../src/Magpie.Extensions.Tmdb/Assets/metadata-settings.html'),
            'utf8',
        );
        const script = document.createElement('script');
        let initialization: Promise<unknown> | undefined;
        Object.defineProperty(script, 'magpieExtensionContext', {
            value: {
                loadAssetText: () => Promise.resolve(templateSource),
                waitUntil: (promise: Promise<unknown>) => {
                    initialization = promise;
                },
            },
        });
        vi.spyOn(document, 'currentScript', 'get').mockReturnValue(script);

        const fetchMock = vi
            .fn<typeof fetch>()
            .mockResolvedValueOnce(
                new Response(
                    JSON.stringify({
                        settings: [{ key: 'language', value: 'en-US' }],
                        secretSettings: { apiKey: { isSet: true } },
                    }),
                    { status: 200, headers: { 'Content-Type': 'application/json' } },
                ),
            )
            .mockResolvedValue(new Response(null, { status: 204 }));
        vi.stubGlobal('fetch', fetchMock);

        const runtimeUrl = `data:text/javascript;base64,${Buffer.from(runtimeSource).toString('base64')}`;
        await import(runtimeUrl);
        await initialization;

        const runtime = (window as SettingsWindow).MagpieMetadataSettings;
        expect(runtime).toBeDefined();
        await runtime?.register({
            tagName: 'magpie-test-secret-settings',
            title: 'Test settings',
            groupName: 'magpie.test',
            fields: [
                { key: 'apiKey', label: 'API key', type: 'password' },
                { key: 'language', label: 'Language', type: 'text' },
            ],
        });

        const element = document.createElement('magpie-test-secret-settings');
        element.setAttribute('data-magpie-extension-id', 'extension-id');
        element.setAttribute('data-magpie-role', 'admin');
        document.body.append(element);

        const apiKeyInput = () => element.shadowRoot?.querySelector<HTMLInputElement>('input[name="apiKey"]');
        const clearButton = () =>
            element.shadowRoot?.querySelector<HTMLButtonElement>('button[aria-label="Clear API key"]');
        const submit = () =>
            element.shadowRoot
                ?.querySelector('form')
                ?.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));

        await vi.waitFor(() => {
            expect(apiKeyInput()).toHaveAttribute('readonly');
        });
        expect(apiKeyInput()).toHaveValue('');
        expect(apiKeyInput()).toHaveAttribute('placeholder', '********');
        expect(clearButton()).toBeInTheDocument();

        const languageInput = element.shadowRoot?.querySelector<HTMLInputElement>('input[name="language"]');
        expect(languageInput).toBeDefined();
        if (languageInput) {
            languageInput.value = 'fr-FR';
            languageInput.dispatchEvent(new InputEvent('input', { bubbles: true }));
        }
        submit();

        await vi.waitFor(() => {
            expect(fetchMock).toHaveBeenCalledTimes(2);
        });
        const languagePatch = JSON.parse(String(fetchMock.mock.calls[1]?.[1]?.body));
        expect(languagePatch).toEqual({ settings: { update: { language: 'fr-FR' } } });

        await vi.waitFor(() => {
            expect(clearButton()).not.toBeDisabled();
        });

        clearButton()?.click();
        expect(apiKeyInput()).not.toHaveAttribute('readonly');
        expect(element.shadowRoot?.activeElement).toBe(apiKeyInput());
        expect(clearButton()).toBeNull();

        apiKeyInput()?.dispatchEvent(new FocusEvent('blur', { bubbles: true }));
        await vi.waitFor(() => {
            expect(apiKeyInput()).toHaveAttribute('readonly');
        });
        expect(clearButton()).toBeInTheDocument();

        submit();
        await vi.waitFor(() => {
            expect(fetchMock).toHaveBeenCalledTimes(2);
        });

        clearButton()?.click();
        const unlockedInput = apiKeyInput();
        if (unlockedInput) {
            unlockedInput.value = 'rotated-secret';
            unlockedInput.dispatchEvent(new InputEvent('input', { bubbles: true }));
        }
        submit();

        await vi.waitFor(() => {
            expect(fetchMock).toHaveBeenCalledTimes(3);
        });
        const updatePatch = JSON.parse(String(fetchMock.mock.calls[2]?.[1]?.body));
        expect(updatePatch).toEqual({ settings: { update: { apiKey: 'rotated-secret' } } });

        await vi.waitFor(() => {
            expect(apiKeyInput()).toHaveAttribute('readonly');
        });
        clearButton()?.click();
        submit();

        await vi.waitFor(() => {
            expect(fetchMock).toHaveBeenCalledTimes(4);
        });
        const clearPatch = JSON.parse(String(fetchMock.mock.calls[3]?.[1]?.body));
        expect(clearPatch).toEqual({ settings: { remove: ['apiKey'] } });
    });
});
