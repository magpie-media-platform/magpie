import { WarningIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { useExtensions } from '@/extensions/useExtensions';

import './ExtensionLoadFailureBanner.css';

export function ExtensionLoadFailureBanner() {
    const { isError, error, retry } = useExtensions();

    if (!isError) {
        return null;
    }

    return (
        <div className="extension-load-failure-banner" data-component="extension-load-failure-banner">
            <Alert
                variant="error"
                title="Extensions could not load"
                icon={<WarningIcon weight="fill" aria-hidden="true" />}
            >
                <div className="extension-load-failure-banner__body">
                    <span>{error ?? 'The extension manifest is unavailable.'}</span>
                    <Button size="sm" onClick={() => void retry().catch(() => undefined)}>
                        Retry extensions
                    </Button>
                </div>
            </Alert>
        </div>
    );
}
