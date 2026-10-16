import { useCallback, useEffect, useMemo, useState, type SubmitEvent, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowClockwiseIcon, ArrowRightIcon, DatabaseIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { getErrorMessage } from '@/lib/errors';
import { useConfigureSetupDatabase, useSetupDatabaseStatus } from '@/hooks/useSetup';
import { useInstanceName } from '@/hooks/useInstanceName';
import { setupStatusQueryKey, type NodeRole } from '@/lib/setupApi';

interface DbProviderStepProps {
    onAdvance: () => void;
    join?: boolean;
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

const ROLE_OPTIONS: readonly { value: NodeRole; label: string }[] = [
    { value: 'Ui', label: 'UI' },
    { value: 'Api', label: 'API' },
    { value: 'JobRunner', label: 'Job runner' },
] as const;

export function DbProviderStep({ onAdvance, join = false }: DbProviderStepProps): ReactNode {
    const instanceName = useInstanceName();
    const queryClient = useQueryClient();
    const statusQuery = useSetupDatabaseStatus();
    const configure = useConfigureSetupDatabase();

    const [providerValue, setProviderValue] = useState<string>(join ? 'postgresql' : 'sqlite');
    const [connectionString, setConnectionString] = useState('');
    const [roles, setRoles] = useState<NodeRole[]>(ROLE_OPTIONS.map((role) => role.value));
    const [excludedJobTypes, setExcludedJobTypes] = useState<string[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [awaitingRestart, setAwaitingRestart] = useState(false);

    const isSubmitting = configure.isPending;
    const status = statusQuery.data ?? null;
    const isInitialized = status?.initialized === true;
    const providerOptions = join ? PROVIDER_OPTIONS.filter((opt) => opt.needsConnectionString) : PROVIDER_OPTIONS;
    const jobTypeOptions = useMemo(
        () =>
            Object.entries(status?.jobTypes ?? {})
                .map(([jobType, displayName]) => ({ jobType, displayName }))
                .sort((a, b) => a.displayName.localeCompare(b.displayName)),
        [status?.jobTypes],
    );
    const runsJobs = roles.includes('JobRunner');
    const runsEveryJobType = excludedJobTypes.length === 0;
    const runsNoJobType = jobTypeOptions.length > 0 && excludedJobTypes.length === jobTypeOptions.length;

    useEffect(() => {
        if (status?.provider && !join) {
            const matching = PROVIDER_OPTIONS.find((opt) => opt.value === status.provider.toLowerCase());
            if (matching) {
                setProviderValue(matching.value);
            }
        }
    }, [status?.provider, join]);

    useEffect(() => {
        if (!statusQuery.isLoading && isInitialized && !awaitingRestart && !join) {
            onAdvance();
        }
    }, [statusQuery.isLoading, isInitialized, awaitingRestart, join, onAdvance]);

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
                    ...(join && {
                        roles,
                        jobTypes:
                            runsJobs && !runsEveryJobType
                                ? jobTypeOptions
                                      .map((option) => option.jobType)
                                      .filter((type) => !excludedJobTypes.includes(type))
                                : [],
                    }),
                });
                setAwaitingRestart(true);
            } catch (err) {
                setError(getErrorMessage(err, 'Failed to save the database configuration. Try again.'));
            }
        },
        [
            configure,
            connectionString,
            selectedProvider,
            join,
            roles,
            runsJobs,
            runsEveryJobType,
            excludedJobTypes,
            jobTypeOptions,
        ],
    );

    const handleRecheck = useCallback(async () => {
        if (join) {
            await queryClient.refetchQueries({ queryKey: setupStatusQueryKey });
            return;
        }
        const result = await statusQuery.refetch();
        if (result.data?.initialized) {
            onAdvance();
        }
    }, [join, onAdvance, queryClient, statusQuery]);

    const toggleRole = (role: NodeRole, checked: boolean) =>
        setRoles((current) => (checked ? [...current, role] : current.filter((value) => value !== role)));

    const toggleJobType = (jobType: string, checked: boolean) =>
        setExcludedJobTypes((current) =>
            checked ? current.filter((value) => value !== jobType) : [...current, jobType],
        );

    if (statusQuery.isLoading) {
        return (
            <div className="setup-page__form" data-step-loading="db">
                <p className="setup-page__subtitle">Checking database status...</p>
            </div>
        );
    }

    const servesUi = !join || roles.includes('Ui');

    if (awaitingRestart) {
        return (
            <div className="setup-page__form" data-step="db" data-substep="restart-required">
                <p className="setup-page__subtitle">
                    Saved the {selectedProvider.label} configuration to <code>config/appsettings.json</code>. Restart{' '}
                    {instanceName} so the new database provider takes effect
                    {servesUi ? ', then return to this page' : ''}.
                </p>
                {servesUi && (
                    <Button
                        type="button"
                        variant="primary"
                        size="lg"
                        onClick={handleRecheck}
                        loading={statusQuery.isFetching}
                        className="setup-page__submit"
                        data-action="recheck-db"
                    >
                        <ArrowClockwiseIcon weight="bold" aria-hidden="true" />I have restarted {instanceName}
                    </Button>
                )}
            </div>
        );
    }

    return (
        <form className="setup-page__form" onSubmit={handleSubmit} noValidate data-step={join ? 'join' : 'db'}>
            <fieldset className="setup-page__field" data-field="provider">
                <legend className="setup-page__legend">
                    Database provider
                    <span aria-hidden="true"> *</span>
                </legend>
                {providerOptions.map((opt) => (
                    <label key={opt.value} className="setup-page__option-row">
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

            {join && (
                <fieldset className="setup-page__field" data-field="roles">
                    <legend className="setup-page__legend">Roles</legend>
                    {ROLE_OPTIONS.map((role) => (
                        <label key={role.value} className="setup-page__option-row">
                            <input
                                type="checkbox"
                                checked={roles.includes(role.value)}
                                disabled={isSubmitting}
                                onChange={(event) => toggleRole(role.value, event.target.checked)}
                                data-role={role.value}
                            />
                            <span>{role.label}</span>
                        </label>
                    ))}
                </fieldset>
            )}

            {join && runsJobs && jobTypeOptions.length > 0 && (
                <details className="setup-page__field" data-field="job-types">
                    <summary className="setup-page__legend">Job types</summary>
                    <label className="setup-page__option-row">
                        <input
                            type="checkbox"
                            checked={runsEveryJobType}
                            ref={(input) => {
                                if (input) {
                                    input.indeterminate = !runsEveryJobType && !runsNoJobType;
                                }
                            }}
                            disabled={isSubmitting}
                            onChange={(event) =>
                                setExcludedJobTypes(
                                    event.target.checked ? [] : jobTypeOptions.map((option) => option.jobType),
                                )
                            }
                            data-job-type="all"
                        />
                        <span>All job types</span>
                    </label>
                    {jobTypeOptions.map((option) => (
                        <label key={option.jobType} className="setup-page__option-row">
                            <input
                                type="checkbox"
                                checked={!excludedJobTypes.includes(option.jobType)}
                                disabled={isSubmitting}
                                onChange={(event) => toggleJobType(option.jobType, event.target.checked)}
                                data-job-type={option.jobType}
                            />
                            <span>{option.displayName}</span>
                        </label>
                    ))}
                </details>
            )}

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
                disabled={join && (roles.length === 0 || (runsJobs && runsNoJobType))}
                className="setup-page__submit"
                data-action="save-db"
            >
                <DatabaseIcon weight="bold" aria-hidden="true" />
                {servesUi ? 'Save and continue' : 'Save'}
                {servesUi && <ArrowRightIcon weight="bold" aria-hidden="true" />}
            </Button>
        </form>
    );
}

DbProviderStep.displayName = 'DbProviderStep';
