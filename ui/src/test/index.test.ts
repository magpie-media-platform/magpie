import { beforeEach, describe, expect, it } from 'vitest';

import indexPage from '../../index.html?raw';

describe('index.html', () => {
    beforeEach(() => {
        localStorage.clear();
        delete document.documentElement.dataset.theme;
        delete document.documentElement.dataset.colorScheme;
    });

    it.each([
        ['Dark', 'Magpie.Api/Ember/Dark', ':root { --bg-primary: #1b1715; }', 'dark', '#1b1715', 'black-translucent'],
        ['Light', 'Magpie.Api/Bubblegum/Light', ':root { --bg-primary: #fceef5; }', 'light', '#fceef5', 'default'],
    ])(
        'indexPage_InstallsTheRememberedThemeOfTheVariant_WhenModeIs%s',
        (mode, id, css, colorScheme, color, statusBarStyle) => {
            // Arrange
            localStorage.setItem(
                'magpie.theme.boot',
                JSON.stringify({
                    mode,
                    Dark: { id: 'Magpie.Api/Ember/Dark', css: ':root { --bg-primary: #1b1715; }', color: '#1b1715' },
                    Light: {
                        id: 'Magpie.Api/Bubblegum/Light',
                        css: ':root { --bg-primary: #fceef5; }',
                        color: '#fceef5',
                    },
                }),
            );
            document.documentElement.innerHTML = indexPage;
            const inlineScript = document.querySelector('script:not([src])')!;
            const script = document.createElement('script');
            script.textContent = inlineScript.textContent;

            // Act
            inlineScript.replaceWith(script);

            // Assert
            expect(document.querySelectorAll('style[data-theme-stylesheet]')).toHaveLength(1);
            expect(document.querySelector(`style[data-theme-stylesheet="${id}"]`)?.textContent).toBe(css);
            expect(document.documentElement.dataset.theme).toBe(id);
            expect(document.documentElement.dataset.colorScheme).toBe(colorScheme);
            expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe(color);
            expect(
                document.querySelector('meta[name="apple-mobile-web-app-status-bar-style"]')?.getAttribute('content'),
            ).toBe(statusBarStyle);
        },
    );

    it('indexPage_KeepsTheDefaultTheme_WhenNoThemeIsRemembered', () => {
        // Arrange
        document.documentElement.innerHTML = indexPage;
        const inlineScript = document.querySelector('script:not([src])')!;
        const script = document.createElement('script');
        script.textContent = inlineScript.textContent;

        // Act
        inlineScript.replaceWith(script);

        // Assert
        expect(document.querySelector('style[data-theme-stylesheet]')).toBeNull();
        expect(document.documentElement.dataset.theme).toBeUndefined();
        expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe('#1a1b26');
    });
});
