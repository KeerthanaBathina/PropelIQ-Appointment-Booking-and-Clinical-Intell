import { test, expect } from '../fixtures/base-fixtures';

/**
 * Health-check E2E tests verifying that both the .NET API and the React frontend
 * are reachable and in a healthy state (US_097, AC-2).
 *
 * These tests are intentionally lightweight and run first in the suite — they act as
 * a canary: if either server is down, all subsequent tests would fail for the wrong reason.
 */
test.describe('Health Check', () => {
  /**
   * Verifies the ASP.NET Core health-check endpoint returns a `Healthy` status.
   * Uses Playwright's `request` fixture for a direct HTTP call (no browser navigation).
   */
  test('API health endpoint returns healthy status', async ({ request, apiBaseUrl }) => {
    const response = await request.get(`${apiBaseUrl}/health`);

    expect(response.ok()).toBeTruthy();

    const body = await response.json();
    expect(body.status).toBe('Healthy');
  });

  /**
   * Verifies the React SPA loads without throwing browser-console errors.
   * Listens for `console.error` events after navigation to catch runtime exceptions
   * that would otherwise be invisible to Playwright.
   */
  test('Frontend loads without console errors', async ({ page }) => {
    const consoleErrors: string[] = [];

    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    await page.goto('/');
    await expect(page).toHaveTitle(/UPACIP/i);

    // Wait for all lazy-loaded chunks and API calls to settle.
    await page.waitForLoadState('networkidle');

    expect(consoleErrors, `Unexpected console errors: ${consoleErrors.join(', ')}`).toHaveLength(0);
  });
});
