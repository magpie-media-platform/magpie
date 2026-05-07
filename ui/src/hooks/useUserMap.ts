import { useMemo } from 'react';

import { useAuth } from '@/auth';
import { useUsers } from '@/hooks/useUsers';

export function useUserMap(): Map<string, string> {
    const { user } = useAuth();
    const { users } = useUsers();

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
