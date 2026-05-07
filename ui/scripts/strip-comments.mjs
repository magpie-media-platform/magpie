import ts from 'typescript';
import { readFileSync, writeFileSync, readdirSync } from 'fs';
import { join, extname } from 'path';

const PRESERVE = /^\s*(eslint-|@ts-|istanbul|c8|v8|webpack|vite)/;
const EXTENSIONS = new Set(['.ts', '.tsx']);

function findFiles(dir) {
    const results = [];
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = join(dir, entry.name);
        if (entry.isDirectory() && entry.name !== 'node_modules') {
            results.push(...findFiles(full));
        } else if (entry.isFile() && EXTENSIONS.has(extname(entry.name))) {
            results.push(full);
        }
    }
    return results;
}

function collectCommentRanges(source) {
    const sf = ts.createSourceFile('f.tsx', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
    const seen = new Set();
    const ranges = [];

    function add(r) {
        if (seen.has(r.pos)) return;
        seen.add(r.pos);
        const text = source.slice(r.pos, r.end).replace(/^\/[/*]\s*/, '');
        if (!PRESERVE.test(text)) {
            ranges.push(r);
        }
    }

    function walk(node) {
        const leading = ts.getLeadingCommentRanges(source, node.getFullStart());
        if (leading) leading.forEach(add);

        const trailing = ts.getTrailingCommentRanges(source, node.getEnd());
        if (trailing) trailing.forEach(add);

        const children = node.getChildren(sf);
        if (children.length > 0) {
            for (const child of children) {
                walk(child);
            }
        }
    }

    walk(sf);
    return ranges;
}

function stripComments(source) {
    const ranges = collectCommentRanges(source);
    ranges.sort((a, b) => b.pos - a.pos);

    let result = source;
    for (const r of ranges) {
        const lineStart = result.lastIndexOf('\n', r.pos - 1) + 1;
        const before = result.slice(lineStart, r.pos);
        const ownLine = before.trim() === '';

        let end = r.end;
        if (ownLine) {
            if (result[end] === '\r' && result[end + 1] === '\n') end += 2;
            else if (result[end] === '\n') end++;
            result = result.slice(0, lineStart) + result.slice(end);
        } else {
            let start = r.pos;
            while (start > lineStart && (result[start - 1] === ' ' || result[start - 1] === '\t')) start--;
            result = result.slice(0, start) + result.slice(end);
        }
    }

    result = result.replace(/\n{3,}/g, '\n\n');
    return result;
}

const files = findFiles('.');
let changed = 0;

for (const f of files) {
    const before = readFileSync(f, 'utf8');
    const after = stripComments(before);
    if (after !== before) {
        writeFileSync(f, after);
        console.log(f);
        changed++;
    }
}

console.log(`\n${changed} file(s) updated`);
