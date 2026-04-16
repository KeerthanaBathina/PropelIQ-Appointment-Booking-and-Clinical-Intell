# Task - task_002_fe_spa_routing_iis_deploy

## Requirement Reference

- User Story: us_002
- Story Location: .propel/context/tasks/EP-TECH/us_002/us_002.md
- Acceptance Criteria:
  - AC-4: Given the frontend is built for production, When the output is deployed to IIS as a static build, Then the SPA serves correctly with client-side routing.
- Edge Case:
  - How does the system handle missing environment variables? Build fails with descriptive error listing required variables.

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

> This task configures routing and IIS deployment support. No specific screen implementation.

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| Language | TypeScript | 5.x |
| Routing | React Router | 6.x |
| Build Tool | Vite | 5.x |
| Infrastructure | IIS | 10 |

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

Configure React Router for client-side SPA routing and set up the production build pipeline for deployment as a static site on IIS 10. This includes creating an IIS `web.config` with URL Rewrite rules that redirect all non-file requests to `index.html` (enabling client-side routing to work on deep links and page refreshes), configuring the Vite production build output, and creating a deployment script that copies the build artifacts to IIS.

## Dependent Tasks

- task_001_fe_react_project_scaffold — React project, package.json, and Vite config must exist before routing and deployment configuration.

## Impacted Components

- **MODIFY** `app/package.json` — Add `react-router-dom` dependency
- **MODIFY** `app/src/App.tsx` — Wrap application in `BrowserRouter` and define route structure
- **NEW** `app/src/router.tsx` — Central route configuration with lazy-loaded route definitions
- **NEW** `app/public/web.config` — IIS URL Rewrite rules for SPA client-side routing fallback
- **NEW** `scripts/deploy-frontend.ps1` — PowerShell script to build and copy output to IIS wwwroot
- **MODIFY** `app/vite.config.ts` — Configure `base` path and `build.outDir` for IIS deployment

## Implementation Plan

1. **Install React Router**: Add `react-router-dom` v6.x to the project dependencies. This provides the `BrowserRouter`, `Routes`, `Route`, and `Outlet` components for declarative client-side routing.
2. **Create route configuration**: Define a central `router.tsx` file with route definitions. Include the placeholder page at the root route (`/`) and a catch-all 404 route. Use `React.lazy` for route-level code splitting to optimize bundle size.
3. **Integrate router in App.tsx**: Wrap the application content in `BrowserRouter` and render the route configuration inside the existing `ThemeProvider` and `QueryClientProvider` hierarchy.
4. **Create IIS web.config**: Add a `web.config` file to `app/public/` (copied to build output by Vite) with an IIS URL Rewrite rule that redirects all requests to `index.html` unless the request maps to an existing file or directory. This is required for SPA deep links and browser refresh to work correctly on IIS.
5. **Configure Vite build output**: Set `build.outDir` to `dist` and configure the `base` path to `/` in `vite.config.ts`. Ensure the production build generates assets with content hashing for cache busting.
6. **Create deployment script**: Write a PowerShell script (`scripts/deploy-frontend.ps1`) that runs `npm run build` in the `app/` directory and copies the contents of `app/dist/` to the IIS wwwroot directory. Include parameter for target path and validation that the build succeeded before copying.
7. **Validate SPA routing on IIS**: After deployment, verify that navigating directly to a deep route (e.g., `/dashboard`) serves `index.html` and React Router handles the route client-side.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   └── UPACIP.Api/
│       └── (backend project from US_001)
├── app/
│   ├── package.json
│   ├── tsconfig.json
│   ├── vite.config.ts
│   ├── .npmrc
│   ├── .env.example
│   ├── index.html
│   └── src/
│       ├── main.tsx
│       ├── App.tsx
│       ├── theme.ts
│       ├── vite-env.d.ts
│       └── pages/
│           └── PlaceholderPage.tsx
└── scripts/
    └── check-sdk.ps1
```

> Assumes task_001_fe_react_project_scaffold is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | app/package.json | Add `react-router-dom` v6.x dependency |
| CREATE | app/src/router.tsx | Central route configuration with lazy-loaded routes and 404 catch-all |
| MODIFY | app/src/App.tsx | Wrap content in `BrowserRouter`, render route outlet |
| CREATE | app/src/pages/NotFoundPage.tsx | 404 page component using MUI Typography |
| CREATE | app/public/web.config | IIS URL Rewrite rule redirecting non-file requests to index.html |
| MODIFY | app/vite.config.ts | Set `base: "/"` and `build.outDir: "dist"` for IIS deployment |
| CREATE | scripts/deploy-frontend.ps1 | PowerShell script to build frontend and copy dist to IIS wwwroot |

## External References

- [React Router v6 documentation](https://reactrouter.com/en/6.28.0)
- [Vite static deployment guide](https://vite.dev/guide/static-deploy)
- [IIS URL Rewrite module for SPA](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/url-rewrite-module-configuration-reference)
- [IIS static site hosting](https://learn.microsoft.com/en-us/iis/manage/creating-websites/scenario-build-a-static-website-on-iis)

## Build Commands

```powershell
# Navigate to frontend project
cd app

# Install new dependency
npm install react-router-dom

# Build for production
npm run build

# Deploy to IIS (from repository root)
cd ..
.\scripts\deploy-frontend.ps1 -TargetPath "C:\inetpub\wwwroot\upacip"

# Verify build output contains web.config
Test-Path app\dist\web.config
```

## Implementation Validation Strategy

- [ ] `npm run build` produces a `dist/` folder with `index.html`, hashed JS/CSS assets, and `web.config`
- [ ] `web.config` is present in `dist/` output and contains IIS URL Rewrite rules
- [ ] Running `npm run dev` and navigating to `/` renders the placeholder page
- [ ] Running `npm run dev` and navigating to a non-existent route renders the 404 page
- [ ] After deploying to IIS, navigating to the root URL serves the SPA
- [ ] After deploying to IIS, navigating directly to a deep route (e.g., `/dashboard`) serves `index.html` and client-side routing resolves the route
- [ ] Browser refresh on a deep route does not return IIS 404 error

## Implementation Checklist

- [ ] Install `react-router-dom` v6.x and add to `package.json` dependencies
- [ ] Create `src/router.tsx` with route definitions: root (`/`) rendering `PlaceholderPage`, catch-all (`*`) rendering `NotFoundPage`, using `React.lazy` for code splitting
- [ ] Create `src/pages/NotFoundPage.tsx` with MUI `Container` and `Typography` displaying a 404 message
- [ ] Modify `src/App.tsx` to wrap application in `BrowserRouter` and render the router outlet inside the existing provider hierarchy
- [ ] Create `app/public/web.config` with IIS URL Rewrite rule: all requests not matching physical files/directories rewrite to `/index.html`
- [ ] Update `vite.config.ts` to set `base: "/"` and `build.outDir: "dist"` with asset content hashing enabled
- [ ] Create `scripts/deploy-frontend.ps1` that accepts a `-TargetPath` parameter, runs `npm run build`, validates build success, and copies `dist/*` to the target IIS directory
