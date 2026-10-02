import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';

import { SubjectPicker } from './SubjectPicker';
import type { ApiSubject, SubjectQueryParams } from '@/hooks/useSubjects';

const mockUseSubjects = vi.fn();
vi.mock('@/hooks/useSubjects', () => ({
    useSubjects: (params?: SubjectQueryParams) => mockUseSubjects(params),
    useSubject: () => ({ subject: null, isLoading: false }),
}));

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => ({ subjectTypes: [{ slug: 'person', name: 'Person', iconName: null, levels: [] }] }),
}));

describe('SubjectPicker server-side search', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseSubjects.mockReturnValue({
            subjects: [
                {
                    id: 'subject-1',
                    metadataContainerId: 'mc-subject-1',
                    kind: 'person',
                    dateLastRefreshed: null,
                    createdAt: '2024-01-01T00:00:00Z',
                    metadataContainer: {
                        id: 'mc-subject-1',
                        createdAt: '2024-01-01T00:00:00Z',
                        contributions: [
                            {
                                id: 'c-subject-1',
                                ordinal: 0,
                                locked: false,
                                providerType: 'provider-a',
                                title: 'Bryan Cranston',
                            },
                        ],
                    },
                } as ApiSubject,
            ],
            isLoading: false,
        });
    });

    it('requests a bounded page sorted by title, never the 1000-row cap', () => {
        render(<SubjectPicker value={null} onChange={vi.fn()} />);

        const params = mockUseSubjects.mock.calls.at(-1)?.[0] as SubjectQueryParams | undefined;
        expect(params?.pageSize).toBe(20);
        expect(params?.sortBy).toBe('title');
        expect(mockUseSubjects.mock.calls.every((call) => (call[0] as SubjectQueryParams)?.pageSize !== 1000)).toBe(
            true,
        );
    });

    it('searches server-side by sending a title-contains filter for the typed query', async () => {
        const user = userEvent.setup();
        render(<SubjectPicker value={null} onChange={vi.fn()} />);

        await user.type(screen.getByLabelText('Search subjects by name'), 'cranston');

        await waitFor(() => {
            const params = mockUseSubjects.mock.calls.at(-1)?.[0] as SubjectQueryParams | undefined;
            expect(params?.filter).toBe('title contains "cranston"');
        });
    });
});
