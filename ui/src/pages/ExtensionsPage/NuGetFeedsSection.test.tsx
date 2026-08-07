import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';

import type { PackageSource } from '@/hooks/usePackageSources';
import type { PackageSourceFormValues } from './PackageSourceForm';

const { patchSource } = vi.hoisted(() => ({ patchSource: vi.fn().mockResolvedValue(undefined) }));

vi.mock('@phosphor-icons/react', () => ({
    PlusIcon: () => <span />,
    CloudIcon: () => <span />,
    PencilSimpleIcon: () => <span />,
    TrashIcon: () => <span />,
    CircleNotchIcon: () => <span />,
    WarningCircleIcon: () => <span />,
}));

vi.mock('@/components/ui/Toast', () => ({
    useToast: () => ({ toast: vi.fn() }),
}));

vi.mock('@/components/ui/Tooltip', () => ({
    Tooltip: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

vi.mock('@/components/ui/Switch', () => ({
    Switch: () => <span />,
}));

vi.mock('@/components/ui/DeleteConfirmDialog/DeleteConfirmDialog', () => ({
    DeleteConfirmDialog: () => null,
}));

vi.mock('@/hooks/usePackageSources', () => ({
    usePackageSources: () => ({
        sources: [
            {
                id: 'src-1',
                name: 'Private feed',
                sourceUrl: 'https://feed.example/v3/index.json',
                enabled: true,
                searchFilterKind: 'Tag',
                searchFilterValue: 'magpie',
                credentialKind: 'Basic',
                username: 'feed-user',
                secretReference: null,
                hasSecret: true,
                createdAt: '2026-08-02T00:00:00Z',
                modifiedAt: null,
            } satisfies PackageSource,
        ],
        isLoading: false,
        error: null,
        refetch: vi.fn(),
        createSource: vi.fn(),
        isCreating: false,
        patchSource,
        isPatching: false,
        setEnabled: vi.fn(),
        deleteSource: vi.fn(),
        isDeleting: false,
    }),
}));

vi.mock('./PackageSourceForm', () => ({
    PackageSourceForm: ({
        open,
        onSubmit,
    }: {
        open: boolean;
        onSubmit: (values: PackageSourceFormValues) => Promise<void>;
    }) =>
        open ? (
            <button
                type="button"
                onClick={() =>
                    void onSubmit({
                        id: 'src-1',
                        name: 'Private feed',
                        sourceUrl: 'https://feed.example/v3/index.json',
                        enabled: true,
                        searchFilterKind: 'Tag',
                        searchFilterValue: 'magpie',
                        credentialKind: 'Basic',
                        username: 'feed-user',
                        secret: 'new-secret',
                        secretReference: '',
                        hasSecret: true,
                    })
                }
            >
                Save feed
            </button>
        ) : null,
}));

import { NuGetFeedsSection } from './NuGetFeedsSection';

describe('NuGetFeedsSection', () => {
    it('patches a replacement Basic secret through encryptedSecret', async () => {
        render(<NuGetFeedsSection />);

        fireEvent.click(screen.getByRole('button', { name: 'Edit Private feed' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save feed' }));

        await waitFor(() =>
            expect(patchSource).toHaveBeenCalledWith('src-1', {
                name: { value: 'Private feed' },
                sourceUrl: { value: 'https://feed.example/v3/index.json' },
                enabled: { value: true },
                searchFilterKind: { value: 'Tag' },
                searchFilterValue: { value: 'magpie' },
                credentialKind: { value: 'Basic' },
                username: { value: 'feed-user' },
                encryptedSecret: { value: 'new-secret' },
                secretReference: { value: null },
            }),
        );
    });
});
