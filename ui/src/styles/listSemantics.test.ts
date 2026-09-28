import { describe, expect, it } from 'vitest';

const sources = import.meta.glob<string>(['/src/**/*.tsx', '!/src/**/*.test.tsx'], {
    query: '?raw',
    import: 'default',
    eager: true,
});

const listOpenings = Object.entries(sources).flatMap(([path, source]) =>
    [...source.matchAll(/<(ul|ol)\b[^<>]*>/g)].map((match) => ({ path, tag: match[0].replace(/\s+/g, ' ') })),
);

describe('list semantics', () => {
    it('finds the list elements it guards', () => {
        expect(listOpenings.length).toBeGreaterThan(0);
    });

    it.each(listOpenings)(
        'declares an explicit role on $tag in $path, because the reset removes list markers',
        ({ tag }) => {
            expect(tag).toMatch(/\brole=|\baria-hidden=/);
        },
    );
});
