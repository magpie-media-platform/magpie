import { useCallback, useState, type SubmitEvent, type ReactNode } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { SignInIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Logo } from '@/components/ui/Logo';
import { useAuth } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { isApiError } from '@/lib/api';
import { RouteLoadingState } from '@/router/RouteLoadingState';

import './LoginPage.css';

interface LoginFromLocation {
    pathname?: string;

    search?: string;

    hash?: string;
}

interface LoginLocationState {
    from?: LoginFromLocation;
}

function getErrorMessage(error: unknown): string {
    if (isApiError(error)) {
        if (error.status === 401 || error.status === 403) {
            return 'Invalid username or password.';
        }
        return error.message || 'Sign in failed. Please try again.';
    }

    if (error instanceof Error && error.message) {
        return error.message;
    }

    return 'Sign in failed. Please try again.';
}

function getRedirectTarget(from?: LoginFromLocation): string {
    const pathname = from?.pathname || '/';
    return `${pathname}${from?.search ?? ''}${from?.hash ?? ''}`;
}

export function LoginPage(): ReactNode {
    useDocumentTitle('Sign In');

    const { isAuthenticated, isLoading, login } = useAuth();
    const navigate = useNavigate();
    const location = useLocation();

    const state = (location.state as LoginLocationState | null) ?? null;
    const fromPath = getRedirectTarget(state?.from);

    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [isSubmitting, setIsSubmitting] = useState(false);

    const handleSubmit = useCallback(
        async (event: SubmitEvent<HTMLFormElement>) => {
            event.preventDefault();

            if (!username.trim() || !password) {
                setError('Please enter your username and password.');
                return;
            }

            setError(null);
            setIsSubmitting(true);
            try {
                await login(username, password);
                void navigate(fromPath, { replace: true });
            } catch (err) {
                setError(getErrorMessage(err));
                setIsSubmitting(false);
            }
        },
        [fromPath, login, navigate, password, username],
    );

    if (isLoading) {
        return <RouteLoadingState />;
    }

    if (isAuthenticated) {
        return <Navigate to={fromPath} replace />;
    }

    return (
        <div className="login-page" data-page="login">
            <div className="login-page__card">
                <div className="login-page__brand">
                    <Logo size={48} variant="solid" />
                    <h1 className="login-page__title">Magpie</h1>
                    <p className="login-page__subtitle">Sign in to continue</p>
                </div>

                <form className="login-page__form" onSubmit={handleSubmit} noValidate>
                    <div className="login-page__field">
                        <Label htmlFor="login-username" required>
                            Username
                        </Label>
                        <Input
                            id="login-username"
                            type="text"
                            value={username}
                            onChange={(event) => setUsername(event.target.value)}
                            autoComplete="username"
                            autoFocus
                            disabled={isSubmitting}
                            required
                            data-field="username"
                            aria-invalid={!!error}
                            aria-describedby={error ? 'login-error' : undefined}
                        />
                    </div>

                    <div className="login-page__field">
                        <Label htmlFor="login-password" required>
                            Password
                        </Label>
                        <Input
                            id="login-password"
                            type="password"
                            value={password}
                            onChange={(event) => setPassword(event.target.value)}
                            autoComplete="current-password"
                            disabled={isSubmitting}
                            required
                            data-field="password"
                            aria-invalid={!!error}
                            aria-describedby={error ? 'login-error' : undefined}
                        />
                    </div>

                    {error && (
                        <p id="login-error" className="login-page__error" role="alert" data-testid="login-error">
                            {error}
                        </p>
                    )}

                    <Button
                        type="submit"
                        variant="primary"
                        size="lg"
                        loading={isSubmitting}
                        className="login-page__submit"
                        data-action="sign-in"
                        aria-label="Sign in"
                    >
                        <SignInIcon weight="bold" aria-hidden="true" />
                        Sign in
                    </Button>
                </form>
            </div>
        </div>
    );
}

LoginPage.displayName = 'LoginPage';

export default LoginPage;
