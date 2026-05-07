import { useRef, useEffect, useCallback, type ComponentPropsWithoutRef } from 'react';
import { EditorState } from '@codemirror/state';
import { EditorView, keymap, placeholder as placeholderExt } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import { autocompletion, closeBrackets, closeBracketsKeymap } from '@codemirror/autocomplete';
import { HighlightStyle, syntaxHighlighting, bracketMatching } from '@codemirror/language';
import { tags as t } from '@lezer/highlight';

import { filterQuery } from './language';

import './FilterQueryEditor.css';

const filterQueryHighlightStyle = HighlightStyle.define([
    { tag: t.variableName, color: 'var(--accent-secondary, #7dcfff)' },
    { tag: t.string, color: 'var(--status-healthy, #9ece6a)' },
    { tag: t.number, color: 'var(--status-degraded, #e0af68)' },
    { tag: t.keyword, color: 'var(--status-stabilizing, #bb9af7)', fontWeight: 'var(--weight-medium)' },
    { tag: t.logicOperator, color: 'var(--status-stabilizing, #bb9af7)', fontWeight: 'var(--weight-semibold)' },
    { tag: [t.compareOperator, t.operatorKeyword], color: 'var(--accent-primary, #7aa2f7)' },
    { tag: t.null, color: 'var(--text-muted, #858db5)', fontStyle: 'italic' },
]);

export interface FilterQueryEditorProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    value: string;

    onChange: (value: string) => void;

    readOnly?: boolean;

    placeholder?: string;

    context?: 'content' | 'subject';
}

export function FilterQueryEditor({
    value,
    onChange,
    readOnly = false,
    placeholder = 'e.g. genre has "Action" and monitoring = Active',
    context = 'content',
    className,
    ...props
}: FilterQueryEditorProps) {
    const containerRef = useRef<HTMLDivElement>(null);
    const viewRef = useRef<EditorView | null>(null);
    const onChangeRef = useRef(onChange);
    onChangeRef.current = onChange;
    const valueRef = useRef(value);
    valueRef.current = value;

    const isInternalUpdate = useRef(false);

    const createEditor = useCallback(() => {
        if (!containerRef.current) {
            return;
        }

        const state = EditorState.create({
            doc: valueRef.current,
            extensions: [
                filterQuery(context),
                syntaxHighlighting(filterQueryHighlightStyle),
                history(),
                bracketMatching(),
                closeBrackets(),
                autocompletion({ activateOnTyping: true, icons: false }),
                placeholderExt(placeholder),
                keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap]),
                EditorView.updateListener.of((update) => {
                    if (update.docChanged) {
                        isInternalUpdate.current = true;
                        onChangeRef.current(update.state.doc.toString());
                    }
                }),
                EditorView.editable.of(!readOnly),
                EditorView.theme({
                    '&': {
                        fontSize: 'var(--text-sm, 0.85rem)',
                        fontFamily: 'var(--font-mono, "JetBrains Mono", monospace)',
                    },
                    '.cm-content': {
                        padding: 'var(--space-sm, 0.5rem) var(--space-md, 1rem)',
                        caretColor: 'var(--accent-primary, #7aa2f7)',
                    },
                    '.cm-cursor': {
                        borderLeftColor: 'var(--accent-primary, #7aa2f7)',
                    },
                    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
                        background: 'rgba(122, 162, 247, 0.2)',
                    },
                    '.cm-activeLine': {
                        background: 'transparent',
                    },
                    '.cm-gutters': {
                        display: 'none',
                    },
                    '.cm-tooltip.cm-tooltip-autocomplete': {
                        background: 'var(--bg-secondary, #24283b)',
                        border: '1px solid var(--border-default, #414868)',
                        borderRadius: 'var(--radius-sm, 6px)',
                    },
                    '.cm-tooltip.cm-tooltip-autocomplete > ul > li': {
                        color: 'var(--text-primary, #c0caf5)',
                        padding: '4px 8px',
                    },
                    '.cm-tooltip.cm-tooltip-autocomplete > ul > li[aria-selected]': {
                        background: 'var(--accent-primary, #7aa2f7)',
                        color: 'var(--bg-primary, #1a1b26)',
                    },
                    '.cm-diagnostic-error': {
                        borderBottom: '2px solid var(--status-unhealthy, #f7768e)',
                    },
                }),
            ],
        });

        const view = new EditorView({
            state,
            parent: containerRef.current,
        });

        viewRef.current = view;
    }, [context, placeholder, readOnly]);

    useEffect(() => {
        createEditor();

        return () => {
            viewRef.current?.destroy();
            viewRef.current = null;
        };
    }, [createEditor]);

    useEffect(() => {
        const view = viewRef.current;
        if (!view) {
            return;
        }

        if (isInternalUpdate.current) {
            isInternalUpdate.current = false;
            return;
        }

        const currentDoc = view.state.doc.toString();
        if (currentDoc !== value) {
            view.dispatch({
                changes: { from: 0, to: currentDoc.length, insert: value },
            });
        }
    }, [value]);

    const classes = ['filter-query-editor', readOnly && 'filter-query-editor--readonly', className]
        .filter(Boolean)
        .join(' ');

    return (
        <div
            ref={containerRef}
            className={classes}
            role="textbox"
            aria-label="Filter query"
            aria-readonly={readOnly || undefined}
            data-readonly={readOnly || undefined}
            {...props}
        />
    );
}
