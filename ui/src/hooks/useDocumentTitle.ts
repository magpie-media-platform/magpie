import { useEffect } from 'react';

const BASE_TITLE = 'Magpie';

export function useDocumentTitle(title?: string): void {
    useEffect(() => {
        document.title = title ? `${title} - ${BASE_TITLE}` : BASE_TITLE;

        return () => {
            document.title = BASE_TITLE;
        };
    }, [title]);
}
