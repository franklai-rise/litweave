import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => { window.__LITWEAVE_MOCK__ = true; });
});

test('mock refresh populates a nested literature canvas and review baseline', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByText('LitWeave').first()).toBeVisible();
  await page.getByRole('button', { name: /Refresh Zotero|刷新 Zotero/ }).first().click();
  // The fixture intentionally belongs to both a parent and a child
  // Collection, so Alias cards are rendered for each visible group.
  await expect(page.locator('.paper-card')).toHaveCount(4);
  await expect(page.getByText('Example research').first()).toBeVisible();
  await expect(page.getByText(/No new items|没有待审核/).first()).toBeVisible();
});
