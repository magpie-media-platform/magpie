import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('@codemirror/lint', () => ({
    linter: (fn: (view: unknown) => Promise<unknown>) => fn,
}));

vi.mock('@codemirror/language', () => ({
    syntaxTree: vi.fn(),
}));

vi.mock('@/lib/filterQuery', () => ({
    parseFilterQuery: vi.fn(),
}));

import { syntaxTree } from '@codemirror/language';
import { parseFilterQuery } from '@/lib/filterQuery';

import { filterQueryLinter } from './lint';

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
const mockParseFilterQuery = vi.mocked(parseFilterQuery);

type LintFn = (
    view: ReturnType<typeof makeView>,
) => Promise<Array<{ from: number; to: number; severity: string; message: string }>>;

afterEach(() => {
    vi.clearAllMocks();
});

describe('filterQueryLinter', () => {
    describe('empty document', () => {
        it('returns no diagnostics for empty string', async () => {
            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView(''));

            expect(diagnostics).toEqual([]);
            expect(mockSyntaxTree).not.toHaveBeenCalled();
            expect(mockParseFilterQuery).not.toHaveBeenCalled();
        });

        it('returns no diagnostics for whitespace-only string', async () => {
            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('   '));

            expect(diagnostics).toEqual([]);
            expect(mockSyntaxTree).not.toHaveBeenCalled();
            expect(mockParseFilterQuery).not.toHaveBeenCalled();
        });
    });

    describe('tree-walking (syntax errors)', () => {
        it('returns no diagnostics when tree has no error nodes', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([{ isError: false, from: 0, to: 10 }]) as never);
            mockParseFilterQuery.mockResolvedValue({ criteria: {} });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('name = "test"'));

            const syntaxDiagnostics = diagnostics.filter((d) => d.message === 'Syntax error');
            expect(syntaxDiagnostics).toHaveLength(0);
        });

        it('creates a syntax error diagnostic for each error node', async () => {
            mockSyntaxTree.mockReturnValue(
                makeTree([
                    { isError: true, from: 5, to: 10 },
                    { isError: true, from: 15, to: 20 },
                ]) as never,
            );

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('name @ "test" extra @'));

            expect(diagnostics).toHaveLength(2);
            expect(diagnostics[0]).toEqual({ from: 5, to: 10, severity: 'error', message: 'Syntax error' });
            expect(diagnostics[1]).toEqual({ from: 15, to: 20, severity: 'error', message: 'Syntax error' });
        });

        it('clamps error node to width 1 when to === from', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([{ isError: true, from: 7, to: 7 }]) as never);

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('name = '));

            expect(diagnostics).toHaveLength(1);
            expect(diagnostics[0].from).toBe(7);
            expect(diagnostics[0].to).toBe(8);
        });

        it('clamps error node to width 1 when to < from', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([{ isError: true, from: 7, to: 6 }]) as never);

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('name = '));

            expect(diagnostics[0].to).toBe(8);
        });

        it('does not call parseFilterQuery when syntax errors are present', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([{ isError: true, from: 0, to: 5 }]) as never);

            const lint = filterQueryLinter('content') as unknown as LintFn;
            await lint(makeView('@ bad'));

            expect(mockParseFilterQuery).not.toHaveBeenCalled();
        });
    });

    describe('API call (semantic errors)', () => {
        it('calls parseFilterQuery with the document text and context', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({ criteria: {} });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            await lint(makeView('monitoring = Active'));

            expect(mockParseFilterQuery).toHaveBeenCalledWith('monitoring = Active', 'content');
        });

        it('passes subject context to parseFilterQuery', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({ criteria: {} });

            const lint = filterQueryLinter('subject') as unknown as LintFn;
            await lint(makeView('kind = "person"'));

            expect(mockParseFilterQuery).toHaveBeenCalledWith('kind = "person"', 'subject');
        });

        it('returns no diagnostics when parseFilterQuery returns no error', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({ criteria: { and: [] } });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('monitoring = Active'));

            expect(diagnostics).toEqual([]);
        });

        it('creates a diagnostic from parseFilterQuery error', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({
                error: { message: 'Unknown field: badfield', position: 0 },
            });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('badfield = "test"'));

            expect(diagnostics).toHaveLength(1);
            expect(diagnostics[0]).toEqual({
                from: 0,
                to: 1,
                severity: 'error',
                message: 'Unknown field: badfield',
            });
        });

        it('positions error at the reported position', async () => {
            const doc = 'monitoring = BadValue';
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({
                error: { message: 'Invalid value', position: 14 },
            });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView(doc));

            expect(diagnostics[0].from).toBe(14);
            expect(diagnostics[0].to).toBe(15);
        });

        it('clamps error position to doc.length when position exceeds it', async () => {
            const doc = 'name = "x"';
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({
                error: { message: 'Unexpected end of input', position: 999 },
            });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView(doc));

            expect(diagnostics[0].from).toBe(10);
            expect(diagnostics[0].to).toBe(10);
        });

        it('clamps error to position exactly at doc.length when position === doc.length', async () => {
            const doc = 'name =';
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockResolvedValue({
                error: { message: 'Expected value', position: 6 },
            });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView(doc));

            expect(diagnostics[0].from).toBe(6);
            expect(diagnostics[0].to).toBe(6);
        });

        it('swallows exceptions from parseFilterQuery and returns empty diagnostics', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockRejectedValue(new Error('Network error'));

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('monitoring = Active'));

            expect(diagnostics).toEqual([]);
        });

        it('swallows non-Error exceptions from parseFilterQuery', async () => {
            mockSyntaxTree.mockReturnValue(makeTree([]) as never);
            mockParseFilterQuery.mockRejectedValue({ status: 500, message: 'Server error' });

            const lint = filterQueryLinter('content') as unknown as LintFn;
            const diagnostics = await lint(makeView('monitoring = Active'));

            expect(diagnostics).toEqual([]);
        });
    });
});
