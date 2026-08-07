import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { deriveOnboardingSteps } from '@/hooks/useOnboardingStatus';

import { OnboardingChecklist } from './OnboardingChecklist';

const renderWithRouter = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

const stepsWith = (done: { indexer?: boolean; downloadClient?: boolean; rootFolder?: boolean; monitored?: boolean }) =>
    deriveOnboardingSteps({
        hasIndexer: done.indexer ?? false,
        hasDownloadClient: done.downloadClient ?? false,
        hasRootFolder: done.rootFolder ?? false,
        hasMonitoredContent: done.monitored ?? false,
        isComplete: false,
    });

describe('OnboardingChecklist', () => {
    it('renders all four pipeline steps', () => {
        const steps = stepsWith({});
        renderWithRouter(<OnboardingChecklist steps={steps} completedCount={0} onDismiss={vi.fn()} />);

        expect(screen.getByText('Add an indexer')).toBeInTheDocument();
        expect(screen.getByText('Add a download client')).toBeInTheDocument();
        expect(screen.getByText('Add a root folder')).toBeInTheDocument();
        expect(screen.getByText('Add content to monitor')).toBeInTheDocument();
    });

    it('shows a "Set up" link only for incomplete steps', () => {
        const steps = stepsWith({ indexer: true });
        renderWithRouter(<OnboardingChecklist steps={steps} completedCount={1} onDismiss={vi.fn()} />);

        const links = screen.getAllByRole('link', { name: /set up:/i });
        expect(links).toHaveLength(3);
        expect(screen.queryByRole('link', { name: /set up: add an indexer/i })).not.toBeInTheDocument();
    });

    it('points each action link at the step route', () => {
        const steps = stepsWith({});
        renderWithRouter(<OnboardingChecklist steps={steps} completedCount={0} onDismiss={vi.fn()} />);

        expect(screen.getByRole('link', { name: /set up: add an indexer/i })).toHaveAttribute(
            'href',
            '/admin/indexers',
        );
        expect(screen.getByRole('link', { name: /set up: add content to monitor/i })).toHaveAttribute(
            'href',
            '/library/add',
        );
    });

    it('renders the progress summary', () => {
        const steps = stepsWith({ indexer: true, downloadClient: true });
        renderWithRouter(<OnboardingChecklist steps={steps} completedCount={2} onDismiss={vi.fn()} />);

        expect(screen.getByLabelText('2 of 4 steps complete')).toBeInTheDocument();
    });

    it('explains how the pipeline works once set up', () => {
        const steps = stepsWith({});
        renderWithRouter(<OnboardingChecklist steps={steps} completedCount={0} onDismiss={vi.fn()} />);

        expect(screen.getByText(/searches your indexers for releases/i)).toBeInTheDocument();
    });

    it('calls onDismiss when the dismiss button is clicked', async () => {
        const onDismiss = vi.fn();
        const steps = stepsWith({});
        renderWithRouter(<OnboardingChecklist steps={steps} completedCount={0} onDismiss={onDismiss} />);

        await userEvent.click(screen.getByRole('button', { name: /dismiss setup checklist/i }));
        expect(onDismiss).toHaveBeenCalledOnce();
    });

    it('renders skeleton steps before status resolves (empty steps)', () => {
        const { container } = renderWithRouter(
            <OnboardingChecklist steps={[]} completedCount={0} onDismiss={vi.fn()} />,
        );
        expect(container.querySelectorAll('.onboarding-checklist__step--skeleton')).toHaveLength(4);
        expect(screen.queryByLabelText(/steps complete/i)).not.toBeInTheDocument();
    });
});
