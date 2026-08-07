import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { ErrorBoundary, ErrorFallback } from './ErrorBoundary';

function ThrowError({ shouldThrow = true }: { shouldThrow?: boolean }) {
    if (shouldThrow) {
        throw new Error('Test error');
    }
    return <div>No error</div>;
}

beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
});

describe('ErrorBoundary', () => {
    it('renders children when there is no error', () => {
        render(
            <ErrorBoundary>
                <div>Child content</div>
            </ErrorBoundary>,
        );
        expect(screen.getByText('Child content')).toBeInTheDocument();
    });

    it('renders default fallback when child throws', () => {
        render(
            <ErrorBoundary>
                <ThrowError />
            </ErrorBoundary>,
        );
        expect(screen.getByRole('alert')).toBeInTheDocument();
        expect(screen.getByText('Failed to render page')).toBeInTheDocument();
        expect(screen.getByText('Test error')).toBeInTheDocument();
    });

    it('has data-component attribute on default fallback', () => {
        render(
            <ErrorBoundary>
                <ThrowError />
            </ErrorBoundary>,
        );
        expect(screen.getByRole('alert')).toHaveAttribute('data-component', 'error-boundary');
    });

    it('renders custom fallback ReactNode', () => {
        render(
            <ErrorBoundary fallback={<div>Custom fallback</div>}>
                <ThrowError />
            </ErrorBoundary>,
        );
        expect(screen.getByText('Custom fallback')).toBeInTheDocument();
        expect(screen.queryByText('Failed to render page')).not.toBeInTheDocument();
    });

    it('renders custom fallback function with error props', () => {
        render(
            <ErrorBoundary fallback={({ error }) => <div data-testid="custom">Error: {error.message}</div>}>
                <ThrowError />
            </ErrorBoundary>,
        );
        expect(screen.getByTestId('custom')).toHaveTextContent('Error: Test error');
    });

    it('calls onError when error occurs', () => {
        const onError = vi.fn();
        render(
            <ErrorBoundary onError={onError}>
                <ThrowError />
            </ErrorBoundary>,
        );
        expect(onError).toHaveBeenCalledOnce();
        expect(onError).toHaveBeenCalledWith(
            expect.objectContaining({ message: 'Test error' }),
            expect.objectContaining({ componentStack: expect.any(String) }),
        );
    });

    it('resets error state when reset button clicked', async () => {
        const user = userEvent.setup();
        let shouldThrow = true;

        const TestComponent = () => {
            if (shouldThrow) {
                throw new Error('Test error');
            }
            return <div>Recovered</div>;
        };

        const { rerender } = render(
            <ErrorBoundary>
                <TestComponent />
            </ErrorBoundary>,
        );

        expect(screen.getByText('Failed to render page')).toBeInTheDocument();

        shouldThrow = false;

        await user.click(screen.getByRole('button', { name: /try again/i }));

        rerender(
            <ErrorBoundary>
                <TestComponent />
            </ErrorBoundary>,
        );

        expect(screen.getByText('Recovered')).toBeInTheDocument();
    });

    it('calls onReset when reset button clicked', async () => {
        const user = userEvent.setup();
        const onReset = vi.fn();

        render(
            <ErrorBoundary onReset={onReset}>
                <ThrowError />
            </ErrorBoundary>,
        );

        await user.click(screen.getByRole('button', { name: /try again/i }));
        expect(onReset).toHaveBeenCalledOnce();
    });

    it('custom fallback function receives resetErrorBoundary', async () => {
        const user = userEvent.setup();
        const onReset = vi.fn();

        render(
            <ErrorBoundary
                onReset={onReset}
                fallback={({ resetErrorBoundary }) => <button onClick={resetErrorBoundary}>Custom reset</button>}
            >
                <ThrowError />
            </ErrorBoundary>,
        );

        await user.click(screen.getByRole('button', { name: /custom reset/i }));
        expect(onReset).toHaveBeenCalledOnce();
    });

    it('has reset button with data-action attribute', () => {
        render(
            <ErrorBoundary>
                <ThrowError />
            </ErrorBoundary>,
        );
        expect(screen.getByRole('button', { name: /try again/i })).toHaveAttribute('data-action', 'reset-error');
    });

    it('resets error state when resetKeys change', () => {
        let shouldThrow = true;

        const TestComponent = () => {
            if (shouldThrow) {
                throw new Error('Reset keys error');
            }
            return <div>Recovered via resetKeys</div>;
        };

        const { rerender } = render(
            <ErrorBoundary resetKeys={['/downloads']}>
                <TestComponent />
            </ErrorBoundary>,
        );

        expect(screen.getByText('Failed to render page')).toBeInTheDocument();

        shouldThrow = false;

        rerender(
            <ErrorBoundary resetKeys={['/jobs']}>
                <TestComponent />
            </ErrorBoundary>,
        );

        expect(screen.getByText('Recovered via resetKeys')).toBeInTheDocument();
        expect(screen.queryByText('Failed to render page')).not.toBeInTheDocument();
    });

    it('does not reset when resetKeys are unchanged between renders', () => {
        let shouldThrow = true;

        const TestComponent = () => {
            if (shouldThrow) {
                throw new Error('No spurious reset');
            }
            return <div>Should not appear</div>;
        };

        const { rerender } = render(
            <ErrorBoundary resetKeys={['/downloads']}>
                <TestComponent />
            </ErrorBoundary>,
        );

        expect(screen.getByText('Failed to render page')).toBeInTheDocument();

        shouldThrow = false;

        rerender(
            <ErrorBoundary resetKeys={['/downloads']}>
                <TestComponent />
            </ErrorBoundary>,
        );

        expect(screen.getByText('Failed to render page')).toBeInTheDocument();
        expect(screen.queryByText('Should not appear')).not.toBeInTheDocument();
    });
});

describe('ErrorFallback', () => {
    it('renders error message', () => {
        const error = new Error('Standalone error');
        render(<ErrorFallback error={error} resetErrorBoundary={() => {}} />);
        expect(screen.getByText('Standalone error')).toBeInTheDocument();
    });

    it('has role="alert"', () => {
        const error = new Error('Alert test');
        render(<ErrorFallback error={error} resetErrorBoundary={() => {}} />);
        expect(screen.getByRole('alert')).toBeInTheDocument();
    });

    it('calls resetErrorBoundary when button clicked', async () => {
        const user = userEvent.setup();
        const reset = vi.fn();
        const error = new Error('Reset test');

        render(<ErrorFallback error={error} resetErrorBoundary={reset} />);
        await user.click(screen.getByRole('button', { name: /try again/i }));

        expect(reset).toHaveBeenCalledOnce();
    });

    it('displays title', () => {
        const error = new Error('Title test');
        render(<ErrorFallback error={error} resetErrorBoundary={() => {}} />);
        expect(screen.getByText('Failed to render page')).toBeInTheDocument();
    });

    it('has data-component attribute', () => {
        const error = new Error('Data attr test');
        render(<ErrorFallback error={error} resetErrorBoundary={() => {}} />);
        expect(screen.getByRole('alert')).toHaveAttribute('data-component', 'error-boundary');
    });
});
