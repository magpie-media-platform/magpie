import { describe, expect, it } from 'vitest';

import { sortTableRows } from './sortTableRows';

describe('sortTableRows', () => {
    it.each([
        [[], ['Beta', 'alpha', 'Gamma']],
        [[{ id: 'title', desc: false }], ['alpha', 'Beta', 'Gamma']],
        [[{ id: 'title', desc: true }], ['Gamma', 'Beta', 'alpha']],
    ])('uses the column text comparator for %o', (sorting, expected) => {
        const rows = [{ title: 'Beta' }, { title: 'alpha' }, { title: 'Gamma' }];

        const result = sortTableRows(rows, [{ accessorKey: 'title', sortingFn: 'text' }], sorting);

        expect(result.map((row) => row.title)).toEqual(expected);
        expect(rows.map((row) => row.title)).toEqual(['Beta', 'alpha', 'Gamma']);
    });
});
