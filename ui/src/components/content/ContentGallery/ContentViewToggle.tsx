import { GridFourIcon, ListIcon } from '@phosphor-icons/react';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/Tabs';

import './ContentGallery.css';

export function ContentViewToggle({
    view,
    onChange,
    testId,
    galleryLabel = 'Gallery',
    label = 'Content view',
}: {
    view: string;
    onChange: (value: string) => void;
    testId?: string;
    galleryLabel?: string;
    label?: string;
}) {
    return (
        <Tabs className="content-view-toggle" value={view} onValueChange={onChange}>
            <TabsList fits={['content']} aria-label={label} data-testid={testId}>
                <TabsTrigger value="list" data-action="view-list">
                    <ListIcon weight="bold" aria-hidden="true" />
                    List
                </TabsTrigger>
                <TabsTrigger value="gallery" data-action="view-gallery">
                    <GridFourIcon weight="bold" aria-hidden="true" />
                    {galleryLabel}
                </TabsTrigger>
            </TabsList>
        </Tabs>
    );
}
