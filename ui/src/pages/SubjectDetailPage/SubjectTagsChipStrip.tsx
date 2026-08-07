import { useCallback } from 'react';

import { TagsChipStrip, type TagsPatch, type TagsPatchCallbacks } from '@/components/ui/TagsChipStrip';
import { useUpdateSubjectTags } from '@/hooks/useSubjects';
import { useTagsByEntity } from '@/hooks/useTags';

interface SubjectTagsChipStripProps {
    subjectId: string;
    subjectListPath: string;
    canEdit: boolean;
}

function escapeFilterString(value: string): string {
    return value.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

export function SubjectTagsChipStrip({ subjectId, subjectListPath, canEdit }: SubjectTagsChipStripProps) {
    const updateMutation = useUpdateSubjectTags();
    const { tags, isLoading } = useTagsByEntity(subjectId, true);
    const tagNames = tags.map((tag) => tag.name).sort((a, b) => a.localeCompare(b));
    const tagHref = useCallback(
        (tagName: string) => {
            const filter = `tag has "${escapeFilterString(tagName)}"`;
            return `${subjectListPath}?filter=${encodeURIComponent(filter)}`;
        },
        [subjectListPath],
    );
    const updateTags = useCallback(
        (patch: TagsPatch, callbacks: TagsPatchCallbacks) => {
            updateMutation.mutate({ id: subjectId, tags: patch }, callbacks);
        },
        [subjectId, updateMutation],
    );

    return (
        <TagsChipStrip
            entityId={subjectId}
            tagNames={tagNames}
            isLoading={isLoading}
            canEdit={canEdit}
            isPending={updateMutation.isPending}
            tagHref={tagHref}
            updateTags={updateTags}
            rowLabel="Tags"
            rowTestId="subject-tags-row"
        />
    );
}

SubjectTagsChipStrip.displayName = 'SubjectTagsChipStrip';

export default SubjectTagsChipStrip;
