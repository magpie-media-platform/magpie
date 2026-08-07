import { Fragment } from 'react';

import { CaretRightIcon, ShapesIcon, WarningCircleIcon } from '@phosphor-icons/react';

import { Badge } from '@/components/ui/Badge';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { EmptyState } from '@/components/ui/EmptyState';
import { formatLibraryItemCount } from '@/components/ui/LibraryCountCard/formatLibraryItemCount';
import { PageHeader } from '@/components/ui/PageHeader';
import { Skeleton } from '@/components/ui/Skeleton';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import {
    useContentLevelCounts,
    useLibraryCounts,
    useLibraryTypes,
    type LibraryCounts,
    type LibraryLevelDefinition,
    type LibraryTypeDefinition,
} from '@/hooks/useLibraryTypes';

import { buildContentLevelPaths } from './contentLevelPaths';

import './ContentTypesPage.css';

function isLeafLevel(levels: LibraryLevelDefinition[], level: LibraryLevelDefinition): boolean {
    return !levels.some((candidate) => candidate.parentSlug === level.slug);
}

function levelDepth(levels: LibraryLevelDefinition[], level: LibraryLevelDefinition): number {
    let depth = 0;
    let current: LibraryLevelDefinition | undefined = level;
    const seen = new Set<string>();
    while (current?.parentSlug && !seen.has(current.slug)) {
        seen.add(current.slug);
        const parent = levels.find((candidate) => candidate.slug === current!.parentSlug);
        if (!parent) {
            break;
        }
        depth += 1;
        current = parent;
    }
    return depth;
}

function orderLevels(levels: LibraryLevelDefinition[]): LibraryLevelDefinition[] {
    return [...levels].sort((a, b) => levelDepth(levels, a) - levelDepth(levels, b) || a.slug.localeCompare(b.slug));
}

function typeHandledFileTypes(levels: LibraryLevelDefinition[]): string[] {
    return [
        ...new Set(
            levels.flatMap((level) => [...(level.acceptedFileTypes ?? []), ...(level.acceptedSidecarTypes ?? [])]),
        ),
    ].sort((a, b) => a.localeCompare(b));
}

function formatLevelCount(count: number): string {
    return count === 1 ? '1 level' : `${count.toLocaleString()} levels`;
}

function ItemCount({
    count,
    className,
    skeletonTestId,
}: {
    count: number | undefined;
    className: string;
    skeletonTestId: string;
}) {
    const countLabel = formatLibraryItemCount(count);

    return countLabel === undefined ? (
        <Skeleton className="content-types__count-skeleton" data-testid={skeletonTestId} />
    ) : (
        <span className={className}>{countLabel}</span>
    );
}

function LevelChip({ level, leaf }: { level: LibraryLevelDefinition; leaf: boolean }) {
    return (
        <span
            className="content-types__chain-node"
            data-leaf={leaf || undefined}
            data-level-slug={level.slug}
            data-testid="content-types__chain-node"
        >
            <CdnPhosphorIcon
                name={level.iconName}
                className="content-types__chain-icon"
                weight="duotone"
                aria-hidden="true"
            />
            <span className="content-types__chain-name">{level.displayName}</span>
            <code className="content-types__slug">{level.slug}</code>
            {leaf && (
                <Badge variant="success" size="sm" data-testid="content-types__leaf-badge">
                    Leaf
                </Badge>
            )}
        </span>
    );
}

function HierarchyChain({
    levels,
    supportedLevels = levels,
}: {
    levels: LibraryLevelDefinition[];
    supportedLevels?: LibraryLevelDefinition[];
}) {
    const paths = buildContentLevelPaths(levels, supportedLevels);

    return (
        <div className="content-types__chain" data-testid="content-types__chain">
            {paths.map((path) => (
                <div
                    className="content-types__chain-path"
                    data-testid="content-types__chain-path"
                    key={path.map((level) => level.slug).join('/')}
                >
                    {path.map((level, index) => (
                        <Fragment key={level.slug}>
                            {index > 0 && (
                                <CaretRightIcon
                                    className="content-types__chain-arrow"
                                    data-testid="content-types__chain-arrow"
                                    weight="bold"
                                    aria-hidden="true"
                                />
                            )}
                            <LevelChip level={level} leaf={index === path.length - 1} />
                        </Fragment>
                    ))}
                </div>
            ))}
        </div>
    );
}

