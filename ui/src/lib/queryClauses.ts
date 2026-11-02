const AND_OR_DELIMITER = /^\s+(and|or)(?:\s+|$)/i;

export interface TopLevelClausesResult {
    clauses: string[];
    hasTopLevelOr: boolean;
    valid: boolean;
}

export function splitTopLevelClauses(query: string): TopLevelClausesResult {
    const trimmed = query.trim();
    if (!trimmed) {
        return { clauses: [], hasTopLevelOr: false, valid: true };
    }

    const clauses: string[] = [];
    let current = '';
    let depth = 0;
    let inString = false;
    let hasTopLevelOr = false;
    let unbalancedParens = false;
    let i = 0;

    while (i < trimmed.length) {
        const ch = trimmed[i];

        if (inString) {
            current += ch;
            if (ch === '\\' && i + 1 < trimmed.length) {
                current += trimmed[i + 1];
                i += 2;
                continue;
            }
            if (ch === '"') {
                inString = false;
            }
            i += 1;
            continue;
        }

        if (ch === '"') {
            inString = true;
            current += ch;
            i += 1;
            continue;
        }

        if (ch === '(') {
            depth += 1;
            current += ch;
            i += 1;
            continue;
        }

        if (ch === ')') {
            if (depth === 0) {
                unbalancedParens = true;
            } else {
                depth -= 1;
            }
            current += ch;
            i += 1;
            continue;
        }

        if (depth === 0 && /\s/.test(ch)) {
            const delimiter = trimmed.slice(i).match(AND_OR_DELIMITER);
            if (delimiter) {
                if (delimiter[1].toLowerCase() === 'or') {
                    hasTopLevelOr = true;
                }
                clauses.push(current.trim());
                current = '';
                i += delimiter[0].length;
                continue;
            }
        }

        current += ch;
        i += 1;
    }

    clauses.push(current.trim());
    const valid = !inString && depth === 0 && !unbalancedParens && !hasTopLevelOr && clauses.every(Boolean);

    return { clauses, hasTopLevelOr, valid };
}

export function splitTopLevelAndClauses(query: string): string[] | null {
    const result = splitTopLevelClauses(query);
    return result.valid ? result.clauses : null;
}
