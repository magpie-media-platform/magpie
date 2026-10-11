import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiFetchResponse } from './api';
import {
    builtInThemes,
    getThemeState,
    initializeTheme,
    initialThemeSettings,
    setThemeCatalog,
    setThemePreference,
    subscribeTheme,
} from './theme';

vi.mock('./api', () => ({ apiFetchResponse: vi.fn() }));

describe('theme', () => {
    const oceanDark = { id: 'Example/Ocean/Dark', name: 'Ocean', variant: 'Dark' } as const;
    const oceanLight = { id: 'Example/Ocean/Light', name: 'Ocean', variant: 'Light' } as const;
    const oceanCatalog = { themes: [...builtInThemes, oceanDark, oceanLight], defaults: initialThemeSettings };
    let lightSystem = false;
    let media: MediaQueryList;
    let dispose: (() => void) | undefined;

    beforeEach(() => {
        localStorage.clear();
        lightSystem = false;
        media = Object.assign(new EventTarget(), {
            media: '(prefers-color-scheme: light)',
        }) as MediaQueryList;
        Object.defineProperty(media, 'matches', { get: () => lightSystem });
        vi.spyOn(window, 'matchMedia').mockReturnValue(media);
        setThemeCatalog({ themes: builtInThemes, defaults: initialThemeSettings });
        setThemePreference(null);
        vi.mocked(apiFetchResponse).mockReset();
        localStorage.clear();
    });

    afterEach(() => {
        dispose?.();
        dispose = undefined;
        vi.restoreAllMocks();
        localStorage.clear();
        document.querySelectorAll('[data-theme-stylesheet]').forEach((element) => element.remove());
        delete document.documentElement.dataset.theme;
        delete document.documentElement.dataset.colorScheme;
    });

    it.each([null, 'invalid', '{}', '{"mode":"invalid","darkTheme":"x","lightTheme":"y"}'])(
        'initializeTheme_AppliesInstanceDefaults_WhenStoredPreferenceIs%s',
        (stored) => {
            // Arrange
            if (stored !== null) localStorage.setItem('magpie.theme', stored);

            // Act
            dispose = initializeTheme();

            // Assert
            expect(getThemeState().preference).toBeNull();
            expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Tokyo Night/Dark');
        },
    );

    it.each([
        ['Magpie.Api/Noir/Dark', 'Dark', 'dark'],
        ['Magpie.Api/Tokyo Night/Dark', 'Dark', 'dark'],
        ['Magpie.Api/Slate/Dark', 'Dark', 'dark'],
        ['Magpie.Api/Ember/Dark', 'Dark', 'dark'],
        ['Magpie.Api/Evergreen/Dark', 'Dark', 'dark'],
        ['Magpie.Api/Frost/Light', 'Light', 'light'],
        ['Magpie.Api/Parchment/Light', 'Light', 'light'],
        ['Magpie.Api/Seafoam/Light', 'Light', 'light'],
        ['Magpie.Api/Banana Stand/Light', 'Light', 'light'],
        ['Magpie.Api/Bubblegum/Light', 'Light', 'light'],
    ])('initializeTheme_InstallsOnlyTheStoredTheme_WhenPreferenceSelects%s', (id, mode, colorScheme) => {
        // Arrange
        localStorage.setItem(
            'magpie.theme',
            JSON.stringify({
                mode,
                darkTheme: mode === 'Dark' ? id : 'Magpie.Api/Tokyo Night/Dark',
                lightTheme: mode === 'Light' ? id : 'Magpie.Api/Frost/Light',
            }),
        );

        // Act
        dispose = initializeTheme();

        // Assert
        expect(document.documentElement.dataset.theme).toBe(id);
        expect(document.documentElement.dataset.colorScheme).toBe(colorScheme);
        expect(document.querySelectorAll('[data-theme-stylesheet]')).toHaveLength(1);
        expect(apiFetchResponse).not.toHaveBeenCalled();
    });

    it('setThemePreference_StoresPreferenceAndNotifiesOnce_WhenPreferenceIsSet', () => {
        // Arrange
        dispose = initializeTheme();
        const listener = vi.fn();
        const unsubscribe = subscribeTheme(listener);

        // Act
        setThemePreference({
            mode: 'Light',
            darkTheme: 'Magpie.Api/Tokyo Night/Dark',
            lightTheme: 'Magpie.Api/Bubblegum/Light',
        });
        unsubscribe();

        // Assert
        expect(JSON.parse(localStorage.getItem('magpie.theme')!)).toEqual({
            mode: 'Light',
            darkTheme: 'Magpie.Api/Tokyo Night/Dark',
            lightTheme: 'Magpie.Api/Bubblegum/Light',
        });
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Bubblegum/Light');
        expect(listener).toHaveBeenCalledOnce();
    });

    it('setThemePreference_RemovesStoredPreferenceAndAppliesDefaults_WhenPreferenceIsNull', () => {
        // Arrange
        dispose = initializeTheme();
        setThemePreference({ ...initialThemeSettings, mode: 'Light' });

        // Act
        setThemePreference(null);

        // Assert
        expect(localStorage.getItem('magpie.theme')).toBeNull();
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Tokyo Night/Dark');
    });

    it('setThemePreference_RemembersTheAppliedThemeOfEachVariant_WhenBothVariantsWereApplied', async () => {
        // Arrange
        vi.mocked(apiFetchResponse)
            .mockResolvedValueOnce(new Response(':root { --bg-primary: #fafafa; }'))
            .mockResolvedValueOnce(new Response(':root { --bg-primary: #101010; }'));
        dispose = initializeTheme();
        setThemeCatalog(oceanCatalog);
        const settings = { darkTheme: 'Example/Ocean/Dark', lightTheme: 'Example/Ocean/Light' };
        setThemePreference({ ...settings, mode: 'Light' });
        await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe('Example/Ocean/Light'));

        // Act
        setThemePreference({ ...settings, mode: 'Dark' });
        await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe('Example/Ocean/Dark'));

        // Assert
        expect(JSON.parse(localStorage.getItem('magpie.theme.boot')!)).toEqual({
            mode: 'Dark',
            Dark: { id: 'Example/Ocean/Dark', css: ':root { --bg-primary: #101010; }', color: '#101010' },
            Light: { id: 'Example/Ocean/Light', css: ':root { --bg-primary: #fafafa; }', color: '#fafafa' },
        });
    });

    it('setThemePreference_ForgetsTheRememberedVariant_WhenTheSelectedThemeIsUnavailable', async () => {
        // Arrange
        vi.mocked(apiFetchResponse).mockResolvedValueOnce(new Response(':root { --bg-primary: #101010; }'));
        dispose = initializeTheme();
        setThemeCatalog({ ...oceanCatalog, defaults: { ...initialThemeSettings, darkTheme: 'Example/Ocean/Dark' } });
        await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe('Example/Ocean/Dark'));

        // Act
        setThemePreference({ mode: 'Light', darkTheme: 'Example/Ocean/Dark', lightTheme: 'Removed/Ocean/Light' });

        // Assert
        expect(JSON.parse(localStorage.getItem('magpie.theme.boot')!)).toEqual({
            mode: 'Light',
            Dark: { id: 'Example/Ocean/Dark', css: ':root { --bg-primary: #101010; }', color: '#101010' },
        });
    });

    it('initializeTheme_KeepsTheBootTheme_WhenTheCatalogIsNotLoaded', async () => {
        // Arrange
        vi.resetModules();
        document.querySelectorAll('[data-theme-stylesheet]').forEach((element) => element.remove());
        const installed = document.createElement('style');
        installed.dataset.themeStylesheet = 'Magpie.Api/Bubblegum/Light';
        document.head.appendChild(installed);
        const theme = await import('./theme');

        // Act
        dispose = theme.initializeTheme();

        // Assert
        expect(document.querySelectorAll('[data-theme-stylesheet]')).toHaveLength(1);
        expect(document.querySelector('[data-theme-stylesheet="Magpie.Api/Bubblegum/Light"]')).toBe(installed);
    });

    it('setThemeCatalog_ReplacesTheBootTheme_WhenTheCatalogSelectsAnotherTheme', async () => {
        // Arrange
        vi.resetModules();
        document.querySelectorAll('[data-theme-stylesheet]').forEach((element) => element.remove());
        const installed = document.createElement('style');
        installed.dataset.themeStylesheet = 'Magpie.Api/Bubblegum/Light';
        document.head.appendChild(installed);
        const theme = await import('./theme');
        dispose = theme.initializeTheme();

        // Act
        theme.setThemeCatalog({
            themes: theme.builtInThemes,
            defaults: {
                mode: 'Light',
                darkTheme: 'Magpie.Api/Tokyo Night/Dark',
                lightTheme: 'Magpie.Api/Seafoam/Light',
            },
        });

        // Assert
        expect(document.querySelectorAll('[data-theme-stylesheet]')).toHaveLength(1);
        expect(document.querySelector('[data-theme-stylesheet="Magpie.Api/Seafoam/Light"]')).not.toBeNull();
    });

    it('setThemeCatalog_AppliesTheNewDefaults_WhenNoPreferenceIsSet', () => {
        // Arrange
        dispose = initializeTheme();

        // Act
        setThemeCatalog({
            themes: builtInThemes,
            defaults: { ...initialThemeSettings, darkTheme: 'Magpie.Api/Ember/Dark' },
        });

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Ember/Dark');
    });

    it('setThemeCatalog_KeepsThePreference_WhenTheDefaultsChange', () => {
        // Arrange
        dispose = initializeTheme();
        setThemePreference({ ...initialThemeSettings, mode: 'Light' });

        // Act
        setThemeCatalog({
            themes: builtInThemes,
            defaults: { ...initialThemeSettings, darkTheme: 'Magpie.Api/Slate/Dark' },
        });

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Frost/Light');
    });

    it.each([true, false])('initializeTheme_FollowsTheDeviceScheme_WhenModeIsSystemAndPreferenceIs%s', (preference) => {
        // Arrange
        const settings = {
            mode: 'System' as const,
            darkTheme: 'Magpie.Api/Evergreen/Dark',
            lightTheme: 'Magpie.Api/Seafoam/Light',
        };
        setThemeCatalog({ themes: builtInThemes, defaults: settings });
        dispose = initializeTheme();
        if (preference) setThemePreference(settings);

        // Act
        lightSystem = true;
        media.dispatchEvent(new Event('change'));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Seafoam/Light');
    });

    it('initializeTheme_IgnoresTheDeviceScheme_WhenModeIsDark', () => {
        // Arrange
        dispose = initializeTheme();
        setThemePreference({ ...initialThemeSettings, darkTheme: 'Magpie.Api/Evergreen/Dark' });

        // Act
        lightSystem = true;
        media.dispatchEvent(new Event('change'));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Evergreen/Dark');
    });

    it('initializeTheme_AppliesThePreferenceOfAnotherTab_WhenStorageChanges', () => {
        // Arrange
        dispose = initializeTheme();
        localStorage.setItem('magpie.theme', JSON.stringify({ ...initialThemeSettings, mode: 'Light' }));

        // Act
        window.dispatchEvent(new StorageEvent('storage', { key: 'magpie.theme' }));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Frost/Light');
    });

    it('initializeTheme_AppliesTheInstanceDefaults_WhenStorageIsCleared', () => {
        // Arrange
        dispose = initializeTheme();
        setThemeCatalog({
            themes: builtInThemes,
            defaults: { ...initialThemeSettings, darkTheme: 'Magpie.Api/Ember/Dark' },
        });
        setThemePreference({ ...initialThemeSettings, mode: 'Light' });
        localStorage.clear();

        // Act
        window.dispatchEvent(new StorageEvent('storage', { key: null }));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Ember/Dark');
    });

    it('setThemePreference_AppliesTheTheme_WhenStorageIsBlocked', () => {
        // Arrange
        vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
            throw new DOMException('Blocked');
        });
        vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new DOMException('Blocked');
        });
        dispose = initializeTheme();

        // Act
        setThemePreference({ ...initialThemeSettings, mode: 'Light' });

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Frost/Light');
    });

    it.each(['missing', 'Magpie.Api/Frost/Light'])(
        'setThemePreference_InstallsNoStylesheet_WhenTheDarkSelectionIs%s',
        (darkTheme) => {
            // Arrange
            dispose = initializeTheme();

            // Act
            setThemePreference({ ...initialThemeSettings, darkTheme });

            // Assert
            expect(document.querySelector('[data-theme-stylesheet]')).toBeNull();
            expect(document.documentElement.dataset.theme).toBeUndefined();
            expect(document.documentElement.dataset.colorScheme).toBeUndefined();
            expect(getThemeState().error).toBe('Selected theme is unavailable. Using Tokyo Night.');
            expect(apiFetchResponse).not.toHaveBeenCalled();
        },
    );

    it.each([
        ['Dark', true],
        ['Light', true],
        ['Dark', false],
        ['Light', false],
    ] as const)(
        'setThemeCatalog_InstallsNoStylesheet_WhenTheSelected%sThemeDisappearsAndPreferenceIs%s',
        async (variant, preference) => {
            // Arrange
            vi.mocked(apiFetchResponse).mockImplementation(() =>
                Promise.resolve(new Response(':root { --bg-primary: #123456; }')),
            );
            const settings = {
                mode: variant,
                darkTheme: 'Example/Ocean/Dark',
                lightTheme: 'Example/Ocean/Light',
            };
            setThemeCatalog({ ...oceanCatalog, defaults: preference ? initialThemeSettings : settings });
            dispose = initializeTheme();
            if (preference) setThemePreference(settings);
            await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe(`Example/Ocean/${variant}`));

            // Act
            setThemeCatalog({ themes: builtInThemes, defaults: preference ? initialThemeSettings : settings });

            // Assert
            expect(document.querySelector('[data-theme-stylesheet]')).toBeNull();
            expect(document.documentElement.dataset.colorScheme).toBeUndefined();
            expect(document.documentElement.dataset.theme).toBeUndefined();
            expect(getThemeState().error).toBe('Selected theme is unavailable. Using Tokyo Night.');
        },
    );

    it('setThemeCatalog_ReappliesTheTheme_WhenTheSelectedThemeReturns', async () => {
        // Arrange
        vi.mocked(apiFetchResponse).mockResolvedValue(new Response(':root { --bg-primary: #123456; }'));
        dispose = initializeTheme();
        setThemePreference({ ...initialThemeSettings, darkTheme: 'Example/Ocean/Dark' });

        // Act
        setThemeCatalog(oceanCatalog);
        await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe('Example/Ocean/Dark'));

        // Assert
        expect(getThemeState().error).toBeNull();
    });

    it('setThemePreference_LoadsTheExtensionStylesheet_WhenAnExtensionThemeIsSelected', async () => {
        // Arrange
        vi.mocked(apiFetchResponse).mockResolvedValue(new Response(':root { --bg-primary: #123456; }'));
        dispose = initializeTheme();
        setThemeCatalog(oceanCatalog);

        // Act
        setThemePreference({ ...initialThemeSettings, darkTheme: 'Example/Ocean/Dark' });
        await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe('Example/Ocean/Dark'));

        // Assert
        expect(apiFetchResponse).toHaveBeenCalledWith('/ui/themes/stylesheet?id=Example%2FOcean%2FDark', {
            skipAuth: true,
        });
        expect(document.querySelector('[data-theme-stylesheet]')).toHaveTextContent(':root { --bg-primary: #123456; }');
    });

    it('setThemePreference_RemovesTheExtensionStylesheet_WhenAnotherThemeIsSelected', async () => {
        // Arrange
        vi.mocked(apiFetchResponse).mockResolvedValue(new Response(':root { --bg-primary: #123456; }'));
        dispose = initializeTheme();
        setThemeCatalog(oceanCatalog);
        setThemePreference({ ...initialThemeSettings, darkTheme: 'Example/Ocean/Dark' });
        await vi.waitFor(() => expect(document.documentElement.dataset.theme).toBe('Example/Ocean/Dark'));

        // Act
        setThemePreference(null);

        // Assert
        expect(document.querySelectorAll('[data-theme-stylesheet]')).toHaveLength(1);
        expect(document.querySelector('[data-theme-stylesheet]')).not.toHaveTextContent('#123456');
    });

    it('setThemePreference_IgnoresALateStylesheet_WhenAnotherThemeWasSelected', async () => {
        // Arrange
        let complete!: (response: Response) => void;
        vi.mocked(apiFetchResponse).mockReturnValue(
            new Promise((resolve) => {
                complete = resolve;
            }),
        );
        dispose = initializeTheme();
        setThemeCatalog(oceanCatalog);
        setThemePreference({ ...initialThemeSettings, darkTheme: 'Example/Ocean/Dark' });
        setThemePreference({ ...initialThemeSettings, mode: 'Light' });

        // Act
        complete(new Response(':root { --bg-primary: #123456; }'));
        await new Promise((resolve) => setTimeout(resolve));

        // Assert
        expect(document.documentElement.dataset.theme).toBe('Magpie.Api/Frost/Light');
        expect(document.querySelector('[data-theme-stylesheet]')).not.toHaveTextContent('#123456');
    });

    it.each(['Dark', 'Light'] as const)(
        'setThemePreference_ReportsTheFailure_WhenThe%sExtensionStylesheetFails',
        async (variant) => {
            // Arrange
            vi.mocked(apiFetchResponse).mockRejectedValue(new Error('Unavailable'));
            dispose = initializeTheme();
            setThemeCatalog(oceanCatalog);

            // Act
            setThemePreference({
                mode: variant,
                darkTheme: 'Example/Ocean/Dark',
                lightTheme: 'Example/Ocean/Light',
            });
            await vi.waitFor(() => expect(getThemeState().error).toBe('Could not load Ocean. Using Tokyo Night.'));

            // Assert
            expect(document.querySelector('[data-theme-stylesheet]')).toBeNull();
            expect(document.documentElement.dataset.theme).toBeUndefined();
            expect(document.documentElement.dataset.colorScheme).toBeUndefined();
        },
    );

    it('initializeTheme_StopsFollowingChangesAndRemovesTheStylesheet_WhenDisposed', () => {
        // Arrange
        const disposeTheme = initializeTheme();
        setThemePreference({ ...initialThemeSettings, mode: 'System' });
        localStorage.setItem('magpie.theme', JSON.stringify({ ...initialThemeSettings, mode: 'Light' }));

        // Act
        disposeTheme();
        lightSystem = true;
        media.dispatchEvent(new Event('change'));
        window.dispatchEvent(new StorageEvent('storage', { key: 'magpie.theme' }));

        // Assert
        expect(getThemeState().preference?.mode).toBe('System');
        expect(document.querySelector('[data-theme-stylesheet]')).toBeNull();
    });
});
