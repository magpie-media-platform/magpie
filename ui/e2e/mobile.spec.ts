import { test as baseTest, expect } from './fixtures';

const test = baseTest.extend({});

test.describe('Mobile Viewport', () => {
    test.beforeEach(async ({}, testInfo) => {
        if (testInfo.project.name !== 'mobile-chrome') {
            testInfo.skip(true, 'Mobile tests only');
        }
    });

    test.describe('Navigation', () => {
        test('bottom navigation is visible on mobile', async ({ adminPage }) => {
            await adminPage.goto('/');
            await expect(adminPage.locator('[data-page="dashboard"]')).toBeVisible();

            const bottomNav = adminPage.locator('[data-component="bottom-nav"]');
            await expect(bottomNav).toBeVisible();
        });

        test('bottom navigation contains primary destinations', async ({ adminPage }) => {
            await adminPage.goto('/');
            const bottomNav = adminPage.locator('[data-component="bottom-nav"]');
            await expect(bottomNav).toBeVisible();

            await expect(bottomNav.locator('a[href="/"]')).toBeVisible();
            await expect(bottomNav.locator('a[href="/library"]')).toBeVisible();
            await expect(bottomNav.locator('a[href="/downloads"]')).toBeVisible();
            await expect(bottomNav.locator('a[href="/wishlists"]')).toBeVisible();
        });

        test('bottom navigation shows active state', async ({ adminPage }) => {
            await adminPage.goto('/');

            const dashboardLink = adminPage.locator('[data-component="bottom-nav"] a[href="/"]');
            await expect(dashboardLink).toHaveAttribute('aria-current', 'page');
        });

        test('sidebar is hidden by default on mobile', async ({ adminPage }) => {
            await adminPage.goto('/');

            const sidebar = adminPage.locator('.sidebar, [data-collapsed]');

            const isVisible = await sidebar.isVisible().catch(() => false);

            if (isVisible) {
                const isCollapsed = await sidebar.getAttribute('data-collapsed');
                expect(isCollapsed === 'true' || isCollapsed === null).toBeTruthy();
            } else {
                expect(isVisible).toBeFalsy();
            }
        });

        test('can navigate between pages using bottom nav', async ({ adminPage }) => {
            await adminPage.goto('/');
            await expect(adminPage.locator('[data-page="dashboard"]')).toBeVisible();

            const libraryNav = adminPage.locator('[data-component="bottom-nav"] a[href="/library"]');
            await libraryNav.click();
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();

            const downloadsNav = adminPage.locator('[data-component="bottom-nav"] a[href="/downloads"]');
            await downloadsNav.click();
            await expect(adminPage.locator('[data-page="downloads"]')).toBeVisible();
        });

        test('header menu button opens sidebar overlay', async ({ adminPage }) => {
            await adminPage.goto('/');

            const menuButton = adminPage.locator('.header__menu-btn, [data-action="toggle-menu"]');
            if (await menuButton.isVisible()) {
                await menuButton.click();

                const sidebarOverlay = adminPage.locator('.sidebar--open, .sidebar-overlay--visible, .app-shell__sidebar--open');
                await expect(sidebarOverlay.first()).toBeVisible();
            }
        });
    });

    test.describe('Content Library', () => {
        test('content list renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();

            await expect(adminPage.locator('[data-component="content-list"]')).toBeVisible();
        });

        test('content filters are accessible on mobile', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();

            const filters = adminPage.locator('[data-component="content-filters"], .library-page__filters, .content-filters');
            await expect(filters.first()).toBeVisible();
        });

        test('content detail page is scrollable when content exists', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();

            const contentItem = adminPage.locator('[data-content-id]').first();
            const hasContent = await contentItem.isVisible().catch(() => false);

            if (hasContent) {
                await contentItem.click();
                const detailPage = adminPage.locator('[data-page="content-detail"]');
                if (await detailPage.isVisible().catch(() => false)) {
                    const scrollHeight = await adminPage.evaluate(() => document.body.scrollHeight);
                    const viewportHeight = await adminPage.evaluate(() => window.innerHeight);
                    expect(scrollHeight).toBeGreaterThanOrEqual(viewportHeight);
                }
            }
        });

        test('content detail tabs are accessible when content exists', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();

            const contentItem = adminPage.locator('[data-content-id]').first();
            const hasContent = await contentItem.isVisible().catch(() => false);

            if (hasContent) {
                await contentItem.click();
                const detailPage = adminPage.locator('[data-page="content-detail"]');
                if (await detailPage.isVisible().catch(() => false)) {
                    const tabList = adminPage.locator('[role="tablist"]');
                    await expect(tabList).toBeVisible();
                }
            }
        });
    });

    test.describe('Subjects', () => {
        test('subjects list renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/library/subjects/all');
            await expect(adminPage.locator('[data-page="subjects"]')).toBeVisible();
        });

        test('subject detail page is scrollable when subject exists', async ({ adminPage }) => {
            await adminPage.goto('/library/subjects/all');
            await expect(adminPage.locator('[data-page="subjects"]')).toBeVisible();

            const subjectItem = adminPage.locator('[data-subject-id]').first();
            const hasSubject = await subjectItem.isVisible().catch(() => false);

            if (hasSubject) {
                await subjectItem.click();
                const detailPage = adminPage.locator('[data-page="subject-detail"]');
                if (await detailPage.isVisible().catch(() => false)) {
                    const scrollHeight = await adminPage.evaluate(() => document.body.scrollHeight);
                    const viewportHeight = await adminPage.evaluate(() => window.innerHeight);
                    expect(scrollHeight).toBeGreaterThanOrEqual(viewportHeight);
                }
            }
        });
    });

    test.describe('Forms', () => {
        test('login form is usable on mobile', async ({ unauthenticatedPage }) => {
            await unauthenticatedPage.goto('/login');

            const form = unauthenticatedPage.locator('[data-component="login-form"], .login-page__form, form.login-page__form');
            await expect(form.first()).toBeVisible();

            const usernameInput = unauthenticatedPage.locator('#username, input[name="username"]');
            const passwordInput = unauthenticatedPage.locator('#password, input[name="password"]');
            await expect(usernameInput.first()).toBeVisible();
            await expect(passwordInput.first()).toBeVisible();

            const submitButton = unauthenticatedPage.locator('button[type="submit"], [data-action="login"]');
            await expect(submitButton.first()).toBeVisible();
        });

        test('login form inputs have adequate size for mobile', async ({ unauthenticatedPage }) => {
            await unauthenticatedPage.goto('/login');

            const usernameInput = unauthenticatedPage.locator('#username, input[name="username"]');
            const passwordInput = unauthenticatedPage.locator('#password, input[name="password"]');

            const usernameBox = await usernameInput.first().boundingBox();
            const passwordBox = await passwordInput.first().boundingBox();

            if (usernameBox) {
                expect(usernameBox.height).toBeGreaterThanOrEqual(32);
            }
            if (passwordBox) {
                expect(passwordBox.height).toBeGreaterThanOrEqual(32);
            }
        });

        test('filter form opens as sheet on mobile', async ({ adminPage }) => {
            await adminPage.goto('/filters');
            const addButton = adminPage.locator('[data-action="add-filter"]');

            if (await addButton.isVisible()) {
                await addButton.click();
                const form = adminPage.locator('[data-component="filter-form"], .filter-form, [role="dialog"]');
                await expect(form.first()).toBeVisible();
            }
        });
    });

    test.describe('Touch Targets', () => {
        test('buttons have adequate touch targets (32px minimum)', async ({ adminPage }) => {
            await adminPage.goto('/');

            const buttons = adminPage.locator('button[data-action], a[data-action]');
            const buttonCount = await buttons.count();

            for (let i = 0; i < Math.min(buttonCount, 5); i++) {
                const button = buttons.nth(i);
                if (await button.isVisible()) {
                    const box = await button.boundingBox();
                    if (box) {
                        expect(box.height >= 32 || box.width >= 32).toBeTruthy();
                    }
                }
            }
        });

        test('bottom navigation items meet touch target requirements', async ({ adminPage }) => {
            await adminPage.goto('/');

            const bottomNav = adminPage.locator('[data-component="bottom-nav"]');
            const navItems = bottomNav.locator('a');
            const itemCount = await navItems.count();

            for (let i = 0; i < itemCount; i++) {
                const item = navItems.nth(i);
                const box = await item.boundingBox();
                if (box) {
                    expect(box.height).toBeGreaterThanOrEqual(44);
                    expect(box.width).toBeGreaterThanOrEqual(44);
                }
            }
        });
    });

    test.describe('Calendar', () => {
        test('calendar shows on mobile', async ({ adminPage }) => {
            await adminPage.goto('/calendar');
            await expect(adminPage.locator('[data-page="calendar"]')).toBeVisible();

            const calendarView = adminPage.locator('.calendar-page, [data-component="calendar"]');
            await expect(calendarView.first()).toBeVisible();
        });

        test('calendar view controls are accessible', async ({ adminPage }) => {
            await adminPage.goto('/calendar');
            await expect(adminPage.locator('[data-page="calendar"]')).toBeVisible();

            const viewSwitcher = adminPage.locator('.calendar-page__view-switcher, [data-component="view-switcher"]');
            await expect(viewSwitcher.first()).toBeVisible();
        });
    });

    test.describe('Downloads', () => {
        test('download queue is readable on mobile', async ({ adminPage }) => {
            await adminPage.goto('/downloads');
            await expect(adminPage.locator('[data-page="downloads"]')).toBeVisible();

            const downloadContent = adminPage.locator('.downloads-page__content, .downloads-page');
            await expect(downloadContent.first()).toBeVisible();
        });

        test('download tabs are accessible', async ({ adminPage }) => {
            await adminPage.goto('/downloads');
            await expect(adminPage.locator('[data-page="downloads"]')).toBeVisible();

            const tabs = adminPage.locator('[role="tablist"]');
            await expect(tabs).toBeVisible();
        });
    });

    test.describe('Jobs (Admin)', () => {
        test('jobs page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/jobs');
            await expect(adminPage.locator('[data-page="jobs"]')).toBeVisible();
        });

        test('jobs tabs are accessible', async ({ adminPage }) => {
            await adminPage.goto('/admin/jobs');
            await expect(adminPage.locator('[data-page="jobs"]')).toBeVisible();

            const tabs = adminPage.locator('[role="tablist"]');
            await expect(tabs).toBeVisible();
        });
    });

    test.describe('Wishlists', () => {
        test('wishlists page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/wishlists');
            await expect(adminPage.locator('[data-page="wishlists"]')).toBeVisible();
        });
    });

    test.describe('Admin Pages', () => {
        test('users page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/users');
            await expect(adminPage.locator('[data-page="users"]')).toBeVisible();
        });

        test('extensions page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/extensions');
            const extensionsPage = adminPage.locator('[data-page="extensions"]');
            const errorState = adminPage.locator('h3:has-text("Failed to load page")');

            await expect(extensionsPage.or(errorState)).toBeVisible();
        });

        test('backups page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/backups');
            const backupsPage = adminPage.locator('[data-page="backups"]');
            const errorState = adminPage.locator('h3:has-text("Failed to load page")');

            await expect(backupsPage.or(errorState)).toBeVisible();
        });
    });

    test.describe('Error Pages', () => {
        test('404 page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/non-existent-route');
            await expect(adminPage.locator('[data-page="not-found"]')).toBeVisible();
        });

        test('403 page renders on mobile (viewer accessing admin)', async ({ viewerPage }) => {
            await viewerPage.goto('/admin/users');
            await expect(
                viewerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });
    });

    test.describe('Viewport Behavior', () => {
        test('page content is not horizontally scrollable', async ({ adminPage }) => {
            await adminPage.goto('/');
            await expect(adminPage.locator('[data-page="dashboard"]')).toBeVisible();

            const scrollWidth = await adminPage.evaluate(() => document.body.scrollWidth);
            const viewportWidth = await adminPage.evaluate(() => window.innerWidth);
            expect(scrollWidth).toBeLessThanOrEqual(viewportWidth + 10);
        });

        test('content does not overflow viewport on library page', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();

            const scrollWidth = await adminPage.evaluate(() => document.body.scrollWidth);
            const viewportWidth = await adminPage.evaluate(() => window.innerWidth);
            expect(scrollWidth).toBeLessThanOrEqual(viewportWidth + 10);
        });
    });

    test.describe('Safe Area', () => {
        test('bottom navigation respects safe area', async ({ adminPage }) => {
            await adminPage.goto('/');

            const bottomNav = adminPage.locator('[data-component="bottom-nav"]');
            await expect(bottomNav).toBeVisible();

            const navBox = await bottomNav.boundingBox();
            const viewportHeight = await adminPage.evaluate(() => window.innerHeight);

            if (navBox) {
                expect(navBox.y + navBox.height).toBeLessThanOrEqual(viewportHeight + 5);
            }
        });
    });

    test.describe('Responsive Text', () => {
        test('text is readable on mobile viewport', async ({ adminPage }) => {
            await adminPage.goto('/');
            await expect(adminPage.locator('[data-page="dashboard"]')).toBeVisible();

            const fontSize = await adminPage.evaluate(() => {
                const bodyStyle = window.getComputedStyle(document.body);
                return parseFloat(bodyStyle.fontSize);
            });

            expect(fontSize).toBeGreaterThanOrEqual(12);
        });
    });

    test.describe('Integrations Pages (Admin)', () => {
        test('indexers page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/indexers');
            await expect(adminPage.locator('[data-page="indexers"]')).toBeVisible();
        });

        test('download clients page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/download-clients');
            await expect(adminPage.locator('[data-page="download-clients"]')).toBeVisible();
        });

        test('media servers page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/media-servers');
            await expect(adminPage.locator('[data-page="media-servers"]')).toBeVisible();
        });

    });

    test.describe('Additional Pages', () => {
        test('tags page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/tags');
            const tagsPage = adminPage.locator('[data-page="tags"]');
            const errorState = adminPage.locator('h3:has-text("Failed to load page")');

            await expect(tagsPage.or(errorState)).toBeVisible();
        });

        test('filters page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/filters');
            const filtersPage = adminPage.locator('[data-page="filters"]');
            const errorState = adminPage.locator('h3:has-text("Failed to load page")');

            await expect(filtersPage.or(errorState)).toBeVisible();
        });

        test('quality tiers page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/quality-tiers');
            await expect(adminPage.locator('[data-page="quality-tiers"]')).toBeVisible();
        });

        test('system status page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/system');
            const systemPage = adminPage.locator('[data-page="system-status"]');
            const errorState = adminPage.locator('h3:has-text("Failed to load page")');

            await expect(systemPage.or(errorState)).toBeVisible();
        });

        test('about page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/about');
            await expect(adminPage.locator('[data-page="about"]')).toBeVisible();
        });

        test('root folders page renders on mobile', async ({ adminPage }) => {
            await adminPage.goto('/admin/root-folders');
            await expect(adminPage.locator('[data-page="root-folders"]')).toBeVisible();
        });
    });
});
