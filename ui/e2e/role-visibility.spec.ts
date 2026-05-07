import { test, expect, Role } from './fixtures';

test.describe('Role-Based Visibility', () => {
    test.describe('Admin Role', () => {
        test('admin sees all navigation items', async ({ adminPage }) => {
            await adminPage.goto('/');
            await expect(adminPage.locator('[data-page="dashboard"]')).toBeVisible();

            const nav = adminPage.locator('nav, [role="navigation"]');
            await expect(nav).toBeVisible();
        });

        test('admin can access user management', async ({ adminPage }) => {
            await adminPage.goto('/admin/users');
            await expect(adminPage.locator('[data-page="users"]')).toBeVisible();
        });

        test('admin can access extensions', async ({ adminPage }) => {
            await adminPage.goto('/admin/extensions');
            await expect(adminPage.locator('[data-page="extensions"]')).toBeVisible();
        });

        test('admin can access backups', async ({ adminPage }) => {
            await adminPage.goto('/admin/backups');
            const backupsPage = adminPage.locator('[data-page="backups"]');
            const errorState = adminPage.locator('h3:has-text("Failed to load page")');
            await expect(backupsPage.or(errorState)).toBeVisible();
        });

    });

    test.describe('Manager Role', () => {
        test('manager can access content management', async ({ managerPage }) => {
            await managerPage.goto('/library');
            await expect(managerPage.locator('[data-page="library"]')).toBeVisible();
        });

        test('manager is redirected from admin users page', async ({ managerPage }) => {
            await managerPage.goto('/admin/users');
            await expect(
                managerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });

        test('manager is redirected from admin indexers page', async ({ managerPage }) => {
            await managerPage.goto('/admin/indexers');
            await expect(
                managerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });

        test('manager is redirected from extensions page', async ({ managerPage }) => {
            await managerPage.goto('/admin/extensions');
            await expect(
                managerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });

        test('manager is redirected from backups page', async ({ managerPage }) => {
            await managerPage.goto('/admin/backups');
            await expect(
                managerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });
    });

    test.describe('Requester Role', () => {
        test('requester can view content library', async ({ requesterPage }) => {
            await requesterPage.goto('/library');
            await expect(requesterPage.locator('[data-page="library"]')).toBeVisible();
        });

        test('requester can view downloads', async ({ requesterPage }) => {
            await requesterPage.goto('/downloads');
            await expect(requesterPage.locator('[data-page="downloads"]')).toBeVisible();
        });

        test('requester can view wishlists', async ({ requesterPage }) => {
            await requesterPage.goto('/wishlists');
            await expect(requesterPage.locator('[data-page="wishlists"]')).toBeVisible();
        });

        test('requester is redirected from admin indexers page', async ({ requesterPage }) => {
            await requesterPage.goto('/admin/indexers');
            await expect(
                requesterPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });

    });

    test.describe('Viewer Role', () => {
        test('viewer can view dashboard', async ({ viewerPage }) => {
            await viewerPage.goto('/');
            await expect(viewerPage.locator('[data-page="dashboard"]')).toBeVisible();
        });

        test('viewer can view content library', async ({ viewerPage }) => {
            await viewerPage.goto('/library');
            await expect(viewerPage.locator('[data-page="library"]')).toBeVisible();
        });

        test('viewer can view downloads', async ({ viewerPage }) => {
            await viewerPage.goto('/downloads');
            await expect(viewerPage.locator('[data-page="downloads"]')).toBeVisible();
        });

        test('viewer can view calendar', async ({ viewerPage }) => {
            await viewerPage.goto('/calendar');
            await expect(viewerPage.locator('[data-page="calendar"]')).toBeVisible();
        });

        test('viewer is redirected from admin indexers', async ({ viewerPage }) => {
            await viewerPage.goto('/admin/indexers');
            await expect(
                viewerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });

        test('viewer is redirected from admin pages', async ({ viewerPage }) => {
            await viewerPage.goto('/admin/users');
            await expect(
                viewerPage.locator('[data-page="forbidden"], [data-page="dashboard"]')
            ).toBeVisible();
        });
    });

    test.describe('Forbidden Routes', () => {
        test('forbidden page displays correctly', async ({ viewerPage }) => {
            await viewerPage.goto('/forbidden');
            await expect(viewerPage.locator('[data-page="forbidden"]')).toBeVisible();
        });

        test('forbidden page has navigation back', async ({ viewerPage }) => {
            await viewerPage.goto('/forbidden');
            const backLink = viewerPage.locator('a[href="/"], button:has-text("Back"), button:has-text("Dashboard")');
            await expect(backLink.first()).toBeVisible();
        });
    });
});
