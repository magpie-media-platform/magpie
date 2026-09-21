import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { ReactNode } from 'react';

import { TooltipProvider } from '@/components/ui/Tooltip';

import { ExclusionsEditor, type ExclusionDraft } from './RulesEditor';

const mockUseLibraryTypes = vi.fn();

vi.mock('@/hooks/useLibraryTypes', () => ({
    useLibraryTypes: () => mockUseLibraryTypes(),
}));

const defaultLibraryTypes = {
    contentTypes: [] as {
        id: string;
        name: string;
        pluralName: string;
        slug: string;
        iconName: null;
        levels: never[];
    }[],
    subjectTypes: [],
    isLoading: false,
    error: null,
    refetch: vi.fn(),
};

function TestWrapper({ children }: { children: ReactNode }) {
    return <TooltipProvider>{children}</TooltipProvider>;
}

function createExclusion(overrides: Partial<ExclusionDraft> = {}): ExclusionDraft {
    return {
        id: 'rule-1',
        name: 'No CAM',
        criteria: [],
        ...overrides,
    };
}

describe('ExclusionsEditor', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseLibraryTypes.mockReturnValue(defaultLibraryTypes);
    });

    it('renders the component and surfaces the scope label', () => {
        render(
            <TestWrapper>
                <ExclusionsEditor exclusions={[]} onChange={vi.fn()} scopeLabel="this tier" />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-component="exclusions-editor"]')).toBeInTheDocument();
        expect(screen.getByText('this tier')).toBeInTheDocument();
        expect(screen.getByText(/no exclusions defined/i)).toBeInTheDocument();
    });

    it('labels exclusion criteria with the exclusion scope, not the tier', () => {
        render(
            <TestWrapper>
                <ExclusionsEditor
                    exclusions={[createExclusion({ criteria: [] })]}
                    onChange={vi.fn()}
                    scopeLabel="the entire list"
                />
            </TestWrapper>,
        );

        expect(screen.getByText(/match this exclusion/i)).toBeInTheDocument();
        expect(screen.queryByText(/this tier/i)).not.toBeInTheDocument();
    });

    it('renders exclusions without a kind selector', () => {
        render(
            <TestWrapper>
                <ExclusionsEditor exclusions={[createExclusion()]} onChange={vi.fn()} scopeLabel="the entire list" />
            </TestWrapper>,
        );

        expect(document.querySelector('[data-exclusion-id="rule-1"]')).toBeInTheDocument();
        expect(screen.queryByLabelText(/kind/i)).not.toBeInTheDocument();
    });

    it('adds an exclusion', async () => {
        const onChange = vi.fn();
        render(
            <TestWrapper>
                <ExclusionsEditor exclusions={[]} onChange={onChange} scopeLabel="this tier" />
            </TestWrapper>,
        );

        await userEvent.click(screen.getByRole('button', { name: /add exclusion/i }));

        expect(onChange).toHaveBeenCalledTimes(1);
        const next = onChange.mock.calls[0][0] as ExclusionDraft[];
        expect(next).toHaveLength(1);
        expect(next[0]).toMatchObject({ name: '', criteria: [] });
    });

    it('removes an exclusion', async () => {
        const onChange = vi.fn();
        render(
            <TestWrapper>
                <ExclusionsEditor exclusions={[createExclusion()]} onChange={onChange} scopeLabel="this tier" />
            </TestWrapper>,
        );

        await userEvent.click(screen.getByRole('button', { name: /remove exclusion/i }));

        expect(onChange).toHaveBeenCalledWith([]);
    });

    it('explains that an exclusion overrides matching tier criteria', () => {
        render(
            <TestWrapper>
                <ExclusionsEditor exclusions={[]} onChange={vi.fn()} scopeLabel="this tier" />
            </TestWrapper>,
        );

        expect(screen.getByText(/prevents this tier from matching, even when its criteria match/i)).toBeInTheDocument();
    });
});
