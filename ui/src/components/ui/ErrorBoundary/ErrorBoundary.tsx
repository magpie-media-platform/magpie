import { Component, type ErrorInfo, type ReactNode } from 'react';

import { WarningCircleIcon, ArrowCounterClockwiseIcon } from '@phosphor-icons/react';

import { Button } from '../Button';

import './ErrorBoundary.css';

export interface ErrorBoundaryProps {
    children: ReactNode;
    fallback?: ReactNode | ((props: FallbackProps) => ReactNode);
    onError?: (error: Error, errorInfo: ErrorInfo) => void;
    onReset?: () => void;
    resetKeys?: unknown[];
}

export interface FallbackProps {
    error: Error;
    resetErrorBoundary: () => void;
}

interface ErrorBoundaryState {
    hasError: boolean;
    error: Error | null;
}

function resetKeysChanged(prev: unknown[] | undefined, next: unknown[] | undefined): boolean {
    if (prev === next) {
        return false;
    }
    if (prev === undefined || next === undefined) {
        return prev !== next;
    }
    if (prev.length !== next.length) {
        return true;
    }
    for (let i = 0; i < prev.length; i++) {
        if (!Object.is(prev[i], next[i])) {
            return true;
        }
    }
    return false;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
    constructor(props: ErrorBoundaryProps) {
        super(props);
        this.state = { hasError: false, error: null };
    }

    static getDerivedStateFromError(error: Error): ErrorBoundaryState {
        return { hasError: true, error };
    }

    componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
        console.error('[ErrorBoundary] Caught error:', error);
        console.error('[ErrorBoundary] Component stack:', errorInfo.componentStack);
        this.props.onError?.(error, errorInfo);
    }

    componentDidUpdate(prevProps: ErrorBoundaryProps): void {
        if (this.state.hasError && resetKeysChanged(prevProps.resetKeys, this.props.resetKeys)) {
            this.setState({ hasError: false, error: null });
        }
    }

    resetErrorBoundary = (): void => {
        this.props.onReset?.();
        this.setState({ hasError: false, error: null });
    };

    render(): ReactNode {
        const { hasError, error } = this.state;
        const { children, fallback } = this.props;

        if (hasError && error) {
            if (fallback !== undefined) {
                if (typeof fallback === 'function') {
                    return fallback({ error, resetErrorBoundary: this.resetErrorBoundary });
                }
                return fallback;
            }

            return <ErrorFallback error={error} resetErrorBoundary={this.resetErrorBoundary} />;
        }

        return children;
    }
}

export function ErrorFallback({ error, resetErrorBoundary }: FallbackProps) {
    return (
        <div className="error-boundary" role="alert" data-component="error-boundary">
            <div className="error-boundary__icon" aria-hidden="true">
                <WarningCircleIcon size={48} weight="fill" />
            </div>
            <div className="error-boundary__content">
                <h2 className="error-boundary__title">Failed to render page</h2>
                <p className="error-boundary__message">{error.message}</p>
            </div>
            <Button variant="secondary" onClick={resetErrorBoundary} data-action="reset-error">
                <ArrowCounterClockwiseIcon size={16} weight="bold" aria-hidden="true" />
                Try again
            </Button>
        </div>
    );
}

export default ErrorBoundary;
