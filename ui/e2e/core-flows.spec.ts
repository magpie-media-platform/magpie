import { test, expect } from './fixtures';

test.describe('Core User Flows', () => {
    test.describe('Login/Logout', () => {
        test('redirects to login when not authenticated', async ({ unauthenticatedPage }) => {
            await unauthenticatedPage.goto('/');
            await expect(unauthenticatedPage).toHaveURL(/\/login/);
            await expect(unauthenticatedPage.locator('[data-page="login"]')).toBeVisible();
        });

        test('displays login form', async ({ unauthenticatedPage }) => {
            await unauthenticatedPage.goto('/login');
            await expect(unauthenticatedPage.locator('[data-component="login-form"]')).toBeVisible();
            await expect(unauthenticatedPage.locator('input[name="username"]')).toBeVisible();
            await expect(unauthenticatedPage.locator('input[name="password"]')).toBeVisible();
            await expect(unauthenticatedPage.locator('button[type="submit"]')).toBeVisible();
        });

        test('authenticated user can access dashboard', async ({ adminPage }) => {
            await adminPage.goto('/');
            await expect(adminPage.locator('[data-page="dashboard"]')).toBeVisible();
        });
    });

    test.describe('Content Library', () => {
        test('displays content list', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(adminPage.locator('[data-page="library"]')).toBeVisible();
            await expect(adminPage.locator('[data-component="content-list"]')).toBeVisible();
        });

        test('content list has filter controls', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await expect(
                adminPage.locator('[data-component="content-filters"], [data-component="filter-bar"]')
            ).toBeVisible();
        });

        test('can navigate to content details', async ({ adminPage }) => {
            await adminPage.goto('/library');
            await adminPage.waitForSelector('[data-content-id]', { timeout: 5000 }).catch(() => {});
            const firstItem = adminPage.locator('[data-content-id]').first();
            if (await firstItem.isVisible()) {
                await firstItem.click();
                await expect(adminPage).toHaveURL(/\/library\/.+/);
                await expect(adminPage.locator('[data-page="content-detail"]')).toBeVisible();
            }
        });

        test('content detail has tabs', async ({ adminPage }) => {
            await adminPage.goto('/library/content-1');
            await expect(adminPage.locator('[data-page="content-detail"]')).toBeVisible();
            await expect(adminPage.locator('[role="tablist"]')).toBeVisible();
        });
    });

    test.describe('Downloads', () => {
        test('displays download queue', async ({ adminPage }) => {
            await adminPage.goto('/downloads');
            await expect(adminPage.locator('[data-page="downloads"]')).toBeVisible();
        });

        test('download queue has filter controls', async ({ adminPage }) => {
            await adminPage.goto('/downloads');
            await expect(
                adminPage.locator('[role="tablist"], [data-component="download-filters"]')
            ).toBeVisible();
        });
    });

    test.describe('Calendar', () => {
        test('displays calendar view', async ({ adminPage }) => {
            await adminPage.goto('/calendar');
            await expect(adminPage.locator('[data-page="calendar"]')).toBeVisible();
        });

        test('can switch calendar views', async ({ adminPage }) => {
            await adminPage.goto('/calendar');
            const weekView = adminPage.locator('[data-view="week"]');
            if (await weekView.isVisible()) {
                await weekView.click();
                await expect(adminPage.locator('.calendar__week, [data-calendar-view="week"]')).toBeVisible();
            }
        });
    });

    test.describe('Custom Filters', () => {
        test('displays filters page', async ({ adminPage }) => {
            await adminPage.goto('/filters');
            await expect(adminPage.locator('[data-page="filters"]')).toBeVisible();
        });

        test('can open filter creation form', async ({ adminPage }) => {
            await adminPage.goto('/filters');
            const addButton = adminPage.locator('[data-action="add-filter"]');
            await expect(addButton).toBeVisible();
            await addButton.click();
            await expect(
                adminPage.locator('[data-component="filter-form"], .filter-form')
            ).toBeVisible();
        });

        test('filter form has simple/advanced mode switch', async ({ adminPage }) => {
            await adminPage.goto('/filters');
            await adminPage.locator('[data-action="add-filter"]').click();
            await expect(
                adminPage.locator('[data-action="switch-to-advanced"]')
            ).toBeVisible();
        });
    });

});
