import { useState, useEffect, useCallback, useMemo, useRef, type ReactNode } from 'react';
import { FloppyDiskIcon, FlaskIcon, XIcon } from '@phosphor-icons/react';

import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription, SheetFooter } from '@/components/ui/Sheet';
import { IntegrationSettingsForm } from '@/components/integrations/IntegrationSettingsForm/IntegrationSettingsForm';
import {
    omitUnchangedSecretSettings,
    secretsConfiguredForEdit,
    useIndexerInstanceSchema,
} from '@/hooks/useProviderSchema';
import { useToast } from '@/components/ui/Toast';
import { foldDefaultsIntoBaseline } from '@/hooks/useFormDirty';
import { isAbortError } from '@/lib/api';
import { getErrorMessage } from '@/lib/errors';
import { buildSettingsPatch } from '@/lib/integrationSettings';
import { useIndexerConnectionMutations, type ApiIndexerConnection } from '@/hooks/useIndexers';

import './IndexerFeedForm.css';

export interface ConfigureInstanceTarget {
    id: string;
    name: string;
    providerType: string;
}

export interface ConfigureConnectionTarget {
    indexerId: string;
    providerType: string;
    connection: ApiIndexerConnection;
}

interface ConfigureInstanceDialogProps {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    indexer?: ConfigureInstanceTarget | null;
    editTarget?: ConfigureConnectionTarget | null;
    onSaved?: () => void;
}

