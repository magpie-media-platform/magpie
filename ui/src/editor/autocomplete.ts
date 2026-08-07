import { syntaxTree } from '@codemirror/language';
import type { CompletionContext, CompletionResult, Completion } from '@codemirror/autocomplete';

import {
    type FieldRegistry,
    type CollectionFieldDef,
    getScalarField,
    getCollectionField,
    isCollectionField,
    getOperatorsForType,
} from './field-registry';

const KEYWORDS = ['and', 'or', 'not'];

function fieldCompletions(registry: FieldRegistry): Completion[] {
    const completions: Completion[] = [];

    for (const f of registry.scalarFields) {
        completions.push({
            label: f.name,
            type: 'variable',
            detail: f.type,
            info: f.description,
        });
    }

    for (const f of registry.collectionFields) {
        completions.push({
            label: f.name,
            type: 'variable',
            detail: 'collection',
            info: `Singular form: ${f.name} has "value"`,
        });
        for (const alias of f.aliases) {
            completions.push({
                label: alias,
                type: 'variable',
                detail: 'collection',
                info: `Plural form: ${alias} has (field = "value")`,
            });
        }
    }

    return completions;
}

function operatorCompletions(operators: string[]): Completion[] {
    return operators.map((op) => ({
        label: op,
        type: 'operator',
    }));
}

function collectionOperatorCompletions(): Completion[] {
    return [
        { label: 'has', type: 'keyword', detail: 'has matching item' },
        { label: '!has', type: 'keyword', detail: 'has no matching item' },
    ];
}

function keywordCompletions(): Completion[] {
    return KEYWORDS.map((kw) => ({
        label: kw,
        type: 'keyword',
    }));
}

function itemFieldCompletions(collectionField: CollectionFieldDef): Completion[] {
    return collectionField.itemFields.map((f) => ({
        label: f.name,
        type: 'variable',
        detail: f.type,
    }));
}

function enumCompletions(values: string[]): Completion[] {
    return values.map((v) => ({
        label: v,
        type: 'enum',
    }));
}

function booleanCompletions(): Completion[] {
    return [
        { label: 'true', type: 'keyword' },
        { label: 'false', type: 'keyword' },
    ];
}

function findFieldName(context: CompletionContext): string | null {
    const tree = syntaxTree(context.state);
    const node = tree.resolveInner(context.pos, -1);

    let current = node;
    while (current.parent) {
        const parentName = current.parent.name;
        if (parentName === 'ScalarExpr' || parentName === 'CollectionExpr') {
            const idNode = current.parent.getChild('Identifier');
            if (idNode) {
                return context.state.sliceDoc(idNode.from, idNode.to);
            }
        }
        current = current.parent;
    }

    return null;
}

function findCollectionContext(context: CompletionContext, registry: FieldRegistry): CollectionFieldDef | null {
    const tree = syntaxTree(context.state);
    let node = tree.resolveInner(context.pos, -1);

    while (node.parent) {
        if (node.parent.name === 'CollectionExpr') {
            const idNode = node.parent.getChild('Identifier');
            if (idNode) {
                const fieldName = context.state.sliceDoc(idNode.from, idNode.to);
                return getCollectionField(registry, fieldName) ?? null;
            }
        }
        node = node.parent;
    }

    return null;
}

function getCompletionContext(
    context: CompletionContext,
    registry: FieldRegistry,
): {
    kind: 'field' | 'operator' | 'value' | 'collectionOp' | 'itemField' | 'itemValue' | 'none';
    fieldName?: string;
    collectionField?: CollectionFieldDef;
} {
    const tree = syntaxTree(context.state);
    const node = tree.resolveInner(context.pos, -1);
    const nodeName = node.name;
    const parentName = node.parent?.name ?? '';

    const collField = findCollectionContext(context, registry);
    if (collField) {
        if (parentName === 'ItemField' && (nodeName === 'ItemOp' || node.prevSibling?.name === 'ItemOp')) {
            return { kind: 'itemValue', collectionField: collField };
        }
        return { kind: 'itemField', collectionField: collField };
    }

    const beforeCursor = context.state.sliceDoc(0, context.pos);
    const valueMatch = beforeCursor.match(/(?:^|[\s(])([a-zA-Z_][a-zA-Z0-9_]*)\s*(?:=|!=)\s*$/);
    if (valueMatch && getScalarField(registry, valueMatch[1])) {
        return { kind: 'value', fieldName: valueMatch[1] };
    }

    const fieldName = findFieldName(context);
    if (fieldName) {
        if (parentName === 'ScalarExpr') {
            const prevSibling = node.prevSibling;
            if (
                prevSibling &&
                (prevSibling.name === 'CompareOp' || prevSibling.name === 'StringOp' || prevSibling.name === 'InOp')
            ) {
                return { kind: 'value', fieldName };
            }
            if (nodeName === 'Identifier' && node.from === node.parent!.firstChild?.from) {
                return { kind: 'field' };
            }
        }
        if (parentName === 'CollectionExpr') {
            const prevSibling = node.prevSibling;
            if (prevSibling?.name === 'CollectionOp') {
                return { kind: 'value', fieldName };
            }
        }
    }

    if (nodeName === 'Identifier' || nodeName === '⚠') {
        const word = context.state.sliceDoc(Math.max(0, context.pos - 50), context.pos);
        const lastWord = word.trim().split(/\s+/).pop() ?? '';

        if (parentName === 'Query' || parentName === '' || parentName === 'ParenExpr') {
            if (lastWord && !KEYWORDS.includes(lastWord.toLowerCase())) {
                const scalar = getScalarField(registry, lastWord);
                if (scalar) {
                    return { kind: 'operator', fieldName: lastWord };
                }
                if (isCollectionField(registry, lastWord)) {
                    return { kind: 'collectionOp', fieldName: lastWord };
                }
            }
            return { kind: 'field' };
        }
    }

    if (context.pos === 0 || /\s/.test(context.state.sliceDoc(context.pos - 1, context.pos))) {
        return { kind: 'field' };
    }

    return { kind: 'none' };
}

export function filterQueryAutocomplete(registry: FieldRegistry) {
    return function autocomplete(context: CompletionContext): CompletionResult | null {
        const word = context.matchBefore(/[\w!]+/);
        if (!word && !context.explicit) {
            return null;
        }

        const ctx = getCompletionContext(context, registry);

        let options: Completion[] = [];

        switch (ctx.kind) {
            case 'field':
                options = [...fieldCompletions(registry), ...keywordCompletions()];
                break;
            case 'operator': {
                const field = getScalarField(registry, ctx.fieldName!);
                if (field) {
                    options = operatorCompletions(getOperatorsForType(field.type));
                }
                break;
            }
            case 'collectionOp':
                options = collectionOperatorCompletions();
                break;
            case 'value': {
                const field = getScalarField(registry, ctx.fieldName!);
                if (field?.enumValues) {
                    options = enumCompletions(field.enumValues);
                } else if (field?.type === 'boolean') {
                    options = booleanCompletions();
                }
                break;
            }
            case 'itemField':
                if (ctx.collectionField) {
                    options = [...itemFieldCompletions(ctx.collectionField), ...keywordCompletions()];
                }
                break;
            case 'itemValue':
                break;
            case 'none':
                if (context.explicit) {
                    options = [...fieldCompletions(registry), ...keywordCompletions()];
                }
                break;
        }

        if (options.length === 0) {
            return null;
        }

        return {
            from: word?.from ?? context.pos,
            options,
            validFor: /^[\w!]*$/,
        };
    };
}
