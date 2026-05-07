import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback } from 'react';
import { ImageBroken } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { DataTable, type DataTableProps } from '../DataTable';
import { Badge } from '../Badge';
import { EmptyState } from '@/components/ui/EmptyState';
import { TagsCell } from '../TagsCell';
import { formatSubjectKindLabel } from '@/types';

import './SubjectList.css';

export interface SubjectItem {
    id: string;

    title: string;

    imageUrl?: string;

    kind: string;

    tags?: string[];

    createdAt?: string;
}

export interface SubjectListProps
    extends Omit<ComponentPropsWithoutRef<'div'>, 'children'>, Omit<DataTableProps<SubjectItem>, 'columns' | 'data'> {
    items: SubjectItem[];

    onSubjectClick?: (item: SubjectItem) => void;

    loading?: boolean;
}

function ImageCell({ imageUrl, title }: { imageUrl?: string; title: string }) {
    if (!imageUrl) {
        return (
            <div className="subject-list__image subject-list__image--placeholder">
                <ImageBroken weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return <img src={imageUrl} alt={`${title} image`} className="subject-list__image" loading="lazy" />;
}

function TitleCell({ title }: { title: string }) {
    return (
        <div className="subject-list__title-cell">
            <span className="subject-list__title">{title}</span>
        </div>
    );
}

function KindCell({ kind }: { kind: string }) {
    return (
        <Badge variant="default" size="sm">
            {formatSubjectKindLabel(kind)}
        </Badge>
    );
}

export const SubjectList = forwardRef<HTMLDivElement, SubjectListProps>(
    (
        {
            items,
            onSubjectClick,
            loading = false,
            className,
            enableSorting = true,
            enablePagination = false,
            pageSize = 25,
            emptyState,
            ...props
        },
        ref,
    ) => {
        const columns: ColumnDef<SubjectItem>[] = useMemo(
            () => [
                {
                    id: 'image',
                    accessorKey: 'imageUrl',
                    header: '',
                    enableSorting: false,
                    size: 60,
                    cell: ({ row }) => <ImageCell imageUrl={row.original.imageUrl} title={row.original.title} />,
                },
                {
                    id: 'title',
                    accessorKey: 'title',
                    header: 'Title',
                    size: 300,
                    cell: ({ row }) => <TitleCell title={row.original.title} />,
                },
                {
                    id: 'kind',
                    accessorKey: 'kind',
                    header: 'Kind',
                    size: 100,
                    cell: ({ row }) => <KindCell kind={row.original.kind} />,
                },
                {
                    id: 'tags',
                    accessorKey: 'tags',
                    header: 'Tags',
                    enableSorting: false,
                    size: 200,
                    cell: ({ row }) => <TagsCell tags={row.original.tags} showEmptyLabel />,
                },
            ],
            [],
        );

        const handleRowClick = useCallback(
            (row: SubjectItem) => {
                if (onSubjectClick) {
                    onSubjectClick(row);
                }
            },
            [onSubjectClick],
        );

        const getRowId = useCallback((row: SubjectItem) => row.id, []);

        const classes = ['subject-list', className].filter(Boolean).join(' ');

        const defaultEmptyState = <EmptyState title="No subjects found" size="sm" />;

        return (
            <div ref={ref} className={classes} data-component="subject-list" {...props}>
                <DataTable<SubjectItem>
                    columns={columns}
                    data={items}
                    onRowClick={onSubjectClick ? handleRowClick : undefined}
                    loading={loading}
                    enableSorting={enableSorting}
                    enablePagination={enablePagination}
                    pageSize={pageSize}
                    getRowId={getRowId}
                    emptyState={emptyState ?? defaultEmptyState}
                />
            </div>
        );
    },
);

SubjectList.displayName = 'SubjectList';

export default SubjectList;
