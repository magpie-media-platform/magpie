import { useMemo, useState } from 'react';

import { Button } from '@/components/ui/Button';
import {
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
} from '@/components/ui/Dialog';
import { Input } from '@/components/ui/Input';
import { Label } from '@/components/ui/Label';
import type {
    AlternateTextSubItemPayload,
    AwardSubItemPayload,
    ContentRatingSubItemPayload,
    CustomMetadataValueSubItemPayload,
    ExternalIdSubItemPayload,
    GenreSubItemPayload,
    ImageSubItemPayload,
    MetadataSubItemKey,
    PreviewSubItemPayload,
    ReferenceSubItemPayload,
    RelationshipSubItemPayload,
    SubItemPayload,
} from '@/types';

import { SubjectPicker, type SubjectSelection } from './SubjectPicker';

type FieldType = 'text' | 'url' | 'number' | 'date';

interface FieldDef {
    name: string;
    label: string;
    type: FieldType;
    required?: boolean;
    placeholder?: string;
}

const FIELDS_BY_KIND: Record<MetadataSubItemKey, FieldDef[]> = {
    genres: [{ name: 'name', label: 'Name', type: 'text', required: true }],
    images: [
        { name: 'type', label: 'Type', type: 'text', required: true },
        { name: 'url', label: 'URL', type: 'url', required: true, placeholder: 'https://...' },
        { name: 'mimeType', label: 'MIME type', type: 'text' },
        { name: 'language', label: 'Language', type: 'text' },
        { name: 'width', label: 'Width', type: 'number' },
        { name: 'height', label: 'Height', type: 'number' },
    ],
    relationships: [
        { name: 'kind', label: 'Relationship kind', type: 'text', required: true },
        { name: 'qualifier', label: 'Qualifier', type: 'text' },
    ],
    externalIds: [
        { name: 'value', label: 'Value', type: 'text', required: true },
        { name: 'source', label: 'Source', type: 'text' },
    ],
    previews: [
        { name: 'kind', label: 'Kind', type: 'text', required: true },
        { name: 'url', label: 'URL', type: 'url', required: true, placeholder: 'https://...' },
        { name: 'mimeType', label: 'MIME type', type: 'text' },
        { name: 'title', label: 'Title', type: 'text' },
        { name: 'releaseDate', label: 'Release date', type: 'date' },
        { name: 'language', label: 'Language', type: 'text' },
    ],
    alternateTexts: [
        { name: 'kind', label: 'Kind', type: 'text', required: true, placeholder: 'Title' },
        { name: 'text', label: 'Text', type: 'text', required: true },
        { name: 'language', label: 'Language', type: 'text' },
    ],
    awards: [
        { name: 'body', label: 'Body', type: 'text', required: true },
        { name: 'name', label: 'Name', type: 'text', required: true },
        { name: 'category', label: 'Category', type: 'text' },
        { name: 'status', label: 'Status', type: 'text', placeholder: 'Won / Nominated' },
        { name: 'year', label: 'Year', type: 'number' },
        { name: 'language', label: 'Language', type: 'text' },
        { name: 'country', label: 'Country', type: 'text' },
    ],
    contentRatings: [
        { name: 'body', label: 'Body', type: 'text', required: true },
        { name: 'rating', label: 'Rating', type: 'text', required: true },
    ],
    customValues: [
        { name: 'key', label: 'Key', type: 'text', required: true },
        { name: 'value', label: 'Value', type: 'text' },
    ],
    references: [
        { name: 'kind', label: 'Kind', type: 'text', required: true, placeholder: 'homepage' },
        { name: 'source', label: 'Source', type: 'text', required: true, placeholder: 'Wikipedia' },
        { name: 'url', label: 'URL', type: 'url', required: true, placeholder: 'https://...' },
        { name: 'displayName', label: 'Display name', type: 'text' },
        { name: 'language', label: 'Language', type: 'text' },
    ],
};

