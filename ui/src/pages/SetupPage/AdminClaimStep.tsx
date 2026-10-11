import { useCallback, useEffect, useRef, useState, type SubmitEvent, type ReactNode } from 'react';
import { UserPlusIcon } from '@phosphor-icons/react';

import { useAuth } from '@/auth';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { useCreateSetupAdmin, useSetupStatus } from '@/hooks/useSetup';
import { isApiError } from '@/lib/api';

interface AdminClaimStepProps {
    onAdvance: () => void | Promise<void>;
}

function getErrorMessage(error: unknown): string {
    if (isApiError(error)) {
        if (error.status === 409) {
            return 'The administrator account has already been claimed.';
        }
        return error.message || 'Failed to create the administrator account. Try again.';
    }

    if (error instanceof Error && error.message) {
        return error.message;
    }

    return 'Failed to create the administrator account. Try again.';
}

export function AdminClaimStep({ onAdvance }: AdminClaimStepProps): ReactNode {
    const statusQuery = useSetupStatus();
    const createAdmin = useCreateSetupAdmin();
    const { isAuthenticated, login } = useAuth();

    const [username, setUsername] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [isAdminCreated, setIsAdminCreated] = useState(false);
    const [isFinalizing, setIsFinalizing] = useState(false);
    const claimStartedRef = useRef(false);
    const signedInRef = useRef(false);

    const isSubmitting = createAdmin.isPending || isFinalizing;
    const requiresClaim = statusQuery.data?.requiresAdminClaim ?? true;

    const continueSetup = useCallback(
        async (loginUsername: string, loginPassword: string) => {
            setIsFinalizing(true);
            setError(null);
            try {
                if (!signedInRef.current) {
                    try {
                        await login(loginUsername, loginPassword);
                        signedInRef.current = true;
                    } catch {
                        setError(
                            'Administrator created, but sign-in failed. Select Continue setup to try signing in again.',
                        );
                        return;
                    }
                }

                await onAdvance();
            } finally {
                setIsFinalizing(false);
            }
        },
        [login, onAdvance],
    );

    useEffect(() => {
        if (!claimStartedRef.current && !statusQuery.isLoading && !requiresClaim && isAuthenticated) {
            claimStartedRef.current = true;
            signedInRef.current = true;
            setIsAdminCreated(true);
            void continueSetup('', '');
        }
    }, [statusQuery.isLoading, requiresClaim, isAuthenticated, continueSetup]);

    const handleSubmit = useCallback(
        async (event: SubmitEvent<HTMLFormElement>) => {
            event.preventDefault();

            const trimmedUsername = username.trim();
            if (!isAdminCreated) {
                if (!trimmedUsername || !password) {
                    setError('Please enter a username and password.');
                    return;
                }

                if (password !== confirmPassword) {
                    setError('Passwords do not match.');
                    return;
                }

                setError(null);
                claimStartedRef.current = true;
                try {
                    await createAdmin.mutateAsync({
                        username: trimmedUsername,
                        password,
                    });
                    setIsAdminCreated(true);
                } catch (err) {
                    setError(getErrorMessage(err));
                    return;
                }
            }

            await continueSetup(trimmedUsername, password);
        },
        [confirmPassword, continueSetup, createAdmin, isAdminCreated, password, username],
    );

    return (
        <form className="setup-page__form" onSubmit={handleSubmit} noValidate data-step="admin">
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
                    disabled={isSubmitting || isAdminCreated}
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
                    disabled={isSubmitting || isAdminCreated}
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
                    disabled={isSubmitting || isAdminCreated}
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
                <UserPlusIcon weight="bold" aria-hidden="true" />
                {isAdminCreated ? 'Continue setup' : 'Create administrator'}
            </Button>
        </form>
    );
}

AdminClaimStep.displayName = 'AdminClaimStep';
