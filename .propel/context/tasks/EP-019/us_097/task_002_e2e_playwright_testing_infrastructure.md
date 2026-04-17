# Task - task_002_e2e_playwright_testing_infrastructure

## Requirement Reference

- User Story: us_097
- Story Location: .propel/context/tasks/EP-019/us_097/us_097.md
- Acceptance Criteria:
  - AC-2: Given the E2E test project is scaffolded, When a developer writes a Playwright test, Then the test runs against the application with browser automation for all supported browsers (Chromium, Firefox, WebKit).
- Edge Case:
  - How does the system handle flaky E2E tests? Playwright retries are configured (max 2 retries) and flaky test results are logged separately from genuine failures.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Testing | Playwright | 1.x |
| Testing | @playwright/test | 1.x |
| Frontend | React | 18.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Scaffold the Playwright end-to-end testing infrastructure with multi-browser support (Chromium, Firefox, WebKit), retry configuration for flaky test resilience, and test organization conventions, satisfying AC-2, TR-030, and edge case 2. This task creates an `e2e/` directory at the project root with Playwright Test runner configuration, browser project definitions, base test fixtures with authenticated/unauthenticated contexts, and sample E2E tests demonstrating page navigation and API interaction patterns. Playwright retries are configured with max 2 retries per test, and flaky test results are reported in a separate output section from genuine failures. The configuration supports both local development (headed mode with devtools) and CI pipeline execution (headless mode with trace artifacts on failure).

## Dependent Tasks

- US_001 — Requires backend project scaffold with running API.
- US_002 — Requires frontend React application scaffold.

## Impacted Components

- **NEW** `e2e/package.json` — Playwright Test dependencies and scripts
- **NEW** `e2e/playwright.config.ts` — Playwright configuration: browsers, retries, base URL, reporters
- **NEW** `e2e/tsconfig.json` — TypeScript configuration for E2E tests
- **NEW** `e2e/fixtures/base-fixtures.ts` — Shared test fixtures: authenticated page, API context
- **NEW** `e2e/fixtures/auth.setup.ts` — Authentication setup: login and store session state
- **NEW** `e2e/tests/health-check.spec.ts` — Sample E2E test: API health endpoint verification
- **NEW** `e2e/tests/login.spec.ts` — Sample E2E test: login flow with browser automation
- **NEW** `e2e/.gitignore` — Ignore test-results, playwright-report, blob-report directories

## Implementation Plan

1. **Create `e2e/package.json` with Playwright dependencies**: Create in `e2e/package.json`:
   ```json
   {
     "name": "upacip-e2e",
     "version": "1.0.0",
     "private": true,
     "scripts": {
       "test": "npx playwright test",
       "test:headed": "npx playwright test --headed",
       "test:chromium": "npx playwright test --project=chromium",
       "test:firefox": "npx playwright test --project=firefox",
       "test:webkit": "npx playwright test --project=webkit",
       "test:debug": "npx playwright test --debug",
       "report": "npx playwright show-report",
       "install-browsers": "npx playwright install --with-deps"
     },
     "devDependencies": {
       "@playwright/test": "^1.44.0"
     }
   }
   ```
   Scripts:
   - `test` — run all tests headless across all browser projects.
   - `test:headed` — run in headed mode for local debugging.
   - `test:chromium/firefox/webkit` — run against a specific browser.
   - `test:debug` — run with Playwright Inspector for step-through debugging.
   - `install-browsers` — download browser binaries (required before first run).

