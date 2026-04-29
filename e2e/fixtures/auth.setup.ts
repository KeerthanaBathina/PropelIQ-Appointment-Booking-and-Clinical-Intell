import { test as setup, expect } from '@playwright/test';
import path from 'path';

/**
 * Authentication setup that runs once per test suite before any browser project starts.
 *
 * Logs in with a test admin account and persists the authenticated session
 * (cookies + localStorage) to `.auth/user.json`. All browser projects load this
 * state so individual tests skip the login flow entirely, keeping the suite fast.
 *
 * Credentials are read from environment variables so that CI pipelines can inject
 * secrets without hard-coding them in source.
 *
 * @see https://playwright.dev/docs/auth
 */

const authFile = path.join(__dirname, '../.auth/user.json');

setup('authenticate', async ({ page }) => {
  await page.goto('/login');

  // Fill credentials — prefer env vars for CI; fall back to local dev defaults.
  await page.getByLabel('Email').fill(
    process.env.TEST_USER_EMAIL ?? 'testadmin@clinic.com',
  );
  await page.getByLabel('Password').fill(
    process.env.TEST_USER_PASSWORD ?? 'Test@12345',
  );

  await page.getByRole('button', { name: 'Sign In' }).click();

  // Wait for redirect to dashboard confirming successful authentication.
  await page.waitForURL('**/dashboard**');
  await expect(page.getByRole('heading', { name: /dashboard/i })).toBeVisible();

  // Persist the authenticated browser context (cookies + localStorage) for reuse.
  await page.context().storageState({ path: authFile });
});
