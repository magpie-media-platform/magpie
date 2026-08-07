import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('@codemirror/lint', () => ({
    linter: (fn: (view: unknown) => unknown) => fn,
}));

vi.mock('@codemirror/language', () => ({
    syntaxTree: vi.fn(),
}));

import { syntaxTree } from '@codemirror/language';

import { filterQueryLinter, describeSyntaxError } from './lint';

type FakeNode = { isError: boolean; from: number; to: number };

function makeTree(nodes: FakeNode[]) {
    return {
        iterate({ enter }: { enter: (node: { type: { isError: boolean }; from: number; to: number }) => void }) {
            for (const node of nodes) {
                enter({ type: { isError: node.isError }, from: node.from, to: node.to });
            }
        },
    };
}

function makeView(doc: string) {
    return { state: { doc: { toString: () => doc } } };
}

const mockSyntaxTree = vi.mocked(syntaxTree);

type LintFn = (view: ReturnType<typeof makeView>) => Array<{
    from: number;
    to: number;
    severity: string;
    message: string;
}>;

afterEach(() => {
    vi.clearAllMocks();
});

describe('filterQueryLinter', () => {
    it('returns no diagnostics for empty input without reading the syntax tree', () => {
        const lint = filterQueryLinter() as unknown as LintFn;

        expect(lint(makeView('   '))).toEqual([]);
        expect(mockSyntaxTree).not.toHaveBeenCalled();
    });

    it('returns no diagnostics when the grammar accepts the query', () => {
        mockSyntaxTree.mockReturnValue(makeTree([]) as never);
        const lint = filterQueryLinter() as unknown as LintFn;

        expect(lint(makeView('monitoring = true'))).toEqual([]);
    });

    it('returns a diagnostic for each grammar error node', () => {
        mockSyntaxTree.mockReturnValue(
            makeTree([
                { isError: true, from: 14, to: 19 },
                { isError: true, from: 20, to: 21 },
            ]) as never,
        );
        const lint = filterQueryLinter() as unknown as LintFn;

        expect(lint(makeView('name = "test" extra @'))).toEqual([
            { from: 14, to: 19, severity: 'error', message: 'Unexpected "extra"' },
            { from: 20, to: 21, severity: 'error', message: 'Unexpected "@"' },
        ]);
    });

    it('describes a zero-width error at the end as an unfinished expression', () => {
        mockSyntaxTree.mockReturnValue(makeTree([{ isError: true, from: 7, to: 7 }]) as never);
        const lint = filterQueryLinter() as unknown as LintFn;

        expect(lint(makeView('name = '))).toEqual([
            {
                from: 7,
                to: 8,
                severity: 'error',
                message: 'Unexpected end of query: the expression looks unfinished',
            },
        ]);
    });
});

describe('describeSyntaxError', () => {
    it('quotes the offending token from the document', () => {
        const doc = 'title contians "x"';
        expect(describeSyntaxError(doc, 6, 14)).toBe('Unexpected "contians"');
    });

    it('truncates an over-long offending token', () => {
        const long = 'a'.repeat(40);
        const doc = `title ${long}`;
        expect(describeSyntaxError(doc, 6, 6 + long.length)).toBe(`Unexpected "${'a'.repeat(24)}..."`);
    });

    it('falls back to a generic message for an interior zero-width gap', () => {
        expect(describeSyntaxError('title  contains "x"', 6, 6)).toBe('Syntax error');
    });
});
