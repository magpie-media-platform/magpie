import { useRef, useEffect, useCallback, useState, type ComponentPropsWithoutRef } from 'react';
import { EditorState, Prec } from '@codemirror/state';
import { EditorView, keymap, placeholder as placeholderExt } from '@codemirror/view';
import { defaultKeymap, history, historyKeymap } from '@codemirror/commands';
import {
    acceptCompletion,
    autocompletion,
    closeBrackets,
    closeBracketsKeymap,
    completionStatus,
} from '@codemirror/autocomplete';
import { HighlightStyle, syntaxHighlighting, bracketMatching } from '@codemirror/language';
import { forEachDiagnostic } from '@codemirror/lint';
import { tags as t } from '@lezer/highlight';

import { filterQuery } from './language';

import './FilterQueryEditor.css';

const filterQueryHighlightStyle = HighlightStyle.define([
    { tag: t.variableName, color: 'var(--accent-secondary)' },
    { tag: t.string, color: 'var(--status-healthy)' },
    { tag: t.number, color: 'var(--status-degraded)' },
    { tag: t.keyword, color: 'var(--status-stabilizing)', fontWeight: 'var(--weight-medium)' },
    { tag: t.logicOperator, color: 'var(--status-stabilizing)', fontWeight: 'var(--weight-semibold)' },
    { tag: [t.compareOperator, t.operatorKeyword], color: 'var(--accent-primary)' },
    { tag: t.null, color: 'var(--text-muted)', fontStyle: 'italic' },
]);

export interface FilterQueryEditorProps extends Omit<ComponentPropsWithoutRef<'div'>, 'onChange'> {
    value: string;

    onChange: (value: string) => void;

    readOnly?: boolean;

    placeholder?: string;

    context?: 'content' | 'subject';

    onErrorChange?: (error: string | null) => void;

    /** Runs the query on Ctrl+Enter (Cmd+Enter on macOS). Enter still inserts a new line. */
    onSubmit?: () => void;
}

