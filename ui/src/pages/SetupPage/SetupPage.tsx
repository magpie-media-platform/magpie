import { useCallback, useState, type FormEvent, type ReactNode } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { UserPlus } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Logo } from '@/components/ui/Logo';
import { useAuth } from '@/auth';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useSetupStatus, useCreateSetupAdmin } from '@/hooks/useSetup';
import { isApiError } from '@/lib/api';
import { RouteLoadingState } from '@/router/RouteLoadingState';

import './SetupPage.css';

function getErrorMessage(error: unknown): string {
    if (isApiError(error)) {
        if (error.status === 409) {
            return 'Setup has already been completed.';
        }
        return error.message || 'Could not create the admin account. Please try again.';
    }

    if (error instanceof Error && error.message) {
        return error.message;
    }

    return 'Could not create the admin account. Please try again.';
}

export function SetupPage(): ReactNode {
    useDocumentTitle('Set Up Magpie');

    const { isAuthenticated } = useAuth();
    const navigate = useNavigate();

    const statusQuery = useSetupStatus();
    const createAdmin = useCreateSetupAdmin();

    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [error, setError] = useState<string | null>(null);

    const isSubmitting = createAdmin.isPending;

    const handleSubmit = useCallback(
        async (event: FormEvent<HTMLFormElement>) => {
            event.preventDefault();

            const trimmedUsername = username.trim();
            if (!trimmedUsername || !password) {
                setError('Please enter a username and password.');
                return;
            }

            if (password !== confirmPassword) {
                setError('Passwords do not match.');
                return;
            }

            setError(null);
            try {
                await createAdmin.mutateAsync({
                    username: trimmedUsername,
                    password,
                });
                navigate('/login', { replace: true });
            } catch (err) {
                setError(getErrorMessage(err));
            }
        },
        [confirmPassword, createAdmin, navigate, password, username],
    );

    if (statusQuery.isLoading) {
        return <RouteLoadingState />;
    }

    // If setup is already complete, send the user away from this page.
    if (statusQuery.data && !statusQuery.data.isRequired) {
        return <Navigate to={isAuthenticated ? '/' : '/login'} replace />;
    }

    return (
        <div className="setup-page" data-page="setup">
            <div className="setup-page__card">
                <div className="setup-page__brand">
                    <Logo size={48} variant="solid" />
                    <h1 className="setup-page__title">Welcome to Magpie</h1>
                    <p className="setup-page__subtitle">Create the first administrator account to get started.</p>
                </div>

                <form className="setup-page__form" onSubmit={handleSubmit} noValidate>
                    <div className="setup-page__field">
                        <Label htmlFor="setup-username" required>
                            Username
                        </Label>
                        <Input
                            id="setup-username"
                            type="text"
                            value={username}
                            onChange={(event) => setUsername(event.target.value)}
                            autoComplete="username"
                            autoFocus
                            disabled={isSubmitting}
                            required
                            data-field="username"
                            aria-describedby={error ? 'setup-error' : undefined}
                        />
                    </div>

                    <div className="setup-page__field">
                        <Label htmlFor="setup-password" required>
                            Password
                        </Label>
                        <Input
                            id="setup-password"
                            type="password"
                            value={password}
                            onChange={(event) => setPassword(event.target.value)}
                            autoComplete="new-password"
                            disabled={isSubmitting}
                            required
                            data-field="password"
                            aria-describedby={error ? 'setup-error' : undefined}
                        />
                    </div>

                    <div className="setup-page__field">
                        <Label htmlFor="setup-confirm-password" required>
                            Confirm password
                        </Label>
                        <Input
                            id="setup-confirm-password"
                            type="password"
                            value={confirmPassword}
                            onChange={(event) => setConfirmPassword(event.target.value)}
                            autoComplete="new-password"
                            disabled={isSubmitting}
                            required
                            data-field="confirm-password"
                            aria-describedby={error ? 'setup-error' : undefined}
                        />
                    </div>

                    {error && (
                        <p id="setup-error" className="setup-page__error" role="alert" data-testid="setup-error">
                            {error}
                        </p>
                    )}

                    <Button
                        type="submit"
                        variant="primary"
                        size="lg"
                        loading={isSubmitting}
                        className="setup-page__submit"
                        data-action="create-admin"
                    >
                        <UserPlus weight="bold" aria-hidden="true" />
                        Create administrator
                    </Button>
                </form>
            </div>
        </div>
    );
}

SetupPage.displayName = 'SetupPage';

export default SetupPage;
