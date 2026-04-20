# Task - task_001_fe_react_project_scaffold

## Requirement Reference

- User Story: us_002
- Story Location: .propel/context/tasks/EP-TECH/us_002/us_002.md
- Acceptance Criteria:
  - AC-1: Given the frontend project is initialized, When a developer runs `npm install && npm run build`, Then the project compiles with zero TypeScript errors.
  - AC-2: Given the frontend project is running, When a developer opens the application in a browser, Then a placeholder page renders using MUI 5 components with the design system's primary color (#1976D2).
  - AC-3: Given the project scaffold is complete, When a developer inspects package.json, Then React 18, TypeScript, MUI 5, React Query, and Zustand are listed as dependencies.
- Edge Case:
  - What happens when Node.js version is incompatible? Package.json engines field enforces minimum Node version with clear error message.
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

> US_002 is a scaffolding story with no specific screen implementation. The placeholder page uses the design system primary color but is not a production screen.

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| Language | TypeScript | 5.x |
| UI Component Library | Material-UI (MUI) | 5.x |
| State Management (Server) | React Query (TanStack Query) | 4.x |
| State Management (Client) | Zustand | 4.x |
| Build Tool | Vite | 5.x |

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

Initialize the React 18 frontend project using Vite with the TypeScript template. Install and configure all core dependencies: Material-UI 5 with a custom theme using the design system primary color (#1976D2), React Query (TanStack Query) for server state management, and Zustand for client state management. Create a placeholder landing page that renders MUI components to verify the scaffold works. Enforce Node.js version compatibility via the `engines` field in `package.json` and add environment variable validation to the build process.

## Dependent Tasks

- None (this is the foundational frontend scaffolding task)

## Impacted Components

- **NEW** `app/` — Frontend project root directory
- **NEW** `app/package.json` — Project manifest with all dependencies and Node engine constraint
- **NEW** `app/tsconfig.json` — TypeScript configuration for strict mode
- **NEW** `app/vite.config.ts` — Vite build configuration with environment variable validation
- **NEW** `app/src/main.tsx` — Application entry point with React 18 createRoot
- **NEW** `app/src/App.tsx` — Root component with MUI ThemeProvider
- **NEW** `app/src/theme.ts` — MUI 5 custom theme with primary color #1976D2
- **NEW** `app/src/pages/PlaceholderPage.tsx` — Placeholder landing page using MUI components

## Implementation Plan

1. **Initialize Vite project**: Run `npm create vite@latest app -- --template react-ts` to scaffold a React 18 + TypeScript project with Vite. This provides fast HMR, built-in TypeScript support, and optimized production builds.
2. **Install core dependencies**: Add `@mui/material @mui/icons-material @emotion/react @emotion/styled` for MUI 5, `@tanstack/react-query` for server state, and `zustand` for client state. Pin all versions to match design.md specifications.
3. **Configure TypeScript**: Update `tsconfig.json` with strict mode enabled, path aliases (`@/` mapping to `src/`), and `jsx: "react-jsx"` for the new JSX transform.
4. **Create MUI theme**: Define a custom MUI theme in `src/theme.ts` with the design system primary color (`#1976D2`). Wrap the application in `ThemeProvider` and `CssBaseline` in the root `App.tsx` component.
5. **Set up React Query**: Create a `QueryClient` instance and wrap the app in `QueryClientProvider` in `App.tsx`. Configure default stale time and retry settings appropriate for healthcare data (5-minute stale time per NFR-030).
6. **Create placeholder page**: Build a `PlaceholderPage` component using MUI `Container`, `Typography`, `Button`, and `AppBar` components to verify the theme renders correctly with the primary color.
7. **Add Node.js engine constraint**: Set `"engines": { "node": ">=18.0.0" }` in `package.json` and add an `.npmrc` file with `engine-strict=true` to enforce the version at install time.
8. **Add environment variable validation**: Create a build-time check in `vite.config.ts` that verifies required environment variables (e.g., `VITE_API_BASE_URL`) are present and fails the build with a descriptive error if missing.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   └── UPACIP.Api/
│       └── (backend project from US_001)
├── scripts/
│   └── check-sdk.ps1
└── (no frontend project exists yet)
```

> Placeholder: Updated during task execution.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/package.json | Project manifest with React 18, MUI 5, React Query 4, Zustand 4, TypeScript 5 dependencies and engines constraint |
| CREATE | app/tsconfig.json | TypeScript strict configuration with path aliases and react-jsx transform |
| CREATE | app/tsconfig.node.json | TypeScript configuration for Vite config file |
| CREATE | app/vite.config.ts | Vite build configuration with environment variable validation |
| CREATE | app/.npmrc | Engine strict enforcement (`engine-strict=true`) |
| CREATE | app/.env.example | Template listing required environment variables with descriptions |
| CREATE | app/index.html | Vite HTML entry point with root div |
| CREATE | app/src/main.tsx | React 18 createRoot entry point rendering App component |
| CREATE | app/src/App.tsx | Root component with MUI ThemeProvider, CssBaseline, and QueryClientProvider |
| CREATE | app/src/theme.ts | MUI 5 custom theme definition with primary color #1976D2 |
| CREATE | app/src/pages/PlaceholderPage.tsx | Placeholder page with MUI AppBar, Container, Typography, and Button |
| CREATE | app/src/vite-env.d.ts | Vite client type declarations |

## External References

- [React 18 documentation](https://react.dev/learn)
- [Vite React TypeScript template](https://vite.dev/guide/#scaffolding-your-first-vite-project)
- [MUI 5 installation guide](https://mui.com/material-ui/getting-started/installation/)
- [MUI 5 theming](https://mui.com/material-ui/customization/theming/)
- [TanStack React Query v4 quick start](https://tanstack.com/query/v4/docs/react/quick-start)
- [Zustand v4 getting started](https://docs.pmnd.rs/zustand/getting-started/introduction)
- [TypeScript strict mode](https://www.typescriptlang.org/tsconfig#strict)

## Build Commands

```powershell
# Navigate to frontend project
cd app

# Install dependencies (enforces Node.js version via .npmrc)
npm install

# Run development server with HMR
npm run dev

# Build for production (validates env vars, zero TS errors required)
npm run build

# Preview production build locally
npm run preview
```

## Implementation Validation Strategy

- [x] `npm install` completes without errors on Node.js 18+
- [x] `npm install` fails with clear error on Node.js < 18
- [x] `npm run build` compiles with zero TypeScript errors
- [x] `npm run dev` starts development server and placeholder page loads in browser
- [x] Placeholder page renders MUI components with primary color #1976D2
- [x] `package.json` lists React 18.x, TypeScript, @mui/material 5.x, @tanstack/react-query 4.x, and zustand 4.x
- [x] Build fails with descriptive error when required environment variables are missing

## Implementation Checklist

- [x] Initialize React 18 + TypeScript project using `npm create vite@latest app -- --template react-ts`
- [x] Install MUI 5 (`@mui/material`, `@mui/icons-material`, `@emotion/react`, `@emotion/styled`), TanStack React Query 4 (`@tanstack/react-query`), and Zustand 4 (`zustand`)
- [x] Configure `tsconfig.json` with `strict: true`, path alias `@/` → `src/`, and `jsx: "react-jsx"`
- [x] Create `src/theme.ts` with MUI `createTheme` using primary color `#1976D2` and export the theme object
- [x] Configure `src/App.tsx` with `ThemeProvider`, `CssBaseline`, and `QueryClientProvider` wrapping the application
- [x] Create `src/pages/PlaceholderPage.tsx` using MUI `AppBar`, `Container`, `Typography`, and `Button` to verify theme rendering
- [x] Add `"engines": { "node": ">=18.0.0" }` to `package.json` and create `.npmrc` with `engine-strict=true`
- [x] Add environment variable validation in `vite.config.ts` that fails build with descriptive error when required `VITE_*` variables are missing, and create `.env.example` documenting all required variables
