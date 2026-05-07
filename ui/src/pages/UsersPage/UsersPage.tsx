import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Users, Key } from '@phosphor-icons/react';

import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUsers } from '@/hooks/useUsers';
import { useApiKeys } from '@/hooks/useApiKeys';

import { UserManagement } from './UserManagement';
import { ApiKeyManagement } from './ApiKeyManagement';

import './UsersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

export function UsersPage() {
    useDocumentTitle('Users');
    const [searchParams, setSearchParams] = useSearchParams();

    const activeTab = searchParams.get('tab') || 'users';
    const setActiveTab = useCallback(
        (tab: string) => {
            setSearchParams(
                (prev) => {
                    const next = new URLSearchParams(prev);
                    if (tab === 'users') {
                        next.delete('tab');
                    } else {
                        next.set('tab', tab);
                    }
                    if (tab !== 'users') {
                        next.delete('role');
                        next.delete('enabled');
                    }
                    return next;
                },
                { replace: true },
            );
        },
        [setSearchParams],
    );

    const { users, refetch: refetchUsers } = useUsers();
    const { apiKeys: myApiKeys, refetch: refetchMyKeys } = useApiKeys();

    const handleRefresh = useCallback(async () => {
        await refetchUsers();
        await refetchMyKeys();
    }, [refetchUsers, refetchMyKeys]);

    return (
        <div className="users-page" data-page="users">
            <PullToRefresh onRefresh={handleRefresh} className="users-page__pull-to-refresh">
                <header className="users-page__header">
                    <div className="users-page__header-left">
                        <Users
                            weight="duotone"
                            className="users-page__icon"
                            aria-hidden="true"
                            data-testid="users-page__icon"
                        />
                        <h1 className="users-page__title">Users</h1>
                        <span className="page-count users-page__count" data-testid="users-page__count">
                            {users.length}
                        </span>
                    </div>
                </header>

                <Tabs value={activeTab} onValueChange={setActiveTab} className="users-page__tabs">
                    <TabsList>
                        <TabsTrigger value="users" data-tab="users">
                            <Users weight="duotone" aria-hidden="true" />
                            Users
                            <span className="tab-count users-page__tab-count" data-testid="users-page__tab-count">
                                {users.length}
                            </span>
                        </TabsTrigger>
                        <TabsTrigger value="api-keys" data-tab="api-keys">
                            <Key weight="duotone" aria-hidden="true" />
                            API Keys
                            <span className="tab-count users-page__tab-count" data-testid="users-page__tab-count">
                                {myApiKeys.length}
                            </span>
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="users" className="users-page__tab-content">
                        <UserManagement />
                    </TabsContent>

                    <TabsContent value="api-keys" className="users-page__tab-content">
                        <ApiKeyManagement />
                    </TabsContent>
                </Tabs>
            </PullToRefresh>
        </div>
    );
}

UsersPage.displayName = 'UsersPage';

export default UsersPage;
