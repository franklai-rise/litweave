import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => { await page.addInitScript(() => { window.__LITWEAVE_MOCK__ = true; }); });

async function addPaper(page: import('@playwright/test').Page, title: string) {
  const before = await page.locator('.paper-node').count();
  await page.locator('.library-paper').filter({ hasText: title }).dblclick();
  const card = page.locator('.paper-node').nth(before);
  await expect(card).toBeVisible();
  return card;
}

test('starts with an empty independent whiteboard and leaves it unchanged while browsing folders', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('Untitled board').first()).toBeVisible();
  await expect(page.locator('.paper-node')).toHaveCount(0);
  await page.getByRole('button', { name: /刷新 Zotero/ }).click();
  await expect(page.locator('.library-paper')).toHaveCount(2);
  await page.locator('.collection-row').filter({ hasText: 'Methods' }).click();
  await expect(page.locator('.paper-node')).toHaveCount(0);
});

test('creates an immediately numbered literature card and a second independent browser-like tab', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /刷新 Zotero/ }).click();
  await addPaper(page, 'Physics-informed structural analysis');
  await expect(page.locator('.paper-node').filter({ hasText: '#1' })).toBeVisible();
  await page.locator('.tab-add').click();
  await expect(page.locator('.board-tab.active')).toContainText('Untitled board');
  await expect(page.locator('.paper-node')).toHaveCount(0);
  await page.locator('.board-tab').filter({ hasText: 'Untitled board' }).first().click();
  await expect(page.locator('.paper-node').filter({ hasText: '#1' })).toBeVisible();
});

test('renames and reorders whiteboard tabs by dragging them', async ({ page }) => {
  await page.goto('/');
  page.once('dialog', dialog => dialog.accept('Board One'));
  await page.getByRole('button', { name: '重命名' }).click();
  await page.locator('.tab-add').click();
  page.once('dialog', dialog => dialog.accept('Board Two'));
  await page.getByRole('button', { name: '重命名' }).click();
  const first = page.locator('.board-tab').filter({ hasText: 'Board One' });
  const second = page.locator('.board-tab').filter({ hasText: 'Board Two' });
  await second.dragTo(first);
  await expect(page.locator('.board-tab').first()).toContainText('Board Two');
});

test('creates an edge by visible relation action without a hidden handle dependency', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /刷新 Zotero/ }).click();
  const source = await addPaper(page, 'Physics-informed structural analysis');
  const target = await addPaper(page, 'A review of explainable mechanics');
  await source.locator('.node-relation-button').click();
  await target.click();
  await expect(page.locator('.react-flow__edge')).toHaveCount(1);
});

test('creates an edge by dragging a connection point', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: /刷新 Zotero/ }).click();
  const source = await addPaper(page, 'Physics-informed structural analysis');
  const target = await addPaper(page, 'A review of explainable mechanics');
  await source.hover({ position: { x: 4, y: 4 } });
  await expect(source.locator('.flow-handle')).toHaveCount(8);
  await source.locator('[data-handleid="anchor-bottom"]').dragTo(target);
  await expect(page.locator('.react-flow__edge')).toHaveCount(1);
});

test('adds an image through the local-image input and exposes all-way connection points', async ({ page }) => {
  await page.goto('/');
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9WlE4L0AAAAASUVORK5CYII=', 'base64');
  await page.locator('input[type=file]').setInputFiles({ name: 'note.png', mimeType: 'image/png', buffer: png });
  await expect(page.locator('.image-node')).toHaveCount(1);
  await expect(page.locator('.image-node .flow-handle')).toHaveCount(8);
  await page.locator('.image-node').dblclick();
  await expect(page.locator('.image-preview')).toBeVisible();
});
