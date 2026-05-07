import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQueryErrorResetBoundary } from '@tanstack/react-query';

import { WarningCircle, ArrowCounterClockwise, WifiSlash, ShieldWarning } from '@phosphor-icons/react';

import { Button } from '../Button/Button';
import { ErrorBoundary, type FallbackProps } from '../ErrorBoundary';
import {
    isNetworkError,
    isAuthError,
    isPermissionError,
    isNotFoundError,
    isRetryableError,
    getErrorMessage,
    AUTH_FAILURE_EVENT,
} from '@/lib';

import './QueryErrorBoundary.css';

export interface QueryErrorBoundaryProps {
    children: ReactNode;

    fallback?: ReactNode | ((props: QueryFallbackProps) => ReactNode);

    onError?: (error: Error) => void;

    onReset?: () => void;
}

export interface QueryFallbackProps extends FallbackProps {
    isRetryable: boolean;

    isNetwork: boolean;

    isAuth: boolean;

    isPermission: boolean;

    isNotFound: boolean;
}

function QueryErrorFallback({
    error,
    resetErrorBoundary,
    isRetryable,
    isNetwork,
    isAuth,
    isPermission,
    isNotFound,
}: QueryFallbackProps) {
    const navigate = useNavigate();

    if (isAuth) {
        return (
            <div
                className="query-error-boundary query-error-boundary--auth"
                role="alert"
                data-component="query-error-boundary"
                data-error-type="auth"
            >
                <div className="query-error-boundary__icon" aria-hidden="true">
                    <ShieldWarning size={48} weight="fill" />
                </div>
                <div className="query-error-boundary__content">
                    <h2 className="query-error-boundary__title">Session Expired</h2>
                    <p className="query-error-boundary__message">
                        Your session has expired. Please log in again to continue.
                    </p>
                </div>
                <Button
                    variant="primary"
                    onClick={() => {
                        window.dispatchEvent(new CustomEvent(AUTH_FAILURE_EVENT));
                    }}
                    data-action="login"
                >
                    Log In
                </Button>
            </div>
        );
    }

    if (isPermission) {
        return (
            <div
                className="query-error-boundary query-error-boundary--permission"
                role="alert"
                data-component="query-error-boundary"
                data-error-type="permission"
            >
                <div className="query-error-boundary__icon" aria-hidden="true">
                    <ShieldWarning size={48} weight="fill" />
                </div>
                <div className="query-error-boundary__content">
                    <h2 className="query-error-boundary__title">Access Denied</h2>
                    <p className="query-error-boundary__message">You do not have permission to view this content.</p>
                </div>
                <Button
                    variant="secondary"
                    onClick={() => {
                        navigate(-1);
                    }}
                    data-action="go-back"
                >
                    Go Back
                </Button>
            </div>
        );
    }

    if (isNetwork) {
        return (
            <div
                className="query-error-boundary query-error-boundary--network"
                role="alert"
                data-component="query-error-boundary"
                data-error-type="network"
            >
                <div className="query-error-boundary__icon" aria-hidden="true">
                    <WifiSlash size={48} weight="fill" />
                </div>
                <div className="query-error-boundary__content">
                    <h2 className="query-error-boundary__title">Connection Error</h2>
                    <p className="query-error-boundary__message">
                        Unable to connect to the server. Please check your network connection.
                    </p>
                </div>
                <Button variant="secondary" onClick={resetErrorBoundary} data-action="retry">
                    <ArrowCounterClockwise size={16} weight="bold" aria-hidden="true" />
                    Try Again
                </Button>
            </div>
        );
    }

    if (isNotFound) {
        return (
            <div
                className="query-error-boundary query-error-boundary--not-found"
                role="alert"
                data-component="query-error-boundary"
                data-error-type="not-found"
            >
                <div className="query-error-boundary__icon" aria-hidden="true">
                    <WarningCircle size={48} weight="fill" />
                </div>
                <div className="query-error-boundary__content">
                    <h2 className="query-error-boundary__title">Not Found</h2>
                    <p className="query-error-boundary__message">The requested content could not be found.</p>
                </div>
                <Button
                    variant="secondary"
                    onClick={() => {
                        navigate(-1);
                    }}
                    data-action="go-back"
                >
                    Go Back
                </Button>
            </div>
        );
    }

    return (
        <div
            className="query-error-boundary"
            role="alert"
            data-component="query-error-boundary"
            data-error-type="generic"
        >
            <div className="query-error-boundary__icon" aria-hidden="true">
                <WarningCircle size={48} weight="fill" />
            </div>
            <div className="query-error-boundary__content">
                <h2 className="query-error-boundary__title">Something went wrong</h2>
                <p className="query-error-boundary__message">{getErrorMessage(error)}</p>
            </div>
            {isRetryable && (
                <Button variant="secondary" onClick={resetErrorBoundary} data-action="retry">
                    <ArrowCounterClockwise size={16} weight="bold" aria-hidden="true" />
                    Try Again
                </Button>
            )}
        </div>
    );
}

export function QueryErrorBoundary({ children, fallback, onError, onReset }: QueryErrorBoundaryProps) {
    const { reset } = useQueryErrorResetBoundary();

    const handleReset = () => {
        reset();

        onReset?.();
    };

    const renderFallback = (props: FallbackProps) => {
        const { error, resetErrorBoundary } = props;

        const queryFallbackProps: QueryFallbackProps = {
            error,
            resetErrorBoundary,
            isRetryable: isRetryableError(error),
            isNetwork: isNetworkError(error),
            isAuth: isAuthError(error),
            isPermission: isPermissionError(error),
            isNotFound: isNotFoundError(error),
        };

        if (fallback !== undefined) {
            if (typeof fallback === 'function') {
                return fallback(queryFallbackProps);
            }
            return fallback;
        }

        return <QueryErrorFallback {...queryFallbackProps} />;
    };

    return (
        <ErrorBoundary fallback={renderFallback} onError={onError} onReset={handleReset}>
            {children}
        </ErrorBoundary>
    );
}

export default QueryErrorBoundary;
