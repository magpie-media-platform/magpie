import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { ArrowRightIcon, FloppyDiskIcon, SkipForwardIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { useToast } from '@/components/ui/Toast';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm/IntegrationSettingsForm';
import { apiGet, isApiError } from '@/lib/api';
import {
    settingsQueryKeys,
    settingsToRecord,
    useUpdateExtensionSettingsGroup,
    type ApiSettingsGroup,
} from '@/hooks/useSettings';

interface MetadataProviderStepProps {
    onAdvance: (skipped?: boolean) => void;
}

export function MetadataProviderStep({ onAdvance }: MetadataProviderStepProps): ReactNode {
    const groupsQuery = useQuery({
        queryKey: settingsQueryKeys.all,
        queryFn: () => apiGet<ApiSettingsGroup[]>('/settings/groups'),
        retry: false,
    });

    const extensionGroups = useMemo(() => {
        const all = groupsQuery.data ?? [];
        return all
            .filter((g) => g.extensionId)
            .filter((g) => (g.schema?.length ?? 0) > 0)
            .sort((a, b) => a.name.localeCompare(b.name));
    }, [groupsQuery.data]);

    const unauthenticated = isApiError(groupsQuery.error) && groupsQuery.error.status === 401;
    const isLoading = groupsQuery.isLoading;

    return (
        <div className="setup-page__form" data-step="metadata">
            <p className="setup-page__hint">
                API keys for installed metadata extensions can be added now, or configured later under Admin {'>'}{' '}
                Extensions.
            </p>

            {isLoading && <p className="setup-page__subtitle">Loading installed extensions...</p>}

            {!isLoading && unauthenticated && (
                <p className="setup-page__hint" data-state="unauthenticated">
                    You are not signed in yet. This step can be skipped, then metadata providers can be configured under
                    Admin {'>'} Extensions after signing in.
                </p>
            )}

            {!isLoading && !unauthenticated && extensionGroups.length === 0 && (
                <p className="setup-page__hint" data-state="empty">
                    No installed extensions need configuration. Continue to the next step.
                </p>
            )}

            {!isLoading && !unauthenticated && extensionGroups.length > 0 && (
                <div className="setup-page__metadata-groups">
                    {extensionGroups.map((group) => (
                        <MetadataGroupCard key={groupKey(group)} group={group} />
                    ))}
                </div>
            )}

            <Button
                type="button"
                variant="secondary"
                size="lg"
                onClick={() => onAdvance()}
                className="setup-page__submit"
                data-action="continue-metadata"
            >
                Continue
                <ArrowRightIcon weight="bold" aria-hidden="true" />
            </Button>
            <Button
                type="button"
                variant="secondary"
                size="lg"
                onClick={() => onAdvance(true)}
                className="setup-page__submit"
                data-action="skip-metadata"
            >
                <SkipForwardIcon weight="bold" aria-hidden="true" />
                Skip for now
                <ArrowRightIcon weight="bold" aria-hidden="true" />
            </Button>
        </div>
    );
}

MetadataProviderStep.displayName = 'MetadataProviderStep';

// The settings-group response identifies a group by its extension and name; it carries no id.
function groupKey(group: ApiSettingsGroup): string {
    return `${group.extensionId ?? 'system'}-${group.name}`.replace(/[^A-Za-z0-9_-]/g, '-');
}

interface MetadataGroupCardProps {
    group: ApiSettingsGroup;
}

function MetadataGroupCard({ group }: MetadataGroupCardProps): ReactNode {
    const { toast } = useToast();
    const schema = useMemo(() => group.schema ?? [], [group.schema]);
    const initial = useMemo(() => {
        const values = settingsToRecord(group.settings);
        for (const setting of schema) {
            if (!(setting.key in values) && !setting.secret && setting.defaultValue != null) {
                values[setting.key] = setting.defaultValue;
            }
        }
        return values;
    }, [group.settings, schema]);
    const [values, setValues] = useState<Record<string, string>>(initial);
    const update = useUpdateExtensionSettingsGroup(group.extensionId ?? '', group.name);
    const secretsConfigured = useMemo(() => {
        const result: Record<string, boolean> = {};
        for (const [key, state] of Object.entries(group.secretSettings ?? {})) {
            result[key] = state.isSet;
        }
        return result;
    }, [group.secretSettings]);
    const handleChange = useCallback((key: string, value: string) => {
        setValues((prev) => ({ ...prev, [key]: value }));
    }, []);

    const handleSave = useCallback(async () => {
        const existingKeys = new Set(group.settings.map((s) => s.key));
        try {
            await update.updateSettings({ values, existingKeys });
            toast({
                title: `Saved ${group.name}`,
                description: 'Settings saved.',
                variant: 'success',
            });
        } catch (err) {
            const message = err instanceof Error ? err.message : 'Failed to save settings.';
            toast({ title: `Failed to save ${group.name}`, description: message, variant: 'error' });
        }
    }, [group.name, group.settings, toast, update, values]);

    return (
        <section className="setup-page__metadata-group" data-group={group.name} data-extension-id={group.extensionId}>
            <h2 className="setup-page__legend">{group.name}</h2>
            <IntegrationSettingsForm
                schema={schema}
                values={values}
                onChange={handleChange}
                secretsConfigured={secretsConfigured}
                extensionId={group.extensionId ?? null}
                idPrefix={`metadata-${groupKey(group)}`}
                disabled={update.isUpdating}
            />
            <Button
                type="button"
                variant="primary"
                size="md"
                onClick={handleSave}
                loading={update.isUpdating}
                data-action="save-metadata-group"
            >
                <FloppyDiskIcon weight="bold" aria-hidden="true" />
                Save
            </Button>
        </section>
    );
}
