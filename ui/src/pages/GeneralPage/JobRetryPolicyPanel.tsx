import { useCallback, useState } from 'react';
import { ArrowsClockwiseIcon, CircleNotchIcon, FloppyDiskIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { ExternalChangeConflictWarning, StaleSaveConfirmationDialog } from '@/components/ui/ExternalChangeConflict';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Skeleton } from '@/components/ui/Skeleton';
import { useSettingsGroupForm } from '@/hooks/useSettingsGroupForm';

const SETTINGS_GROUP = 'job-retry';

const DEFAULT_BASE_SECONDS = 10;
const DEFAULT_FACTOR = 2;
const DEFAULT_CAP_SECONDS = 1800;
const DEFAULT_MAX_ATTEMPTS = 5;

type JobRetryFormValues = {
    baseSeconds: string;
    factor: string;
    capSeconds: string;
    maxAttempts: string;
};

type JobRetryFormErrors = Partial<Record<keyof JobRetryFormValues, string>>;

const defaultValues: JobRetryFormValues = {
    baseSeconds: '',
    factor: '',
    capSeconds: '',
    maxAttempts: '',
};

function validateNumber(value: string, label: string): string | undefined {
    if (value.trim() === '') {
        return `${label} is required.`;
    }

    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
        return `${label} must be a number.`;
    }

    if (parsed < 1) {
        return `${label} must be at least 1.`;
    }

    return undefined;
}

function validateMaxAttempts(value: string): string | undefined {
    const numericError = validateNumber(value, 'Maximum attempts');
    if (numericError) {
        return numericError;
    }

    if (!Number.isInteger(Number(value))) {
        return 'Maximum attempts must be a whole number.';
    }

    return undefined;
}

function validate(values: JobRetryFormValues): JobRetryFormErrors {
    const errors: JobRetryFormErrors = {};
    errors.baseSeconds = validateNumber(values.baseSeconds, 'Base delay');
    errors.factor = validateNumber(values.factor, 'Backoff factor');
    errors.capSeconds = validateNumber(values.capSeconds, 'Maximum delay');
    errors.maxAttempts = validateMaxAttempts(values.maxAttempts);
    return errors;
}

function hasValidationErrors(errors: JobRetryFormErrors): boolean {
    return Object.values(errors).some(Boolean);
}

