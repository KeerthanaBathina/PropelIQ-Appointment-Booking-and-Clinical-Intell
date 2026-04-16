# Epic - Unified Patient Access & Clinical Intelligence Platform

## Epic Summary Table

| Epic ID | Epic Title | Mapped Requirement IDs |
|---------|------------|------------------------|
| EP-TECH | Project Foundation & Infrastructure Scaffolding | TR-001, TR-002, TR-003, TR-004, TR-007, TR-008, TR-016, TR-017, TR-018, TR-020, TR-022, FR-096 |
| EP-DATA | Core Data Entities & Persistence Layer | TR-005, DR-001, DR-002, DR-003, DR-004, DR-005, DR-006, DR-007, DR-008, DR-009, DR-010, DR-011 |
| EP-001 | User Management & Authentication | FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, FR-010, NFR-011, NFR-013 |
| EP-002 | Appointment Scheduling Core | FR-011, FR-012, FR-013, FR-014, FR-015, FR-016, FR-017, FR-018, FR-019, FR-020, FR-021, FR-022 |
| EP-003 | Appointment Extended Features & Calendar | FR-023, FR-024, FR-025, AIR-006, TR-015, TR-026, NFR-001, NFR-005 |
| EP-004 | Patient Intake & Onboarding | FR-026, FR-027, FR-028, FR-029, FR-030, FR-031, FR-032, FR-033, FR-034, FR-035, AIR-001, AIR-008 |
| EP-005 | Reminders & Notifications | FR-036, FR-037, FR-038, FR-039, FR-040, FR-041, FR-042, FR-043, FR-044, FR-045, TR-023, TR-024 |
| EP-006 | Clinical Document Upload & AI Parsing | FR-046, FR-047, FR-048, FR-049, FR-050, FR-051, FR-055, FR-057, FR-058, FR-059, FR-060, AIR-002 |
| EP-007 | Clinical Data Consolidation & Conflict Detection | FR-052, FR-053, FR-054, FR-056, AIR-005, AIR-007, AIR-009, AIR-010, AIR-S09, AIR-S10 |
| EP-008 | Medical Coding & Billing Preparation | FR-061, FR-062, FR-063, FR-064, FR-065, FR-066, FR-067, FR-068, FR-069, FR-070, AIR-003, AIR-004 |
| EP-009 | Queue Management & Patient Flow | FR-071, FR-072, FR-073, FR-074, FR-075, FR-076, FR-077, FR-078, FR-079, FR-080, NFR-004, NFR-008 |
| EP-010 | Staff & Admin Dashboards | FR-081, FR-082, FR-083, FR-084, FR-085, FR-086, FR-087, FR-088, FR-089, FR-090, NFR-046, NFR-047 |
| EP-011 | Security, Encryption & Access Control | FR-091, FR-092, FR-093, FR-094, FR-095, NFR-009, NFR-010, NFR-012, NFR-014, NFR-015, NFR-016, NFR-017 |
| EP-012 | AI Gateway & Provider Management | TR-006, TR-010, TR-012, TR-014, TR-025, AIR-O01, AIR-O02, AIR-O03, AIR-O04, AIR-O05, AIR-O07, AIR-O08, AIR-O09 |
| EP-013 | AI Quality, Monitoring & Safety | AIR-Q01, AIR-Q02, AIR-Q03, AIR-Q04, AIR-Q05, AIR-Q06, AIR-Q07, AIR-Q08, AIR-Q09, AIR-S01, AIR-S02, AIR-S03 |
| EP-014 | AI RAG Pipeline & Knowledge Base | AIR-R01, AIR-R02, AIR-R03, AIR-R04, AIR-R05, AIR-R06, AIR-S04, AIR-S05, AIR-S06, AIR-S07, AIR-S08, AIR-O06, AIR-O10 |
| EP-015 | Performance, Scalability & Reliability | NFR-002, NFR-003, NFR-006, NFR-007, NFR-019, NFR-022, NFR-023, NFR-026, NFR-028, NFR-029, NFR-030, NFR-031, FR-097, FR-098 |
| EP-016 | Data Integrity, Retention & Archival | DR-012, DR-013, DR-014, DR-015, DR-016, DR-017, DR-018, DR-019, DR-020, DR-021, NFR-033, NFR-043 |
| EP-017 | Data Backup, Recovery & Migration | DR-022, DR-023, DR-024, DR-025, DR-026, DR-027, DR-028, DR-029, DR-030, DR-031, DR-032, DR-033, FR-100 |
| EP-018 | HIPAA Compliance & Patient Rights | NFR-018, NFR-021, NFR-024, NFR-025, NFR-032, NFR-035, NFR-041, NFR-042, NFR-044, NFR-045 |
| EP-019 | Development Standards, Testing & Quality | TR-009, TR-011, TR-013, TR-019, TR-021, TR-027, TR-028, TR-029, TR-030, TR-031, TR-032, TR-033, TR-034 |
| EP-020 | Frontend Accessibility & Operational Standards | NFR-020, NFR-027, NFR-034, NFR-036, NFR-037, NFR-038, NFR-039, NFR-040, NFR-048, NFR-049, NFR-050, NFR-096, FR-099, TR-035 |

