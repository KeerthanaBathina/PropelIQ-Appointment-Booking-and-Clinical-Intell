# UPACIP — Unified Patient Access & Clinical Intelligence Platform

A full-stack healthcare appointment booking and clinical intelligence application built with:

- **Backend**: ASP.NET Core 8 Web API · PostgreSQL 16 · Redis · EF Core · ASP.NET Identity
- **Frontend**: React 18 · TypeScript · Vite · Material UI · Zustand · TanStack Query

---

## Running the Application

### Prerequisites

| Tool | Minimum Version | Notes |
|------|----------------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 | `dotnet --version` |
| [Node.js](https://nodejs.org/) | 18.0 | `node --version` |
| [PostgreSQL](https://www.postgresql.org/download/windows/) | 16 | Must be running locally |
| [Redis](https://redis.io/docs/getting-started/installation/install-redis-on-windows/) | 7.x | Required for session management |

---

### Step 1 — Provision the Database

Run the automated provisioning script (creates the `upacip` database and `upacip_app` role):

```powershell
.\scripts\provision-database.ps1 `
  -AppDbPassword "upacip_dev_password" `
  -PostgresPassword "<your-postgres-superuser-password>"
```

> **Manual install fallback**: Download the PostgreSQL 16 installer from https://www.postgresql.org/download/windows/ and pass `-InstallerPath "C:\Downloads\postgresql-16.x-windows-x64.exe"`.

---

### Step 2 — Backend Setup

#### 2a — Configure User Secrets

Run from the `src/UPACIP.Api` folder:

```powershell
cd src/UPACIP.Api

dotnet user-secrets set "ConnectionStrings:DefaultConnection" `
  "Host=localhost;Port=5432;Database=upacip;Username=upacip_app;Password=upacip_dev_password;Maximum Pool Size=100;Timeout=30;SSL Mode=Prefer;Trust Server Certificate=true"

dotnet user-secrets set "JwtSettings:SigningKey" "upacip-dev-jwt-signing-key-2026-secure!"

dotnet user-secrets set "Mfa:TotpEncryptionKey" "upacip-dev-mfa-totp-encryption-key-2026!"
```

Verify:

```powershell
dotnet user-secrets list
```

#### 2b — Apply Database Migrations

```powershell
# From the solution root
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

If `dotnet-ef` is not installed:

```powershell
dotnet tool install --global dotnet-ef
```

#### 2c — Seed Development Data

```powershell
psql -U upacip_app -d upacip -f scripts/seed-data.sql
```

**Seed credentials** (development only):

| Role | Email | Password |
|------|-------|----------|
| Admin | `admin@upacip.dev` | `SeedPassword1!` |
| Staff | `staff1@upacip.dev` | `SeedPassword1!` |
| Staff | `staff2@upacip.dev` | `SeedPassword1!` |
| Patient | `patient1@upacip.dev` | `SeedPassword1!` |
| Patient | `patient2@upacip.dev` | `SeedPassword1!` |

#### 2d — Start Redis

```powershell
# If installed as a Windows service
Start-Service Redis

# Or start directly
redis-server
```

#### 2e — Run the Backend

```powershell
# From the solution root
dotnet run --project src/UPACIP.Api
```

The API starts at:
- HTTP: **http://localhost:5000**
- Swagger UI: **http://localhost:5000/swagger**
- Health check: **http://localhost:5000/health**

---

### Step 3 — Frontend Setup

#### 3a — Verify Environment File

```powershell
cd app
Get-Content .env
# Should show: VITE_API_BASE_URL=
```

`VITE_API_BASE_URL` must be **empty** in development. Vite automatically proxies all `/api/*`
requests to `http://localhost:5000`. Set a full URL only for production builds.

#### 3b — Install Dependencies

```powershell
npm install
```

#### 3c — Start the Frontend

```powershell
npm run dev
```

The app opens at **http://localhost:3000**.

---

## Application Pages

### Public Pages (no login required)

| Page | URL | Description |
|------|-----|-------------|
| Sign In | `/login` | Email + password; MFA TOTP on second step |
| Create Account | `/register` | New patient registration |
| Forgot Password | `/forgot-password` | Sends reset link to email |
| Reset Password | `/reset-password?token=…&email=…` | Set new password via emailed link |
| Email Verification | `/verify-email?token=…` | Activate newly registered account |

### Authenticated Pages

| Page | URL | Roles |
|------|-----|-------|
| Dashboard Router | `/dashboard` | All |
| Patient Dashboard | `/patient/dashboard` | Patient |
| Book Appointment | `/patient/appointments/book` | Patient |
| Appointment History | `/patient/appointments/history` | Patient |
| AI Intake | `/patient/intake/ai` | Patient |
| Manual Intake | `/patient/intake/manual` | Patient |
| Staff Dashboard | `/staff/dashboard` | Staff |
| Arrival Queue | `/staff/queue` | Staff |
| Patient Search | `/staff/patients/search` | Staff |
| Patient Profile 360° | `/staff/patients/:id/profile` | Staff |
| Document Upload | `/staff/documents/:patientId` | Staff |
| Medical Coding Review | `/staff/patients/:id/coding` | Staff |
| Admin Dashboard | `/admin/dashboard` | Admin |

---

## Auth Flow Details

### Sign In

1. Go to **http://localhost:3000/login**
2. Enter email and password
3. On success → redirected to `/dashboard` → auto-routed to role dashboard:
   - **Patient** → `/patient/dashboard`
   - **Staff** → `/staff/dashboard`
   - **Admin** → `/admin/dashboard`
4. If MFA is enabled, a TOTP code prompt appears before redirection

### Create Account

1. Go to **http://localhost:3000/register**
2. Fill in: First Name, Last Name, Email, Phone, Date of Birth, Password
3. A "Check your email" confirmation appears
4. Click the verification link in the email to activate

> **Development tip** — If email delivery is not configured, manually confirm the email:
> ```sql
> UPDATE "AspNetUsers" SET "EmailConfirmed" = true WHERE "Email" = 'your@email.com';
> ```

### Forgot Password

1. Go to **http://localhost:3000/forgot-password**
2. Enter the registered email and click "Send Reset Link"
3. The response is always the same success message (anti-enumeration)
4. Click the emailed link to reach `/reset-password?token=…&email=…`
5. Set a new password meeting complexity requirements
6. Click "Sign In" to return to login

**Password requirements**: 8+ characters · uppercase · lowercase · digit · special character

---

## Troubleshooting

### Login fails with "Unable to connect"

- Confirm backend is running: `curl http://localhost:5000/health`
- Confirm `app/.env` has `VITE_API_BASE_URL=` (empty, not set to a URL)
- Confirm Vite dev server shows `Local: http://localhost:3000/`

### 401 after correct credentials

- Verify the database has been seeded: `psql -U upacip_app -d upacip -f scripts/seed-data.sql`
- Verify `EmailConfirmed = true` for the user in `AspNetUsers`
- Run `dotnet user-secrets list` from `src/UPACIP.Api` to confirm `JwtSettings:SigningKey` is set

### Account locked

5 failed login attempts trigger a 30-minute lockout. To manually unlock:

```sql
UPDATE "AspNetUsers"
SET "LockoutEnd" = NULL, "AccessFailedCount" = 0
WHERE "Email" = 'user@example.com';
```

### Database connection refused

- Verify PostgreSQL service: `Get-Service postgresql*`
- Confirm the password in user secrets matches `provision-database.ps1 -AppDbPassword`

### Redis connection error on startup

Start Redis (`redis-server`) or the API starts in degraded mode (session caching and slot
caching will be unavailable but core auth and booking continue to function).

### Frontend build fails: "Missing required environment variables"

For `npm run build` (production), set `VITE_API_BASE_URL` to the backend URL in `app/.env`.
For `npm run dev`, leave it empty.

---

## Running Tests

```powershell
# All backend tests
dotnet test

# Individual suites
dotnet test tests/UPACIP.Service.Tests
dotnet test tests/UPACIP.Api.Tests
dotnet test tests/UPACIP.ArchTests

# End-to-end (requires backend + frontend both running)
cd e2e ; npm install ; npx playwright install ; npx playwright test
```

---

## Build for Production

```powershell
# Backend
dotnet publish src/UPACIP.Api -c Release -o publish/api

# Frontend (set VITE_API_BASE_URL in app/.env first)
cd app ; npm run build   # output: app/dist/
```

---

## Project Structure

```
├── app/                        # React + TypeScript frontend (Vite)
│   ├── src/
│   │   ├── pages/              # Route-level page components
│   │   ├── components/         # Shared UI components
│   │   ├── hooks/              # React hooks (useAuth, useLogin, etc.)
│   │   ├── guards/             # ProtectedRoute (role-based access control)
│   │   ├── lib/                # apiClient.ts — fetch wrapper
│   │   ├── context/            # SessionTimeoutProvider
│   │   └── router.tsx          # All route definitions
│   └── .env                    # Frontend environment (VITE_API_BASE_URL)
├── src/
│   ├── UPACIP.Api/             # ASP.NET Core Web API
│   ├── UPACIP.DataAccess/      # EF Core DbContext, entities, migrations
│   ├── UPACIP.Service/         # Business logic services
│   └── UPACIP.Contracts/       # Shared interfaces and DTOs
├── tests/                      # Unit, integration, architecture, load tests
├── e2e/                        # Playwright end-to-end tests
├── scripts/                    # PowerShell automation (provision, deploy, seed)
└── config/                     # Feature flags, content filter rules
```

---

## Security Notes

- Refresh tokens: **HttpOnly, Secure, SameSite=Strict** cookies
- Access tokens: **sessionStorage** (cleared on browser close)
- Passwords hashed with **BCrypt** (work factor 10)
- JWT signed with **HMAC-SHA256** (32+ char key, stored in user secrets)
- Session inactivity timeout: **15 minutes** (13-minute warning modal)

---

# PropelIQ-Copilot

## Executive Summary

PropelIQ-Copilot is an AI-powered development framework that automates software development workflows from requirements gathering to implementation. It provides structured workflows for generating specifications, user stories, tasks, and code while enforcing comprehensive development standards across multiple languages and frameworks. The system combines business analysis, architecture design, and quality assurance into a unified platform for rapid, high-quality software delivery.

## Setup

PropelIQ-Copilot requires initial configuration to integrate with your project and enable AI-powered workflow automation. This setup process configures the framework files, API credentials, and MCP server integrations necessary for full functionality.

### Prerequisites

Before beginning setup, ensure you have:
- VS Code installed on your system
- Node.js and npm (required for MCP server integrations)
- Context7 API key (obtain from [Context7](https://context7.com))

### Installation Steps

#### Step 1: Copy Framework Files

Copy the following directories and files to your project root:
- `.github/` - Contains prompt definitions and development instructions
- `.propel/` - Contains templates and project artifacts
- `.vscode/` - Contains MCP configuration
- `.env-example` - Environment variable template

#### Step 2: Configure Environment Variables

1. Rename `.env-example` to `.env` in your project root
2. Open the `.env` file and replace the placeholder with your actual Context7 API key:

```bash
CONTEXT7_API_KEY=your-actual-api-key-here
```

#### Step 3: Configure Source Control

Add the following entries to your project's `.gitignore` file to prevent committing sensitive configuration:

```gitignore
# PropelIQ-Copilot Framework
.env
.github/*
.propel/templates/*
.vscode/mcp.json
```

This ensures API keys and framework-specific files remain local to your development environment.

#### Step 4: Verify Installation

1. Open "Chat" by pressing `Ctrl + Alt + I`
2. Type `\` (backslash) to view available prompts
3. Confirm that all workflows from `.github/prompts/` appear in the list

If commands are visible, your setup is complete and PropelIQ-Copilot is ready to use.

## Prompts

| Prompt | Description | Input | Output | Usage Example |
|----------|-------------|-------|--------|---------------|
| `analyze-codebase` | Entry point for comprehensive codebase analysis with architectural insights and strategic recommendations | Repository URL, folder path, technology stack, business domain (optional) | `.propel/context/docs/codeanalysis.md` | `/analyze-codebase` |
| `analyze-implementation` | Reviews completed code changes against task requirements for scope alignment and quality assessment | Task file path | `.propel/context/tasks/us_XXX/reviews/task-review-<task-id>.md`, console output | `/analyze-implementation .propel/context/tasks/task_001/task_001.md` |
| `analyze-ux` | UI/UX analysis via Playwright for visual consistency, accessibility, and responsiveness. | Task file path (recommended), or server URL with app path | `.propel/context/tasks/us_XXX/reviews/ui-review-<task-id>.md` | `/analyze-ux .propel/context/tasks/us_001/task_001.md` |
| `build-prototype` | Transforms business hypotheses into working validation prototypes within 80 hours with priority-based scoping | Business hypothesis, feature idea, problem statement | Working prototype code | `/build-prototype "E-commerce checkout flow"` |
| `create-automation-test` | Decomposes functional requirements into feature-level and E2E test workflow specifications for Playwright automation | spec.md, feature name, or UC-XXX (optional), --type flag | `.propel/context/test/tw_<feature>.md`, `.propel/context/test/e2e_<journey>.md` | `/create-automation-test --type feature` |
| `create-epics` | Generates EP-XXX epics with requirement mappings, priorities, business value. | File path to requirements document, free text epic scope (optional) | `.propel/context/docs/epics.md` | `/create-epics spec.md` |
| `create-project-plan` | Generates project plan with scope, milestones, AI-adjusted cost baseline, auto-derived team composition, and risk register from discovery phase outputs | spec.md (required), design.md (required), `--start-date`, `--buffer` flags (optional) | `.propel/context/docs/project_plan.md` | `/create-project-plan` |
| `create-figma-spec` | Derives screens from requirements, creates screen specifications with state requirements, flows, and design tokens | Path to spec.md file (optional) | `.propel/context/docs/figma_spec.md`, `.propel/context/docs/designsystem.md` | `/create-figma-spec` |
| `create-iac` | Generates Terraform modules for multi-cloud providers from infrastructure specification | infra-spec.md, --provider flag | `.propel/context/iac/<provider>/terraform/` | `/create-iac --provider <cloud>` |
| `create-pipeline-scripts` | Generates GitHub Actions workflow files from CI/CD specification | cicd-spec.md, --platform flag | `.propel/context/pipelines/github-actions/` | `/create-pipeline-scripts` |
| `create-spec` | Generates functional requirements (FR-XXX) and Use Case specifications with PlantUML diagrams | Feature specifications, business requirements, project scope documents (.pdf, .txt, .md, .docx) | `.propel/context/docs/spec.md` | `/create-spec project-scope.md` |
| `create-sprint-plan` | Generates sprint plans with dependency-ordered story allocation, sprint goals, capacity planning, and critical path analysis | epics.md (required), us_*.md (required), project_plan.md (recommended), EP-XXX filter, `--sprint-duration`, `--velocity`, `--buffer` flags (optional) | `.propel/context/docs/sprint_plan.md` | `/create-sprint-plan` |
| `create-test-plan` | Generates comprehensive E2E test plans from spec.md and design.md with full requirement traceability | spec.md, design.md, feature name (optional), --scope flag | `.propel/context/docs/test_plan_[feature].md` | `/create-test-plan --scope full` |
| `create-user-stories` | Generates INVEST-compliant user stories with acceptance criteria, effort estimation, and Visual Design Context section | Scope file path, Epic ID, feature text, or epic URL | `.propel/context/tasks/us_XXX/us_XXX.md` | `/create-user-stories scope.md EP-001` |
| `design-architecture` | Generates comprehensive design documents including NFR, TR, DR requirements and architecture patterns | Feature file path (optional) | `.propel/context/docs/design.md` | `/design-architecture .propel/context/docs/spec.md` |
| `design-model` | Generates UML architectural diagrams including conceptual, component, deployment, data flow, ERD, and sequence diagrams | Path to spec.md or codeanalysis.md (optional) | `.propel/context/docs/models.md` | `/design-model spec.md` |
| `generate-figma` | Transforms screen specifications into production-ready Figma structures with component libraries and clickable prototypes | figma_spec.md, designsystem.md | Figma artifacts and JPG exports | `/generate-figma` |
| `generate-playwright-scripts` | Generates production-ready Playwright TypeScript scripts from test workflow specifications | tw_*.md or e2e_*.md file(s), --type flag | `test-automation/tests/*.spec.ts`, `test-automation/e2e/*.spec.ts`, `test-automation/pages/*.page.ts` | `/generate-playwright-scripts` |
| `generate-wireframe` | Generates wireframe with SCR-XXX traceability, navigation map, and UXR coverage validation. | figma_spec.md (primary), fidelity level (low/high), `--generate-wireframes` flag | `.propel/context/wireframes/[Lo-Fi\|Hi-Fi]/wireframe-SCR-XXX-{name}.html`, `navigation-map.md` | `/generate-wireframe --fidelity=high` |
| `implement-tasks` | Implements features, fixes bugs, and completes development tasks with systematic validation | Task file path(s), `--skip-history` (optional) | Implementation code | `/implement-tasks .propel/context/tasks/us_001/task_001.md` |
| `plan-bug-resolution` | Performs comprehensive bug triage, root cause analysis, and generates fix implementation tasks | Bug report file, URL, issue description, or error log, `--skip-history` (optional) | `.propel/context/tasks/bug_XXX/` | `/plan-bug-resolution bug-report.md` |
| `plan-cicd-pipeline` | Designs CI/CD pipeline architecture with CICD-XXX requirements and security gates | design.md, spec.md, --platform flag | `.propel/context/devops/cicd-spec.md` | `/plan-cicd-pipeline` |
| `plan-cloud-infrastructure` | Generates infrastructure specification with INFRA-XXX, SEC-XXX requirements from design.md | design.md, spec.md (optional), --cloud-provider flag | `.propel/context/devops/infra-spec.md` | `/plan-cloud-infrastructure` |
| `plan-development-tasks` | Generates implementation tasks from feature requirements with context integration | User story file path, URL, text, or feature requirements, `--skip-history` (optional) | `.propel/context/tasks/us_XXX/task_*.md` | `/plan-development-tasks .propel/context/tasks/us_001/us_001.md` |
| `plan-unit-tests` | Generates comprehensive unit test plans from user story specifications with test coverage analysis | User story file path, ID, URL, or text | `.propel/context/tasks/us_XXX/unittest/` | `/plan-unittest us_001` |
| `pull-request` | Pull request creation with comprehensive validation, conflict detection, and platform support (GitHub/Azure DevOps) | Platform, title, description (optional) | Pull request on configured platform | `/pull-request feature/auth main "Add OAuth2 authentication"` |
| `review-code` | Comprehensive code review with technology-specific analysis, static analysis, and architectural assessment | File path(s) or local changes (git diff), `--skip-history` (optional) | `.propel/code-reviews/review_<timestamp>.md`, `.propel/learnings/findings-registry.md` | `/review-code --file src/UserService.cs` or `/review-code` |
| `review-devops-security` | Mandatory security gate reviewing IaC and pipelines for compliance | infra-spec.md, cicd-spec.md, generated artifacts | `.propel/context/devops/security-reviews/review_*.md` | `/review-devops-security` |

## Agentic Orchestrators

Agentic orchestrators automate multi-step workflows by sequentially invoking individual workflows, managing conditional branching, and providing unified progress tracking. Each orchestrator targets a specific phase of the development lifecycle.

| Orchestrator | Description | Handoff | Usage Example |
|--------------|-------------|---------|---------------|
| `discovery-agent` | Orchestrates technical discovery: `create-spec` → `design-architecture` → `design-model` → `create-figma-spec` [if UI] → `create-test-plan` | `/backlog-agent` | `/discovery-agent Project_Scope.md` |
| `backlog-agent` | Transforms specs into backlog: `create-epics` → `generate-wireframe` [optional] → `create-user-stories` | `/build-feature-agent` | `/backlog-agent --generate-wireframes` |
| `build-feature-agent` | Orchestrates user story implementation: `plan-development-tasks` → implement → analyze → UX review [if UI] → code review → unit tests | /devops-agent | `/build-feature-agent us_001` |
| `bug-fixing-agent` | Orchestrates bug resolution: `plan-bug-resolution` → identify test impact → implement → analyze → UX review [if UI] → code review → update tests [if needed] → run test suite | PR/Deploy | `/bug-fixing-agent bug_042` |
| `devops-agent` | Orchestrates DevOps phase: `plan-cloud-infrastructure` → `create-iac` → `plan-cicd-pipeline` → `create-pipeline-scripts` → `review-devops-security` | Artifacts ready for deployment | `/devops-agent --scope full` |
| `validation-agent` | Orchestrates test automation: `create-automation-test` → `generate-playwright-scripts` → test execution → validation report | `/pull-request` or `/devops-agent` | `/validation-agent --type both` |

## Instructions

| Instruction | Description |
|------|-------------|
| `ai-assistant-usage-policy` | Prevents cascade from wreaking havoc across your codebase. Prioritizes explicit user commands, factual verification over internal knowledge, and adherence to interaction philosophy. |
| `agile-methodology-guidelines` | Story breakdown best practices with max 5 story points per story (1 point = 8 hours), ensuring independent and deliverable standalone units. |
| `angular-development-standards` | Modern Angular patterns following official style guide with TypeScript strict mode, standalone components, signals, and reactive forms. |
| `aspnet-webapi-standards` | ASP.NET Core 9 REST API development with Controllers and Minimal API styles, proper routing, validation, and error handling. |
| `backend-development-standards` | Higher-level system design ensuring correctness under failure, evolvability, data integrity, observability, and production readiness. |
| `cicd-pipeline-standards` | CI/CD pipeline security and quality gates including SAST, SCA, container scanning, secrets detection, and deployment approvals. |
| `cloud-architecture-standards` | Well-Architected Framework patterns for multi-cloud providers covering networking, compute, security, and monitoring. |
| `code-anti-patterns` | Fast detection and prevention of common mistakes including god objects, circular dependencies, magic constants, and silent error swallowing. |
| `code-documentation-standards` | Self-explanatory code with minimal comments. Comment WHY, not WHAT. Write code that speaks for itself. |
| `csharp-coding-standards` | C# development standards with high-confidence suggestions, design decision documentation, and modern language feature usage. |
| `database-standards` | Higher-order data architecture covering modeling, normalization, indexing, query optimization, migration strategies, and operational quality. |
| `development-foundations` | Reference guidelines for analyzing existing tasks, understanding patterns, and maintaining consistency in task structure and naming conventions. |
| `dotnet-architecture-standards` | SOLID principles and .NET best practices for robust, maintainable systems with dependency injection, middleware patterns, and clean architecture. |
| `dry-principle-guidelines` | Enforces single-source-of-truth, prevents duplication, ensures minimal-impact changes. Anti-redundancy and surgical change rules for all artifacts. |
| `figma-design-standards` | Figma design standards for 6-page file structure, naming conventions, auto layout, component organization, and prototype flows. |
| `frontend-development-standards` | Advanced structural and operational frontend guidance covering styling, state management, component architecture, and build optimization. |
| `gitops-standards` | GitOps principles for declarative infrastructure, environment promotion, and automated reconciliation. |
| `iterative-development-guide` | Mandatory workflow process instructions requiring full review before execution and exact adherence without deviation. |
| `language-agnostic-standards` | Cross-language baseline applying KISS, YAGNI, fail-fast validation, size limits, naming clarity, error handling, and deterministic tests. |
| `markdown-styleguide` | Markdown content rules for headings, lists, code blocks, links, front matter, and proper formatting conventions. |
| `mcp-integration-standards` | MCP usage guidelines covering pagination policy for tools returning large lists, logs, files, or code with proper iteration patterns. |
| `performance-best-practices` | Performance optimization covering frontend (rendering, assets), backend (caching, async), and database (indexing, query) layers with measurement-first approach. |
| `playwright-standards` | Quick-reference checklist for Playwright locator priority, required patterns, forbidden patterns, and assertion usage. |
| `playwright-testing-guide` | Playwright testing focusing on stability, flakiness avoidance, suite sustainability, proper selectors, and explicit waits. |
| `playwright-typescript-guide` | Playwright code quality standards for test writing with TypeScript, including naming conventions and structure patterns. |
| `react-development-standards` | Modern React patterns following official documentation with hooks, functional components, context, and performance optimization. |
| `security-standards-owasp` | Enforces OWASP security best practices covering access control, encryption, injection prevention, secure configurations, and vulnerability management. |
| `software-architecture-patterns` | Architecture pattern selection matrix with guidance on layered, hexagonal, CQRS, event-driven, and microservices patterns. |
| `stored-procedure-standards` | SQL development standards for table/column naming (singular form), indexing, query optimization, and stored procedure best practices. |
| `terraform-iac-standards` | Terraform IaC standards for multi-cloud providers including module design, state management, and security patterns. |
| `template-implementation-guide` | Template structure compliance ensuring workflows produce outcomes following referenced template structure in `.propel/templates` folder. |
| `typescript-styleguide` | TypeScript development targeting 5.x/ES2022 with strict mode, type safety, modern syntax, and proper module organization. |
| `ui-ux-design-standards` | UI design and system guidance covering tokens, layout hierarchy, accessibility alignment, and interaction quality with users-first philosophy. |
| `uml-text-code-standards` | PlantUML and Mermaid diagram standards ensuring clarity, consistency, single-page focus, and proper notation usage. |
| `unit-testing-standards` | Test strategies by technology focusing on minimal tests during development, core user flows only, and strategic test placement. |
| `web-accessibility-standards` | WCAG 2.2 Level AA accessibility guidance ensuring semantic HTML, keyboard navigation, ARIA attributes, and color contrast compliance. |

## Templates

| Template | Description |
|----------|-------------|
| `automated-e2e-template` | E2E journey test workflow with session requirements, phase-based steps, checkpoints, and cross-UC traceability. |
| `automated-testing-template` | Feature-level test workflow with Happy Path, Edge Case, and Error test cases per Use Case, including YAML steps and page objects. |
| `code-review-template` | Comprehensive code review report with security assessment, licensing compliance, technology-specific analysis, and actionable feedback. |
| `codebase-analysis-template` | Comprehensive reverse engineering documentation with executive summaries, technical deep-dives, and actionable insights. |
| `cicd-specification-template` | CI/CD specification with CICD-XXX requirements, pipeline stages, security gates, and environment configurations. |
| `component-inventory-template` | UI component catalog extracted from wireframes with type classification, screen usage, and implementation status. |
| `design-analysis-template` | Design review report documenting blockers, high-priority issues, and UX findings with screenshots. |
| `design-model-template` | UML models template including conceptual, component, deployment, data flow diagrams, ERD, and sequence diagrams. |
| `design-reference-template` | UI impact assessment linking user stories to Figma projects, design images, or design system documentation. |
| `design-specification-template` | Context-rich implementation guide with validation loops, architecture goals, and progressive success strategy. |
| `devops-security-review-template` | Security review report with infrastructure posture, IaC analysis, pipeline security, and compliance assessment. |
| `epics-template` | Epic template with summary table, EP-XXX IDs, mapped requirement IDs, priorities, business value, and deliverables. |
| `figma-specification-template` | Figma design specification with UX requirements (UXR-XXX), screen inventory, component library, and design system tokens. |
| `findings-registry-template` | Registry of critical/high code review findings with root cause analysis for historical context and pattern detection. |
| `iac-module-template` | Terraform module documentation with inputs, outputs, resources, dependencies, and usage examples. |
| `infra-specification-template` | Infrastructure specification with INFRA-XXX, SEC-XXX, OPS-XXX, ENV-XXX requirements for cloud deployments. |
| `issue-triage-template` | Bug triage results with root cause analysis, fix implementation tasks, and systematic validation. |
| `project-plan-template` | Project plan with executive summary, scope definition, AI-adjusted effort estimation, team composition, milestones, cost baseline, risk register, and sprint planning bridge. |
| `requirements-template` | Standardized feature requirements with ID formats, use cases, user stories, and acceptance criteria. |
| `sprint-plan-template` | Sprint plan with configuration, epic dependency map, dependency-ordered sprint backlog, sprint goals, critical path analysis, load balance assessment, and coverage report. |
| `task-analysis-template` | Implementation analysis report with traceability matrix, logical findings, and pass/fail verdict. |
| `task-template` | AI-optimized task structure with context-rich details, validation loops, and iterative refinement approach. |
| `test-plan-template` | E2E test plan with test cases for FR, UC, NFR, TR, DR requirements, risk assessment, traceability matrix, and entry/exit criteria. |
| `unit-test-template` | Unit test plan template with user story reference, test coverage strategy, and validation criteria. |
| `user-story-template` | Canonical user story format with acceptance criteria, edge cases, traceability IDs, and INVEST principles. |
| `wireframe-reference-template` | Information architecture documentation with Figma/HTML wireframe references and screen inventory. |

---

## Prompt Execution Hierarchy

### Greenfield Projects (New Development)

```
/create-spec <project-scope.md>
│   Output: spec.md (functional requirements, use cases)
│
├── /design-architecture
│   │   Output: design.md (non-functional, technical, data requirements)
│   │
│   ├── /design-model
│   │   │   Output: models.md (UML diagrams, sequences)
│   │   │
│   │   └── /create-epics
│   │       │   Output: epics.md (epic decomposition, mappings) [includes EP-TECH]
│   │       │
│   │       └── /create-user-stories <EP-XXX>
│   │           │   Output: us_XXX.md (stories, acceptance criteria)
│   │           │
│   │           ├── /plan-development-tasks <us_XXX.md>
│   │           │   │   Output: task_XXX.md (implementation tasks)
│   │           │   │
│   │           │   └── /implement-tasks <task_XXX.md>
│   │           │       │   Output: Source code (added/updated files)
│   │           │       │
│   │           │       ├── /analyze-implementation <task_XXX.md>
│   │           │       │       Output: Console (gap analysis, verdict)
│   │           │       │
│   │           │       ├── /analyze-ux [IF UI impact]
│   │           │       │       Output: ux-analysis.md (accessibility, compliance)
│   │           │       │
│   │           │       ├── /review-code
│   │           │       │       Output: review_<timestamp>.md (findings, recommendations)
│   │           │       │
│   │           │       └── /pull-request
│   │           │               Output: PR (GitHub/Azure DevOps)
│   │           │
│   │           └── /plan-unit-tests <us_XXX>
│   │               │   Output: test_plan_XXX.md (test cases, coverage)
│   │               │
│   │               └── /implement-tasks <test_plan_XXX.md>
│   │                       Output: Source code (added/updated files)
│   │
│   ├── /create-project-plan
│   │   │   Output: project_plan.md (scope, milestones, cost baseline, risk register, team composition)
│   │   │
│   │   └── /create-sprint-plan [requires epics.md, us_*.md]
│   │           Output: sprint_plan.md (dependency-ordered sprints, sprint goals, critical path)
│   │
│   ├── /plan-cloud-infrastructure
│   │   │   Output: .propel/context/devops/infra-spec.md
│   │   │
│   │   └── /create-iac
│   │           Output: .propel/context/iac/<provider>/terraform/
│   │
│   ├── /plan-cicd-pipeline
│   │   │   Output: .propel/context/devops/cicd-spec.md
│   │   │
│   │   └── /create-pipeline-scripts
│   │           Output: .propel/context/pipelines/github-actions/
│   │
│   └── /review-devops-security
│           Output: .propel/context/devops/security-reviews/review_*.md
│
├── /create-figma-spec [IF UI impact]
│   │   Output: figma_spec.md (UX requirements, screens)
│   │   Output: designsystem.md (tokens, typography, colors)
│   │
│   ├── /generate-wireframe
│   │       Output: wireframes/ (HTML wireframes, inventory)
│   │
│   └── /generate-figma
│           Output: Figma artifacts (structures, exports)
│
└── /create-test-plan [--scope full|critical|regression|feature:<name>]
    │   Output: test_plan_[feature].md (test cases, traceability)
    │
    └── /create-automation-test [--type feature|e2e|both]
        │   Output: tw_<feature>.md, e2e_<journey>.md (test workflow specifications)
        │
        └── /generate-playwright-scripts [--type feature|e2e|both]
                Output: test-automation/ (Playwright TypeScript scripts, page objects)
```

### Brownfield Projects (Existing Codebase)

#### Quick Enhancement (Direct to User Stories)

For small enhancements where requirements are clear and no epic decomposition is needed.

```
/analyze-codebase
│   Output: codeanalysis.md (architecture, patterns, debt)
│
├── /design-model [references codeanalysis.md]
│   │   Output: models.md (UML diagrams, sequences)
│   │
│   └── /create-user-stories <enhancement-requirement>
│       │   Output: us_XXX.md (stories, acceptance criteria)
│       │
│       ├── /plan-development-tasks <us_XXX.md>
│       │   │   Output: task_XXX.md (implementation tasks)
│       │   │
│       │   └── /implement-tasks <task_XXX.md>
│       │       │   Output: Source code (added/updated files)
│       │       │
│       │       ├── /analyze-implementation <task_XXX.md>
│       │       │       Output: Console (gap analysis, verdict)
│       │       │
│       │       ├── /analyze-ux [IF UI impact]
│       │       │       Output: ux-analysis.md (accessibility, compliance)
│       │       │
│       │       ├── /review-code
│       │       │       Output: review_<timestamp>.md (findings, recommendations)
│       │       │
│       │       └── /pull-request
│       │               Output: PR (GitHub/Azure DevOps)
│       │
│       └── /plan-unit-tests <us_XXX>
│           │   Output: test_plan_XXX.md (test cases, coverage)
│           │
│           └── /implement-tasks <test_plan_XXX.md>
│                   Output: Source code (added/updated files)
│
└── /create-figma-spec [IF UI impact]
    │   Output: figma_spec.md (UX requirements, screens)
    │   Output: designsystem.md (tokens, typography, colors)
    │
    ├── /generate-wireframe
    │       Output: wireframes/ (HTML wireframes, inventory)
    │
    └── /generate-figma
            Output: Figma artifacts (structures, exports)
```

#### Medium Enhancement (With Epic Decomposition)

For larger enhancements requiring epic breakdown but not full requirements specification.

```
/analyze-codebase
│   Output: codeanalysis.md (architecture, patterns, debt)
│
├── /design-model [references codeanalysis.md]
│   │   Output: models.md (UML diagrams, sequences)
│   │
│   ├── /create-epics <enhancement-feature.md>
│   │   │   Output: epics.md (epic decomposition, mappings) [NO EP-TECH]
│   │   │
│   │   └── /create-user-stories <EP-XXX>
│   │       │   Output: us_XXX.md (stories, acceptance criteria)
│   │       │
│   │       ├── /plan-development-tasks <us_XXX.md>
│   │       │   │   Output: task_XXX.md (implementation tasks)
│   │       │   │
│   │       │   └── /implement-tasks <task_XXX.md>
│   │       │       │   Output: Source code (added/updated files)
│   │       │       │
│   │       │       ├── /analyze-implementation <task_XXX.md>
│   │       │       │       Output: Console (gap analysis, verdict)
│   │       │       │
│   │       │       ├── /analyze-ux [IF UI impact]
│   │       │       │       Output: ux-analysis.md (accessibility, compliance)
│   │       │       │
│   │       │       ├── /review-code
│   │       │       │       Output: review_<timestamp>.md (findings, recommendations)
│   │       │       │
│   │       │       └── /pull-request
│   │       │               Output: PR (GitHub/Azure DevOps)
│   │       │
│   │       └── /plan-unit-tests <us_XXX>
│   │           │   Output: test_plan_XXX.md (test cases, coverage)
│   │           │
│   │           └── /implement-tasks <test_plan_XXX.md>
│   │                   Output: Source code (added/updated files)
│   │
│   ├── /create-sprint-plan [requires epics.md, us_*.md]
│   │       Output: sprint_plan.md (dependency-ordered sprints, sprint goals, critical path)
│   │
│   ├── /plan-cloud-infrastructure
│   │   │   Output: .propel/context/devops/infra-spec.md
│   │   │
│   │   └── /create-iac
│   │           Output: .propel/context/iac/<provider>/terraform/
│   │
│   ├── /plan-cicd-pipeline
│   │   │   Output: .propel/context/devops/cicd-spec.md
│   │   │
│   │   └── /create-pipeline-scripts
│   │           Output: .propel/context/pipelines/github-actions/
│   │
│   └── /review-devops-security
│           Output: .propel/context/devops/security-reviews/review_*.md
│
└── /create-figma-spec [IF UI impact]
    │   Output: figma_spec.md (UX requirements, screens)
    │   Output: designsystem.md (tokens, typography, colors)
    │
    ├── /generate-wireframe
    │       Output: wireframes/ (HTML wireframes, inventory)
    │
    └── /generate-figma
            Output: Figma artifacts (structures, exports)
```

#### Comprehensive Enhancement (Full Requirements Analysis)

For major enhancements requiring complete requirements specification and architecture design.

```
/analyze-codebase
│   Output: codeanalysis.md (architecture, patterns, debt)
│
└── /create-spec <enhancement-scope.md>
    │   Output: spec.md (functional requirements, use cases)
    │
    ├── /design-architecture
    │   │   Output: design.md (non-functional, technical, data requirements)
    │   │
    │   ├── /design-model
    │   │   │   Output: models.md (UML diagrams, sequences)
    │   │   │
    │   │   └── /create-epics
    │   │       │   Output: epics.md (epic decomposition, mappings) [NO EP-TECH]
    │   │       │
    │   │       └── /create-user-stories <EP-XXX>
    │   │           │   Output: us_XXX.md (stories, acceptance criteria)
    │   │           │
    │   │           ├── /plan-development-tasks <us_XXX.md>
    │   │           │   │   Output: task_XXX.md (implementation tasks)
    │   │           │   │
    │   │           │   └── /implement-tasks <task_XXX.md>
    │   │           │       │   Output: Source code (added/updated files)
    │   │           │       │
    │   │           │       ├── /analyze-implementation <task_XXX.md>
    │   │           │       │       Output: Console (gap analysis, verdict)
    │   │           │       │
    │   │           │       ├── /analyze-ux [IF UI impact]
    │   │           │       │       Output: ux-analysis.md (accessibility, compliance)
    │   │           │       │
    │   │           │       ├── /review-code
    │   │           │       │       Output: review_<timestamp>.md (findings, recommendations)
    │   │           │       │
    │   │           │       └── /pull-request
    │   │           │               Output: PR (GitHub/Azure DevOps)
    │   │           │
    │   │           └── /plan-unit-tests <us_XXX>
    │   │               │   Output: test_plan_XXX.md (test cases, coverage)
    │   │               │
    │   │               └── /implement-tasks <test_plan_XXX.md>
    │   │                       Output: Source code (added/updated files)
    │   │
    │   ├── /create-project-plan
    │   │   │   Output: project_plan.md (scope, milestones, cost baseline, risk register, team composition)
    │   │   │
    │   │   └── /create-sprint-plan [requires epics.md, us_*.md]
    │   │           Output: sprint_plan.md (dependency-ordered sprints, sprint goals, critical path)
    │   │
    │   ├── /plan-cloud-infrastructure
    │   │   │   Output: .propel/context/devops/infra-spec.md
    │   │   │
    │   │   └── /create-iac
    │   │           Output: .propel/context/iac/<provider>/terraform/
    │   │
    │   ├── /plan-cicd-pipeline
    │   │   │   Output: .propel/context/devops/cicd-spec.md
    │   │   │
    │   │   └── /create-pipeline-scripts
    │   │           Output: .propel/context/pipelines/github-actions/
    │   │
    │   └── /review-devops-security
    │           Output: .propel/context/devops/security-reviews/review_*.md
    │
    ├── /create-figma-spec [IF UI impact]
    │   │   Output: figma_spec.md (UX requirements, screens)
    │   │   Output: designsystem.md (tokens, typography, colors)
    │   │
    │   ├── /generate-wireframe
    │   │       Output: wireframes/ (HTML wireframes, inventory)
    │   │
    │   └── /generate-figma
    │           Output: Figma artifacts (structures, exports)
    │
    └── /create-test-plan [--scope full|critical|regression|feature:<name>]
        │   Output: test_plan_[feature].md (test cases, traceability)
        │
        └── /create-automation-test [--type feature|e2e|both]
            │   Output: tw_<feature>.md, e2e_<journey>.md (test workflow specifications)
            │
            └── /generate-playwright-scripts [--type feature|e2e|both]
                    Output: test-automation/ (Playwright TypeScript scripts, page objects)
```

### Bug Resolution Track

```
/plan-bug-resolution <bug-report.md>
│   Output: task_XXX.md (root cause, fix tasks)
│
└── /implement-tasks <task_XXX.md>
    │   Output: Source code (bug fixes)
    │
    ├── /analyze-implementation <task_XXX.md>
    │       Output: Console (gap analysis, verdict)
    │
    ├── /analyze-ux [IF UI impact]
    │       Output: ux-analysis.md (accessibility, compliance)
    │
    ├── /review-code
    │       Output: review_<timestamp>.md (findings, recommendations)
    │
    └── /pull-request
            Output: PR (GitHub/Azure DevOps)
```

### Rapid Prototyping Track

```
/build-prototype "<business hypothesis>"
    Output: mvp/ (working prototype)
    Constraint: 80-hour timebox (priority-based scoping if exceeds budget)
```

---

*For detailed prompt documentation, refer to individual prompt files in `.github/prompts/`. For comprehensive instructions details, see files in `.github/instructions/`. For template structures, see files in `.propel/templates/`.*