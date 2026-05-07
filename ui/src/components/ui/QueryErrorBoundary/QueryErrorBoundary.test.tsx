import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';

import { AUTH_FAILURE_EVENT } from '@/lib';

import { QueryErrorBoundary } from './QueryErrorBoundary';

const mockNavigate = vi.fn();
vi.mock('react-router-dom', async () => {
    const actual = await vi.importActual('react-router-dom');
    return {
        ...actual,
        useNavigate: () => mockNavigate,
    };
});

function createTestQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: {
                retry: false,
            },
        },
    });
}

function TestWrapper({ children }: { children: React.ReactNode }) {
    const queryClient = createTestQueryClient();
    return (
        <QueryClientProvider client={queryClient}>
            <MemoryRouter>{children}</MemoryRouter>
        </QueryClientProvider>
    );
}

function ThrowError({ error }: { error: Error }) {
    throw error;
}

beforeEach(() => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    mockNavigate.mockReset();
});

describe('QueryErrorBoundary', () => {
    it('renders children when there is no error', () => {
        render(
            <TestWrapper>
                <QueryErrorBoundary>
                    <div>Child content</div>
                </QueryErrorBoundary>
            </TestWrapper>,
        );
        expect(screen.getByText('Child content')).toBeInTheDocument();
    });

    it('renders error fallback when child throws', () => {
        render(
            <TestWrapper>
                <QueryErrorBoundary>
                    <ThrowError error={new Error('Test error')} />
                </QueryErrorBoundary>
            </TestWrapper>,
        );
        expect(screen.getByRole('alert')).toBeInTheDocument();
        expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    });

    it('has data-component attribute on fallback', () => {
        render(
            <TestWrapper>
                <QueryErrorBoundary>
                    <ThrowError error={new Error('Test error')} />
                </QueryErrorBoundary>
            </TestWrapper>,
        );
        expect(screen.getByRole('alert')).toHaveAttribute('data-component', 'query-error-boundary');
    });

    describe('Error type handling', () => {
        it('shows network error message for status 0', () => {
            const error = Object.assign(new Error('Network error'), {
                name: 'ApiError',
                status: 0,
                message: 'Network error',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByText('Connection Error')).toBeInTheDocument();
            expect(screen.getByText(/unable to connect to the server/i)).toBeInTheDocument();
            expect(screen.getByRole('alert')).toHaveAttribute('data-error-type', 'network');
        });

        it('shows auth error message for status 401', () => {
            const error = Object.assign(new Error('Unauthorized'), {
                name: 'ApiError',
                status: 401,
                message: 'Unauthorized',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByText('Session Expired')).toBeInTheDocument();
            expect(screen.getByRole('alert')).toHaveAttribute('data-error-type', 'auth');
        });

        it('shows permission error message for status 403', () => {
            const error = Object.assign(new Error('Forbidden'), {
                name: 'ApiError',
                status: 403,
                message: 'Forbidden',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByText('Access Denied')).toBeInTheDocument();
            expect(screen.getByRole('alert')).toHaveAttribute('data-error-type', 'permission');
        });

        it('shows not found error message for status 404', () => {
            const error = Object.assign(new Error('Not found'), {
                name: 'ApiError',
                status: 404,
                message: 'Not found',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByText('Not Found')).toBeInTheDocument();
            expect(screen.getByRole('alert')).toHaveAttribute('data-error-type', 'not-found');
        });

        it('shows generic error for server errors', () => {
            const error = Object.assign(new Error('Server error'), {
                name: 'ApiError',
                status: 500,
                message: 'Server error',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByText('Something went wrong')).toBeInTheDocument();
            expect(screen.getByRole('alert')).toHaveAttribute('data-error-type', 'generic');
        });
    });

    describe('Retry functionality', () => {
        it('shows retry button for retryable errors', () => {
            const error = Object.assign(new Error('Server error'), {
                name: 'ApiError',
                status: 500,
                message: 'Server error',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
        });

        it('shows retry button for network errors', () => {
            const error = Object.assign(new Error('Network error'), {
                name: 'ApiError',
                status: 0,
                message: 'Network error',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
        });

        it('does not show retry button for non-retryable errors', () => {
            const error = Object.assign(new Error('Bad request'), {
                name: 'ApiError',
                status: 400,
                message: 'Bad request',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.queryByRole('button', { name: /try again/i })).not.toBeInTheDocument();
        });

        it('retry button has data-action attribute', () => {
            const error = Object.assign(new Error('Server error'), {
                name: 'ApiError',
                status: 500,
                message: 'Server error',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /try again/i })).toHaveAttribute('data-action', 'retry');
        });
    });

    describe('Auth error handling', () => {
        it('shows login button for auth errors', () => {
            const error = Object.assign(new Error('Unauthorized'), {
                name: 'ApiError',
                status: 401,
                message: 'Unauthorized',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /log in/i })).toBeInTheDocument();
        });

        it('login button has data-action attribute', () => {
            const error = Object.assign(new Error('Unauthorized'), {
                name: 'ApiError',
                status: 401,
                message: 'Unauthorized',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /log in/i })).toHaveAttribute('data-action', 'login');
        });

        it('login button dispatches AUTH_FAILURE_EVENT instead of navigating', async () => {
            const user = userEvent.setup();
            const listener = vi.fn();
            window.addEventListener(AUTH_FAILURE_EVENT, listener);

            const error = Object.assign(new Error('Unauthorized'), {
                name: 'ApiError',
                status: 401,
                message: 'Unauthorized',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );

            await user.click(screen.getByRole('button', { name: /log in/i }));

            expect(listener).toHaveBeenCalledOnce();

            window.removeEventListener(AUTH_FAILURE_EVENT, listener);
        });
    });

    describe('Permission error handling', () => {
        it('shows go back button for permission errors', () => {
            const error = Object.assign(new Error('Forbidden'), {
                name: 'ApiError',
                status: 403,
                message: 'Forbidden',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /go back/i })).toBeInTheDocument();
        });

        it('go back button has data-action attribute', () => {
            const error = Object.assign(new Error('Forbidden'), {
                name: 'ApiError',
                status: 403,
                message: 'Forbidden',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /go back/i })).toHaveAttribute('data-action', 'go-back');
        });

        it('go back button uses navigate(-1) instead of window.history.back()', async () => {
            const user = userEvent.setup();
            const error = Object.assign(new Error('Forbidden'), {
                name: 'ApiError',
                status: 403,
                message: 'Forbidden',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );

            await user.click(screen.getByRole('button', { name: /go back/i }));

            expect(mockNavigate).toHaveBeenCalledOnce();
            expect(mockNavigate).toHaveBeenCalledWith(-1);
        });
    });

    describe('Not found error handling', () => {
        it('shows go back button for not found errors', () => {
            const error = Object.assign(new Error('Not found'), {
                name: 'ApiError',
                status: 404,
                message: 'Not found',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /go back/i })).toBeInTheDocument();
        });

        it('go back button has data-action attribute', () => {
            const error = Object.assign(new Error('Not found'), {
                name: 'ApiError',
                status: 404,
                message: 'Not found',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByRole('button', { name: /go back/i })).toHaveAttribute('data-action', 'go-back');
        });

        it('go back button uses navigate(-1) instead of window.history.back()', async () => {
            const user = userEvent.setup();
            const error = Object.assign(new Error('Not found'), {
                name: 'ApiError',
                status: 404,
                message: 'Not found',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );

            await user.click(screen.getByRole('button', { name: /go back/i }));

            expect(mockNavigate).toHaveBeenCalledOnce();
            expect(mockNavigate).toHaveBeenCalledWith(-1);
        });
    });

    describe('Custom fallback', () => {
        it('renders custom fallback ReactNode', () => {
            render(
                <TestWrapper>
                    <QueryErrorBoundary fallback={<div>Custom fallback</div>}>
                        <ThrowError error={new Error('Test error')} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByText('Custom fallback')).toBeInTheDocument();
            expect(screen.queryByText('Something went wrong')).not.toBeInTheDocument();
        });

        it('renders custom fallback function with props', () => {
            render(
                <TestWrapper>
                    <QueryErrorBoundary
                        fallback={({ error, isRetryable }) => (
                            <div data-testid="custom">
                                Error: {error.message}, Retryable: {String(isRetryable)}
                            </div>
                        )}
                    >
                        <ThrowError error={new Error('Test error')} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByTestId('custom')).toHaveTextContent('Error: Test error, Retryable: false');
        });

        it('custom fallback receives error type flags', () => {
            const error = Object.assign(new Error('Network error'), {
                name: 'ApiError',
                status: 0,
                message: 'Network error',
            });
            render(
                <TestWrapper>
                    <QueryErrorBoundary
                        fallback={({ isNetwork, isAuth, isPermission, isNotFound }) => (
                            <div data-testid="custom">
                                Network: {String(isNetwork)}, Auth: {String(isAuth)}, Permission: {String(isPermission)}
                                , NotFound: {String(isNotFound)}
                            </div>
                        )}
                    >
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(screen.getByTestId('custom')).toHaveTextContent(
                'Network: true, Auth: false, Permission: false, NotFound: false',
            );
        });
    });

    describe('Callbacks', () => {
        it('calls onError when error occurs', () => {
            const onError = vi.fn();
            const error = new Error('Test error');
            render(
                <TestWrapper>
                    <QueryErrorBoundary onError={onError}>
                        <ThrowError error={error} />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );
            expect(onError).toHaveBeenCalledOnce();

            expect(onError).toHaveBeenCalledWith(
                error,
                expect.objectContaining({ componentStack: expect.any(String) }),
            );
        });

        it('calls onReset when reset is triggered', async () => {
            const user = userEvent.setup();
            const onReset = vi.fn();
            let shouldThrow = true;

            const TestComponent = () => {
                if (shouldThrow) {
                    const error = Object.assign(new Error('Server error'), {
                        name: 'ApiError',
                        status: 500,
                        message: 'Server error',
                    });
                    throw error;
                }
                return <div>Recovered</div>;
            };

            render(
                <TestWrapper>
                    <QueryErrorBoundary onReset={onReset}>
                        <TestComponent />
                    </QueryErrorBoundary>
                </TestWrapper>,
            );

            expect(screen.getByText('Something went wrong')).toBeInTheDocument();

            shouldThrow = false;
            await user.click(screen.getByRole('button', { name: /try again/i }));

            expect(onReset).toHaveBeenCalledOnce();
        });
    });
});
