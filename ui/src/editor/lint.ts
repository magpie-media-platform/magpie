import { syntaxTree } from '@codemirror/language';
import { linter, type Diagnostic } from '@codemirror/lint';

import { parseFilterQuery } from '@/lib/filterQuery';

export function filterQueryLinter(context: 'content' | 'subject') {
    return linter(
        async (view) => {
            const diagnostics: Diagnostic[] = [];
            const doc = view.state.doc.toString();

            if (!doc.trim()) {
                return diagnostics;
            }

            const tree = syntaxTree(view.state);
            tree.iterate({
                enter(node) {
                    if (node.type.isError) {
                        diagnostics.push({
                            from: node.from,
                            to: Math.max(node.to, node.from + 1),
                            severity: 'error',
                            message: 'Syntax error',
                        });
                    }
                },
            });

            if (diagnostics.length === 0) {
                try {
                    const result = await parseFilterQuery(doc, context);
                    if (result.error) {
                        const pos = Math.min(result.error.position, doc.length);
                        diagnostics.push({
                            from: pos,
                            to: Math.min(pos + 1, doc.length),
                            severity: 'error',
                            message: result.error.message,
                        });
                    }
                } catch {
                    // ignore
                }
            }

            return diagnostics;
        },
        { delay: 400 },
    );
}