## Epic Description

### EP-TECH: Project Foundation & Infrastructure Scaffolding

**Business Value**: Enables all subsequent development by establishing the project foundation, development environment, deployment pipeline, and base architecture for the healthcare platform.

**Description**: Bootstrap the green-field project with .NET 8 backend (ASP.NET Core Web API), React 18 frontend with TypeScript, PostgreSQL 16 database, and Upstash Redis caching layer. Establish Windows Server 2022 deployment with IIS for frontend and Windows Services for backend. Configure HTTPS with TLS 1.2+, health check endpoints, centralized configuration management, and feature flags infrastructure. This epic produces the deployable skeleton upon which all feature epics build.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- .NET 8 ASP.NET Core Web API project scaffold with Swagger/OpenAPI
- React 18 + TypeScript + MUI 5 frontend scaffold
- PostgreSQL 16 database provisioning with connection pooling
- Upstash Redis integration for caching and session storage
- ASP.NET Core Identity + JWT authentication scaffold
- IIS static hosting configuration for React SPA
- Windows Services deployment configuration for backend
- HTTPS/TLS 1.2+ certificate setup (Let's Encrypt)
- Health check endpoints (/health, /ready)
- Centralized appsettings.json configuration with environment overrides
- Feature flags infrastructure via configuration files

**Dependent EPICs**:
- None

---

### EP-DATA: Core Data Entities & Persistence Layer

**Business Value**: Enables data operations for all feature epics requiring persistence by establishing the complete entity model, relationships, integrity constraints, and vector storage foundation.

**Description**: Implement Entity Framework Core 8 code-first migrations for all 10 domain entities defined in design.md (Patient, Appointment, IntakeData, ClinicalDocument, ExtractedData, MedicalCode, User, AuditLog, QueueEntry, NotificationLog). Install pgvector 0.5.x extension for vector similarity search. Establish foreign key constraints, unique indexes, enum mappings, JSONB columns, optimistic locking version fields, and soft-delete patterns. Produce seed/mock data scripts for development and testing.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- EF Core 8 entity models for all 10 domain entities
- Code-first migration scripts with rollback support
- pgvector extension installation and 384-dim vector column configuration
- Foreign key relationships (Patient→Appointments, ClinicalDocument→ExtractedData, etc.)
- Unique constraints (email uniqueness on Patient/User)
- JSONB column configuration (preferred_slot_criteria, mandatory_fields, data_content)
- Optimistic locking version field on Appointment entity
- Soft-delete pattern (deleted_at) on Patient entity
- Seed/mock data scripts for development environment
- Data integrity validation rules at database level

**Dependent EPICs**:
- EP-TECH - Foundational - Requires base project scaffold and database provisioning

---

### EP-001: User Management & Authentication

**Business Value**: Establishes secure patient and staff access with RBAC, enabling all user-facing workflows while maintaining HIPAA-compliant authentication safeguards.

**Description**: Implement complete user lifecycle management including patient self-registration with email verification, role-based access control (Patient, Staff, Admin), secure session management with 15-minute timeout, password hashing (bcrypt/Argon2), password reset via email token, MFA for staff/admin, concurrent session prevention, account lockout after 5 failed attempts, and last-login display. Covers UC-001 authentication prerequisites.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Patient registration with email verification flow
- RBAC implementation (Patient, Staff, Admin roles)
- Session management with 15-minute inactivity timeout
- Password hashing (bcrypt, 10 rounds minimum)
- Password reset via secure email token (1-hour expiry)
- Immutable audit logs for authentication events
- Concurrent session prevention logic
- MFA option for staff and admin roles
- Account lockout (5 failed attempts, 30-minute lock)
- Last login timestamp and location display

**Dependent EPICs**:
- EP-TECH - Foundational - Requires JWT auth scaffold and Identity framework
- EP-DATA - Foundational - Requires User and AuditLog entity persistence

---

### EP-002: Appointment Scheduling Core

**Business Value**: Delivers the primary patient-facing booking experience — the core revenue-enabling workflow that reduces no-shows through intelligent scheduling and dynamic slot management.

**Description**: Implement appointment slot viewing with date/time/provider filters, booking with optimistic locking to prevent double-booking, 90-day advance booking, cancellation with 24-hour policy, automatic slot release within 1 minute, waitlist registration, automatic waitlist notification, dynamic preferred slot swap background job, staff override for swaps, walk-in booking restricted to staff, same-day walk-in support, and no-show risk scoring. Covers UC-001 (Patient Books Appointment) and UC-010 (Preferred Slot Swap) core flows.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Appointment slot availability API with date/time/provider filtering
- Optimistic locking booking transaction (version field compare-and-swap)
- 90-day advance booking enforcement
- Cancellation workflow with 24-hour policy and 1-minute slot release
- Waitlist registration and automatic notification on slot availability
- Dynamic preferred slot swap background job
- Staff override capability for automatic swaps
- Walk-in booking (staff-only) with same-day support
- No-show risk score calculation and display (hybrid AI/rule-based)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and Redis caching
- EP-DATA - Foundational - Requires Appointment and QueueEntry entities

---

### EP-003: Appointment Extended Features & Calendar

**Business Value**: Enhances patient convenience with rescheduling, appointment history, and calendar synchronization while ensuring booking performance targets are met.

**Description**: Implement appointment rescheduling (24-hour policy), patient appointment history with status tracking, iCal calendar sync (Google Calendar, Outlook), no-show risk AI classification model, optimistic locking for concurrent operations, and performance optimization for 2-second booking response and 100 bookings/minute throughput. Extends UC-001 with calendar integration and UC-010 with performance targets.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Appointment rescheduling with 24-hour policy enforcement
- Patient appointment history view with status tracking (scheduled, completed, cancelled, no-show)
- iCal format export for Google Calendar and Outlook sync
- No-show risk AI classification model integration
- Optimistic locking implementation for booking transactions
- Booking response time optimization (<2s at P95)
- Throughput optimization (100 bookings/min peak)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and caching infrastructure
- EP-DATA - Foundational - Requires Appointment entity with version field

---

### EP-004: Patient Intake & Onboarding

**Business Value**: Reduces staff prep time by 50% through AI-assisted conversational intake that collects patient information efficiently while maintaining fallback to manual forms.

**Description**: Implement dual-mode patient intake: AI conversational agent using RAG pattern with natural language dialogue and manual form-based alternative. Support seamless switching between modes without data loss, mandatory field collection (name, DOB, contact, emergency), optional field collection (insurance, medical history, medications, allergies), auto-save every 30 seconds, minor age restriction with guardian consent, and soft insurance pre-check against dummy records. Covers UC-002 (Patient Completes AI Intake).

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- AI conversational intake interface with NLU dialogue
- Manual form-based intake alternative
- Seamless AI-to-manual mode switching with data preservation
- Mandatory field collection (name, DOB, contact, emergency contact)
- Optional field collection (insurance, medical history, medications, allergies)
- Auto-save every 30 seconds to prevent data loss
- Minor (under 18) booking restriction with guardian consent
- Soft insurance pre-check against dummy validation records
- Insurance validation failure notification to staff
- RAG context retrieval for medical terminology grounding

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and frontend scaffold
- EP-DATA - Foundational - Requires Patient and IntakeData entities

---

### EP-005: Reminders & Notifications

**Business Value**: Directly reduces no-show rates by 30% through multi-channel (email + SMS) reminders at optimal intervals with intelligent retry logic and delivery tracking.

**Description**: Implement complete notification lifecycle: immediate booking confirmation (email + SMS), 24-hour reminder (email + SMS), 2-hour reminder (SMS only), SMS opt-out support, PDF confirmation with QR code, cancellation links in reminders, waitlist availability alerts within 5 minutes, dynamic slot swap notifications, delivery logging with status tracking, and retry logic with exponential backoff (max 3 retries). Covers UC-004 (System Sends Appointment Reminders).

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- SMTP integration (SendGrid/Gmail) for email delivery
- Twilio API integration for SMS delivery
- Booking confirmation (immediate, email + SMS)
- 24-hour reminder (email + SMS) batch job
- 2-hour reminder (SMS only) batch job
- SMS opt-out preference management
- PDF confirmation generation with QR code
- Cancellation link in all reminder communications
- Waitlist notification within 5 minutes of slot availability
- Dynamic slot swap notification (old + new times)
- Notification delivery logging (sent, failed, bounced)
- Exponential backoff retry (max 3 attempts)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires background job infrastructure and API framework
- EP-DATA - Foundational - Requires Appointment and NotificationLog entities

---

### EP-006: Clinical Document Upload & AI Parsing

**Business Value**: Eliminates 20+ minutes per patient of manual data extraction by automatically parsing clinical documents and extracting structured data with AI confidence scoring.

**Description**: Implement clinical document upload (PDF, DOCX, TXT, PNG, JPG) with format/size validation (max 10MB), secure AES-256 encrypted storage, document categorization (lab result, prescription, clinical note, imaging report), asynchronous AI parsing pipeline via Redis queue, AI-powered extraction of medications, diagnoses, procedures, and allergies using GPT-4o-mini with Claude fallback, source document attribution, confidence score assignment, low-confidence flagging (<80%), document re-upload support, and document preview with highlighted extraction regions. Covers UC-005 (Staff Uploads Documents) and UC-006 (Generate 360 Profile) parsing portion.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Multi-format document upload (PDF, DOCX, TXT, PNG, JPG)
- File format and size validation (max 10MB)
- Secure file storage with AES-256 encryption
- Document categorization UI (lab, prescription, note, imaging)
- Async parsing pipeline via Redis queue
- AI extraction: medications, diagnoses, procedures, allergies
- Source document attribution for all extracted data points
- Confidence score calculation and display per data point
- Low-confidence flagging (<80%) for manual verification
- Document re-upload and re-processing workflow
- Document preview with highlighted extraction regions

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and Redis queue
- EP-DATA - Foundational - Requires ClinicalDocument and ExtractedData entities

---

### EP-007: Clinical Data Consolidation & Conflict Detection

**Business Value**: Prevents medication errors and treatment risks by automatically consolidating multi-document data into a unified patient profile with intelligent conflict detection and staff escalation.

**Description**: Implement cross-document data consolidation into unified 360-degree patient profile, profile versioning with timestamp and user attribution, AI-powered conflict detection (medication discrepancies, duplicate diagnoses, date inconsistencies), critical conflict escalation with urgent flags (medication contraindications), side-by-side comparison view for staff resolution, seamless fallback to manual workflow when AI confidence drops below 80%, and chronological plausibility validation for clinical event dates. Covers UC-006 (Generate 360 Profile) consolidation portion.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Multi-document data consolidation into unified patient profile
- Profile versioning with timestamp and user attribution
- AI-powered conflict detection (medication, diagnosis, date conflicts)
- Critical conflict escalation with urgent flags
- Side-by-side comparison view for staff conflict resolution
- Manual workflow fallback (AI confidence <80%)
- Chronological plausibility validation for clinical events
- Source citations linking data points to document sections

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and AI infrastructure
- EP-DATA - Foundational - Requires ExtractedData and Patient profile entities

---

### EP-008: Medical Coding & Billing Preparation

**Business Value**: Protects revenue by reducing claim denials through accurate ICD-10/CPT code mapping with >98% AI-human agreement and transparent justification trails.

**Description**: Implement AI-powered ICD-10 diagnosis code mapping with justification text, CPT procedure code mapping with justification, current code library maintenance with quarterly updates, human-in-the-loop verification workflow (staff approval before finalization), staff override with manual code selection and justification logging, AI-human agreement rate calculation and display, coding discrepancy flagging for multi-code scenarios, multi-code assignment support, and payer-specific rule validation with claim denial risk flagging. Covers UC-007 (System Performs Medical Coding).

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- AI-powered ICD-10 code mapping with justification text
- AI-powered CPT code mapping with justification text
- ICD-10/CPT code library maintenance with quarterly updates
- Staff verification and approval workflow (human-in-the-loop)
- Staff override with manual code selection and justification
- AI-human agreement rate calculation and daily display
- Coding discrepancy flagging (multiple codes per diagnosis/procedure)
- Multi-code assignment for multiple billable diagnoses
- Payer-specific rule validation and claim denial risk flags
- Audit trail for all code changes with user attribution

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and AI infrastructure
- EP-DATA - Foundational - Requires MedicalCode and ExtractedData entities

---

### EP-009: Queue Management & Patient Flow

**Business Value**: Optimizes operational efficiency by providing real-time arrival queue visibility, intelligent wait time management, and automatic no-show detection for staff workflow.

**Description**: Implement staff arrival marking, automatic wait time calculation from arrival timestamp, real-time arrival queue sorted by appointment time and priority, priority queue for urgent walk-ins, manual queue order adjustment, automatic no-show marking after 15 minutes, average wait time calculation, queue filtering by provider/type/status, configurable wait time threshold alerts (default 30 minutes), and queue history for analytics. Covers UC-003 (Walk-in Registration) and UC-008 (Staff Manages Arrival Queue).

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Patient arrival status marking (arrived, no-show, cancelled)
- Automatic wait time calculation from arrival timestamp
- Real-time arrival queue dashboard sorted by time and priority
- Priority queue management for urgent walk-in patients
- Manual queue order adjustment by staff
- Automatic no-show marking (15 minutes past scheduled time)
- Average wait time calculation and display
- Queue filtering by provider, appointment type, arrival status
- Wait time threshold alert (configurable, default 30 min)
- Queue history for operational analytics
- Sub-second dashboard response time (cached views)
- Real-time AI intake response (<1s per exchange)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires API framework and Redis caching
- EP-DATA - Foundational - Requires QueueEntry and Appointment entities

---

### EP-010: Staff & Admin Dashboards

**Business Value**: Empowers staff with unified operational views and gives admins full system configurability, reducing training time and enabling self-service system management.

**Description**: Implement staff dashboard with daily schedule, arrival queue, and pending tasks; admin dashboard with system metrics, user management, and configuration. Support admin-configurable appointment slot templates by provider/day, business hours and holiday scheduling, notification template editing, no-show risk threshold configuration, staff account creation/deactivation (preserving history), patient search by name/DOB/phone, and complete patient profile viewing. Covers UC-009 (Admin Configures System Settings).

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Staff dashboard (daily schedule, arrival queue, pending tasks)
- Admin dashboard (system metrics, user management, configuration)
- Appointment slot template configuration by provider and day
- Business hours and holiday schedule management
- Notification template editing with variable placeholders
- No-show risk threshold and scoring parameter configuration
- Staff account creation and management
- Account deactivation without historical data deletion
- Patient search by name, DOB, or phone number
- Complete patient profile view (appointments, intake, documents)
- WCAG 2.1 Level AA accessibility compliance
- Mobile-responsive design (320px to 2560px)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires frontend scaffold and API framework
- EP-DATA - Foundational - Requires all domain entities for dashboard queries

---

### EP-011: Security, Encryption & Access Control

**Business Value**: Achieves zero HIPAA violations by implementing defense-in-depth security with encryption at rest and in transit, immutable audit trails, and comprehensive access controls.

**Description**: Implement AES-256 encryption for all data at rest, TLS 1.2+ for all data in transit, immutable audit logs for all data access/modifications/deletions with user attribution, automatic 15-minute session timeout, configurable data retention policies per category, session management security hardening, concurrent session prevention, account lockout enforcement, and PII redaction from application logs and error messages. Covers FR-091 through FR-095 (Infrastructure & Compliance).

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- AES-256 encryption at rest for database and file storage
- TLS 1.2+ enforcement for all client-server communication
- Immutable audit log system with user attribution
- Automatic 15-minute session timeout enforcement
- Data retention policy configuration per category
- PII redaction from application logs and error messages
- Input validation and sanitization (SQL injection, XSS, command injection)
- Account lockout (5 failed attempts, 30-minute lock)
- Concurrent session prevention
- Password hashing enforcement (bcrypt/Argon2, 10+ rounds)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires base infrastructure and HTTPS setup
- EP-DATA - Foundational - Requires AuditLog entity

---

### EP-012: AI Gateway & Provider Management

**Business Value**: Centralizes AI provider management with cost controls, resilience patterns, and multi-provider fallback to ensure clinical AI features remain available and budget-compliant.

**Description**: Implement custom .NET AI Gateway service abstracting OpenAI GPT-4o-mini (primary) and Anthropic Claude 3.5 Sonnet (fallback) with Polly resilience library. Configure OpenAI GPT-4o-mini as primary LLM and Claude 3.5 Sonnet as fallback provider. Enforce per-request token budgets (4K/1K document parsing, 500/200 intake, 2K/500 coding), circuit breaker (open after 5 failures, retry after 30s), model version rollback within 1 hour, request queuing for document parsing, retry with exponential backoff (max 3), and daily cost monitoring with budget alerts. Covers vertical slice architecture for AI components.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- AI Gateway .NET service with Polly resilience policies
- OpenAI GPT-4o-mini primary provider integration
- Anthropic Claude 3.5 Sonnet fallback provider integration
- Token budget enforcement per request type (AIR-O01, AIR-O02, AIR-O03)
- Circuit breaker (5 consecutive failures → open, 30s retry)
- Model version rollback capability (within 1 hour)
- Request queuing for document parsing pipeline
- Exponential backoff retry (max 3 attempts)
- Daily AI cost monitoring and budget threshold alerts
- Vertical slice architecture for AI components
- Asynchronous message queue processing (Redis queue)
- API Gateway pattern for provider abstraction

**Dependent EPICs**:
- EP-TECH - Foundational - Requires .NET project scaffold and Redis infrastructure
- EP-DATA - Foundational - Requires database for cost tracking and audit logging

---

### EP-013: AI Quality, Monitoring & Safety

**Business Value**: Ensures clinical safety by enforcing >98% AI-human agreement rates, calibrated confidence scoring, hallucination prevention, and mandatory human verification for all AI-generated medical outputs.

**Description**: Implement AI quality monitoring including medical coding accuracy tracking (>98% AI-human agreement), data extraction accuracy (>95% precision/recall), latency monitoring (<1s intake, <30s parsing, <5s coding), hallucination rate tracking (<5%), calibrated confidence score distribution (0-1), mandatory flagging for scores <0.80, daily AI-human agreement dashboard, PII redaction before external API calls, medical code library validation, and human-in-the-loop verification enforcement.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Medical coding accuracy tracking (>98% AI-human agreement)
- Data extraction accuracy monitoring (>95% precision/recall)
- AI latency monitoring dashboards (intake, parsing, coding)
- Hallucination rate tracking (<5% for medical justifications)
- Calibrated confidence score assignment (0-1 distribution)
- Mandatory review flagging for confidence <0.80
- Daily AI-human agreement metrics dashboard
- PII redaction pipeline before external API calls
- Medical code library validation (ICD-10/CPT)
- Human-in-the-loop verification enforcement

**Dependent EPICs**:
- EP-TECH - Foundational - Requires monitoring infrastructure
- EP-DATA - Foundational - Requires MedicalCode and ExtractedData entities

---

### EP-014: AI RAG Pipeline & Knowledge Base

**Business Value**: Grounds AI responses in verified medical knowledge, preventing hallucination and ensuring accurate terminology for conversational intake, document parsing, and medical coding.

**Description**: Implement RAG pipeline with 512-token document chunking (20% overlap), top-5 retrieval with cosine similarity threshold (>=0.75), semantic re-ranking, separate vector indexes for medical terminology/intake templates/coding guidelines, hybrid search (semantic + keyword via PostgreSQL FTS), quarterly knowledge base refresh for ICD-10/CPT updates, embedding caching for frequently used terms, prompt injection sanitization, access control matching source document permissions, content filtering for harmful responses, AI rate limiting (100 requests/user/hour), and A/B testing support for model version upgrades.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- 512-token document chunking with 20% overlap
- Top-5 vector retrieval (cosine similarity >= 0.75)
- Semantic re-ranking of retrieved chunks
- Separate vector indexes (terminology, intake templates, coding guidelines)
- Hybrid search (pgvector similarity + PostgreSQL FTS)
- Quarterly knowledge base refresh workflow
- Medical terminology embedding caching
- Prompt injection sanitization for AI intake inputs
- Access control enforcement on AI-extracted clinical data
- Content filtering for harmful AI responses
- Rate limiting (100 AI requests/user/hour)
- A/B testing framework for model version upgrades
- Audit logging for all AI prompts and responses

**Dependent EPICs**:
- EP-TECH - Foundational - Requires database and API infrastructure
- EP-DATA - Foundational - Requires pgvector extension and vector indexes

---

### EP-015: Performance, Scalability & Reliability

**Business Value**: Ensures the platform supports 1000+ concurrent users with sub-second response times and 99.9% uptime through caching, connection pooling, and resilience patterns.

**Description**: Implement performance optimization for document parsing (<30s at P95), 1000+ concurrent user support, medical coding generation (<5s), reminder batch processing (<10 minutes), 99.9% uptime target, graceful AI service degradation with manual fallback, circuit breaker for external dependencies, horizontal scaling preparation, connection pooling (max 100 concurrent DB connections), asynchronous processing for AI workloads, Redis caching with 5-min TTL, error rate monitoring (<0.1%), and Redis caching infrastructure for frequently accessed data.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Document parsing performance optimization (<30s at P95)
- Concurrent user load testing and optimization (1000+)
- Medical coding latency optimization (<5s per diagnosis/procedure)
- Reminder batch job optimization (<10 min per batch run)
- 99.9% uptime monitoring and alerting
- Graceful AI degradation with manual workflow fallback
- Circuit breaker for external dependencies (SMS, email, AI)
- Horizontal scaling architecture preparation
- Database connection pooling (max 100 connections)
- Asynchronous processing for long-running AI workflows
- Redis caching with 5-minute TTL for slots and profiles
- Error rate monitoring and alerting (<0.1% threshold)
- Redis caching infrastructure for frequently accessed data
- System uptime monitoring (99.9% rolling 30-day)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires base infrastructure and Redis setup

---

### EP-016: Data Integrity, Retention & Archival

**Business Value**: Ensures regulatory compliance and data quality by enforcing validation rules, retention policies, and archival procedures aligned with HIPAA requirements.

**Description**: Implement data validation rules for date ranges (90-day appointment window), email format validation, duplicate booking prevention (patient_id + appointment_time unique constraint), ICD-10/CPT code validation against libraries, orphan record prevention with cascading deletes, audit log retention (7-year minimum per HIPAA), clinical record indefinite retention, appointment history retention (3 years), notification log retention (90 days), cancelled appointment archival (1 year), soft-delete for patient records, and application-level data integrity validation.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Date range validation (appointments within 90 days)
- Email format validation (regex pattern)
- Unique constraint enforcement (patient_id + appointment_time)
- ICD-10/CPT code validation against current libraries
- Cascading delete for dependent entities (orphan prevention)
- Audit log 7-year retention policy
- Clinical record indefinite retention
- Appointment history 3-year retention
- Notification log 90-day retention
- Cancelled appointment 1-year archival
- Soft delete pattern for patient records (deleted_at timestamp)
- Application-level data integrity validation layer

**Dependent EPICs**:
- EP-TECH - Foundational - Requires database infrastructure
- EP-DATA - Foundational - Requires entity models and constraints

---

### EP-017: Data Backup, Recovery & Migration

**Business Value**: Protects against data loss with automated backup procedures, point-in-time recovery, and versioned schema migrations enabling zero-downtime deployments.

**Description**: Implement automated daily database backups (2 AM local time), tiered backup retention (daily 30 days, weekly 90 days, monthly 1 year), geographically separate backup storage, AES-256 backup encryption, quarterly backup restoration testing, point-in-time recovery with 15-minute transaction log backups, database schema versioning via migration scripts, transactional migration execution with automatic rollback, migration history tracking, backward-compatible schema changes, post-migration integrity verification via checksum, and CSV data import for initial system population.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Automated daily backups (2 AM local time)
- Tiered retention (30-day daily, 90-day weekly, 1-year monthly)
- Geographically separate backup storage
- AES-256 backup encryption
- Quarterly backup restoration testing procedures
- Point-in-time recovery (15-minute transaction log backups)
- Schema versioning via EF Core migration scripts
- Transactional migration execution with automatic rollback
- Migration history table with timestamp tracking
- Backward-compatible schema changes for zero-downtime
- Post-migration checksum verification
- CSV data import support for initial population
- Database backup capability with PITR

**Dependent EPICs**:
- EP-TECH - Foundational - Requires database infrastructure and deployment scripts
- EP-DATA - Foundational - Requires entity models for migration scripts

---

### EP-018: HIPAA Compliance & Patient Rights

**Business Value**: Achieves 100% HIPAA compliance by implementing Privacy Rule and Security Rule safeguards, patient data rights (access, deletion), and structured logging with correlation IDs.

**Description**: Implement HIPAA Privacy Rule compliance for PHI protection, Security Rule administrative/physical/technical safeguards, input validation and sanitization (SQL injection, XSS, command injection prevention), zero-downtime database migrations, 1-hour RPO and 4-hour RTO recovery targets, retry logic with exponential backoff (max 3 retries) for transient failures, structured logging with correlation IDs for request tracing, right-to-access with patient data export within 30 days, and right-to-deletion with complete data removal within 30 days.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- HIPAA Privacy Rule compliance implementation
- HIPAA Security Rule safeguards (administrative, physical, technical)
- Right-to-access patient data export (within 30 days)
- Right-to-deletion complete data removal (within 30 days)
- Input validation and sanitization (injection prevention)
- Zero-downtime database migration support
- Recovery targets (1-hour RPO, 4-hour RTO)
- Retry logic with exponential backoff (max 3 retries)
- Structured logging with correlation IDs for tracing
- Configurable data retention periods per data category

**Dependent EPICs**:
- EP-TECH - Foundational - Requires base infrastructure and logging setup

---

### EP-019: Development Standards, Testing & Quality

**Business Value**: Ensures long-term maintainability and code quality through standardized architecture patterns, automated testing infrastructure, and API documentation.

**Description**: Implement layered architecture (Presentation → Service → Data Access) for deterministic workflows, RESTful API design with HATEOAS, CQRS pattern for audit log access, AES-256 encryption configuration at application level, feature flag infrastructure via configuration, rate limiting middleware (100 req/min/user), request correlation IDs for distributed tracing, xUnit testing framework with Moq mocking, Playwright E2E testing setup, Serilog structured logging with Seq Community Edition, Swagger/Swashbuckle API documentation, C# coding conventions enforcement, and code quality gates (zero warnings, StyleCop).

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Layered architecture implementation (Presentation → Service → Data)
- RESTful API design with HATEOAS links
- CQRS pattern for audit log read/write separation
- AES-256 encryption at application level
- Feature flag infrastructure via configuration files
- Rate limiting middleware (100 req/min/user)
- Request correlation ID middleware for distributed tracing
- xUnit test framework setup with Moq dependency mocking
- Playwright E2E test framework setup
- Serilog structured logging with Seq sink
- Swagger/Swashbuckle OpenAPI documentation
- C# coding conventions (PascalCase, camelCase, async suffix)
- Code quality gates (zero compiler warnings, StyleCop)

**Dependent EPICs**:
- EP-TECH - Foundational - Requires project scaffold for pattern implementation

---

### EP-020: Frontend Accessibility & Operational Standards

**Business Value**: Ensures inclusive access for all users through WCAG 2.1 AA compliance, mobile responsiveness, and establishes operational monitoring and maintainability standards.

**Description**: Implement health check endpoints with <500ms response, database partitioning preparation for multi-tenant expansion, idempotent API endpoints for state-changing operations, 80% automated test coverage for critical business logic, semantic versioning for API releases, OpenAPI 3.0 API documentation, feature flags for gradual rollout, centralized configuration management, WCAG 2.1 inline validation feedback (<200ms), keyboard navigation for all interactive elements, consistent UI/UX patterns across all interfaces, native Windows Services deployment configuration, and health check monitoring endpoints.

**UI Impact**: Yes

**Screen References**: N/A

**Key Deliverables**:
- Health check endpoints (<500ms response)
- Multi-tenant database partitioning preparation
- Idempotent API endpoints for state-changing operations
- 80% test coverage for critical business logic
- Semantic versioning for API releases
- OpenAPI 3.0 documentation generation
- Feature flags for gradual capability rollout
- Centralized configuration management
- Inline validation feedback (<200ms)
- Keyboard navigation for all interactive elements
- Consistent UI/UX patterns across interfaces
- Windows Services deployment configuration
- Health check monitoring infrastructure
- Semantic versioning for API releases

**Dependent EPICs**:
- EP-TECH - Foundational - Requires frontend scaffold and API framework
