import type { HTMLAttributes } from 'react';

import { WarningCircleIcon } from '@phosphor-icons/react';

import { Alert } from '@/components/ui/Alert';

import './ProviderSearchWarnings.css';

interface ProviderSearchWarningsProps extends HTMLAttributes<HTMLDivElement> {
    warnings: readonly string[];
    hasResults: boolean;
}

export function ProviderSearchWarnings({ warnings, hasResults, className, ...props }: ProviderSearchWarningsProps) {
    const distinct = [...new Set(warnings)];
    if (distinct.length === 0) {
        return null;
    }

    return (
        <Alert
            className={['provider-search-warnings', className].filter(Boolean).join(' ')}
            variant="warning"
            role="status"
            aria-live="polite"
            icon={<WarningCircleIcon weight="duotone" />}
            title={hasResults ? 'Some results may be missing' : 'This search could not return results'}
            data-component="provider-search-warnings"
            {...props}
        >
            <ul className="provider-search-warnings__list">
                {distinct.map((warning) => (
                    <li key={warning} className="provider-search-warnings__item">
                        {warning}
                    </li>
                ))}
            </ul>
        </Alert>
    );
}

export default ProviderSearchWarnings;