export function ConfigureInstanceDialog({
    open,
    onOpenChange,
    indexer,
    editTarget,
    onSaved,
}: ConfigureInstanceDialogProps): ReactNode {
    const { toast } = useToast();
    const { createConnection, updateConnection, testSettings, cancelTestSettings, isTestingSettings } =
        useIndexerConnectionMutations();
    const testClosedRef = useRef(false);

    const isEditing = !!editTarget;
    const indexerId = editTarget?.indexerId ?? indexer?.id ?? null;
    const providerType = editTarget?.providerType ?? indexer?.providerType;
    const definitionName = editTarget?.connection.name ?? indexer?.name ?? '';

    const [name, setName] = useState('');
    const [settings, setSettings] = useState<Record<string, string>>({});
    const [submitting, setSubmitting] = useState(false);
    const baselineRef = useRef<Record<string, string>>({});

    const schemaQuery = useIndexerInstanceSchema(open && indexerId ? indexerId : null);
    const schema = useMemo(() => schemaQuery.data ?? [], [schemaQuery.data]);
    const secretsConfigured = useMemo(() => secretsConfiguredForEdit(schema, isEditing), [schema, isEditing]);
    const openedKeyRef = useRef<string | null>(null);
    const seededSchemaRef = useRef(false);

    useEffect(() => {
        if (!open) {
            openedKeyRef.current = null;
            return;
        }
        const key = editTarget ? `edit:${editTarget.connection.id}` : `add:${indexer?.id ?? ''}`;
        if (openedKeyRef.current === key) {
            return;
        }
        openedKeyRef.current = key;
        seededSchemaRef.current = false;

        if (editTarget) {
            const seeded = { ...(editTarget.connection.settings ?? {}) };
            setName(editTarget.connection.name);
            setSettings(seeded);
            baselineRef.current = seeded;
        } else {
            setName(indexer?.name ?? '');
            setSettings({});
            baselineRef.current = {};
        }
    }, [open, editTarget, indexer]);

    useEffect(() => {
        if (!open || editTarget || seededSchemaRef.current || schema.length === 0) {
            return;
        }
        seededSchemaRef.current = true;
        setSettings((prev) => {
            const folded = foldDefaultsIntoBaseline(prev, baselineRef.current, schema);
            baselineRef.current = folded.baseline;
            return folded.changed ? folded.next : prev;
        });
    }, [open, editTarget, schema]);

    useEffect(() => {
        if (open) {
            return;
        }
        testClosedRef.current = true;
        cancelTestSettings();
    }, [open, cancelTestSettings]);

    useEffect(() => () => cancelTestSettings(), [cancelTestSettings]);

    const handleChange = useCallback((key: string, value: string) => {
        setSettings((prev) => ({ ...prev, [key]: value }));
    }, []);

    const handleTest = useCallback(async () => {
        if (!indexerId && !providerType) {
            return;
        }
        const submittedSettings = omitUnchangedSecretSettings(settings, schema);
        testClosedRef.current = false;
        try {
            const result = await testSettings({
                id: indexerId ?? undefined,
                providerType,
                settings: submittedSettings,
            });
            if (testClosedRef.current) {
                return;
            }
            toast({
                title: definitionName || 'Indexer',
                description: result.message,
                variant: result.success ? 'success' : 'error',
            });
        } catch (err) {
            if (testClosedRef.current || isAbortError(err)) {
                return;
            }
            toast({
                title: definitionName || 'Indexer',
                description: getErrorMessage(err, 'Failed to test connection'),
                variant: 'error',
            });
        }
    }, [indexerId, providerType, settings, schema, testSettings, toast, definitionName]);

    const handleSubmit = useCallback(
        async (e: React.SubmitEvent) => {
            e.preventDefault();
            if (!indexerId) {
                return;
            }
            setSubmitting(true);
            try {
                const submittedSettings = omitUnchangedSecretSettings(settings, schema);
                if (editTarget) {
                    const existingSettings = omitUnchangedSecretSettings(editTarget.connection.settings ?? {}, schema);
                    await updateConnection(editTarget.indexerId, editTarget.connection.id, {
                        name: name.trim() ? { value: name.trim() } : undefined,
                        settings: buildSettingsPatch(submittedSettings, existingSettings),
                    });
                    toast({
                        title: name || definitionName,
                        description: 'Indexer instance updated.',
                        variant: 'success',
                    });
                    onSaved?.();
                    onOpenChange(false);
                } else {
                    await createConnection(indexerId, {
                        name: name.trim() || definitionName,
                        enabled: true,
                        settings: submittedSettings,
                    });
                    toast({
                        title: name || definitionName,
                        description: 'Indexer instance created and enabled.',
                        variant: 'success',
                    });
                    onSaved?.();
                    onOpenChange(false);
                }
            } catch (err) {
                toast({
                    title: definitionName || 'Indexer',
                    description: getErrorMessage(err, 'Failed to save instance'),
                    variant: 'error',
                });
            } finally {
                setSubmitting(false);
            }
        },
        [
            indexerId,
            editTarget,
            name,
            settings,
            schema,
            definitionName,
            createConnection,
            updateConnection,
            onSaved,
            onOpenChange,
            toast,
        ],
    );

    const busy = submitting || isTestingSettings;
    const action = isEditing ? 'Save' : 'Add';
    const outcome = isEditing ? 'update it for searching' : 'enable it for searching';

    return (
        <Sheet open={open} onOpenChange={onOpenChange}>
            <SheetContent
                side="right"
                className="indexer-feed-form__sheet indexer-feed-form__sheet--wide"
                data-component="configure-instance"
            >
                <SheetHeader>
                    <SheetTitle>{isEditing ? 'Edit indexer instance' : 'Add indexer instance'}</SheetTitle>
                    <SheetDescription>
                        {isEditing
                            ? `Update the "${definitionName}" connection.`
                            : definitionName
                              ? `Configure and enable "${definitionName}" for searching.`
                              : ''}
                    </SheetDescription>
                </SheetHeader>

                <form onSubmit={handleSubmit} className="indexer-feed-form">
                    <div className="indexer-feed-form__body">
                        <div className="indexer-feed-form__field">
                            <Label htmlFor="instance-name">Name</Label>
                            <Input
                                id="instance-name"
                                value={name}
                                onChange={(e) => setName(e.target.value)}
                                placeholder={definitionName}
                                disabled={busy}
                                data-field="instance-name"
                            />
                        </div>

                        {schema.length > 0 ? (
                            <div className="indexer-feed-form__field">
                                <IntegrationSettingsForm
                                    schema={schema}
                                    values={settings}
                                    onChange={handleChange}
                                    secretsConfigured={secretsConfigured}
                                    disabled={busy}
                                    idPrefix="instance-setting"
                                />
                            </div>
                        ) : (
                            <p className="indexer-feed-form__help">
                                This indexer needs no additional configuration. Select {action} to {outcome}.
                            </p>
                        )}
                    </div>

                    <SheetFooter>
                        <Button type="button" variant="ghost" disabled={busy} onClick={() => onOpenChange(false)}>
                            <XIcon weight="bold" aria-hidden="true" />
                            {isEditing ? 'Close' : 'Cancel'}
                        </Button>
                        <Button
                            type="button"
                            variant="outline"
                            loading={isTestingSettings}
                            disabled={busy}
                            onClick={handleTest}
                            data-action="test-instance"
                        >
                            <FlaskIcon weight="bold" aria-hidden="true" />
                            Test
                        </Button>
                        <Button
                            type="submit"
                            variant="primary"
                            loading={submitting}
                            disabled={busy}
                            data-action="add-instance"
                        >
                            <FloppyDiskIcon weight="bold" aria-hidden="true" />
                            {action}
                        </Button>
                    </SheetFooter>
                </form>
            </SheetContent>
        </Sheet>
    );
}

ConfigureInstanceDialog.displayName = 'ConfigureInstanceDialog';

export default ConfigureInstanceDialog;
