import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { LibraryImportMonitoringMode } from '@/hooks/useBulkAcceptLibraryImportCandidates';

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
        <>
            <button onClick={() => setAction(() => run)}>Open accept</button>
            <AcceptImportDialog
                action={action}
                monitoringMode={mode}
                onMonitoringModeChange={setMode}
                onClose={() => setAction(null)}
            />
        </>
    );
}

describe('AcceptImportDialog', () => {
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
