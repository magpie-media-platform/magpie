import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { DatabaseIcon, ListPlusIcon, RssIcon } from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { PageHeader } from '@/components/ui/PageHeader';
import { PullToRefresh } from '@/components/ui/PullToRefresh';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useIndexers } from '@/hooks/useIndexers';

import { ConfiguredManagement } from './ConfiguredManagement';
import { AvailableCatalog } from './AvailableCatalog';
import { FeedsManagement } from './FeedsManagement';

import './IndexersPage.css';
import '@/styles/card-actions.css';

export function IndexersPage() {
    useDocumentTitle('Indexers');
    const [searchParams, setSearchParams] = useSearchParams();

    const activeTab = searchParams.get('tab') || 'configured';
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === 'configured') {
                        next.delete('tab');
                    } else {
                        next.set('tab', tab);
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const { indexers, feeds, refetch } = useIndexers();

    const configuredCount = indexers.filter((i) => (i.connectionCount ?? 0) > 0).length;
    const availableCount = indexers.length;
    const totalFeeds = feeds.length;

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    return (
        <div className="indexers-page" data-page="indexers">
            <PullToRefresh onRefresh={handleRefresh} className="indexers-page__pull-to-refresh">
                <PageHeader
                    className="indexers-page__header"
                    title="Indexers"
                    description="Connections to release indexers and feeds."
                    icon={<DatabaseIcon weight="duotone" aria-hidden="true" data-testid="indexers-page__icon" />}
                />

                <Tabs value={activeTab} onValueChange={setActiveTab} className="indexers-page__tabs">
                    <TabsList>
                        <TabsTrigger value="configured" data-tab="configured">
                            <DatabaseIcon weight="duotone" aria-hidden="true" />
                            Configured
                            <span className="tab-count indexers-page__tab-count" data-testid="indexers-page__tab-count">
                                {configuredCount}
                            </span>
                        </TabsTrigger>
                        <TabsTrigger value="available" data-tab="available">
                            <ListPlusIcon weight="duotone" aria-hidden="true" />
                            Available
                            <span className="tab-count indexers-page__tab-count" data-testid="indexers-page__tab-count">
                                {availableCount}
                            </span>
                        </TabsTrigger>
                        <TabsTrigger value="feeds" data-tab="feeds">
                            <RssIcon weight="duotone" aria-hidden="true" />
                            Feeds
                            <span className="tab-count indexers-page__tab-count" data-testid="indexers-page__tab-count">
                                {totalFeeds}
                            </span>
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="configured" className="indexers-page__tab-content">
                        <ConfiguredManagement onGoToCatalog={() => setActiveTab('available')} />
                    </TabsContent>

                    <TabsContent value="available" className="indexers-page__tab-content">
                        <AvailableCatalog />
                    </TabsContent>

                    <TabsContent value="feeds" className="indexers-page__tab-content">
                        <FeedsManagement />
                    </TabsContent>
                </Tabs>
            </PullToRefresh>
        </div>
    );
}

IndexersPage.displayName = 'IndexersPage';

export default IndexersPage;
