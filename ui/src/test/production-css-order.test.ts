import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

describe('production CSS cascade order', () => {
    it('establishes the global layer order before the application can load chunk CSS', () => {
        const htmlPath = resolve(process.cwd(), 'index.html');
        const html = readFileSync(htmlPath, 'utf8');
        const document = new DOMParser().parseFromString(html, 'text/html');
        const prelude = document.querySelector('head > style[data-cascade-layer-order]');
        const moduleScript = document.querySelector('script[type="module"]');

        expect(prelude?.textContent?.trim()).toBe('@layer reset, base, components, utilities;');
        expect(moduleScript).not.toBeNull();
        expect(prelude?.compareDocumentPosition(moduleScript!)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });
});
