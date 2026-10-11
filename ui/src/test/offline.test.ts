import { beforeEach, describe, expect, it } from 'vitest';

import offlinePage from '../../public/offline.html?raw';

describe('offline.html', () => {
    beforeEach(() => {
        localStorage.clear();
    });

    it('offlinePage_NamesTheInstanceInTheTitleAndTheHeading_WhenTheInstanceNameIsStored', () => {
        // Arrange
        localStorage.setItem('magpie.instanceName', 'Home Lab');
        document.documentElement.innerHTML = offlinePage;
        const inlineScript = document.querySelector('script')!;
        const script = document.createElement('script');
        script.textContent = inlineScript.textContent;

        // Act
        inlineScript.replaceWith(script);

        // Assert
        expect(document.title).toBe('Home Lab is offline');
        expect(document.querySelector('h1')?.textContent).toBe('Home Lab is offline');
    });

    it('offlinePage_NamesMagpieInTheTitleAndTheHeading_WhenNoInstanceNameIsStored', () => {
        // Arrange
        document.documentElement.innerHTML = offlinePage;
        const inlineScript = document.querySelector('script')!;
        const script = document.createElement('script');
        script.textContent = inlineScript.textContent;

        // Act
        inlineScript.replaceWith(script);

        // Assert
        expect(document.title).toBe('Magpie is offline');
        expect(document.querySelector('h1')?.textContent).toBe('Magpie is offline');
    });
});
