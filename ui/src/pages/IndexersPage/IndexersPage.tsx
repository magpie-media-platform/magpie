import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Database, Rss } from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { PullToRefresh } from '@/components/ui/PullToRefresh';

import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useIndexers } from '@/hooks/useIndexers';

import { IndexerManagement } from './IndexerManagement';
import { FeedsManagement } from './FeedsManagement';

import './IndexersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

export function IndexersPage() {
    useDocumentTitle('Indexers');
    const [searchParams, setSearchParams] = useSearchParams();

    const activeTab = searchParams.get('tab') || 'indexers';
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === 'indexers') {
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

    const totalIndexers = indexers.length;
    const totalFeeds = feeds.length;

    const handleRefresh = useCallback(async () => {
        await refetch();
    }, [refetch]);

    return (
        <div className="indexers-page" data-page="indexers">
            <PullToRefresh onRefresh={handleRefresh} className="indexers-page__pull-to-refresh">
                <header className="indexers-page__header">
                    <div className="indexers-page__header-left">
                        <Database
                            weight="duotone"
                            className="indexers-page__icon"
                            aria-hidden="true"
                            data-testid="indexers-page__icon"
                        />
                        <h1 className="indexers-page__title">Indexers</h1>
                        <span className="page-count indexers-page__count" data-testid="indexers-page__count">
                            {totalIndexers}
                        </span>
                    </div>
                </header>

                <Tabs value={activeTab} onValueChange={setActiveTab} className="indexers-page__tabs">
                    <TabsList>
                        <TabsTrigger value="indexers" data-tab="indexers">
                            <Database weight="duotone" aria-hidden="true" />
                            Indexers
                            <span className="tab-count indexers-page__tab-count" data-testid="indexers-page__tab-count">
                                {totalIndexers}
                            </span>
                        </TabsTrigger>
                        <TabsTrigger value="feeds" data-tab="feeds">
                            <Rss weight="duotone" aria-hidden="true" />
                            Feeds
                            <span className="tab-count indexers-page__tab-count" data-testid="indexers-page__tab-count">
                                {totalFeeds}
                            </span>
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="indexers" className="indexers-page__tab-content">
                        <IndexerManagement />
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
