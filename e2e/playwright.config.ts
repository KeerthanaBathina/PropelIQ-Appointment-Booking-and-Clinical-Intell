import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for UPACIP E2E test suite (US_097, AC-2).
 *
 * Supports three browser projects (Chromium, Firefox, WebKit) and a separate
 * authentication setup project that runs once before any browser project starts.
 *
 * Retry / flaky-test strategy (edge case 2):
 * - CI: retries=2, trace/video captured on first retry for diagnosis.
 * - Local: retries=0, headed mode available via `npm run test:headed`.
 * - HTML reporter groups retried (flaky) tests separately from genuine failures.
 */
export default defineConfig({
  testDir: './tests',

  // Each test file runs in full isolation across browsers.
  fullyParallel: true,

  // Fail fast in CI when a test contains `test.only` — avoids committing focused runs.
  forbidOnly: !!process.env.CI,

  // Edge case 2: max 2 retries in CI; none locally to keep dev feedback instant.
  retries: process.env.CI ? 2 : 0,

  // Single worker in CI to prevent resource contention on limited-CPU runners.
  workers: process.env.CI ? 1 : undefined,

  reporter: [
    // Interactive HTML report — flaky retried tests appear under a separate "Flaky" heading.
    ['html', { open: 'never' }],
    // Streaming console output for CI logs.
    ['list'],
    // Machine-readable JSON for CI parsing / badge generation.
    ['json', { outputFile: 'test-results/results.json' }],
  ],

  use: {
    // React dev server (overridden by BASE_URL env var in CI).
    baseURL: process.env.BASE_URL ?? 'http://localhost:5173',

    // Edge case 2: capture trace on first retry so flaky failures are diagnosable.
    trace: 'on-first-retry',

    // Capture screenshot on any failure for visual diff.
    screenshot: 'only-on-failure',

    // Record video on first retry to replay exact flaky sequence.
    video: 'on-first-retry',
  },

  projects: [
    // ── Authentication setup ──────────────────────────────────────────────────
    // Runs auth.setup.ts once; stores session state to .auth/user.json.
    // All browser projects declare this as a dependency.
    {
      name: 'setup',
      testMatch: /.*\.setup\.ts/,
    },

    // ── AC-2: Chromium ────────────────────────────────────────────────────────
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'e2e/.auth/user.json',
      },
      dependencies: ['setup'],
    },

    // ── AC-2: Firefox ─────────────────────────────────────────────────────────
    {
      name: 'firefox',
      use: {
        ...devices['Desktop Firefox'],
        storageState: 'e2e/.auth/user.json',
      },
      dependencies: ['setup'],
    },

    // ── AC-2: WebKit (Safari engine) ──────────────────────────────────────────
    {
      name: 'webkit',
      use: {
        ...devices['Desktop Safari'],
        storageState: 'e2e/.auth/user.json',
      },
      dependencies: ['setup'],
    },
  ],

  // Auto-start the React dev server before running tests.
  // In CI, the server is started fresh each run; locally it reuses an already running instance.
  webServer: {
    command: 'npm run dev',
    cwd: '../app',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
});
