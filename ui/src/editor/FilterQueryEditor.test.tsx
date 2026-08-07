import { act, render, screen } from '@testing-library/react';
import { EditorView } from '@codemirror/view';
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

            const editor = screen.getByTestId('filter-editor');
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

            expect(screen.getByTestId('filter-editor')).toHaveAttribute('aria-label', 'Filter query input');
        });

        it('sets aria-readonly when readOnly is true', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} readOnly data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).toHaveAttribute('aria-readonly', 'true');
        });

        it('does not set aria-readonly when readOnly is false', () => {
            render(<FilterQueryEditor value="" onChange={vi.fn()} readOnly={false} data-testid="filter-editor" />);

            expect(screen.getByTestId('filter-editor')).not.toHaveAttribute('aria-readonly');
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
        it('accepts different initial values', () => {
            const queries = ['monitoring = true', 'genre has "Action"', 'name contains "test" and status = Active'];

            for (const query of queries) {
                const { container, unmount } = render(<FilterQueryEditor value={query} onChange={vi.fn()} />);

                expect(container.querySelector('.filter-query-editor')).toBeInTheDocument();
                unmount();
            }
        });

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
            for (let i = 0; i < 3; i++) {
                const { unmount } = render(<FilterQueryEditor value={`test ${i}`} onChange={vi.fn()} />);
                unmount();
            }
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
            expect(editor).toHaveAttribute('aria-invalid', 'true');
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
});
