import { chromium } from '@playwright/test';
import assert from 'node:assert/strict';

const browser = await chromium.launch({ headless: true });
const context = await browser.newContext();
const page = await context.newPage();
page.on('pageerror', error => console.error(error));
try {
    await page.goto('http://127.0.0.1:15491/e2e/rename-mask.html');
    await page.getByRole('button', { name: 'Open manual association', exact: true }).click();
    const toggle = page.getByRole('checkbox', { name: 'Apply rename mask' });
    await toggle.waitFor();
    assert.equal(await toggle.isChecked(), false);
    assert.equal(await page.getByText('/library/Named/Episode-0.mkv', { exact: true }).count(), 0);
    await toggle.check();
    await page.getByText('/library/Named/Episode-0.mkv', { exact: true }).waitFor();
    let requests = JSON.parse(await page.getByLabel('Fixture request log').innerText());
    assert.equal(requests.some(request => request.path === 'associate'), false);
    await page.getByRole('button', { name: 'Associate and apply valid renames' }).click();
    await page.getByRole('status').filter({ hasText: '1 completed; 0 failed.' }).waitFor();
    requests = JSON.parse(await page.getByLabel('Fixture request log').innerText());
    assert.equal(requests.at(-2).path, 'associate');
    assert.equal(requests.at(-1).path, '/api/content/content/files/rename');
    assert.equal(requests.at(-1).body.files[0].mediaFileId, 'attached');
    await page.getByRole('button', { name: 'Close', exact: true }).first().click();
    await page.getByRole('button', { name: 'Open association without mask' }).click();
    await page.getByRole('button', { name: 'Associate file', exact: true }).waitFor({ state: 'visible' });
    await page.getByRole('button', { name: 'Associate file', exact: true }).click();
    assert.equal(await page.getByRole('checkbox').count(), 0);
    requests = JSON.parse(await page.getByLabel('Fixture request log').innerText());
    assert.equal(requests.filter(request => request.path.endsWith('/files/rename')).length, 1);
    await page.getByRole('button', { name: 'Open descendant rename' }).click();
    await page.getByText('The source file does not exist.', { exact: true }).waitFor();
    const scroll = await page.locator('.file-rename-preview').evaluate(element => ({
        height: element.clientHeight, scroll: element.scrollHeight,
        overflow: getComputedStyle(element).overflowY, rows: element.querySelectorAll('tr').length,
    }));
    console.log({ scroll });
    assert.equal(scroll.scroll > scroll.height && scroll.overflow === 'auto', true);
    await page.getByRole('button', { name: 'Apply valid renames' }).click();
    await page.getByRole('status').filter({ hasText: '79 completed; 1 failed.' }).waitFor();
    await page.getByRole('button', { name: 'Close', exact: true }).first().click();
    await page.getByRole('button', { name: 'Open import acceptance' }).click();
    await toggle.waitFor();
    assert.equal(await toggle.isChecked(), false);
    await toggle.check();
    await page.getByText('/library/Named/Episode-0.mkv', { exact: true }).waitFor();
    requests = JSON.parse(await page.getByLabel('Fixture request log').innerText());
    assert.equal(requests.some(request => request.path.endsWith('/bulk-accept')), false);
    await page.getByRole('button', { name: 'Accept', exact: true }).click();
    await page.getByRole('status').filter({ hasText: '1 imports queued; 0 skipped.' }).waitFor();
    requests = JSON.parse(await page.getByLabel('Fixture request log').innerText());
    assert.deepEqual(requests.at(-1).body.ids, ['candidate']);
    assert.equal(requests.at(-1).body.monitoringMode, 'Inherit');
    assert.equal(requests.at(-1).body.renamePreview.files[0].destinationPath, '/library/Named/Episode-0.mkv');
    console.log(JSON.stringify({ requests }, null, 2));
    console.log('PASS: disabled and hidden rename defaults, pre-association preview, explicit apply, scrollable bulk partial results, pre-import preview and accepted plan submission');
} finally {
    await context.close();
    await browser.close();
}