2. **Create `playwright.config.ts` with multi-browser and retry configuration (AC-2, edge case 2)**: Create in `e2e/playwright.config.ts`:
   ```typescript
   import { defineConfig, devices } from '@playwright/test';

   export default defineConfig({
     testDir: './tests',
     fullyParallel: true,
     forbidOnly: !!process.env.CI,
     retries: process.env.CI ? 2 : 0,
     workers: process.env.CI ? 1 : undefined,
     reporter: [
       ['html', { open: 'never' }],
       ['list'],
       ['json', { outputFile: 'test-results/results.json' }],
     ],
     use: {
       baseURL: process.env.BASE_URL || 'http://localhost:5173',
       trace: 'on-first-retry',
       screenshot: 'only-on-failure',
       video: 'on-first-retry',
     },
     projects: [
       {
         name: 'setup',
         testMatch: /.*\.setup\.ts/,
       },
       {
         name: 'chromium',
         use: {
           ...devices['Desktop Chrome'],
           storageState: 'e2e/.auth/user.json',
         },
         dependencies: ['setup'],
       },
       {
         name: 'firefox',
         use: {
           ...devices['Desktop Firefox'],
           storageState: 'e2e/.auth/user.json',
         },
         dependencies: ['setup'],
       },
       {
         name: 'webkit',
         use: {
           ...devices['Desktop Safari'],
           storageState: 'e2e/.auth/user.json',
         },
         dependencies: ['setup'],
       },
     ],
     webServer: {
       command: 'npm run dev',
       cwd: '../app',
       url: 'http://localhost:5173',
       reuseExistingServer: !process.env.CI,
       timeout: 120000,
     },
   });
   ```
   Key configuration:
   - **Multi-browser (AC-2)**: Three projects — Chromium, Firefox, WebKit — each using device-specific defaults.
   - **Retries (edge case 2)**: `retries: 2` in CI, `0` locally. First retry captures trace/video for diagnosis.
   - **Reporters**: HTML (interactive), list (console), JSON (CI parsing). HTML reporter enables manual inspection of flaky vs genuine failures.
   - **Artifacts**: `trace: 'on-first-retry'` captures Playwright trace on retry. `screenshot: 'only-on-failure'` captures page state. `video: 'on-first-retry'` records video for flaky test analysis.
   - **Authentication**: `setup` project runs authentication before browser projects. Stored session in `.auth/user.json`.
   - **Web server**: Auto-starts React dev server before tests, reuses existing server locally.

3. **Create `tsconfig.json` for E2E tests**: Create in `e2e/tsconfig.json`:
   ```json
   {
     "compilerOptions": {
       "target": "ESNext",
       "module": "ESNext",
       "moduleResolution": "bundler",
       "strict": true,
       "esModuleInterop": true,
       "skipLibCheck": true,
       "forceConsistentCasingInFileNames": true,
       "resolveJsonModule": true,
       "baseUrl": ".",
       "paths": {
         "@fixtures/*": ["./fixtures/*"]
       }
     },
     "include": ["tests/**/*.ts", "fixtures/**/*.ts"]
   }
   ```

4. **Create authentication setup fixture**: Create in `e2e/fixtures/auth.setup.ts`:
   ```typescript
   import { test as setup, expect } from '@playwright/test';

   const authFile = 'e2e/.auth/user.json';

   setup('authenticate', async ({ page }) => {
     await page.goto('/login');
     await page.getByLabel('Email').fill(process.env.TEST_USER_EMAIL || 'testadmin@clinic.com');
     await page.getByLabel('Password').fill(process.env.TEST_USER_PASSWORD || 'Test@12345');
     await page.getByRole('button', { name: 'Sign In' }).click();

     await page.waitForURL('/dashboard');
     await expect(page.getByRole('heading', { name: /dashboard/i })).toBeVisible();

     await page.context().storageState({ path: authFile });
   });
   ```
   Authentication runs once per test suite and stores session state for reuse across all browser projects — avoids login overhead per test.

5. **Create base test fixtures**: Create in `e2e/fixtures/base-fixtures.ts`:
   ```typescript
   import { test as base, expect } from '@playwright/test';

   type Fixtures = {
     apiBaseUrl: string;
   };

   export const test = base.extend<Fixtures>({
     apiBaseUrl: async ({}, use) => {
       const url = process.env.API_BASE_URL || 'http://localhost:5000';
       await use(url);
     },
   });

   export { expect };
   ```
   Provides:
   - `apiBaseUrl` — configurable API base URL for direct API testing within E2E context.
   - Re-exports `expect` for consistent imports across test files.

6. **Create sample health check E2E test**: Create in `e2e/tests/health-check.spec.ts`:
   ```typescript
   import { test, expect } from '@fixtures/base-fixtures';

   test.describe('Health Check', () => {
     test('API health endpoint returns healthy status', async ({ request, apiBaseUrl }) => {
       const response = await request.get(`${apiBaseUrl}/health`);
       expect(response.ok()).toBeTruthy();

       const body = await response.json();
       expect(body.status).toBe('Healthy');
     });

     test('Frontend loads without errors', async ({ page }) => {
       await page.goto('/');
       await expect(page).toHaveTitle(/UPACIP/i);
       const consoleErrors: string[] = [];
       page.on('console', (msg) => {
         if (msg.type() === 'error') consoleErrors.push(msg.text());
       });
       await page.waitForLoadState('networkidle');
       expect(consoleErrors).toHaveLength(0);
     });
   });
   ```

