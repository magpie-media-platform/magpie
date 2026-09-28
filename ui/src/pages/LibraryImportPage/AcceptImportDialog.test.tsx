import { useState } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';

import type { LibraryImportMonitoringMode } from '@/hooks/useBulkAcceptLibraryImportCandidates';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import * as apiModule from '@/lib/api';

vi.mock('./ImportMonitoringSelect', () => ({
    ImportMonitoringSelect: ({
        value,
        onChange,
    }: {
        value: LibraryImportMonitoringMode | '';
        onChange: (mode: LibraryImportMonitoringMode) => void;
    }) => <button onClick={() => onChange('Future')}>{value || 'Select monitoring'}</button>,
}));

import { AcceptImportDialog, type ImportAcceptAction } from './AcceptImportDialog';

function DialogHarness({ run }: { run: ImportAcceptAction }) {
    const [action, setAction] = useState<ImportAcceptAction | null>(() => run);
    const [mode, setMode] = useState<LibraryImportMonitoringMode | ''>('');
    return (
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
            <button onClick={() => setAction(() => run)}>Open accept</button>
            <AcceptImportDialog
                action={action}
                monitoringMode={mode}
                onMonitoringModeChange={setMode}
                onClose={() => setAction(null)}
            />
        </QueryClientProvider>
    );
}

describe('AcceptImportDialog', () => {
    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('hides optional renaming and associates normally when the preview has no applicable mask', async () => {
        const user = userEvent.setup();
        const run = Object.assign(vi.fn(), { renameSelection: { ids: ['candidate'] } });
        const post = vi.spyOn(apiModule, 'apiPost').mockResolvedValue({ hasMask: false, files: [] });
        const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
        render(
            <QueryClientProvider client={client}>
                <AcceptImportDialog
                    action={run}
                    monitoringMode="Future"
                    onMonitoringModeChange={vi.fn()}
                    onClose={vi.fn()}
                />
            </QueryClientProvider>,
        );

        await waitFor(() => {
            expect(post).toHaveBeenCalledWith('/import/rename-preview', { ids: ['candidate'] });
            expect(screen.queryByText('Loading rename preview…')).not.toBeInTheDocument();
        });
        expect(screen.queryByRole('checkbox', { name: 'Apply rename mask' })).not.toBeInTheDocument();
        await user.click(screen.getByRole('button', { name: 'Accept' }));
        expect(run).toHaveBeenCalledWith('Future');
        expect(post).toHaveBeenCalledTimes(1);
    });

    it('does not run until a mode is chosen and confirmed, then remembers that mode', async () => {
        const user = userEvent.setup();
        const run = vi.fn();
        render(<DialogHarness run={run} />);

        expect(screen.getByRole('button', { name: 'Accept' })).toBeDisabled();
        expect(run).not.toHaveBeenCalled();

        await user.click(screen.getByRole('button', { name: 'Select monitoring' }));
        await user.click(screen.getByRole('button', { name: 'Accept' }));
        expect(run).toHaveBeenCalledWith('Future');

        await user.click(screen.getByRole('button', { name: 'Open accept' }));
        expect(screen.getByRole('button', { name: 'Future' })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Accept' })).not.toBeDisabled();
    });

    it('cancels without running the action', async () => {
        const user = userEvent.setup();
        const run = vi.fn();
        render(<DialogHarness run={run} />);

        await user.click(screen.getByRole('button', { name: 'Cancel' }));
        expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
        expect(run).not.toHaveBeenCalled();
    });
});
