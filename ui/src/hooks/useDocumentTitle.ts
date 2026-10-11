import { useEffect } from 'react';

import { useInstanceName } from '@/hooks/useInstanceName';

export function useDocumentTitle(title?: string): void {
    const instanceName = useInstanceName();

    useEffect(() => {
        document.title = title ? `${title} - ${instanceName}` : instanceName;

        return () => {
            document.title = instanceName;
        };
    }, [title, instanceName]);
}