const KIND_LABELS: Record<MetadataSubItemKey, string> = {
    genres: 'genre',
    images: 'image',
    relationships: 'relationship',
    externalIds: 'external ID',
    previews: 'preview',
    alternateTexts: 'alternate text',
    awards: 'award',
    contentRatings: 'content rating',
    customValues: 'custom value',
    references: 'reference',
};

type FieldValues = Record<string, string>;

interface SubItemFormDialogProps {
    kind: MetadataSubItemKey;
    mode: 'add' | 'edit';
    initialValues?: FieldValues;
    initialSubject?: SubjectSelection | null;
    pending: boolean;
    onCancel: () => void;
    onSubmit: (payload: SubItemPayload) => void;
}

function isAbsoluteUrl(value: string): boolean {
    try {
        const url = new URL(value);
        return url.protocol === 'http:' || url.protocol === 'https:';
    } catch {
        return false;
    }
}

export function SubItemFormDialog({
    kind,
    mode,
    initialValues,
    initialSubject,
    pending,
    onCancel,
    onSubmit,
}: SubItemFormDialogProps) {
    const fields = FIELDS_BY_KIND[kind];
    const isRelationship = kind === 'relationships';

    const [values, setValues] = useState<FieldValues>(() => {
        const base: FieldValues = {};
        for (const field of fields) {
            base[field.name] = initialValues?.[field.name] ?? '';
        }
        return base;
    });
    const [subject, setSubject] = useState<SubjectSelection | null>(initialSubject ?? null);
    const [submitted, setSubmitted] = useState(false);

    const errors = useMemo(() => {
        const result: Record<string, string> = {};
        for (const field of fields) {
            const raw = values[field.name]?.trim() ?? '';
            if (field.required && raw === '') {
                result[field.name] = `${field.label} is required.`;
            } else if (field.type === 'url' && raw !== '' && !isAbsoluteUrl(raw)) {
                result[field.name] = 'Enter an absolute http(s) URL.';
            } else if (field.type === 'number' && raw !== '' && Number.isNaN(Number(raw))) {
                result[field.name] = 'Enter a number.';
            }
        }
        return result;
    }, [fields, values]);

    const subjectInvalid = isRelationship && !isSubjectComplete(subject);
    const hasErrors = Object.keys(errors).length > 0 || subjectInvalid;

    const setValue = (name: string, value: string) => setValues((prev) => ({ ...prev, [name]: value }));

    const handleSubmit = () => {
        setSubmitted(true);
        if (hasErrors) {
            return;
        }
        onSubmit(buildPayload(kind, fields, values, subject));
    };

    const heading = `${mode === 'add' ? 'Add' : 'Edit'} ${KIND_LABELS[kind]}`;

    return (
        <Dialog open onOpenChange={(open) => (!open ? onCancel() : undefined)}>
            <DialogContent>
                <DialogHeader>
                    <DialogTitle>{heading}</DialogTitle>
                    <DialogDescription>
                        Adds this value as manual metadata, which takes priority over provider values.
                    </DialogDescription>
                </DialogHeader>
                <div className="metadata-inspector__dialog-body">
                    {fields.map((field) => {
                        const fieldId = `subitem-${field.name}`;
                        const showError = submitted && errors[field.name];
                        return (
                            <div key={field.name} className="metadata-inspector__dialog-field">
                                <Label htmlFor={fieldId}>
                                    {field.label}
                                    {field.required && <span aria-hidden="true"> *</span>}
                                </Label>
                                <Input
                                    id={fieldId}
                                    type={
                                        field.type === 'number' ? 'number' : field.type === 'date' ? 'date' : field.type
                                    }
                                    value={values[field.name] ?? ''}
                                    placeholder={field.placeholder}
                                    onChange={(e) => setValue(field.name, e.target.value)}
                                    error={!!showError}
                                    errorMessage={showError || undefined}
                                />
                            </div>
                        );
                    })}

                    {isRelationship && (
                        <div className="metadata-inspector__dialog-field">
                            <Label>
                                Subject <span aria-hidden="true">*</span>
                            </Label>
                            <SubjectPicker value={subject} onChange={setSubject} error={submitted && subjectInvalid} />
                            {submitted && subjectInvalid && (
                                <p className="metadata-inspector__field-error" role="alert">
                                    Pick an existing subject or enter a new subject name and kind.
                                </p>
                            )}
                        </div>
                    )}
                </div>
                <DialogFooter>
                    <Button variant="ghost" onClick={onCancel} disabled={pending}>
                        Cancel
                    </Button>
                    <Button variant="primary" onClick={handleSubmit} loading={pending}>
                        {mode === 'add' ? 'Add' : 'Save'}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    );
}

