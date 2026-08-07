import { normalizedHealthStatuses } from '@/types/healthStatus';

export function mapStatusParam(param: string | null): string {
    if (!param) {
        return '';
    }
    return normalizedHealthStatuses[param.toLowerCase()] ?? '';
}
