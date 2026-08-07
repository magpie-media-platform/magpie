import { useParams } from 'react-router-dom';

import { CubeIcon } from '@phosphor-icons/react';

import { EmptyState } from '@/components/ui/EmptyState';
import { Slot } from '@/components/ui/Slot/Slot';
import { useExtensions, useSlotExtensions } from '@/extensions/useExtensions';
import type { UIExtension } from '@/extensions/types';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';

import './ExtensionPage.css';

function resolveExtensionTitle(navExtensions: UIExtension[], routePath: string, slug: string): string {
    const match = navExtensions.find((extension) => {
        const href = extension.attributes?.href?.trim();
        return href === routePath || href?.startsWith(`${routePath}?`) || href?.startsWith(`${routePath}#`);
    });

    return match?.attributes?.label?.trim() || match?.name || slug;
}

export function ExtensionPage({ admin = false }: { admin?: boolean }) {
    const { slug } = useParams<{ slug: string }>();
    const slot = admin ? `admin.route.${slug}` : `route.${slug}`;
    const routePath = admin ? `/admin/ext/${slug}` : `/ext/${slug}`;

    const { getExtensionsForSlot } = useExtensions();
    const { extensions, isReady } = useSlotExtensions(slot);
    const hasContent = extensions.length > 0;
    const extensionTitle = resolveExtensionTitle(
        getExtensionsForSlot(admin ? 'nav.admin' : 'nav.main'),
        routePath,
        slug ?? 'Extension',
    );
    const pageTitle = admin ? `${extensionTitle} Settings` : extensionTitle;
    useDocumentTitle(pageTitle);

    return (
        <div
            className={['extension-page', admin ? 'extension-page--admin' : undefined].filter(Boolean).join(' ')}
            data-page="extension"
            data-extension-slug={slug}
        >
            <h1 className="extension-page__title">{pageTitle}</h1>
            {isReady && !hasContent ? (
                <EmptyState
                    icon={<CubeIcon weight="duotone" />}
                    title={extensionTitle}
                    description="This extension does not provide a page here."
                />
            ) : (
                <Slot slot={slot} />
            )}
        </div>
    );
}

ExtensionPage.displayName = 'ExtensionPage';

export default ExtensionPage;
