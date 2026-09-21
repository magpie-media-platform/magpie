import { forwardRef, type ComponentPropsWithoutRef, useMemo, useCallback, useEffect, useState } from 'react';
import { ImageBrokenIcon } from '@phosphor-icons/react';
import { type ColumnDef } from '@tanstack/react-table';

import { DataTable, type DataTableProps } from '../DataTable';
import { Badge } from '../Badge';
import { EmptyState } from '@/components/ui/EmptyState';
import { TagsCell } from '../TagsCell';
import { resolveSubjectKindLabel } from '@/types';

import './SubjectList.css';

export interface SubjectItem {
    id: string;

    title: string;

    imageUrl?: string;

    kind: string;

    kindLabel?: string;

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
    const [failed, setFailed] = useState(false);

    useEffect(() => {
        setFailed(false);
    }, [imageUrl]);

    if (!imageUrl || failed) {
        return (
            <div className="subject-list__image subject-list__image--placeholder">
                <ImageBrokenIcon weight="thin" aria-hidden="true" />
            </div>
        );
    }

    return (
        <img
            src={imageUrl}
            alt={`${title} image`}
            className="subject-list__image"
            loading="lazy"
            onError={() => setFailed(true)}
        />
    );
}

function TitleCell({ title }: { title: string }) {
    return (
        <div className="subject-list__title-cell">
            <span className="subject-list__title" title={title}>
                {title}
            </span>
        </div>
    );
}

function KindCell({ kind, label }: { kind: string; label?: string }) {
    return (
        <Badge variant="default" size="sm">
            {label ?? resolveSubjectKindLabel(kind)}
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
            manualSorting,
            sorting,
            onSortingChange,
            enablePagination = false,
            pageSize = 25,
            manualPagination,
            pagination,
            onPaginationChange,
            rowCount,
            emptyState,
            keepHeaderWhenEmpty,
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
                    meta: { mobileRole: 'header' },
                    size: 300,
                    cell: ({ row }) => <TitleCell title={row.original.title} />,
                },
                {
                    id: 'kind',
                    accessorKey: 'kind',
                    header: 'Kind',
                    size: 100,
                    cell: ({ row }) => <KindCell kind={row.original.kind} label={row.original.kindLabel} />,
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

        const defaultEmptyState = <EmptyState title="No matches found" size="sm" />;

        return (
            <div ref={ref} className={classes} data-component="subject-list" {...props}>
                <DataTable<SubjectItem>
                    columns={columns}
                    data={items}
                    onRowClick={onSubjectClick ? handleRowClick : undefined}
                    loading={loading}
                    enableSorting={enableSorting}
                    manualSorting={manualSorting}
                    sorting={sorting}
                    onSortingChange={onSortingChange}
                    enablePagination={enablePagination}
                    pageSize={pageSize}
                    manualPagination={manualPagination}
                    pagination={pagination}
                    onPaginationChange={onPaginationChange}
                    rowCount={rowCount}
                    getRowId={getRowId}
                    emptyState={emptyState ?? defaultEmptyState}
                    keepHeaderWhenEmpty={keepHeaderWhenEmpty}
                />
            </div>
        );
    },
);

SubjectList.displayName = 'SubjectList';

export default SubjectList;