7. **Create sample login flow E2E test**: Create in `e2e/tests/login.spec.ts`:
   ```typescript
   import { test, expect } from '@playwright/test';

   test.describe('Login Flow', () => {
     test.use({ storageState: { cookies: [], origins: [] } });

     test('successful login redirects to dashboard', async ({ page }) => {
       await page.goto('/login');
       await page.getByLabel('Email').fill('testadmin@clinic.com');
       await page.getByLabel('Password').fill('Test@12345');
       await page.getByRole('button', { name: 'Sign In' }).click();

       await expect(page).toHaveURL(/dashboard/);
       await expect(page.getByRole('heading', { name: /dashboard/i })).toBeVisible();
     });

     test('invalid credentials show error message', async ({ page }) => {
       await page.goto('/login');
       await page.getByLabel('Email').fill('invalid@clinic.com');
       await page.getByLabel('Password').fill('wrongpassword');
       await page.getByRole('button', { name: 'Sign In' }).click();

       await expect(page.getByText(/invalid credentials/i)).toBeVisible();
       await expect(page).toHaveURL(/login/);
     });
   });
   ```
   These tests use `storageState: { cookies: [], origins: [] }` to override the default authenticated state, testing the unauthenticated login flow.

8. **Create `.gitignore` for E2E artifacts**: Create in `e2e/.gitignore`:
   ```
   node_modules/
   test-results/
   playwright-report/
   blob-report/
   .auth/
   ```
   Excludes browser binaries, test artifacts, reports, and authentication state files from version control.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── tests/
│   ├── UPACIP.ArchTests/                            ← from US_096 task_001
│   ├── UPACIP.Service.Tests/                        ← from task_001
│   ├── UPACIP.Api.Tests/                            ← from task_001
│   └── UPACIP.Tests.Common/                         ← from task_001
├── app/                                             ← React frontend
│   ├── package.json
│   └── src/
├── Server/
├── config/
└── scripts/
```

> Assumes US_001 (backend scaffold), US_002 (frontend scaffold), and task_001 (xUnit/Moq infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | e2e/package.json | Playwright Test dependencies and npm scripts |
| CREATE | e2e/playwright.config.ts | Multi-browser config: Chromium, Firefox, WebKit + retries + reporters |
| CREATE | e2e/tsconfig.json | TypeScript config for E2E test files |
| CREATE | e2e/fixtures/auth.setup.ts | Authentication setup: login once, store session state |
| CREATE | e2e/fixtures/base-fixtures.ts | Shared fixtures: apiBaseUrl, re-exported expect |
| CREATE | e2e/tests/health-check.spec.ts | Sample E2E: API health + frontend load verification |
| CREATE | e2e/tests/login.spec.ts | Sample E2E: login success + invalid credentials flow |
| CREATE | e2e/.gitignore | Ignore test-results, playwright-report, .auth |

## External References

- [Playwright — Getting Started](https://playwright.dev/docs/intro)
- [Playwright — Test Configuration](https://playwright.dev/docs/test-configuration)
- [Playwright — Test Retries](https://playwright.dev/docs/test-retries)
- [Playwright — Authentication](https://playwright.dev/docs/auth)
- [Playwright — Multi-Browser Projects](https://playwright.dev/docs/test-projects)

## Build Commands

```powershell
# Install Playwright and browser binaries
cd e2e
npm install
npx playwright install --with-deps

# Run all E2E tests (headless, all browsers)
npm test

# Run specific browser
npm run test:chromium

# Run in headed mode for local debugging
npm run test:headed

# View HTML report
npm run report
```

## Implementation Validation Strategy

- [ ] `npm install` succeeds in e2e/ directory
- [ ] `npx playwright install --with-deps` downloads Chromium, Firefox, and WebKit
- [ ] `npx playwright test --project=chromium` discovers and runs tests (AC-2)
- [ ] `npx playwright test --project=firefox` runs tests in Firefox (AC-2)
- [ ] `npx playwright test --project=webkit` runs tests in WebKit (AC-2)
- [ ] Retry configuration: CI retries 2 times, local retries 0 (edge case 2)
- [ ] Trace captured on first retry (`trace: 'on-first-retry'`) (edge case 2)
- [ ] Screenshot captured on failure (`screenshot: 'only-on-failure'`)
- [ ] HTML report distinguishes flaky retried tests from genuine failures (edge case 2)
- [ ] Authentication setup runs once and session is reused across browser projects

## Implementation Checklist

- [ ] Create e2e/package.json with @playwright/test dependency and npm scripts
- [ ] Configure playwright.config.ts with Chromium, Firefox, WebKit projects
- [ ] Set retries to 2 in CI with trace/video/screenshot on retry
- [ ] Create auth.setup.ts for one-time login and session storage
- [ ] Create base-fixtures.ts with apiBaseUrl and re-exported expect
- [ ] Write health-check.spec.ts sample test (API + frontend)
- [ ] Write login.spec.ts sample test (success + invalid credentials)
- [ ] Create .gitignore for test artifacts and auth state
