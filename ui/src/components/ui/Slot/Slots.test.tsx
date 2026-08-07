import { render } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AdminBackupsSlot, AdminExtensionsSlot, AdminProvidersSlot, AdminUsersSlot } from './AdminSlots';
import { CalendarMainSlot } from './CalendarSlots';
import {
    ContentDetailsActionsSlot,
    ContentDetailsFilesSlot,
    ContentDetailsHeaderSlot,
    ContentDetailsHistorySlot,
    ContentDetailsMetadataSlot,
    ContentDetailsOverviewSlot,
    ContentDetailsTabsSlot,
    ContentListActionsSlot,
    ContentListColumnsSlot,
    ContentListSlot,
} from './ContentSlots';
import { DashboardMainSlot, DashboardWidgetsSlot } from './DashboardSlots';
import { DownloadQueueActionsSlot, DownloadQueueSlot } from './DownloadSlots';
import { FiltersEditorSlot, FiltersListSlot } from './FiltersSlots';
import { HeaderLeftSlot, HeaderRightSlot, HeaderSlot, OverlaySlot } from './GlobalSlots';
import { SubjectDetailsHeaderSlot, SubjectDetailsTabsSlot, SubjectListSlot } from './SubjectSlots';
import { SystemLogsSlot, SystemStatusSlot } from './SystemSlots';

const mockUseExtensions = vi.fn();
const mockUseSlotExtensions = vi.fn();

vi.mock('@/extensions/useExtensions', () => ({
    useExtensions: () => mockUseExtensions(),
    useSlotExtensions: (slot: string) => mockUseSlotExtensions(slot),
}));

const slotWrappers: [string, React.ComponentType<{ children?: React.ReactNode; className?: string }>][] = [
    ['dashboard.main', DashboardMainSlot],
    ['dashboard.widgets', DashboardWidgetsSlot],

    ['content.list', ContentListSlot],
    ['content.list.columns', ContentListColumnsSlot],
    ['content.list.actions', ContentListActionsSlot],
    ['content.details.header', ContentDetailsHeaderSlot],
    ['content.details.actions', ContentDetailsActionsSlot],
    ['content.details.metadata', ContentDetailsMetadataSlot],
    ['content.details.tabs', ContentDetailsTabsSlot],
    ['content.details.overview', ContentDetailsOverviewSlot],
    ['content.details.files', ContentDetailsFilesSlot],
    ['content.details.history', ContentDetailsHistorySlot],

    ['subject.list', SubjectListSlot],
    ['subject.details.header', SubjectDetailsHeaderSlot],
    ['subject.details.tabs', SubjectDetailsTabsSlot],

    ['downloads.queue', DownloadQueueSlot],
    ['downloads.queue.actions', DownloadQueueActionsSlot],

    ['admin.users', AdminUsersSlot],
    ['admin.providers', AdminProvidersSlot],
    ['admin.extensions', AdminExtensionsSlot],
    ['admin.backups', AdminBackupsSlot],

    ['calendar.main', CalendarMainSlot],

    ['system.status', SystemStatusSlot],
    ['system.logs', SystemLogsSlot],

    ['filters.editor', FiltersEditorSlot],
    ['filters.list', FiltersListSlot],

    ['global.header', HeaderSlot],
    ['global.overlay', OverlaySlot],
    ['header.left', HeaderLeftSlot],
    ['header.right', HeaderRightSlot],
];

describe('Slot wrappers', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mockUseExtensions.mockReturnValue({
            apiBaseUrl: '/api',
            userId: 'user-123',
            username: 'testuser',
            role: 'Admin',
        });
        mockUseSlotExtensions.mockReturnValue({
            extensions: [],
            isReady: true,
            isLoading: false,
        });
    });

    it.each(slotWrappers)('"%s" wrapper sets the correct data-slot attribute', (slotPath, Component) => {
        const { container } = render(
            <Component>
                <div>child</div>
            </Component>,
        );

        const slotDiv = container.querySelector('[data-slot]');
        expect(slotDiv).toHaveAttribute('data-slot', slotPath);
    });

    it.each(slotWrappers)('"%s" wrapper renders without children', (slotPath, Component) => {
        const { container } = render(<Component />);

        expect(container.querySelector(`[data-slot="${slotPath}"]`)).toBeInTheDocument();
    });

    it.each(slotWrappers)('"%s" wrapper passes className through', (slotPath, Component) => {
        const { container } = render(
            <Component className="custom">
                <div>child</div>
            </Component>,
        );

        const slotDiv = container.querySelector(`[data-slot="${slotPath}"]`);
        expect(slotDiv).toHaveClass('slot', 'custom');
    });
});