export function JobRetryPolicyPanel() {
    const [showValidationErrors, setShowValidationErrors] = useState(false);
    const [showStaleSaveConfirmation, setShowStaleSaveConfirmation] = useState(false);
    const {
        values,
        handleChange,
        hasChanges,
        isLoading,
        loadError,
        refetch,
        isUncreated,
        isUpdating,
        conflictState,
        save,
    } = useSettingsGroupForm<JobRetryFormValues>({
        settingsGroup: SETTINGS_GROUP,
        defaultValues,
        fromRecord: (record) => ({
            baseSeconds: record['base-seconds'] ?? '',
            factor: record['factor'] ?? '',
            capSeconds: record['cap-seconds'] ?? '',
            maxAttempts: record['max-attempts'] ?? '',
        }),
    });

    const validationErrors = validate(values);

    const saveSettings = useCallback(() => {
        void save(
            {
                'base-seconds': values.baseSeconds,
                factor: values.factor,
                'cap-seconds': values.capSeconds,
                'max-attempts': values.maxAttempts,
            },
            { successDescription: 'Job retry settings updated.' },
        );
    }, [values, save]);

    const handleSave = useCallback(() => {
        const errors = validate(values);
        if (hasValidationErrors(errors)) {
            setShowValidationErrors(true);
            return;
        }

        setShowValidationErrors(false);
        if (conflictState === 'stale') {
            setShowStaleSaveConfirmation(true);
            return;
        }
        saveSettings();
    }, [conflictState, saveSettings, values]);

    return (
        <section
            className="general-page__settings"
            aria-labelledby="job-retry-settings-title"
            data-component="job-retry-settings"
        >
            <header className="general-page__settings-header">
                <ArrowsClockwiseIcon weight="duotone" className="general-page__settings-icon" aria-hidden="true" />
                <h2 id="job-retry-settings-title" className="general-page__settings-title">
                    Job retry backoff
                </h2>
            </header>

            <div className="general-page__settings-content">
                {isLoading ? (
                    <div className="general-page__loading" data-testid="job-retry-loading">
                        <Skeleton width="100%" height="36px" />
                        <Skeleton width="100%" height="36px" />
                        <Skeleton width="100%" height="36px" />
                        <Skeleton width="100%" height="36px" />
                    </div>
                ) : loadError ? (
                    <Alert
                        variant="warning"
                        icon={<WarningCircleIcon weight="duotone" />}
                        data-testid="job-retry-error"
                    >
                        <p>Failed to load retry settings.</p>
                        <Button variant="secondary" size="sm" onClick={() => refetch()}>
                            Retry
                        </Button>
                    </Alert>
                ) : (
                    <>
                        {isUncreated && (
                            <div className="general-page__info" data-testid="job-retry-info">
                                <p className="general-page__info-text">
                                    Retry settings are created when first saved. They apply only to jobs configured to
                                    retry after failure. Jobs configured to fail or return to the queue are unaffected.
                                </p>
                            </div>
                        )}

                        {conflictState && (
                            <ExternalChangeConflictWarning
                                state={conflictState}
                                entityLabel="Settings"
                                id="job-retry-settings-external-change"
                            />
                        )}

                        <div className="general-page__settings-row">
                            <div className="general-page__settings-field">
                                <Label htmlFor="job-retry-base-seconds">Base delay (seconds)</Label>
                                <p className="general-page__settings-hint">
                                    Initial wait before the first retry. Default {DEFAULT_BASE_SECONDS}.
                                </p>
                            </div>
                            <Input
                                id="job-retry-base-seconds"
                                type="number"
                                inputMode="numeric"
                                min={1}
                                step="0.5"
                                required
                                value={values.baseSeconds}
                                onChange={(e) => handleChange('baseSeconds', e.target.value)}
                                placeholder={String(DEFAULT_BASE_SECONDS)}
                                className="general-page__settings-input"
                                data-setting="base-seconds"
                                error={showValidationErrors && !!validationErrors.baseSeconds}
                                errorMessage={showValidationErrors ? validationErrors.baseSeconds : undefined}
                            />
                        </div>

                        <div className="general-page__settings-row">
                            <div className="general-page__settings-field">
                                <Label htmlFor="job-retry-factor">Backoff factor</Label>
                                <p className="general-page__settings-hint">
                                    Multiplier applied between attempts. With base 10 and factor 2, attempts wait 10s,
                                    20s, 40s, ... up to the cap. Default {DEFAULT_FACTOR}.
                                </p>
                            </div>
                            <Input
                                id="job-retry-factor"
                                type="number"
                                inputMode="decimal"
                                min={1}
                                step="0.1"
                                required
                                value={values.factor}
                                onChange={(e) => handleChange('factor', e.target.value)}
                                placeholder={String(DEFAULT_FACTOR)}
                                className="general-page__settings-input"
                                data-setting="factor"
                                error={showValidationErrors && !!validationErrors.factor}
                                errorMessage={showValidationErrors ? validationErrors.factor : undefined}
                            />
                        </div>

                        <div className="general-page__settings-row">
                            <div className="general-page__settings-field">
                                <Label htmlFor="job-retry-cap-seconds">Maximum delay (seconds)</Label>
                                <p className="general-page__settings-hint">
                                    Maximum wait between retries. Default: {DEFAULT_CAP_SECONDS} seconds.
                                </p>
                            </div>
                            <Input
                                id="job-retry-cap-seconds"
                                type="number"
                                inputMode="numeric"
                                min={1}
                                required
                                value={values.capSeconds}
                                onChange={(e) => handleChange('capSeconds', e.target.value)}
                                placeholder={String(DEFAULT_CAP_SECONDS)}
                                className="general-page__settings-input"
                                data-setting="cap-seconds"
                                error={showValidationErrors && !!validationErrors.capSeconds}
                                errorMessage={showValidationErrors ? validationErrors.capSeconds : undefined}
                            />
                        </div>

                        <div className="general-page__settings-row">
                            <div className="general-page__settings-field">
                                <Label htmlFor="job-retry-max-attempts">Maximum attempts</Label>
                                <p className="general-page__settings-hint">
                                    Stop retrying once a job's attempt count exceeds this. Default{' '}
                                    {DEFAULT_MAX_ATTEMPTS}.
                                </p>
                            </div>
                            <Input
                                id="job-retry-max-attempts"
                                type="number"
                                inputMode="numeric"
                                min={1}
                                step={1}
                                required
                                value={values.maxAttempts}
                                onChange={(e) => handleChange('maxAttempts', e.target.value)}
                                placeholder={String(DEFAULT_MAX_ATTEMPTS)}
                                className="general-page__settings-input"
                                data-setting="max-attempts"
                                error={showValidationErrors && !!validationErrors.maxAttempts}
                                errorMessage={showValidationErrors ? validationErrors.maxAttempts : undefined}
                            />
                        </div>

                        <div className="general-page__settings-actions">
                            <Button
                                variant="primary"
                                size="sm"
                                onClick={handleSave}
                                disabled={!hasChanges || isUpdating}
                                data-action="save-job-retry"
                            >
                                {isUpdating ? (
                                    <>
                                        <CircleNotchIcon
                                            weight="bold"
                                            className="general-page__spinner"
                                            aria-hidden="true"
                                        />
                                        Saving...
                                    </>
                                ) : (
                                    <>
                                        <FloppyDiskIcon weight="bold" aria-hidden="true" />
                                        Save Settings
                                    </>
                                )}
                            </Button>
                        </div>
                    </>
                )}
            </div>
            <StaleSaveConfirmationDialog
                open={showStaleSaveConfirmation}
                entityLabel="settings"
                saving={isUpdating}
                onCancel={() => setShowStaleSaveConfirmation(false)}
                onConfirm={() => {
                    setShowStaleSaveConfirmation(false);
                    saveSettings();
                }}
            />
        </section>
    );
}

JobRetryPolicyPanel.displayName = 'JobRetryPolicyPanel';

export default JobRetryPolicyPanel;
