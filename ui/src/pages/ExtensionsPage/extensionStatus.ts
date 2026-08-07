import type { BadgeVariant } from '@/components/ui/Badge';
import type { ExtensionItem } from '@/hooks/useExtensionManagement';

export function getStatusVariant(ext: ExtensionItem): BadgeVariant {
    if (ext.disabledReason) {
        return 'unhealthy';
    }
    if (!ext.enabled) {
        return 'unknown';
    }
    return 'healthy';
}

export function getStatusLabel(ext: ExtensionItem): string {
    if (ext.disabledReason) {
        return 'Failed';
    }
    if (!ext.enabled) {
        return 'Disabled';
    }
    return 'Enabled';
}
