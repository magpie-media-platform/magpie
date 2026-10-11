import { readdirSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

describe('theme tokens', () => {
    it('references only defined palette tokens', () => {
        const srcDir = resolve(process.cwd(), 'src');
        const tokens = readFileSync(resolve(srcDir, 'styles/tokens.css'), 'utf8');
        const defined = new Set(Array.from(tokens.matchAll(/(--[\w-]+)\s*:/g), (match) => match[1]));
        const undefinedReferences = readdirSync(srcDir, { recursive: true, encoding: 'utf8' })
            .filter((file) => file.endsWith('.css'))
            .flatMap((file) =>
                Array.from(
                    readFileSync(resolve(srcDir, file), 'utf8').matchAll(
                        /var\((--(?:bg|accent|filter|text|button|switch|border|status|fulfillment|shadow)-[\w-]+)/g,
                    ),
                )
                    .filter((match) => !defined.has(match[1]))
                    .map((match) => `${file.replaceAll('\\', '/')}: ${match[1]}`),
            );

        expect(undefinedReferences).toEqual([]);
    });
});
