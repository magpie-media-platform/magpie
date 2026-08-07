import { syntaxTree } from '@codemirror/language';
import { linter, type Diagnostic } from '@codemirror/lint';

export function describeSyntaxError(doc: string, from: number, to: number): string {
    const token = doc.slice(from, to).trim();
    if (token) {
        const snippet = token.length > 24 ? `${token.slice(0, 24)}...` : token;
        return `Unexpected "${snippet}"`;
    }
    if (from >= doc.trimEnd().length) {
        return 'Unexpected end of query: the expression looks unfinished';
    }
    return 'Syntax error';
}

export function filterQueryLinter() {
    return linter(
        (view) => {
            const doc = view.state.doc.toString();

            if (!doc.trim()) {
                return [];
            }

            const grammarDiagnostics: Diagnostic[] = [];
            const tree = syntaxTree(view.state);
            tree.iterate({
                enter(node) {
                    if (node.type.isError) {
                        grammarDiagnostics.push({
                            from: node.from,
                            to: Math.max(node.to, node.from + 1),
                            severity: 'error',
                            message: describeSyntaxError(doc, node.from, node.to),
                        });
                    }
                },
            });

            return grammarDiagnostics;
        },
        { delay: 400 },
    );
}
