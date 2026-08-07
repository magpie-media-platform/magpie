import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { SubjectTagsChipStrip } from './SubjectTagsChipStrip';

const mockMutate = vi.fn();
const mockUseTagsByEntity = vi.fn();

vi.mock('@/hooks/useSubjects', () => ({
    useUpdateSubjectTags: () => ({ mutate: mockMutate, isPending: false }),
}));

vi.mock('@/hooks/useTags', () => ({
    useTagsByEntity: (entityId: string, enabled: boolean) => mockUseTagsByEntity(entityId, enabled),
}));

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: vi.fn() }),
}));

function renderStrip(canEdit = true) {
    return render(
        <MemoryRouter>
            <SubjectTagsChipStrip subjectId="subject-1" subjectListPath="/library/subjects/person" canEdit={canEdit} />
        </MemoryRouter>,
    );
}

describe('SubjectTagsChipStrip', () => {
    beforeEach(() => {
        mockMutate.mockReset();
        mockUseTagsByEntity.mockReset();
        mockUseTagsByEntity.mockReturnValue({
            tags: [{ name: 'Action Picks' }],
            isLoading: false,
        });
    });

    it('labels the aligned row with visible Tags text rather than a glyph', () => {
        renderStrip();

        const row = screen.getByTestId('subject-tags-row');
        const label = row.querySelector('.detail-info-row__label');
        expect(label?.textContent).toBe('Tags');
        expect(label?.querySelector('svg')).toBeNull();
    });

    it('links chips to the subject listing with the tag filter', () => {
        renderStrip();

        expect(screen.getByRole('link', { name: 'Action Picks' })).toHaveAttribute(
            'href',
            `/library/subjects/person?filter=${encodeURIComponent('tag has "Action Picks"')}`,
        );
    });

    it('uses the subject tag patch operation for free-text additions', async () => {
        const user = userEvent.setup();
        renderStrip();

        await user.click(screen.getByRole('button', { name: 'Add tag' }));
        await user.type(screen.getByPlaceholderText('New tag'), 'favorite{Enter}');

        await waitFor(() => {
            expect(mockMutate).toHaveBeenCalledWith(
                { id: 'subject-1', tags: { add: ['favorite'] } },
                expect.anything(),
            );
        });
    });

    it('hides an empty read-only row', () => {
        mockUseTagsByEntity.mockReturnValue({ tags: [], isLoading: false });

        const { container } = renderStrip(false);

        expect(container).toBeEmptyDOMElement();
    });
});
