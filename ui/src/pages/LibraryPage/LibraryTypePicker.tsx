import { CdnPhosphorIcon } from '@/components/ui/CdnPhosphorIcon';
import { PageHeader } from '@/components/ui/PageHeader';
import { LibraryCountCard } from '@/components/ui/LibraryCountCard/LibraryCountCard';
import { formatLibraryItemCount } from '@/components/ui/LibraryCountCard/formatLibraryItemCount';
import {
    useContentLevelCounts,
    type LibraryLevelDefinition,
    type LibraryTypeDefinition,
} from '@/hooks/useLibraryTypes';

import './LibraryTypePicker.css';

interface LevelCardProps {
    contentTypeSlug: string;
    level: LibraryLevelDefinition;
    count: number | undefined;
}

function LevelCard({ contentTypeSlug, level, count }: LevelCardProps) {
    return (
        <LibraryCountCard
            to={`/library/content/${contentTypeSlug}/${level.slug}`}
            icon={<CdnPhosphorIcon name={level.iconName} weight="duotone" />}
            name={level.displayName}
            countLabel={formatLibraryItemCount(count)}
            dataComponent="level-card"
            linkDataAttributes={{ 'data-level-slug': level.slug }}
            iconTestId="library-type-picker__icon"
            skeletonTestId="library-type-picker__count-skeleton"
            classNames={{
                link: 'library-type-picker__link',
                card: 'library-type-picker__card',
                content: 'library-type-picker__content',
                icon: 'library-type-picker__icon',
                info: 'library-type-picker__info',
                name: 'library-type-picker__name',
                count: 'library-type-picker__count',
                skeleton: 'library-type-picker__count-skeleton',
            }}
        />
    );
}

interface LibraryTypePickerProps {
    contentType: LibraryTypeDefinition;
    rootLevels: LibraryLevelDefinition[];
}

export function LibraryTypePicker({ contentType, rootLevels }: LibraryTypePickerProps) {
    const { counts } = useContentLevelCounts(contentType.slug, rootLevels);

    return (
        <div className="library-type-picker" data-page="library-type-picker" data-content-type={contentType.slug}>
            <PageHeader
                className="library-type-picker__header"
                title={contentType.name}
                description="Choose a category to browse."
                icon={
                    <CdnPhosphorIcon
                        name={contentType.iconName}
                        weight="duotone"
                        aria-hidden="true"
                        data-testid="library-type-picker__page-icon"
                    />
                }
            />

            <div className="library-type-picker__grid">
                {rootLevels.map((level) => (
                    <LevelCard
                        key={level.slug}
                        contentTypeSlug={contentType.slug}
                        level={level}
                        count={counts[level.slug]}
                    />
                ))}
            </div>
        </div>
    );
}

LibraryTypePicker.displayName = 'LibraryTypePicker';

export default LibraryTypePicker;
