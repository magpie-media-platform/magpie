import { TelevisionIcon } from '@phosphor-icons/react';

import { DetailInfoRow } from '@/components/ui/DetailInfoGrid';
import { ExternalIds, type ExternalId, type ExternalIdsLeadingChip } from '@/components/ui/ExternalIds';
import { MetadataLinks } from '@/components/ui/MetadataLinks';
import { useMediaServerLinks } from '@/hooks/useContent';
import type { ContentExternalId, ContentReference } from '@/lib/metadata';
import { safeHttpUrl } from '@/lib/safeUrl';

interface ContentViewOnLinksProps {
    contentId: string | undefined;
    externalIds: ContentExternalId[] | undefined;
    references?: ContentReference[];
}

export function ContentViewOnLinks({ contentId, externalIds, references }: ContentViewOnLinksProps) {
    const { data: mediaServerLinks, isLoading: mediaServersLoading } = useMediaServerLinks(contentId);

    const leadingChips: ExternalIdsLeadingChip[] =
        !mediaServersLoading && mediaServerLinks
            ? mediaServerLinks.map((link) => ({
                  key: `media-${link.mediaServerId}`,
                  label: link.serverName,
                  url: link.url,
                  source: link.providerType,
                  testId: `media-server-link-${link.providerType}`,
                  dataMediaServerId: link.mediaServerId,
                  icon: <TelevisionIcon weight="fill" aria-hidden="true" />,
              }))
            : [];

    const ids: ExternalId[] = (externalIds ?? []).map((extId) => ({
        source: extId.source,
        id: extId.value,
        url: extId.url,
    }));
    const hasLinkedExternalId = ids.some((id) => safeHttpUrl(id.url) !== null);
    const hasLinks = leadingChips.length > 0 || hasLinkedExternalId || (references?.length ?? 0) > 0;

    if (!hasLinks) {
        return null;
    }

    return (
        <DetailInfoRow
            label="Links"
            data-testid="content-links-row"
            value={
                <div className="detail-info-links">
                    <ExternalIds
                        variant="chips"
                        data-component="view-on-links"
                        leadingChips={leadingChips}
                        ids={ids}
                        itemTestId={(extId) => `external-source-link-${extId.source.toLowerCase()}`}
                    />
                    <MetadataLinks references={references ?? []} label={null} />
                </div>
            }
        />
    );
}

ContentViewOnLinks.displayName = 'ContentViewOnLinks';

export default ContentViewOnLinks;
