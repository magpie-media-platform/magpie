import { useParams } from 'react-router-dom';

import { Slot } from '@/components/ui/Slot/Slot';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';

export function ExtensionPage({ admin = false }: { admin?: boolean }) {
    useDocumentTitle('Extension');
    const { slug } = useParams<{ slug: string }>();
    const slot = admin ? `admin.route.${slug}` : `route.${slug}`;

    return (
        <div className="extension-page" data-page="extension" data-extension-slug={slug}>
            <Slot slot={slot} />
        </div>
    );
}

ExtensionPage.displayName = 'ExtensionPage';

export default ExtensionPage;
