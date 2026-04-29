import { test as base, expect, type APIRequestContext } from '@playwright/test';

/**
 * Custom fixture type extending the default Playwright fixtures with
 * UPACIP-specific helpers (US_097, AC-2).
 */
type UPACIPFixtures = {
  /**
   * Base URL of the .NET API server.
   * Defaults to `http://localhost:5000`; override with the `API_BASE_URL` env var
   * for staging/production-smoke runs.
   */
  apiBaseUrl: string;
};

/**
 * Extended `test` function that includes UPACIP-specific fixtures.
 *
 * Usage:
 * ```typescript
 * import { test, expect } from '@fixtures/base-fixtures';
 *
 * test('example', async ({ page, apiBaseUrl }) => {
 *   const res = await page.request.get(`${apiBaseUrl}/health`);
 *   expect(res.ok()).toBeTruthy();
 * });
 * ```
 */
export const test = base.extend<UPACIPFixtures>({
  apiBaseUrl: async ({}, use) => {
    const url = process.env.API_BASE_URL ?? 'http://localhost:5000';
    await use(url);
  },
});

// Re-export `expect` so test files can import both from a single source.
export { expect };
