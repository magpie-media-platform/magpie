import { useMemo } from 'react';

import { useAuth } from '@/auth';
import { useUsers } from '@/hooks/useUsers';

export function useUserMap(): Map<string, string> {
    const { user, hasPermission } = useAuth();
    const { users } = useUsers(undefined, { enabled: hasPermission('users.manage') });

    return useMemo(() => {
        const map = new Map<string, string>();
        if (user) {
            map.set(user.id, user.username);
        }
        for (const u of users) {
            map.set(u.id, u.username);
        }
        return map;
    }, [user, users]);
}