function BadgeList({ label, values }: { label: string; values: string[] }) {
    if (values.length === 0) {
        return null;
    }

    return (
        <div className="content-types__facet">
            <span className="content-types__facet-label">{label}</span>
            <div className="content-types__facet-values">
                {values.map((value) => (
                    <Badge key={value} variant="info" size="sm" badgeStyle="outline">
                        {value}
                    </Badge>
                ))}
            </div>
        </div>
    );
}

function LevelDetails({
    levels,
    level,
    itemCount,
}: {
    levels: LibraryLevelDefinition[];
    level: LibraryLevelDefinition;
    itemCount: number | undefined;
}) {
    const leaf = isLeafLevel(levels, level);
    const facets = (level.standardMetadataFields ?? []).map((field) => field.displayName ?? field.field);
    const customFields = level.customMetadataFields ?? [];
    const factFields = level.standardFactFields ?? [];

    return (
        <div className="content-types__level" data-level-slug={level.slug} data-testid="content-types__level">
            <div className="content-types__level-heading">
                <CdnPhosphorIcon
                    name={level.iconName}
                    className="content-types__level-icon"
                    weight="duotone"
                    aria-hidden="true"
                />
                <span className="content-types__level-name">{level.displayName}</span>
                <code className="content-types__slug" data-testid="content-types__level-slug">
                    {level.slug}
                </code>
                {leaf && (
                    <Badge variant="success" size="sm">
                        Leaf
                    </Badge>
                )}
            </div>
            <ItemCount
                count={itemCount}
                className="content-types__level-count"
                skeletonTestId="content-types__level-count-skeleton"
            />

            <BadgeList label="Supported facets" values={facets} />

            <BadgeList label="Accepted files" values={level.acceptedFileTypes ?? []} />

            <BadgeList label="Sidecars" values={level.acceptedSidecarTypes ?? []} />

            {level.subfolder && level.parentSlug === null && (
                <div className="content-types__facet">
                    <span className="content-types__facet-label">Root subfolder</span>
                    <div className="content-types__facet-values">
                        <code className="content-types__slug" data-testid="content-types__level-subfolder">
                            {level.subfolder}
                        </code>
                    </div>
                </div>
            )}

            {customFields.length > 0 && (
                <div className="content-types__facet">
                    <span className="content-types__facet-label">Custom fields</span>
                    <div className="content-types__facet-values">
                        {customFields.map((field) => (
                            <Badge key={field.key} variant="primary" size="sm" badgeStyle="outline">
                                {field.displayName ?? field.key}
                                {field.format ? ` (${field.format})` : ''}
                            </Badge>
                        ))}
                    </div>
                </div>
            )}

            {factFields.length > 0 && (
                <div className="content-types__facet">
                    <span className="content-types__facet-label">Standard facts</span>
                    <div className="content-types__facet-values">
                        {factFields.map((field) => (
                            <Badge key={field.key} variant="default" size="sm" badgeStyle="outline">
                                {field.displayName ?? field.key}
                                {field.format ? ` (${field.format})` : ''}
                            </Badge>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
}

function ContentTypeSection({ type, itemCount }: { type: LibraryTypeDefinition; itemCount: number | undefined }) {
    const orderedLevels = orderLevels(type.levels);
    const { counts: levelCounts } = useContentLevelCounts(type.slug, orderedLevels);
    const handledFileTypes = typeHandledFileTypes(type.levels);

    return (
        <Card className="content-types__card" data-content-type={type.slug} data-testid="content-types__type">
            <CardHeader className="content-types__card-header">
                <span className="content-types__card-icon">
                    <CdnPhosphorIcon name={type.iconName} weight="duotone" aria-hidden="true" />
                </span>
                <div className="content-types__card-titles">
                    <h2 className="content-types__card-title">{type.name}</h2>
                    <span className="content-types__card-subtitle">
                        <code className="content-types__slug">{type.slug}</code>
                        <span className="content-types__card-plural">{type.pluralName}</span>
                    </span>
                    <ItemCount
                        count={itemCount}
                        className="content-types__card-count"
                        skeletonTestId="content-types__type-count-skeleton"
                    />
                </div>
            </CardHeader>

            <CardBody className="content-types__card-body">
                <section className="content-types__block">
                    <h3 className="content-types__block-title">Hierarchy</h3>
                    <HierarchyChain levels={type.levels} />
                </section>

                <section className="content-types__block">
                    <h3 className="content-types__block-title">Levels</h3>
                    <div className="content-types__levels">
                        {orderedLevels.map((level) => (
                            <LevelDetails
                                key={level.slug}
                                levels={type.levels}
                                level={level}
                                itemCount={levelCounts[level.slug]}
                            />
                        ))}
                    </div>
                </section>

                <section className="content-types__block">
                    <h3 className="content-types__block-title">File handling</h3>
                    <div className="content-types__files">
                        <BadgeList label="Accepted files and sidecars" values={handledFileTypes} />
                        {handledFileTypes.length === 0 && (
                            <span className="content-types__empty-note">
                                No level of this content type handles any file.
                            </span>
                        )}
                    </div>
                </section>
            </CardBody>
        </Card>
    );
}

function TypeIndex({ types, counts }: { types: LibraryTypeDefinition[]; counts: LibraryCounts }) {
    return (
        <Card className="content-types__index" data-testid="content-types__index">
            <CardHeader className="content-types__index-header">
                <h2 className="content-types__block-title">All types</h2>
            </CardHeader>
            <CardBody className="content-types__index-body">
                <table className="content-types__index-table">
                    <thead>
                        <tr>
                            <th scope="col">Type</th>
                            <th scope="col">Accepted files and sidecars</th>
                            <th scope="col">Levels</th>
                            <th scope="col">Items</th>
                        </tr>
                    </thead>
                    <tbody>
                        {types.map((type) => {
                            return (
                                <tr
                                    key={type.slug}
                                    data-content-type={type.slug}
                                    data-testid="content-types__index-row"
                                >
                                    <td>
                                        <span className="content-types__index-type">
                                            <CdnPhosphorIcon
                                                name={type.iconName}
                                                className="content-types__index-icon"
                                                weight="duotone"
                                                aria-hidden="true"
                                            />
                                            <span className="content-types__index-name">{type.name}</span>
                                            <code className="content-types__slug">{type.slug}</code>
                                        </span>
                                    </td>
                                    <td>{typeHandledFileTypes(type.levels).join(', ') || 'None'}</td>
                                    <td>{formatLevelCount(type.levels.length)}</td>
                                    <td>
                                        <ItemCount
                                            count={counts[type.slug]}
                                            className="content-types__index-count"
                                            skeletonTestId="content-types__index-count-skeleton"
                                        />
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </CardBody>
        </Card>
    );
}

export function ContentTypesPage() {
    useDocumentTitle('Content Types');
    const { contentTypes, isLoading, error } = useLibraryTypes();
    const { counts: typeCounts } = useLibraryCounts(contentTypes, []);

    return (
        <div className="content-types-page" data-page="content-types">
            <PageHeader
                className="content-types-page__header"
                title="Content Types"
                description="Content types available on this instance. Definitions are provided by extensions and are read-only."
                icon={<ShapesIcon weight="duotone" aria-hidden="true" />}
            />

            {error ? (
                <EmptyState icon={<WarningCircleIcon weight="duotone" />} title="Failed to load content types" />
            ) : isLoading ? (
                <EmptyState title="Loading content types..." />
            ) : contentTypes.length === 0 ? (
                <EmptyState
                    icon={<ShapesIcon weight="duotone" />}
                    title="No content types"
                    description="No content type definitions are available. An extension that provides one must be installed."
                />
            ) : (
                <div className="content-types-page__content">
                    <TypeIndex types={contentTypes} counts={typeCounts} />
                    <div className="content-types-page__sections">
                        {contentTypes.map((type) => (
                            <ContentTypeSection key={type.slug} type={type} itemCount={typeCounts[type.slug]} />
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
}

ContentTypesPage.displayName = 'ContentTypesPage';

export default ContentTypesPage;