export function FilterQueryEditor({
    value,
    onChange,
    readOnly = false,
    placeholder = 'genre has "Action" and monitoring != Unmonitored',
    context = 'content',
    onErrorChange,
    onSubmit,
    className,
    'aria-label': ariaLabel = 'Filter query',
    'aria-labelledby': ariaLabelledBy,
    'aria-describedby': ariaDescribedBy,
    ...props
}: FilterQueryEditorProps) {
    const containerRef = useRef<HTMLDivElement>(null);
    const viewRef = useRef<EditorView | null>(null);
    const onChangeRef = useRef(onChange);
    onChangeRef.current = onChange;
    const onSubmitRef = useRef(onSubmit);
    onSubmitRef.current = onSubmit;
    const valueRef = useRef(value);
    valueRef.current = value;
    const ariaRef = useRef({ ariaLabel, ariaLabelledBy, ariaDescribedBy });
    ariaRef.current = { ariaLabel, ariaLabelledBy, ariaDescribedBy };

    const [errorMessage, setErrorMessage] = useState<string | null>(null);
    const errorRef = useRef<string | null>(null);
    const onErrorChangeRef = useRef(onErrorChange);
    onErrorChangeRef.current = onErrorChange;

    const syncDiagnostics = useCallback((view: EditorView) => {
        let firstError: string | null = null;
        forEachDiagnostic(view.state, (d) => {
            if (firstError === null && d.severity === 'error') {
                firstError = d.message;
            }
        });
        if (firstError !== errorRef.current) {
            errorRef.current = firstError;
            setErrorMessage(firstError);
            onErrorChangeRef.current?.(firstError);
        }
    }, []);

    const lastEmittedRef = useRef(value);

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
                EditorView.lineWrapping,
                Prec.highest(
                    keymap.of([
                        {
                            key: 'Mod-Enter',
                            run: (view) => {
                                if (!onSubmitRef.current) {
                                    return false;
                                }
                                if (completionStatus(view.state) !== null) {
                                    acceptCompletion(view);
                                    return true;
                                }
                                onSubmitRef.current();
                                return true;
                            },
                        },
                    ]),
                ),
                keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap]),
                EditorView.updateListener.of((update) => {
                    if (update.docChanged) {
                        const text = update.state.doc.toString();
                        if (text !== lastEmittedRef.current || text !== valueRef.current) {
                            lastEmittedRef.current = text;
                            onChangeRef.current(text);
                        }
                    }
                    syncDiagnostics(update.view);
                }),
                EditorView.editable.of(!readOnly),
                EditorView.contentAttributes.of((view) => {
                    const { ariaLabel, ariaLabelledBy, ariaDescribedBy } = ariaRef.current;
                    let invalid = false;
                    forEachDiagnostic(view.state, (diagnostic) => {
                        invalid ||= diagnostic.severity === 'error';
                    });
                    return {
                        'aria-label': ariaLabel,
                        ...(ariaLabelledBy ? { 'aria-labelledby': ariaLabelledBy } : {}),
                        ...(ariaDescribedBy ? { 'aria-describedby': ariaDescribedBy } : {}),
                        ...(invalid ? { 'aria-invalid': 'true' } : {}),
                        ...(readOnly ? { 'aria-readonly': 'true' } : {}),
                    };
                }),
                EditorView.theme({
                    '&': {
                        fontSize: 'var(--text-sm)',
                        fontFamily: 'var(--font-mono)',
                    },
                    '.cm-content': {
                        padding: 'var(--space-sm) var(--space-md)',
                        caretColor: 'var(--accent-primary)',
                    },
                    '.cm-cursor': {
                        borderLeftColor: 'var(--accent-primary)',
                    },
                    '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
                        background: 'color-mix(in srgb, var(--accent-primary) 20%, transparent)',
                    },
                    '.cm-activeLine': {
                        background: 'transparent',
                    },
                    '.cm-gutters': {
                        display: 'none',
                    },
                    '.cm-tooltip': {
                        background: 'var(--bg-secondary)',
                        color: 'var(--text-primary)',
                        border: '1px solid var(--border-default)',
                    },
                    '.cm-tooltip.cm-tooltip-autocomplete': {
                        borderRadius: 'var(--radius-sm)',
                    },
                    '.cm-tooltip.cm-completionInfo': {
                        background: 'var(--bg-tertiary)',
                        color: 'var(--text-secondary)',
                    },
                    '.cm-tooltip.cm-tooltip-autocomplete > ul > li': {
                        color: 'var(--text-primary)',
                        padding: 'var(--space-xs) var(--space-sm)',
                    },
                    '.cm-tooltip.cm-tooltip-autocomplete > ul > li[aria-selected]': {
                        background: 'var(--accent-fill)',
                        color: 'var(--text-on-accent)',
                    },
                    '.cm-tooltip-autocomplete li[aria-selected] .cm-completionDetail': {
                        color: 'inherit',
                        opacity: '1',
                    },
                    '.cm-diagnostic-error': {
                        borderBottom: '2px solid var(--status-unhealthy)',
                    },
                }),
            ],
        });

        const view = new EditorView({
            state,
            parent: containerRef.current,
        });

        viewRef.current = view;
    }, [context, placeholder, readOnly, syncDiagnostics]);

    useEffect(() => {
        createEditor();

        return () => {
            viewRef.current?.destroy();
            viewRef.current = null;
        };
    }, [createEditor]);

    useEffect(() => {
        viewRef.current?.dispatch({});
    }, [ariaLabel, ariaLabelledBy, ariaDescribedBy]);

    useEffect(() => {
        const view = viewRef.current;
        if (!view) {
            return;
        }

        const currentDoc = view.state.doc.toString();
        if (currentDoc === value || lastEmittedRef.current === value) {
            return;
        }

        const prevSelection = view.state.selection.main;
        const caret = Math.min(prevSelection.head, value.length);
        lastEmittedRef.current = value;
        view.dispatch({
            changes: { from: 0, to: currentDoc.length, insert: value },
            selection: { anchor: caret },
        });
    }, [value]);

    const classes = [
        'filter-query-editor',
        readOnly && 'filter-query-editor--readonly',
        errorMessage && 'filter-query-editor--invalid',
        className,
    ]
        .filter(Boolean)
        .join(' ');

    return (
        <div className="filter-query-editor-field" data-component="filter-query-editor">
            <div
                ref={containerRef}
                className={classes}
                data-readonly={readOnly || undefined}
                data-invalid={errorMessage ? true : undefined}
                {...props}
            />
            {errorMessage && (
                <p className="filter-query-editor__error" role="alert" data-component="filter-query-error">
                    {errorMessage}
                </p>
            )}
        </div>
    );
}
