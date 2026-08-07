import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';

import { PathControl } from './PathControl';

const mockApply = vi.fn();

vi.mock('@/hooks/useContent', () => ({
    useApplyContentPath: () => ({ mutateAsync: mockApply, isPending: false }),
}));

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: vi.fn() }),
}));

vi.mock('@/components/ui/PathInput', () => ({
    PathInput: ({ value, onChange, ...props }: { value: string; onChange: (value: string) => void }) => (
        <input {...props} value={value} onChange={(event) => onChange(event.target.value)} />
    ),
}));

describe('PathControl', () => {
    it('updates the path through the content patch', async () => {
        const user = userEvent.setup();
        mockApply.mockResolvedValue(undefined);
        render(<PathControl contentHierarchyItemId="content-1" rawPath="old" />);

        await user.click(screen.getByRole('button', { name: 'Edit content path' }));
        const input = screen.getByRole('textbox', { name: 'Content path' });
        await user.clear(input);
        await user.type(input, 'new');
        await user.click(screen.getByRole('button', { name: 'Save path' }));

        expect(mockApply).toHaveBeenCalledWith({ contentHierarchyItemId: 'content-1', path: 'new' });
    });
});
