import { keepPreviousData, useQuery } from '@tanstack/react-query';

import { Button } from '@/components/ui/Button';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { apiPost } from '@/lib/api';
import './RenameTokenReference.css';

export type RenameTokenFamily = 'File' | 'Metadata' | 'Hierarchy' | 'MediaInfo' | 'Relationship';

export interface RenameTokenInfo {
    name: string;
    family: RenameTokenFamily;
    description: string;
    example: string;
    value?: string | null;
    available?: boolean | null;
}

export interface RenameTokenPreview {
    directory?: string | null;
    fileName?: string | null;
    error?: string | null;
}

export interface RenameTokenReferenceData {
    tokens: RenameTokenInfo[];
    preview?: RenameTokenPreview | null;
}

export interface RenameTokenRequest {
    contentType?: string;
    template?: string;
    contentHierarchyItemId?: string;
    mediaFileId?: string;
}

const familyLabels: Record<RenameTokenFamily, string> = {
    File: 'File',
    Metadata: 'Metadata',
    Hierarchy: 'Hierarchy',
    MediaInfo: 'Media information',
    Relationship: 'Related subjects',
};

interface RenameTokenReferenceProps {
    request: RenameTokenRequest;
    onInsert?: (token: string) => void;
}

export function RenameTokenReference({ request, onInsert }: RenameTokenReferenceProps) {
    const template = useDebouncedValue(request.template);
    const { contentType, contentHierarchyItemId, mediaFileId } = request;
    const reference = useQuery({
        queryKey: ['rename-tokens', contentType, contentHierarchyItemId, mediaFileId, template],
        queryFn: () =>
            apiPost<RenameTokenReferenceData>('/content/rename-tokens', {
                contentType,
                contentHierarchyItemId,
                mediaFileId,
                template,
            }),
        placeholderData: keepPreviousData,
        refetchOnWindowFocus: false,
    });
    const selected = contentHierarchyItemId !== undefined;
    const preview = reference.data?.preview;
    const families = Object.keys(familyLabels) as RenameTokenFamily[];

    return (
        <div className="rename-token-reference">
            {reference.error && <p role="alert">{reference.error.message}</p>}
            {preview && (
                <div className="rename-token-reference__preview" aria-live="polite">
                    <p className="rename-token-reference__heading">
                        {selected ? 'Preview for this file' : 'Preview with example values'}
                    </p>
                    {preview.error ? (
                        <p role="alert" className="rename-token-reference__error">
                            The file fails: {preview.error}
                        </p>
                    ) : (
                        <dl>
                            <dt>Directory</dt>
                            <dd>
                                <code>{preview.directory ?? 'The folder-providing parent'}</code>
                            </dd>
                            <dt>File name</dt>
                            <dd>
                                <code>{preview.fileName}</code>
                            </dd>
                        </dl>
                    )}
                </div>
            )}
            <details className="rename-token-reference__tokens">
                <summary>Available tokens</summary>
                <p className="rename-token-reference__note">
                    A missing value or an unknown token fails the file. Other files still proceed. Language lists use{' '}
                    <code>+</code>. Other lists use spaces. Use <code>:upper</code>, <code>:lower</code>, or{' '}
                    <code>:00</code> for padded numbers.
                </p>
                {reference.isPending && <p>Loading tokens…</p>}
                {families.map((family) => {
                    const tokens = reference.data?.tokens.filter((token) => token.family === family) ?? [];
                    if (tokens.length === 0) return null;
                    return (
                        <section key={family} aria-label={familyLabels[family]}>
                            <h4 className="rename-token-reference__heading">{familyLabels[family]}</h4>
                            <ul role="list">
                                {tokens.map((token) => (
                                    <li key={token.name} data-token={token.name}>
                                        <div className="rename-token-reference__name">
                                            <code>{`{${token.name}}`}</code>
                                            {onInsert && (
                                                <Button
                                                    type="button"
                                                    size="sm"
                                                    variant="ghost"
                                                    aria-label={`Insert {${token.name}}`}
                                                    onClick={() => onInsert(`{${token.name}}`)}
                                                >
                                                    Insert
                                                </Button>
                                            )}
                                        </div>
                                        <p>{token.description}</p>
                                        <p>
                                            Example: <code>{token.example}</code>
                                        </p>
                                        {token.available === true && (
                                            <p>
                                                Value on this file: <code>{token.value}</code>
                                            </p>
                                        )}
                                        {token.available === false && (
                                            <p className="rename-token-reference__error">
                                                No value on this file. A mask that uses this token fails the file.
                                            </p>
                                        )}
                                    </li>
                                ))}
                            </ul>
                        </section>
                    );
                })}
            </details>
        </div>
    );
}
