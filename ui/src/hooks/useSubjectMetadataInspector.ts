import { createMetadataInspectorHooks, scanSubjectRefs } from '@/hooks/metadataInspectorHooks';
import { subjectQueryKeys } from '@/hooks/useSubjects';

const inspectorKey = (id: string) => [...subjectQueryKeys.detail(id), 'metadata-inspector'] as const;

const subjectMetadataInspectorHooks = createMetadataInspectorHooks({
    basePath: '/subjects',
    inspectorKey,
    detailKey: (id) => subjectQueryKeys.detail(id),
    resolveSubjectRefs: scanSubjectRefs,
    entityEvents: ['subject.updated', 'subject.deleted'],
    refreshJobType: 'SubjectMetadataRefreshItem',
});

export const useSubjectMetadataInspector = subjectMetadataInspectorHooks.useMetadataInspector;
export const usePatchSubjectMetadataInspector = subjectMetadataInspectorHooks.usePatchMetadataInspector;
export const useReorderSubjectMetadataContributions = subjectMetadataInspectorHooks.useReorderMetadataContributions;