function isSubjectComplete(subject: SubjectSelection | null): boolean {
    if (!subject) {
        return false;
    }
    if (subject.mode === 'existing') {
        return subject.subjectId !== '';
    }
    return subject.subjectName.trim() !== '' && subject.subjectKind !== '';
}

function buildPayload(
    kind: MetadataSubItemKey,
    fields: FieldDef[],
    values: FieldValues,
    subject: SubjectSelection | null,
): SubItemPayload {
    const collected: Record<string, string | number> = {};
    for (const field of fields) {
        const raw = values[field.name]?.trim() ?? '';
        if (raw === '') {
            continue;
        }
        collected[field.name] = field.type === 'number' ? Number(raw) : raw;
    }

    const str = (name: string): string => collected[name] as string;
    const optStr = (name: string): string | undefined => collected[name] as string | undefined;
    const optNum = (name: string): number | undefined => collected[name] as number | undefined;

    switch (kind) {
        case 'genres': {
            const payload: GenreSubItemPayload = { name: str('name') };
            return payload;
        }
        case 'images': {
            const payload: ImageSubItemPayload = {
                type: str('type'),
                url: str('url'),
                mimeType: optStr('mimeType'),
                language: optStr('language'),
                width: optNum('width'),
                height: optNum('height'),
            };
            return payload;
        }
        case 'relationships': {
            const payload: RelationshipSubItemPayload = {
                kind: str('kind'),
                qualifier: optStr('qualifier'),
            };
            if (subject) {
                if (subject.mode === 'existing') {
                    payload.subjectId = subject.subjectId;
                } else {
                    payload.subjectName = subject.subjectName.trim();
                    payload.subjectKind = subject.subjectKind;
                }
            }
            return payload;
        }
        case 'externalIds': {
            const payload: ExternalIdSubItemPayload = {
                value: str('value'),
                source: optStr('source'),
            };
            return payload;
        }
        case 'previews': {
            const payload: PreviewSubItemPayload = {
                kind: str('kind'),
                url: str('url'),
                mimeType: optStr('mimeType'),
                title: optStr('title'),
                releaseDate: optStr('releaseDate'),
                language: optStr('language'),
            };
            return payload;
        }
        case 'alternateTexts': {
            const payload: AlternateTextSubItemPayload = {
                kind: str('kind'),
                text: str('text'),
                language: optStr('language'),
            };
            return payload;
        }
        case 'awards': {
            const payload: AwardSubItemPayload = {
                body: str('body'),
                name: str('name'),
                category: optStr('category'),
                status: optStr('status'),
                year: optNum('year'),
                language: optStr('language'),
                country: optStr('country'),
            };
            return payload;
        }
        case 'contentRatings': {
            const payload: ContentRatingSubItemPayload = {
                body: str('body'),
                rating: str('rating'),
            };
            return payload;
        }
        case 'customValues': {
            const payload: CustomMetadataValueSubItemPayload = {
                key: str('key'),
                value: optStr('value'),
            };
            return payload;
        }
        case 'references': {
            const payload: ReferenceSubItemPayload = {
                kind: str('kind'),
                source: str('source'),
                url: str('url'),
                displayName: optStr('displayName'),
                language: optStr('language'),
            };
            return payload;
        }
    }
}
