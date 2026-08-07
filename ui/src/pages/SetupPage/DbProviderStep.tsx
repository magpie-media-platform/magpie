import { useCallback, useEffect, useState, type SubmitEvent, type ReactNode } from 'react';
import { ArrowClockwiseIcon, ArrowRightIcon, DatabaseIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { isApiError } from '@/lib/api';
import { useConfigureSetupDatabase, useSetupDatabaseStatus } from '@/hooks/useSetup';

interface DbProviderStepProps {
    onAdvance: () => void;
}

interface ProviderOption {
    value: string;
    label: string;
    needsConnectionString: boolean;
    connectionHint?: string;
}

const PROVIDER_OPTIONS: readonly ProviderOption[] = [
    {
        value: 'sqlite',
        label: 'SQLite (local file, default)',
        needsConnectionString: false,
        connectionHint:
            'Leave blank to use the default file under the Magpie data directory. Provide a full SQLite connection string only to relocate the database.',
    },
    {
        value: 'postgresql',
        label: 'PostgreSQL',
        needsConnectionString: true,
        connectionHint: 'Example: Host=localhost;Database=magpie;Username=magpie;Password=...',
    },
    {
        value: 'sqlserver',
        label: 'SQL Server',
        needsConnectionString: true,
        connectionHint: 'Example: Server=.\\SQLEXPRESS;Database=Magpie;Trusted_Connection=true;',
    },
] as const;

function getErrorMessage(error: unknown): string {
    if (isApiError(error)) {
        return error.message || 'Failed to save the database configuration. Try again.';
    }
    if (error instanceof Error && error.message) {
        return error.message;
    }
    return 'Failed to save the database configuration. Try again.';
}

export function DbProviderStep({ onAdvance }: DbProviderStepProps): ReactNode {
    const statusQuery = useSetupDatabaseStatus();
    const configure = useConfigureSetupDatabase();

    const [providerValue, setProviderValue] = useState<string>('sqlite');
    const [connectionString, setConnectionString] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [awaitingRestart, setAwaitingRestart] = useState(false);

    const isSubmitting = configure.isPending;
    const status = statusQuery.data ?? null;
    const isInitialized = status?.initialized === true;

    useEffect(() => {
        if (status?.provider) {
            const matching = PROVIDER_OPTIONS.find((opt) => opt.value === status.provider.toLowerCase());
            if (matching) {
                setProviderValue(matching.value);
            }
        }
    }, [status?.provider]);

    useEffect(() => {
        if (!statusQuery.isLoading && isInitialized && !awaitingRestart) {
            onAdvance();
        }
    }, [statusQuery.isLoading, isInitialized, awaitingRestart, onAdvance]);

    const selectedProvider = PROVIDER_OPTIONS.find((opt) => opt.value === providerValue)!;

    const handleSubmit = useCallback(
        async (event: SubmitEvent<HTMLFormElement>) => {
            event.preventDefault();
            setError(null);

            const trimmedConnection = connectionString.trim();

            if (selectedProvider.needsConnectionString && !trimmedConnection) {
                setError(`A connection string is required for the ${selectedProvider.label} provider.`);
                return;
            }

            try {
                await configure.mutateAsync({
                    provider: selectedProvider.value,
                    connectionString: trimmedConnection || null,
                });
                setAwaitingRestart(true);
            } catch (err) {
                setError(getErrorMessage(err));
            }
        },
        [configure, connectionString, selectedProvider],
    );

    const handleRecheck = useCallback(async () => {
        const result = await statusQuery.refetch();
        if (result.data?.initialized) {
            onAdvance();
        }
    }, [onAdvance, statusQuery]);

    if (statusQuery.isLoading) {
        return (
            <div className="setup-page__form" data-step-loading="db">
                <p className="setup-page__subtitle">Checking database status...</p>
            </div>
        );
    }

    if (awaitingRestart) {
        return (
            <div className="setup-page__form" data-step="db" data-substep="restart-required">
                <p className="setup-page__subtitle">
                    Saved the {selectedProvider.label} configuration to <code>config/appsettings.json</code>. Restart
                    Magpie so the new database provider takes effect, then return to this page.
                </p>
                <Button
                    type="button"
                    variant="primary"
                    size="lg"
                    onClick={handleRecheck}
                    loading={statusQuery.isFetching}
                    className="setup-page__submit"
                    data-action="recheck-db"
                >
                    <ArrowClockwiseIcon weight="bold" aria-hidden="true" />I have restarted Magpie
                </Button>
            </div>
        );
    }

    return (
        <form className="setup-page__form" onSubmit={handleSubmit} noValidate data-step="db">
            <fieldset className="setup-page__field" data-field="provider">
                <legend className="setup-page__legend">
                    Database provider
                    <span aria-hidden="true"> *</span>
                </legend>
                {PROVIDER_OPTIONS.map((opt) => (
                    <label key={opt.value} className="setup-page__radio-row">
                        <input
                            type="radio"
                            name="db-provider"
                            value={opt.value}
                            checked={providerValue === opt.value}
                            disabled={isSubmitting}
                            onChange={(event) => setProviderValue(event.target.value)}
                            data-provider={opt.value}
                        />
                        <span>{opt.label}</span>
                    </label>
                ))}
            </fieldset>

            <div className="setup-page__field">
                <Label htmlFor="setup-db-conn" required={selectedProvider.needsConnectionString}>
                    Connection string
                </Label>
                <Input
                    id="setup-db-conn"
                    type="text"
                    value={connectionString}
                    onChange={(event) => setConnectionString(event.target.value)}
                    disabled={isSubmitting}
                    data-field="connection-string"
                    placeholder={selectedProvider.connectionHint}
                    aria-describedby="setup-db-conn-hint"
                />
                <p id="setup-db-conn-hint" className="setup-page__hint">
                    {selectedProvider.connectionHint}
                </p>
            </div>

            {error && (
                <p id="setup-db-error" className="setup-page__error" role="alert" data-testid="setup-error">
                    {error}
                </p>
            )}

            <Button
                type="submit"
                variant="primary"
                size="lg"
                loading={isSubmitting}
                className="setup-page__submit"
                data-action="save-db"
            >
                <DatabaseIcon weight="bold" aria-hidden="true" />
                Save and continue
                <ArrowRightIcon weight="bold" aria-hidden="true" />
            </Button>
        </form>
    );
}

DbProviderStep.displayName = 'DbProviderStep';
