import { render } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { AdminBackupsSlot, AdminExtensionsSlot } from './AdminSlots';
import { CalendarMainSlot } from './CalendarSlots';
import {
    ContentDetailsActionsSlot,
    ContentDetailsFilesSlot,
    ContentDetailsHeaderSlot,
    ContentDetailsMetadataSlot,
    ContentDetailsOverviewSlot,
    ContentListActionsSlot,
    ContentListColumnsSlot,
    ContentListSlot,
} from './ContentSlots';
import { DashboardMainSlot, DashboardWidgetsSlot } from './DashboardSlots';
import { HeaderLeftSlot, HeaderRightSlot, HeaderSlot } from './GlobalSlots';
import { SubjectDetailsHeaderSlot, SubjectListSlot } from './SubjectSlots';
import { SystemStatusSlot } from './SystemSlots';

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
    ['content.details.overview', ContentDetailsOverviewSlot],
    ['content.details.files', ContentDetailsFilesSlot],

    ['subject.list', SubjectListSlot],
    ['subject.details.header', SubjectDetailsHeaderSlot],
    ['admin.extensions', AdminExtensionsSlot],
    ['admin.backups', AdminBackupsSlot],

    ['calendar.main', CalendarMainSlot],

    ['system.status', SystemStatusSlot],

    ['global.header', HeaderSlot],
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
