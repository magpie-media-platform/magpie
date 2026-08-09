import { access, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';

import { expect, test, type BrowserContext } from '@playwright/test';

import { ADMIN_PASSWORD, ADMIN_USERNAME, ApiClient, loginAdmin } from '../helpers/api';
import { restoreCheckpoint } from '../helpers/checkpoints';
import { cli } from '../helpers/cli';
import { FIXTURES_ROOT } from '../helpers/fixtures';
import { restartHost } from '../helpers/host';
import { mocks } from '../helpers/mocks';
import { loginUi, loginUiContext } from '../helpers/ui';

interface Extension {
    id: string;
    packageId: string;
    version: string;
    name: string;
    filePath: string;
    enabled: boolean;
    disabledReason?: string | null;
    managed: boolean;
}

interface ExtensionListItem {
    extension: Extension;
}

interface ExtensionInstallResult {
    extension: Extension;
    restartRequired: boolean;
}

interface PackageSource {
    id: string;
    name: string;
    sourceUrl: string;
    enabled: boolean;
}

interface NuGetSearchResult {
    id: string;
    version: string;
    title: string | null;
    tags: string[];
}

interface ProviderDescriptor {
    kind: string;
    providerType: string;
}

interface SettingsGroup {
    name: string;
    extensionId: string;
    canTest: boolean;
    schemaResolved: boolean;
    settings: Array<{ key: string; value: string }>;
    schema: Array<{ key: string; displayName: string; required: boolean }>;
}

interface UiManifest {
    extensions: Array<{ extensionId: string; slot: string; tagName: string }>;
    assets: Array<{ extensionId: string; relativePath: string | null }>;
}

interface User {
    id: string;
    username: string;
}

const LIFECYCLE_PACKAGE_ID = 'Magpie.Extensions.IntegrationTestLifecycle';
const LIFECYCLE_DEFINITION_ID = '8a7fef37-6dd6-4bb5-a2ae-69c78d14e24c';
const LIFECYCLE_PROVIDER_TYPE = 'lifecycle-notification';
const LIFECYCLE_GROUP = 'lifecycle';
const BROKEN_PACKAGE_ID = 'Magpie.Extensions.IntegrationTestBrokenAssembly';
const BROKEN_DISABLED_REASON = 'The extension assembly failed to load.';
const SHIMS_PACKAGE_ID = 'Magpie.Extensions.IntegrationTestShims';
const SHIMS_EXTENSION_ID = 'd6e20b46-39ef-4afb-8b85-fabce83f4c25';
const SHIMS_SETTINGS_GROUP = 'integration-test-shims';
const VIEWER_USERNAME = 'lifecycle-viewer';
const VIEWER_PASSWORD = 'lifecycle-viewer-pa55!';
const SETTINGS_SCENARIO = 'journey-06-settings';
const INDEXER_SCENARIO = 'journey-06-indexer';

test.describe.serial('Journey 06: add, upgrade, and remove a plugin', () => {
    test('adds, upgrades, and removes a plugin', async ({ browser, page }) => {
        test.setTimeout(900_000);

        let api!: ApiClient;
        let lifecycleId = '';
        let lifecycleFilePath = '';
        let shimsId = '';
        let packageSourceId = '';
        let viewerId = '';
        let viewerContext: BrowserContext | undefined;
        const disabledPackageSourceIds: string[] = [];

        try {
            await test.step('restore the configured baseline and sign in as the administrator', async () => {
                api = await restoreCheckpoint('configured-baseline', async (restored) => {
                    const installed = await restored.get<ExtensionListItem[]>('/api/extensions');
                    expect(installed.map((item) => item.extension.packageId)).toContain(SHIMS_PACKAGE_ID);
                    expect(installed.map((item) => item.extension.packageId)).not.toContain(LIFECYCLE_PACKAGE_ID);
                    shimsId = installed.find((item) => item.extension.packageId === SHIMS_PACKAGE_ID)!.extension.id;
                });
                await loginUi(page);
            });

            await test.step('install a package by upload and restart into its settings page', async () => {
                const installed = await cli.json<ExtensionInstallResult>([
                    'extensions',
                    'upload',
                    '--file',
                    '/app/test-packages/Magpie.Extensions.IntegrationTestLifecycle.1.0.0.nupkg',
                ]);
                expect(installed).toMatchObject({
                    restartRequired: true,
                    extension: {
                        packageId: LIFECYCLE_PACKAGE_ID,
                        name: 'Lifecycle fixture',
                        version: '1.0.0',
                        enabled: true,
                        managed: false,
                    },
                });
                lifecycleId = installed.extension.id;
                lifecycleFilePath = installed.extension.filePath;

                api = await restartHost(api);
                const settings = await api.get<SettingsGroup>(
                    `/api/settings/groups/${lifecycleId}/${LIFECYCLE_GROUP}`,
                );
                expect(settings).toMatchObject({
                    name: LIFECYCLE_GROUP,
                    extensionId: lifecycleId,
                    canTest: false,
                    schemaResolved: true,
                    schema: [
                        {
                            key: 'endpoint',
                            displayName: 'Endpoint',
                            required: false,
                        },
                    ],
                });

                await page.goto('/admin/ext/lifecycle');
                await expect(page.getByRole('heading', { name: 'Lifecycle fixture Settings' })).toBeVisible();
                await expect(page.locator('magpie-lifecycle-settings')).toHaveText('Lifecycle settings loaded.');
            });

            await test.step('its settings group answers the test endpoint', async () => {
                await mocks.settings.useScenario(SETTINGS_SCENARIO);
                await mocks.settings.armTestConfiguration({
                    success: true,
                    message: 'journey settings accepted',
                });
                await mocks.settings.clearCalls();

                expect(
                    await api.get<SettingsGroup>(
                        `/api/settings/groups/${SHIMS_EXTENSION_ID}/${SHIMS_SETTINGS_GROUP}`,
                    ),
                ).toMatchObject({
                    name: SHIMS_SETTINGS_GROUP,
                    extensionId: shimsId,
                    canTest: true,
                    schemaResolved: true,
                });
                expect(
                    await api.post(`/api/settings/groups/${SHIMS_EXTENSION_ID}/${SHIMS_SETTINGS_GROUP}/test`, {
                        settings: { endpoint: 'local://journey-06' },
                    }),
                ).toEqual({
                    success: true,
                    message: 'journey settings accepted',
                    failureKind: 'None',
                });
                const calls = await mocks.settings.calls<{ settingsKeys: string[] }>('TestConfigurationAsync');
                expect(calls).toHaveLength(1);
                expect(calls[0]!.request.settingsKeys).toEqual(['endpoint']);
            });

            await test.step('the page it contributes renders for a non-administrator', async () => {
                const viewer = await api.post<User>('/api/users', {
                    username: VIEWER_USERNAME,
                    password: VIEWER_PASSWORD,
                    role: 'Viewer',
                    enabled: true,
                });
                viewerId = viewer.id;
                viewerContext = await browser.newContext();
                await loginUiContext(viewerContext, VIEWER_USERNAME, VIEWER_PASSWORD);
                const viewerPage = await viewerContext.newPage();
                await viewerPage.goto('/ext/lifecycle');
                await expect(viewerPage.getByRole('heading', { name: 'Lifecycle fixture' })).toBeVisible();
                await expect(viewerPage.locator('magpie-lifecycle-page')).toHaveText(
                    'Lifecycle page 1.0.0 loaded for a viewer.',
                );
                await viewerContext.close();
                viewerContext = undefined;
            });

            await test.step('the manifest and asset endpoints serve what those pages load', async () => {
                const manifest = await api.get<UiManifest>('/api/ui/manifest');
                expect(manifest.extensions).toEqual(
                    expect.arrayContaining([
                        expect.objectContaining({
                            extensionId: LIFECYCLE_DEFINITION_ID,
                            slot: 'admin.route.lifecycle',
                            tagName: 'magpie-lifecycle-settings',
                        }),
                        expect.objectContaining({
                            extensionId: LIFECYCLE_DEFINITION_ID,
                            slot: 'route.lifecycle',
                            tagName: 'magpie-lifecycle-page',
                        }),
                    ]),
                );
                expect(manifest.assets).toContainEqual(
                    expect.objectContaining({
                        extensionId: LIFECYCLE_DEFINITION_ID,
                        relativePath: 'lifecycle.js',
                    }),
                );
                expect(await api.get<string>(`/api/ui/assets/${LIFECYCLE_DEFINITION_ID}/lifecycle.js`)).toContain(
                    'magpie-lifecycle-page',
                );
            });

            await test.step('disable it, persist its settings, and watch its provider type leave the pickers', async () => {
                expect(await api.post<Extension>(`/api/extensions/${lifecycleId}/disable`)).toMatchObject({
                    id: lifecycleId,
                    enabled: false,
                });
                api = await restartHost(api);
                expect(await api.get<ProviderDescriptor[]>('/api/providers')).not.toContainEqual(
                    expect.objectContaining({ kind: 'notification', providerType: LIFECYCLE_PROVIDER_TYPE }),
                );
                expect(
                    await api.patch<void>(`/api/settings/groups/${lifecycleId}/${LIFECYCLE_GROUP}`, {
                        settings: { add: { endpoint: 'local://disabled' } },
                    }),
                ).toBeUndefined();
                expect(
                    (
                        await api.get<SettingsGroup>(
                            `/api/settings/groups/${lifecycleId}/${LIFECYCLE_GROUP}`,
                        )
                    ).settings,
                ).toEqual([]);

                await page.goto('/admin/notifications');
                await page.locator('[data-action="add-rule"]').first().click();
                const form = page.locator('[data-component="notification-rule-form"]');
                await form.locator('[data-field="providerType"]').click();
                await expect(page.getByRole('option', { name: mocks.notification.slug, exact: true })).toBeVisible();
                await expect(page.getByRole('option', { name: LIFECYCLE_PROVIDER_TYPE, exact: true })).toHaveCount(0);
                await page.keyboard.press('Escape');
                await form.locator('[data-action="cancel"]').click();
            });

            await test.step('re-enable it across a restart and the pickers come back', async () => {
                expect(await api.post<Extension>(`/api/extensions/${lifecycleId}/enable`)).toMatchObject({
                    id: lifecycleId,
                    enabled: true,
                });
                api = await restartHost(api);
                expect(await api.get<Extension>(`/api/extensions/${lifecycleId}`)).toMatchObject({ enabled: true });
                expect(await api.get<ProviderDescriptor[]>('/api/providers')).toContainEqual(
                    expect.objectContaining({ kind: 'notification', providerType: LIFECYCLE_PROVIDER_TYPE }),
                );
                expect(
                    (
                        await api.get<SettingsGroup>(
                            `/api/settings/groups/${lifecycleId}/${LIFECYCLE_GROUP}`,
                        )
                    ).settings,
                ).toContainEqual(expect.objectContaining({ key: 'endpoint', value: 'local://disabled' }));
                await api.patch<void>(`/api/settings/groups/${lifecycleId}/${LIFECYCLE_GROUP}`, {
                    settings: { remove: ['endpoint'] },
                });

                await page.goto('/admin/notifications');
                await page.locator('[data-action="add-rule"]').first().click();
                const form = page.locator('[data-component="notification-rule-form"]');
                await form.locator('[data-field="providerType"]').click();
                await expect(page.getByRole('option', { name: LIFECYCLE_PROVIDER_TYPE, exact: true })).toBeVisible();
                await page.keyboard.press('Escape');
                await form.locator('[data-action="cancel"]').click();
            });

            await test.step('create a package source and find a newer version through it', async () => {
                const sources = await api.get<PackageSource[]>('/api/extensions/package-sources');
                for (const source of sources.filter((candidate) => candidate.enabled)) {
                    disabledPackageSourceIds.push(source.id);
                    await api.patch(`/api/extensions/package-sources/${source.id}`, {
                        enabled: { value: false },
                    });
                }
                const source = await api.post<PackageSource>('/api/extensions/package-sources', {
                    name: 'Journey lifecycle feed',
                    sourceUrl: 'http://package-feed:8081/index.json',
                    enabled: true,
                    searchFilterKind: 'None',
                    credentialKind: 'None',
                });
                packageSourceId = source.id;
                expect(source).toMatchObject({
                    name: 'Journey lifecycle feed',
                    sourceUrl: 'http://package-feed:8081/index.json',
                    enabled: true,
                });

                const found = await api.get<NuGetSearchResult[]>(
                    `/api/extensions/search?q=${encodeURIComponent(LIFECYCLE_PACKAGE_ID)}`,
                );
                expect(found).toContainEqual(
                    expect.objectContaining({
                        id: LIFECYCLE_PACKAGE_ID,
                        version: '1.1.0',
                        title: 'Lifecycle fixture',
                        tags: ['magpie', 'integration-test'],
                    }),
                );

                const installed = await api.post<ExtensionInstallResult>('/api/extensions', {
                    packageId: LIFECYCLE_PACKAGE_ID,
                    version: '1.0.0',
                });
                expect(installed).toMatchObject({
                    restartRequired: true,
                    extension: {
                        id: lifecycleId,
                        packageId: LIFECYCLE_PACKAGE_ID,
                        version: '1.0.0',
                        managed: true,
                    },
                });
                lifecycleFilePath = installed.extension.filePath;
            });

            await test.step('upgrade to latest without naming a version', async () => {
                const upgraded = await api.post<ExtensionInstallResult>(
                    `/api/extensions/${lifecycleId}/upgrade/latest`,
                );
                expect(upgraded).toMatchObject({
                    restartRequired: true,
                    extension: { id: lifecycleId, version: '1.1.0', managed: true },
                });
                lifecycleFilePath = upgraded.extension.filePath;
                api = await restartHost(api);
                expect(await api.get<Extension>(`/api/extensions/${lifecycleId}`)).toMatchObject({
                    id: lifecycleId,
                    version: '1.1.0',
                    enabled: true,
                    managed: true,
                });
                await page.goto('/ext/lifecycle');
                await expect(page.locator('magpie-lifecycle-page')).toHaveText(
                    'Lifecycle page 1.1.0 loaded for a viewer.',
                );
            });

            await test.step('uninstall it and prove the settings page, provider entries and routes are gone', async () => {
                await api.delete(`/api/extensions/${lifecycleId}`);
                await expect(access(dirname(lifecycleFilePath))).rejects.toMatchObject({ code: 'ENOENT' });
                api = await restartHost(api);

                const installed = await api.get<ExtensionListItem[]>('/api/extensions');
                expect(installed.map((item) => item.extension.packageId)).not.toContain(LIFECYCLE_PACKAGE_ID);
                expect(await api.get<ProviderDescriptor[]>('/api/providers')).not.toContainEqual(
                    expect.objectContaining({ providerType: LIFECYCLE_PROVIDER_TYPE }),
                );
                await expect(
                    api.get(`/api/settings/groups/${lifecycleId}/${LIFECYCLE_GROUP}`),
                ).rejects.toMatchObject({ status: 404 });
                expect(await api.get<SettingsGroup[]>('/api/settings/groups')).not.toContainEqual(
                    expect.objectContaining({ extensionId: lifecycleId, name: LIFECYCLE_GROUP }),
                );
                const manifest = await api.get<UiManifest>('/api/ui/manifest');
                expect(
                    manifest.extensions.some((extension) => extension.extensionId === LIFECYCLE_DEFINITION_ID),
                ).toBe(false);
                expect(manifest.assets.some((asset) => asset.extensionId === LIFECYCLE_DEFINITION_ID)).toBe(false);

                await page.goto('/ext/lifecycle');
                await expect(page.getByText('This extension does not provide a page here.', { exact: true })).toBeVisible();
            });

            await test.step('a deliberately broken package reports an error instead of taking the host down', async () => {
                const startedAt = (await api.get<{ startedAt: string }>('/api/system')).startedAt;
                await page.goto('/admin/extensions');
                await page.locator('[data-action="add-extension"]').click();
                const refusedUpload = page.waitForResponse(
                    (response) =>
                        response.url().endsWith('/api/extensions/upload') && response.request().method() === 'POST',
                );
                await page
                    .getByTestId('install-extension-dialog__file-input')
                    .setInputFiles('/app/test-packages/Magpie.Extensions.IntegrationTestBroken.nupkg');
                expect((await refusedUpload).status()).toBe(400);
                await expect(page.getByTestId('install-extension-dialog__error')).toContainText('Installation failed');
                expect((await api.get<{ startedAt: string }>('/api/system')).startedAt).toBe(startedAt);

                const acceptedUpload = page.waitForResponse(
                    (response) =>
                        response.url().endsWith('/api/extensions/upload') && response.request().method() === 'POST',
                );
                await page
                    .getByTestId('install-extension-dialog__file-input')
                    .setInputFiles(
                        '/app/test-packages/Magpie.Extensions.IntegrationTestBrokenAssembly.1.0.0.nupkg',
                    );
                const acceptedResponse = await acceptedUpload;
                expect(acceptedResponse.status()).toBe(201);
                const installedBroken = (await acceptedResponse.json()) as ExtensionInstallResult;
                expect(installedBroken).toMatchObject({
                    restartRequired: true,
                    extension: { packageId: BROKEN_PACKAGE_ID, version: '1.0.0', enabled: true },
                });

                api = await restartHost(api);
                const broken = (await api.get<ExtensionListItem[]>('/api/extensions')).find(
                    (item) => item.extension.packageId === BROKEN_PACKAGE_ID,
                )!.extension;
                expect(broken).toMatchObject({ enabled: false, disabledReason: BROKEN_DISABLED_REASON });

                await page.goto('/admin/extensions');
                const brokenCard = page.getByRole('listitem').filter({ hasText: 'Broken assembly fixture' });
                await expect(brokenCard.getByText('Failed', { exact: true })).toBeVisible();
                await expect(brokenCard.locator('.extensions-page__card-reason')).toHaveText(BROKEN_DISABLED_REASON);
            });

            await test.step('upgrade the shim extension itself and prove the mock seams still resolve', async () => {
                const shims = (await api.get<ExtensionListItem[]>('/api/extensions')).find(
                    (item) => item.extension.packageId === SHIMS_PACKAGE_ID,
                )!.extension;
                const managed = await api.post<ExtensionInstallResult>('/api/extensions', {
                    packageId: SHIMS_PACKAGE_ID,
                    version: '1.0.0',
                });
                expect(managed).toMatchObject({
                    restartRequired: true,
                    extension: { id: shims.id, version: '1.0.0', managed: true },
                });
                const upgraded = await api.post<ExtensionInstallResult>(
                    `/api/extensions/${shims.id}/upgrade/latest`,
                );
                expect(upgraded).toMatchObject({
                    restartRequired: true,
                    extension: { id: shims.id, version: '1.1.0', managed: true },
                });

                api = await restartHost(api);
                expect(await api.get<Extension>(`/api/extensions/${shims.id}`)).toMatchObject({ version: '1.1.0' });
                expect(
                    await api.post(`/api/settings/groups/${SHIMS_EXTENSION_ID}/${SHIMS_SETTINGS_GROUP}/test`, {
                        settings: { endpoint: 'local://after-shim-upgrade' },
                    }),
                ).toEqual({
                    success: true,
                    message: 'journey settings accepted',
                    failureKind: 'None',
                });

                await mocks.indexer.useScenario(INDEXER_SCENARIO);
                await mocks.indexer.armTestConnection({
                    success: true,
                    message: 'mock indexer resolved after shim upgrade',
                });
                expect(
                    await api.post('/api/indexers/test', {
                        providerType: mocks.indexer.slug,
                        settings: {
                            apiUrl: 'https://shim-upgrade.indexer.invalid',
                            apiKey: 'shim-upgrade-secret',
                        },
                    }),
                ).toEqual({
                    success: true,
                    message: 'mock indexer resolved after shim upgrade',
                    failureKind: 'None',
                });
            });
        } finally {
            const cleanupResults: PromiseSettledResult<unknown>[] = [];
            let restartRequired = false;

            cleanupResults.push(
                ...(await Promise.allSettled([
                    (async () => {
                        const restored = await cli.json<ExtensionInstallResult>([
                            'extensions',
                            'upload',
                            '--file',
                            '/app/test-packages/Magpie.Extensions.IntegrationTestShims.1.0.0.nupkg',
                        ]);
                        expect(restored).toMatchObject({
                            restartRequired: true,
                            extension: { packageId: SHIMS_PACKAGE_ID, version: '1.0.0', managed: false },
                        });
                        restartRequired = true;
                    })(),
                ])),
            );

            cleanupResults.push(
                ...(await Promise.allSettled([
                    viewerContext?.close() ?? Promise.resolve(),
                    mocks.settings.useScenario('default'),
                    mocks.indexer.useScenario('default'),
                    rm(resolve(FIXTURES_ROOT, mocks.settings.slug, 'scenarios', SETTINGS_SCENARIO), {
                        recursive: true,
                        force: true,
                    }),
                    rm(resolve(FIXTURES_ROOT, mocks.indexer.slug, 'scenarios', INDEXER_SCENARIO), {
                        recursive: true,
                        force: true,
                    }),
                ])),
            );

            let cleanupApi = api;
            const [cleanupLogin] = await Promise.allSettled([loginAdmin(ADMIN_USERNAME, ADMIN_PASSWORD)]);
            cleanupResults.push(cleanupLogin);
            if (cleanupLogin.status === 'fulfilled') {
                cleanupApi = cleanupLogin.value.api;
            }

            if (cleanupApi) {
                const [installedResult] = await Promise.allSettled([
                    cleanupApi.get<ExtensionListItem[]>('/api/extensions'),
                ]);
                cleanupResults.push(installedResult);
                if (installedResult.status === 'fulfilled') {
                    const cleanupTargets = installedResult.value
                        .map((item) => item.extension)
                        .filter((extension) =>
                            [LIFECYCLE_PACKAGE_ID, BROKEN_PACKAGE_ID].includes(extension.packageId),
                        );
                    cleanupResults.push(
                        ...(await Promise.allSettled(
                            cleanupTargets.map(async (extension) => {
                                await cleanupApi.delete(`/api/extensions/${extension.id}`);
                                await expect(access(dirname(extension.filePath))).rejects.toMatchObject({
                                    code: 'ENOENT',
                                });
                                restartRequired = true;
                            }),
                        )),
                    );
                }

                cleanupResults.push(
                    ...(await Promise.allSettled([
                        packageSourceId
                            ? cleanupApi.delete(`/api/extensions/package-sources/${packageSourceId}`)
                            : Promise.resolve(),
                        viewerId ? cleanupApi.delete(`/api/users/${viewerId}`) : Promise.resolve(),
                        ...disabledPackageSourceIds.map((sourceId) =>
                            cleanupApi.patch(`/api/extensions/package-sources/${sourceId}`, {
                                enabled: { value: true },
                            }),
                        ),
                    ])),
                );

                if (restartRequired) {
                    const [restartResult] = await Promise.allSettled([restartHost(cleanupApi)]);
                    cleanupResults.push(restartResult);
                    if (restartResult.status === 'fulfilled') {
                        cleanupApi = restartResult.value;
                    }
                }

                cleanupResults.push(
                    ...(await Promise.allSettled([
                        (async () => {
                            const installed = await cleanupApi.get<ExtensionListItem[]>('/api/extensions');
                            expect(installed.map((item) => item.extension.packageId)).not.toContain(
                                LIFECYCLE_PACKAGE_ID,
                            );
                            expect(installed.map((item) => item.extension.packageId)).not.toContain(BROKEN_PACKAGE_ID);
                            expect(
                                installed.find((item) => item.extension.packageId === SHIMS_PACKAGE_ID)?.extension,
                            ).toMatchObject({ version: '1.0.0', managed: false });
                        })(),
                    ])),
                );
            }

            expect(
                cleanupResults
                    .filter((result) => result.status === 'rejected')
                    .map((result) => String((result as PromiseRejectedResult).reason)),
            ).toEqual([]);
        }
    });
});
