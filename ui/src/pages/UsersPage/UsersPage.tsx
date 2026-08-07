import { useCallback, useEffect, useMemo, useState } from 'react';
import { UsersIcon, KeyIcon } from '@phosphor-icons/react';

import { useAuth, Role } from '@/auth';
import { PageHeader } from '@/components/ui/PageHeader';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/Tabs';
import { PullToRefresh } from '@/components/ui/PullToRefresh';
import { useDocumentTitle } from '@/hooks/useDocumentTitle';
import { useUrlTabState } from '@/hooks/useUrlTabState';
import { useUsers } from '@/hooks/useUsers';
import { useApiKeys, useAllApiKeys } from '@/hooks/useApiKeys';

import { UserManagement } from './UserManagement';
import { ApiKeyManagement, type ApiKeyScope } from './ApiKeyManagement';

import './UsersPage.css';
import '@/styles/card-actions.css';
import '@/styles/list-toolbar.css';

const USER_TABS = ['users', 'api-keys'] as const;
const USERS_PAGE_SIZE = 25;

export function UsersPage() {
    useDocumentTitle('Users');
    const [activeTab, setUrlActiveTab] = useUrlTabState({ defaultTab: 'users', allowedTabs: USER_TABS });
    const setActiveTab = useCallback((tab: string) => setUrlActiveTab(tab, { replace: true }), [setUrlActiveTab]);
    const [apiKeyScope, setApiKeyScope] = useState<ApiKeyScope>('mine');

    const { hasRole } = useAuth();
    const canViewAllApiKeys = hasRole(Role.Admin);
    const isViewingAllApiKeys = canViewAllApiKeys && apiKeyScope === 'all';
    const [usersPage, setUsersPage] = useState(1);
    const userQueryParams = useMemo(() => ({ pageNumber: usersPage, pageSize: USERS_PAGE_SIZE }), [usersPage]);
    const usersQuery = useUsers(userQueryParams);
    const { totalItems: totalUsers, isLoading: areUsersLoading, refetch: refetchUsers } = usersQuery;
    const usersPageCount = Math.max(1, Math.ceil(totalUsers / USERS_PAGE_SIZE));
    const { apiKeys: myApiKeys, refetch: refetchMyKeys } = useApiKeys();
    const { apiKeys: allApiKeys, refetch: refetchAllKeys } = useAllApiKeys(isViewingAllApiKeys);
    const apiKeyTabCount = isViewingAllApiKeys ? allApiKeys.length : myApiKeys.length;

    useEffect(() => {
        if (!areUsersLoading && usersPage > usersPageCount) {
            setUsersPage(usersPageCount);
        }
    }, [areUsersLoading, usersPage, usersPageCount]);

    const handleRefresh = useCallback(async () => {
        await refetchUsers();
        if (isViewingAllApiKeys) {
            await refetchAllKeys();
        } else {
            await refetchMyKeys();
        }
    }, [refetchUsers, isViewingAllApiKeys, refetchAllKeys, refetchMyKeys]);

    return (
        <div className="users-page" data-page="users">
            <PullToRefresh onRefresh={handleRefresh} className="users-page__pull-to-refresh">
                <PageHeader
                    className="users-page__header"
                    title="Users"
                    icon={<UsersIcon weight="duotone" aria-hidden="true" data-testid="users-page__icon" />}
                />

                <Tabs value={activeTab} onValueChange={setActiveTab} className="users-page__tabs">
                    <TabsList>
                        <TabsTrigger value="users" data-tab="users">
                            <UsersIcon weight="duotone" aria-hidden="true" />
                            Users
                            <span className="tab-count users-page__tab-count" data-testid="users-page__tab-count">
                                {totalUsers}
                            </span>
                        </TabsTrigger>
                        <TabsTrigger value="api-keys" data-tab="api-keys">
                            <KeyIcon weight="duotone" aria-hidden="true" />
                            API Keys
                            <span className="tab-count users-page__tab-count" data-testid="users-page__tab-count">
                                {apiKeyTabCount}
                            </span>
                        </TabsTrigger>
                    </TabsList>

                    <TabsContent value="users" className="users-page__tab-content">
                        <UserManagement
                            queryParams={userQueryParams}
                            page={usersPage}
                            pageCount={usersPageCount}
                            onPageChange={setUsersPage}
                        />
                    </TabsContent>

                    <TabsContent value="api-keys" className="users-page__tab-content">
                        <ApiKeyManagement scope={apiKeyScope} onScopeChange={setApiKeyScope} />
                    </TabsContent>
                </Tabs>
            </PullToRefresh>
        </div>
    );
}

UsersPage.displayName = 'UsersPage';

export default UsersPage;
