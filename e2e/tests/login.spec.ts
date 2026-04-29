import { test, expect } from '@playwright/test';

/**
 * Login flow E2E tests (US_097, AC-2).
 *
 * These tests explicitly override the default authenticated storage state by using
 * `test.use({ storageState: { cookies: [], origins: [] } })` so they exercise the
 * actual login page rather than landing on the dashboard via a pre-authenticated session.
 *
 * Covered scenarios:
 * 1. Happy path — valid credentials redirect to /dashboard.
 * 2. Invalid credentials — error message shown, URL stays on /login.
 */
test.describe('Login Flow', () => {
  // Override the authenticated storage state inherited from playwright.config.ts.
  // Every test in this describe block starts in an unauthenticated browser context.
  test.use({ storageState: { cookies: [], origins: [] } });

  /**
   * AC-2: Verifies the full login sequence from form submission to dashboard redirect.
   * Uses accessible selectors (getByLabel, getByRole) to remain resilient against
   * CSS class / layout changes.
   */
  test('successful login redirects to dashboard', async ({ page }) => {
    await page.goto('/login');

    await page.getByLabel('Email').fill('testadmin@clinic.com');
    await page.getByLabel('Password').fill('Test@12345');
    await page.getByRole('button', { name: 'Sign In' }).click();

    await expect(page).toHaveURL(/dashboard/);
    await expect(page.getByRole('heading', { name: /dashboard/i })).toBeVisible();
  });

  /**
   * AC-2 / edge case: Verifies that invalid credentials display an error message
   * and do NOT navigate away from the login page (prevents silent no-op on auth failure).
   */
  test('invalid credentials show error message', async ({ page }) => {
    await page.goto('/login');

    await page.getByLabel('Email').fill('nobody@invalid.com');
    await page.getByLabel('Password').fill('WrongPassword!99');
    await page.getByRole('button', { name: 'Sign In' }).click();

    // Error message must be visible and user must remain on the login page.
    await expect(page.getByText(/invalid credentials/i)).toBeVisible();
    await expect(page).toHaveURL(/login/);
  });
});
