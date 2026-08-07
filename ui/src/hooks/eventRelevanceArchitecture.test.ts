import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

const RELEVANCE_MODULES = [
    'contentEventImpact.ts',
    'contentRelevanceSnapshot.ts',
    'eventQueryRelevance.ts',
    'useInvalidateQueriesOnEvents.ts',
] as const;

function source(module: string): string {
    return readFileSync(resolve(process.cwd(), 'src/hooks', module), 'utf8');
}

describe('event relevance architecture', () => {
    it.each(RELEVANCE_MODULES)('%s does not import the api client', (module) => {
        const imports = [...source(module).matchAll(/^import[^;]*from\s+'([^']+)';$/gm)].map((match) => match[1]);

        expect(imports).not.toContain('@/lib/api');
        expect(imports.filter((specifier) => specifier.includes('/api'))).toEqual([]);
    });

    it.each(RELEVANCE_MODULES)('%s never issues a network request', (module) => {
        const text = source(module);

        expect(text).not.toMatch(/\bapiGet\b|\bapiPost\b|\bapiPatch\b|\bapiDelete\b/);
        expect(text).not.toMatch(/\bfetch\s*\(|XMLHttpRequest|EventSource|WebSocket/);
        expect(text).not.toMatch(/fetchQuery|ensureQueryData|prefetchQuery|fetchInfiniteQuery/);
    });

    it.each(['contentEventImpact.ts', 'contentRelevanceSnapshot.ts', 'eventQueryRelevance.ts'] as const)(
        '%s never reaches the query client for anything but cached reads',
        (module) => {
            const text = source(module);

            expect(text).not.toMatch(/refetchQueries|invalidateQueries|\.fetch\b/);
        },
    );

    it('resolves relevance synchronously', () => {
        const text = source('eventQueryRelevance.ts');

        expect(text).not.toMatch(/\basync\b|\bawait\b|Promise</);
        expect(text).not.toMatch(/'resolve'|resolveContext|resolveRelationships|invalidation\.unresolved/);
    });

    it('keeps the deleted network-capable resolver deleted', () => {
        const text = source('useInvalidateQueriesOnEvents.ts');

        expect(text).not.toMatch(/resolveEventInvalidationContext|eventInvalidationContext/);
    });
});
