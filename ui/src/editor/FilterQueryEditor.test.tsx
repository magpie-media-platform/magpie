import { act, fireEvent, render, screen } from '@testing-library/react';
import { EditorView } from '@codemirror/view';
import { undo } from '@codemirror/commands';
import { lintGutter, setDiagnostics } from '@codemirror/lint';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';

import { FilterQueryEditor } from './FilterQueryEditor';

vi.mock('./language', () => ({
    filterQuery: () => [lintGutter()],
}));

vi.mock('./lint', () => ({
    filterQueryLinter: () => [],
}));

describe('FilterQueryEditor', () => {
    beforeEach(() => {
        if (!Element.prototype.scrollTo) {
            Element.prototype.scrollTo = vi.fn();
        }
    });

    afterEach(() => {
        vi.clearAllMocks();
    });

    describe('rendering', () => {
        it.each([
            ' .cm-tooltip {background: var(--bg-secondary); color: var(--text-primary); border: 1px solid var(--border-default);}',
            ' .cm-tooltip.cm-completionInfo {background: var(--bg-tertiary); color: var(--text-secondary);}',
            ' .cm-tooltip-autocomplete li[aria-selected] .cm-completionDetail {color: inherit; opacity: 1;}',
        ])('FilterQueryEditor_StylesTheTooltipsWithThemeTokens_WhenRendered %s', (rule) => {
            // Arrange

            // Act
            render(<FilterQueryEditor value="" onChange={vi.fn()} />);

            // Assert
            expect(document.head.textContent).toContain(rule);
        });

        it('renders container element', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).toBeInTheDocument();
        });

        it('applies base class', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).toHaveClass('filter-query-editor');
        });

        it('applies custom className', () => {
            render(
                <FilterQueryEditor value="" onChange={vi.fn()} className="custom-class" data-testid="filter-editor" />,
            );

            const editor = screen.getByTestId('filter-editor');
            expect(editor).toHaveClass('filter-query-editor');
            expect(editor).toHaveClass('custom-class');
        });

        it('applies readonly class when readOnly is true', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} readOnly data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).toHaveAttribute('data-readonly', 'true');
        });

        it('does not apply readonly class when readOnly is false', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} readOnly={false} data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).not.toHaveAttribute('data-readonly', 'true');
        });

        it('has role="textbox" and aria-label by default', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} data-testid="filter-editor" />);

            const editor = screen.getByRole('textbox', { name: 'Filter query' });
            expect(editor).toHaveAttribute('contenteditable', 'true');
            expect(screen.getAllByRole('textbox')).toHaveLength(1);
            expect(editor).toHaveAttribute('role', 'textbox');
            expect(editor).toHaveAttribute('aria-label', 'Filter query');
        });

        it('allows aria-label override via props', () => {
            render(
                <FilterQueryEditor
                    value=""
                    onChange={vi.fn()}
                    data-testid="filter-editor"
                    aria-label="Filter query input"
                />,
            );

            expect(screen.getByRole('textbox')).toHaveAttribute('aria-label', 'Filter query input');
        });

        it('sets aria-readonly when readOnly is true', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} readOnly data-testid="filter-editor" />);

            expect(screen.getByRole('textbox')).toHaveAttribute('aria-readonly', 'true');
        });

        it('preserves focus, selection and undo history when aria attributes change', () => {
            // Arrange
            const onChange = vi.fn();
            const { rerender } = render(<FilterQueryEditor value="a" onChange={onChange} />);
            const editor = screen.getByRole('textbox');
            const view = EditorView.findFromDOM(editor)!;
            act(() => {
                view.focus();
                view.dispatch({ changes: { from: 1, insert: 'b' }, selection: { anchor: 2 } });
            });

            // Act
            rerender(
                <FilterQueryEditor
                    value="ab"
                    onChange={onChange}
                    aria-label="Query"
                    aria-labelledby="label"
                    aria-describedby="help"
                />,
            );

            // Assert
            expect(screen.getByRole('textbox')).toBe(editor);
            expect(editor).toHaveFocus();
            expect(editor).toHaveAttribute('aria-label', 'Query');
            expect(editor).toHaveAttribute('aria-labelledby', 'label');
            expect(editor).toHaveAttribute('aria-describedby', 'help');
            expect(view.state.selection.main.head).toBe(2);
            rerender(<FilterQueryEditor value="ab" onChange={onChange} />);
            expect(editor).toHaveAttribute('aria-label', 'Filter query');
            expect(editor).not.toHaveAttribute('aria-labelledby');
            expect(editor).not.toHaveAttribute('aria-describedby');
            act(() => {
                expect(undo(view)).toBe(true);
            });
            expect(view.state.doc.toString()).toBe('a');
        });

        it('does not set aria-readonly when readOnly is false', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} readOnly={false} data-testid="filter-editor" />);

            expect(screen.getByRole('textbox')).not.toHaveAttribute('aria-readonly');
        });
    });

    describe('props', () => {
        it('accepts value prop', () => {
            const { container } = render(<FilterQueryEditor value="monitoring = true" onChange={vi.fn()} />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });

        it('accepts placeholder prop', () => {
            const { container } = render(
                <FilterQueryEditor value="" onChange={vi.fn()} placeholder="Enter filter query" />,
            );

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });

        it('accepts content context', () => {
            const { container } = render(<FilterQueryEditor value="" onChange={vi.fn()} context="content" />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });

        it('accepts subject context', () => {
            const { container } = render(<FilterQueryEditor value="" onChange={vi.fn()} context="subject" />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });
    });

    describe('onChange callback', () => {
        it('does not call onChange on initial render', () => {
            const handleChange = vi.fn();
            render(<FilterQueryEditor value="" onChange={handleChange} />);

            expect(handleChange).not.toHaveBeenCalled();
        });

        it('does not call onChange when value prop changes externally', () => {
            const handleChange = vi.fn();
            const { rerender } = render(<FilterQueryEditor value="initial" onChange={handleChange} />);

            rerender(<FilterQueryEditor value="updated" onChange={handleChange} />);

            expect(handleChange).not.toHaveBeenCalled();
        });

        it('calls onChange with new value when the editor document changes', () => {
            const handleChange = vi.fn();
            render(<FilterQueryEditor value="" onChange={handleChange} data-testid="filter-editor" />);

            const container = screen.getByTestId('filter-editor');
            const view = EditorView.findFromDOM(container);
            expect(view, 'EditorView should be mounted on the container').not.toBeNull();

            act(() => {
                view!.dispatch({ changes: { from: 0, insert: 'foo' } });
            });

            expect(handleChange).toHaveBeenCalledTimes(1);
            expect(handleChange).toHaveBeenCalledWith('foo');
        });

        it('does not wipe the document when a char is inserted inside auto-closed quotes', () => {
            const handleChange = vi.fn();
            const { rerender } = render(
                <FilterQueryEditor value={'contentType=""'} onChange={handleChange} data-testid="filter-editor" />,
            );

            const container = screen.getByTestId('filter-editor');
            const view = EditorView.findFromDOM(container);
            expect(view).not.toBeNull();

            act(() => {
                view!.dispatch({ changes: { from: 13, insert: 'x' } });
            });

            const emitted = 'contentType="x"';
            expect(handleChange).toHaveBeenCalledWith(emitted);

            act(() => {
                rerender(<FilterQueryEditor value={emitted} onChange={handleChange} data-testid="filter-editor" />);
            });

            expect(EditorView.findFromDOM(container)!.state.doc.toString()).toBe(emitted);
        });

        it('calls onChange with the full document text on subsequent edits', () => {
            const handleChange = vi.fn();
            render(<FilterQueryEditor value="" onChange={handleChange} data-testid="filter-editor" />);

            const container = screen.getByTestId('filter-editor');
            const view = EditorView.findFromDOM(container);
            expect(view).not.toBeNull();

            act(() => {
                view!.dispatch({ changes: { from: 0, insert: 'a' } });
            });
            act(() => {
                view!.dispatch({ changes: { from: 1, insert: 'b' } });
            });

            expect(handleChange).toHaveBeenCalledTimes(2);
            expect(handleChange).toHaveBeenNthCalledWith(1, 'a');
            expect(handleChange).toHaveBeenNthCalledWith(2, 'ab');
        });
    });

    describe('external value changes', () => {
        it.each(['monitoring = true', 'genre has "Action"', 'name contains "test" and status = Active'])(
            'accepts the initial value %s',
            (query) => {
                const { container } = render(<FilterQueryEditor value={query} onChange={vi.fn()} />);

                expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
            },
        );

        it('handles value prop changes', async () => {
            const { container, rerender } = render(<FilterQueryEditor value="initial" onChange={vi.fn()} />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();

            rerender(<FilterQueryEditor value="updated" onChange={vi.fn()} />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });

        it('handles empty value', () => {
            const { container } = render(<FilterQueryEditor value="" onChange={vi.fn()} />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });
    });

    describe('readOnly behavior', () => {
        it('toggles readonly state', () => {
            const { rerender } = render(
                <FilterQueryEditor value="" onChange={vi.fn()} readOnly={false} data-testid="filter-editor" />,
            );

            expect(screen.getByTestId('filter-editor')).not.toHaveAttribute('data-readonly', 'true');

            rerender(<FilterQueryEditor value="" onChange={vi.fn()} readOnly={true} data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).toHaveAttribute('data-readonly', 'true');
        });
    });

    describe('cleanup', () => {
        it('unmounts without error', () => {
            const { unmount } = render(<FilterQueryEditor value="test" onChange={vi.fn()} />);

            expect(() => unmount()).not.toThrow();
        });

        it('handles multiple mount/unmount cycles', () => {
            const first = render(<FilterQueryEditor value="test 0" onChange={vi.fn()} />);
            first.unmount();
            const second = render(<FilterQueryEditor value="test 1" onChange={vi.fn()} />);
            second.unmount();
            const third = render(<FilterQueryEditor value="test 2" onChange={vi.fn()} />);
            third.unmount();
        });
    });

    describe('context switching', () => {
        it('recreates editor when context changes', () => {
            const { container, rerender } = render(<FilterQueryEditor value="" onChange={vi.fn()} context="content" />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();

            rerender(<FilterQueryEditor value="" onChange={vi.fn()} context="subject" />);

            expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
        });
    });

    describe('error surfacing', () => {
        const dispatchDiagnostics = (
            container: HTMLElement,
            diagnostics: { from: number; to: number; severity: 'error' | 'warning'; message: string }[],
        ) => {
            const editor = container.querySelector('.filter-query-editor') as HTMLElement;
            const view = EditorView.findFromDOM(editor);
            expect(view).not.toBeNull();
            act(() => {
                view!.dispatch(setDiagnostics(view!.state, diagnostics));
            });
        };

        it('renders the parser error message as visible text when a diagnostic is present', () => {
            const { container } = render(
                <FilterQueryEditor value={'title contians "x"'} onChange={vi.fn()} data-testid="filter-editor" />,
            );

            expect(screen.queryByRole('alert')).not.toBeInTheDocument();

            dispatchDiagnostics(container, [
                { from: 6, to: 14, severity: 'error', message: "Expected field name, got 'contians'" },
            ]);

            const alert = screen.getByRole('alert');
            expect(alert).toHaveTextContent("Expected field name, got 'contians'");
            expect(alert).toHaveClass('filter-query-editor__error');
        });

        it('marks the editor invalid while an error is present', () => {
            const { container } = render(
                <FilterQueryEditor value="(" onChange={vi.fn()} data-testid="filter-editor" />,
            );

            dispatchDiagnostics(container, [{ from: 0, to: 1, severity: 'error', message: "Expected ')'" }]);

            const editor = screen.getByTestId('filter-editor');
            expect(editor).toHaveClass('filter-query-editor--invalid');
            expect(screen.getByRole('textbox')).toHaveAttribute('aria-invalid', 'true');
        });

        it('clears the error text when the diagnostics resolve', () => {
            const { container } = render(
                <FilterQueryEditor value="(" onChange={vi.fn()} data-testid="filter-editor" />,
            );

            dispatchDiagnostics(container, [{ from: 0, to: 1, severity: 'error', message: "Expected ')'" }]);
            expect(screen.getByRole('alert')).toBeInTheDocument();

            dispatchDiagnostics(container, []);
            expect(screen.queryByRole('alert')).not.toBeInTheDocument();
            expect(screen.getByTestId('filter-editor')).not.toHaveClass('filter-query-editor--invalid');
        });

        it('reports the first error message through onErrorChange', () => {
            const handleError = vi.fn();
            const { container } = render(
                <FilterQueryEditor
                    value="("
                    onChange={vi.fn()}
                    onErrorChange={handleError}
                    data-testid="filter-editor"
                />,
            );

            dispatchDiagnostics(container, [{ from: 0, to: 1, severity: 'error', message: "Expected ')'" }]);
            expect(handleError).toHaveBeenLastCalledWith("Expected ')'");

            dispatchDiagnostics(container, []);
            expect(handleError).toHaveBeenLastCalledWith(null);
        });

        it('ignores warning-severity diagnostics for the error line', () => {
            const { container } = render(
                <FilterQueryEditor value="x" onChange={vi.fn()} data-testid="filter-editor" />,
            );

            dispatchDiagnostics(container, [{ from: 0, to: 1, severity: 'warning', message: 'just a hint' }]);

            expect(screen.queryByRole('alert')).not.toBeInTheDocument();
            expect(screen.getByTestId('filter-editor')).not.toHaveClass('filter-query-editor--invalid');
        });
    });

    describe('submit', () => {
        it('runs the query once on Ctrl+Enter without inserting a line', () => {
            // Arrange
            const onSubmit = vi.fn();
            const onChange = vi.fn();
            render(<FilterQueryEditor value="year > 2000" onChange={onChange} onSubmit={onSubmit} />);

            // Act
            fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter', ctrlKey: true });

            // Assert
            expect(onSubmit).toHaveBeenCalledTimes(1);
            expect(onChange).not.toHaveBeenCalled();
        });

        it('inserts a new line on Enter and does not run the query', () => {
            // Arrange
            const onSubmit = vi.fn();
            const onChange = vi.fn();
            render(<FilterQueryEditor value="year > 2000" onChange={onChange} onSubmit={onSubmit} />);
            const view = EditorView.findFromDOM(screen.getByRole('textbox'))!;
            act(() => view.dispatch({ selection: { anchor: view.state.doc.length } }));

            // Act
            fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' });

            // Assert
            expect(onSubmit).not.toHaveBeenCalled();
            expect(onChange).toHaveBeenCalledWith('year > 2000\n');
        });
    });
});
