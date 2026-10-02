import { act, fireEvent, render, screen } from '@testing-library/react';
import type { HTMLAttributes, ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';

vi.mock('@phosphor-icons/react', () => ({
    FloppyDiskIcon: () => <svg aria-hidden="true" />,
    XIcon: () => <svg aria-hidden="true" />,
}));

vi.mock('@/hooks/useUnsavedChangesGuard', () => ({
    useUnsavedChangesGuard: () => undefined,
}));

vi.mock('@/components/ui/Select', () => ({
    Select: ({ children }: { children: ReactNode }) => <div>{children}</div>,
    SelectTrigger: ({ children, ...props }: HTMLAttributes<HTMLButtonElement>) => (
        <button type="button" {...props}>
            {children}
        </button>
    ),
    SelectContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
    SelectItem: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/Sheet', () => ({
    Sheet: ({ open, children }: { open?: boolean; children: ReactNode }) => (open ? <div>{children}</div> : null),
    SheetContent: ({ children, ...props }: HTMLAttributes<HTMLDivElement>) => <div {...props}>{children}</div>,
    SheetHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
    SheetTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>,
    SheetDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
    SheetFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/UnsavedChangesDialog', () => ({
    UnsavedChangesDialog: () => null,
}));

import { PackageSourceForm, type PackageSourceFormValues } from './PackageSourceForm';

const validSourceUrl = 'https://feed.example/v3/index.json';

function validCreateSource(overrides: Partial<PackageSourceFormValues> = {}): PackageSourceFormValues {
    return {
        name: 'Private feed',
        sourceUrl: validSourceUrl,
        enabled: true,
        searchFilterKind: 'Tag',
        searchFilterValue: 'magpie',
        credentialKind: 'None',
        username: '',
        secret: '',
        secretReference: '',
        hasSecret: false,
        ...overrides,
    };
}

function renderForm(initialValues: PackageSourceFormValues) {
    const onSubmit = vi.fn();

    render(<PackageSourceForm open onOpenChange={vi.fn()} initialValues={initialValues} onSubmit={onSubmit} />);

    return { onSubmit };
}

function existingBasicSource(overrides: Partial<PackageSourceFormValues> = {}): PackageSourceFormValues {
    return {
        id: 'src-1',
        name: 'Private feed',
        sourceUrl: validSourceUrl,
        enabled: true,
        searchFilterKind: 'Tag',
        searchFilterValue: 'magpie',
        credentialKind: 'Basic',
        username: 'feed-user',
        secret: '',
        secretReference: '',
        hasSecret: true,
        ...overrides,
    };
}

function save() {
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
}

describe('PackageSourceForm validation', () => {
    it.each([
        ['an HTTP URL', 'http://127.0.0.1:17126/qa-disabled-index.json', 'Source URL must be an absolute HTTPS URL.'],
        ['a malformed URL', 'bad-url', 'Source URL must be an absolute HTTPS URL.'],
        ['an invalid URL scheme', 'ftp://feed.example/v3/index.json', 'Source URL must be an absolute HTTPS URL.'],
        ['a relative URL', '/v3/index.json', 'Source URL must be an absolute HTTPS URL.'],
        [
            'embedded credentials',
            'https://user:pass@feed.example/v3/index.json',
            'Source URL must not include credentials.',
        ],
    ])('rejects %s on the Source URL field and keeps the draft', async (_name, sourceUrl, message) => {
        const { onSubmit } = renderForm(validCreateSource({ sourceUrl }));

        save();

        expect(await screen.findByText(message)).toBeInTheDocument();
        expect(screen.getByLabelText(/^Source URL/)).toHaveAccessibleDescription(message);
        expect(screen.getByLabelText(/^Source URL/)).toHaveValue(sourceUrl);
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it.each([
        ['creating an enabled feed', validCreateSource({ sourceUrl: 'http://feed.example/v3/index.json' })],
        [
            'creating a disabled feed',
            validCreateSource({ sourceUrl: 'http://feed.example/v3/index.json', enabled: false }),
        ],
        [
            'editing a disabled feed',
            existingBasicSource({ sourceUrl: 'http://feed.example/v3/index.json', enabled: false }),
        ],
    ])('requires an HTTPS Source URL when %s', async (_name, initialValues) => {
        const { onSubmit } = renderForm(initialValues);

        save();

        expect(await screen.findByText('Source URL must be an absolute HTTPS URL.')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('accepts an HTTPS Source URL for a disabled feed', async () => {
        const initialValues = validCreateSource({ enabled: false });
        const { onSubmit } = renderForm(initialValues);

        await act(async () => {
            save();
        });

        expect(screen.queryByText('Source URL must be an absolute HTTPS URL.')).not.toBeInTheDocument();
        expect(onSubmit).toHaveBeenCalledWith(initialValues);
    });

    it('requires a Basic secret when creating a feed', async () => {
        const { onSubmit } = renderForm(validCreateSource({ credentialKind: 'Basic' }));

        save();

        expect(await screen.findByText('secret is required for basic credentials.')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });

    it('allows a stored Basic secret to remain unchanged when editing a feed', async () => {
        const initialValues = existingBasicSource();
        const { onSubmit } = renderForm(initialValues);

        await act(async () => {
            save();
        });

        expect(screen.queryByText('secret is required for basic credentials.')).not.toBeInTheDocument();
        expect(onSubmit).toHaveBeenCalledWith(initialValues);
    });

    it('masks a stored Basic secret behind a clear control', () => {
        renderForm(existingBasicSource());

        const secret = screen.getByLabelText('Secret');
        expect(secret).toHaveAttribute('readonly');
        expect(secret).toHaveAttribute('placeholder', '********');
        expect(screen.getByRole('button', { name: 'Replace Secret' })).toBeInTheDocument();
    });

    it('requires a nonblank Basic secret when replacing a stored secret', async () => {
        const { onSubmit } = renderForm(existingBasicSource());

        fireEvent.click(screen.getByRole('button', { name: 'Replace Secret' }));
        fireEvent.change(screen.getByLabelText('Secret'), { target: { value: '   ' } });
        save();

        expect(await screen.findByText('secret is required for basic credentials.')).toBeInTheDocument();
        expect(onSubmit).not.toHaveBeenCalled();
    });
});

it('names every select when the source form is open', () => {
    // Arrange
    const onSubmit = vi.fn();
    // Act
    render(<PackageSourceForm open onOpenChange={vi.fn()} onSubmit={onSubmit} />);
    // Assert
    expect(screen.getByRole('button', { name: 'Discovery filter' })).toHaveTextContent('Require NuGet tag');
    expect(screen.getByRole('button', { name: 'Credentials' })).toHaveTextContent('None');
});
