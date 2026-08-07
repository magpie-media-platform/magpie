import { useUpdateContent } from '@/hooks/useContent';
import { useTagsByEntity } from '@/hooks/useTags';
import {
    TagsChipStrip as SharedTagsChipStrip,
    type TagsPatch,
    type TagsPatchCallbacks,
} from '@/components/ui/TagsChipStrip';

interface TagsChipStripProps {
    contentId: string | undefined;
    canEdit: boolean;
}

function escapeFilterString(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

function tagFilterHref(tagName: string): string {
    const filter = `tag has "${escapeFilterString(tagName)}"`;
    return `/library/all?filter=${encodeURIComponent(filter)}`;
}

export function TagsChipStrip({ contentId, canEdit }: TagsChipStripProps) {
    const updateMutation = useUpdateContent();
    const { tags, isLoading } = useTagsByEntity(contentId ?? '', !!contentId);
    const tagNames = tags.map((tag) => tag.name).sort((a, b) => a.localeCompare(b));

    const updateTags = (patch: TagsPatch, callbacks: TagsPatchCallbacks) => {
        if (!contentId) {
            return;
        }
        updateMutation.mutate(
            {
                id: contentId,
                tagsToAdd: patch.add,
                tagsToRemove: patch.remove,
            },
            callbacks,
        );
    };

    return (
        <SharedTagsChipStrip
            entityId={contentId}
            tagNames={tagNames}
            isLoading={isLoading}
            canEdit={canEdit}
            isPending={updateMutation.isPending}
            tagHref={tagFilterHref}
            updateTags={updateTags}
            rowLabel="Tags"
            rowTestId="content-tags-row"
        />
    );
}

TagsChipStrip.displayName = 'TagsChipStrip';

export default TagsChipStrip;
