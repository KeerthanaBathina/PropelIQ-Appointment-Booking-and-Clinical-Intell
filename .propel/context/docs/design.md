# Architecture Design

## Project Overview
The Unified Patient Access & Clinical Intelligence Platform is a next-generation healthcare system that combines patient-centric appointment scheduling with AI-powered clinical data aggregation and medical coding. The platform serves patients, administrative staff, and system administrators to improve operational efficiency, reduce no-show rates, automate clinical data extraction, and ensure accurate medical coding while maintaining 100% HIPAA compliance. The system operates as a standalone aggregator in Phase 1, with planned EHR integration in future phases.

## Architecture Goals
- **Goal #1: Operational Efficiency**: Reduce appointment no-shows by 30% and staff clinical prep time by 50% through intelligent automation
- **Goal #2: Clinical Accuracy**: Achieve >98% AI-human agreement on data extraction and medical coding with human-in-the-loop verification
- **Goal #3: Security & Compliance**: Maintain 100% HIPAA compliance with zero security breaches through defense-in-depth architecture
- **Goal #4: Performance at Scale**: Support 1000+ concurrent users with <2s booking response time and 99.9% uptime
- **Goal #5: Cost Optimization**: Deliver production system using free/open-source infrastructure without paid cloud services
- **Goal #6: AI Transparency**: Provide explainable AI with confidence scores and source attribution for all automated decisions

## Non-Functional Requirements

### Performance
- NFR-001: System MUST process appointment booking requests and display confirmation within 2 seconds at 95th percentile
- NFR-002: System MUST complete document parsing and clinical data extraction within 30 seconds per document at 95th percentile
- NFR-003: System MUST support 1000+ concurrent users without performance degradation
- NFR-004: System MUST serve patient dashboard and staff queue views with sub-second response time when cached
- NFR-005: System MUST handle 100 appointment bookings per minute during peak hours without errors
- NFR-006: System MUST complete medical coding generation within 5 seconds per diagnosis/procedure
- NFR-007: System MUST send appointment reminders batch within 10 minutes for each scheduled run (24h, 2h windows)
- NFR-008: System MUST complete AI conversational intake session with real-time response (<1s per exchange)

### Security
- NFR-009: System MUST encrypt all data at rest using AES-256 encryption
- NFR-010: System MUST encrypt all data in transit using TLS 1.2 or higher
- NFR-011: System MUST implement role-based access control (RBAC) with principle of least privilege for Patient, Staff, and Admin roles
- NFR-012: System MUST create immutable audit logs for all authentication events, data access, modifications, and deletions with user attribution
- NFR-013: System MUST hash passwords using bcrypt or Argon2 with minimum 10 rounds (never store plaintext)
- NFR-014: System MUST implement automatic session timeout after 15 minutes of inactivity
- NFR-015: System MUST prevent concurrent sessions for the same user account
- NFR-016: System MUST implement account lockout after 5 failed login attempts for 30 minutes
- NFR-017: System MUST redact personally identifiable information (PII) from application logs and error messages
- NFR-018: System MUST validate and sanitize all user inputs to prevent injection attacks (SQL, XSS, command injection)

### Availability
- NFR-019: System MUST achieve 99.9% uptime measured over rolling 30-day period (max 43 minutes downtime/month)
- NFR-020: System MUST implement health check endpoints returning status within 500ms
- NFR-021: System MUST support zero-downtime database schema migrations for minor updates
- NFR-022: System MUST gracefully handle AI service unavailability with fallback to manual workflows
- NFR-023: System MUST implement circuit breaker for external dependencies (SMS gateway, email service, AI models)
- NFR-024: System MUST support point-in-time database recovery with maximum 1-hour RPO (Recovery Point Objective)
- NFR-025: System MUST restore service within 4 hours RTO (Recovery Time Objective) after major failure

### Scalability
- NFR-026: System MUST support horizontal scaling of backend services for future cloud migration
- NFR-027: System MUST partition database tables by tenant/facility for multi-tenant future expansion
- NFR-028: System MUST implement connection pooling with maximum 100 concurrent database connections
- NFR-029: System MUST use asynchronous processing for document parsing and medical coding workflows
- NFR-030: System MUST implement Redis caching with 5-minute TTL for frequently accessed appointment slots and patient profiles

### Reliability
- NFR-031: System MUST maintain error rate below 0.1% for critical user workflows (booking, intake, coding)
- NFR-032: System MUST implement retry logic with exponential backoff for transient failures (max 3 retries)
- NFR-033: System MUST validate data integrity using database constraints and application-level validation
- NFR-034: System MUST implement idempotent API endpoints for all state-changing operations
- NFR-035: System MUST log all errors with structured logging including correlation IDs for request tracing

### Maintainability
- NFR-036: System MUST maintain automated test coverage of at least 80% for critical business logic
- NFR-037: System MUST use semantic versioning for API changes with backward compatibility for minor versions
- NFR-038: System MUST document all public APIs using OpenAPI 3.0 specification
- NFR-039: System MUST implement feature flags for gradual rollout of new capabilities
- NFR-040: System MUST maintain centralized configuration management separate from codebase

### Compliance
- NFR-041: System MUST comply with HIPAA Privacy Rule for patient health information protection
- NFR-042: System MUST comply with HIPAA Security Rule for administrative, physical, and technical safeguards
- NFR-043: System MUST support data retention policies with configurable retention periods per data category
- NFR-044: System MUST support right-to-access requests with patient data export within 30 days
- NFR-045: System MUST support right-to-deletion requests with complete data removal within 30 days

### Usability
- NFR-046: System MUST support WCAG 2.1 Level AA accessibility standards for patient-facing interfaces
- NFR-047: System MUST support mobile-responsive design for viewport widths from 320px to 2560px
- NFR-048: System MUST provide inline validation feedback within 200ms of user input
- NFR-049: System MUST support keyboard navigation for all interactive elements
- NFR-050: System MUST maintain consistent UI/UX patterns across patient, staff, and admin interfaces

## Data Requirements

### Data Structures
- DR-001: System MUST store patient records with email as unique identifier and enforce uniqueness constraints
- DR-002: System MUST store appointment records with optimistic locking (version field) to prevent double-booking
- DR-003: System MUST store clinical documents with metadata including upload date, uploader user ID, document category, and processing status
- DR-004: System MUST store extracted clinical data with source document attribution and confidence scores
- DR-005: System MUST store medical codes (ICD-10, CPT) with justification text and user attribution for audit trail
- DR-006: System MUST store user sessions with expiration timestamp and last activity timestamp
- DR-007: System MUST store notification delivery logs with status (sent, failed, bounced) and retry count
- DR-008: System MUST store queue entries with arrival timestamp, priority level, and current status

### Data Integrity
- DR-009: System MUST enforce referential integrity between appointments and patient records using foreign key constraints
- DR-010: System MUST enforce referential integrity between clinical data and source documents for traceability
- DR-011: System MUST validate email format using regex pattern before storing patient contact information
- DR-012: System MUST validate date ranges ensuring appointment dates are within 90 days from current date
- DR-013: System MUST prevent orphaned records by cascading deletes for dependent entities
- DR-014: System MUST enforce unique constraint on (patient_id, appointment_time) to prevent duplicate bookings
- DR-015: System MUST validate ICD-10 and CPT codes against current code libraries before storage

### Data Retention
- DR-016: System MUST retain audit logs for minimum 7 years per HIPAA requirements
- DR-017: System MUST retain patient clinical records indefinitely until patient requests deletion
- DR-018: System MUST retain appointment history for minimum 3 years for operational analytics
- DR-019: System MUST retain notification delivery logs for 90 days for troubleshooting
- DR-020: System MUST archive cancelled appointments after 1 year to separate archival storage
- DR-021: System MUST implement soft delete for patient records with deleted_at timestamp

### Data Backup
- DR-022: System MUST perform automated database backups daily at 2 AM local time
- DR-023: System MUST retain daily backups for 30 days, weekly backups for 90 days, monthly backups for 1 year
- DR-024: System MUST store backups in geographically separate location from primary database
- DR-025: System MUST encrypt backup files using AES-256 before storage
- DR-026: System MUST test backup restoration quarterly to verify recovery procedures
- DR-027: System MUST implement point-in-time recovery with transaction log backups every 15 minutes

### Data Migration
- DR-028: System MUST support database schema versioning using migration scripts (e.g., Flyway, Liquibase)
- DR-029: System MUST execute migrations in transaction blocks with automatic rollback on failure
- DR-030: System MUST maintain migration history table tracking applied migrations with timestamp
- DR-031: System MUST support backward-compatible schema changes for zero-downtime deployments
- DR-032: System MUST validate data integrity after migration completion using checksum verification
- DR-033: System MUST support data import from CSV format for initial system population

### Domain Entities
- **Patient**: Represents end users with attributes: patient_id (UUID), email (unique), password_hash, full_name, date_of_birth, phone_number, emergency_contact, created_at, updated_at, deleted_at (soft delete). Relationships: one-to-many with Appointments, IntakeData, ClinicalDocuments.
- **Appointment**: Represents scheduled visits with attributes: appointment_id (UUID), patient_id (FK), appointment_time (timestamp), status (enum: scheduled, completed, cancelled, no-show), is_walk_in (boolean), preferred_slot_criteria (JSON), version (optimistic lock), created_at, updated_at. Relationships: many-to-one with Patient, one-to-many with NotificationLogs.
- **IntakeData**: Represents patient intake information with attributes: intake_id (UUID), patient_id (FK), intake_method (enum: ai_conversational, manual_form), mandatory_fields (JSON), optional_fields (JSON), insurance_info (JSON), completed_at, created_at, updated_at. Relationships: many-to-one with Patient.
- **ClinicalDocument**: Represents uploaded patient documents with attributes: document_id (UUID), patient_id (FK), document_category (enum: lab_result, prescription, clinical_note, imaging_report), file_path (string), upload_date (timestamp), uploader_user_id (FK), processing_status (enum: queued, processing, completed, failed), created_at, updated_at. Relationships: many-to-one with Patient, one-to-many with ExtractedData.
- **ExtractedData**: Represents AI-extracted clinical information with attributes: extracted_id (UUID), document_id (FK), data_type (enum: medication, diagnosis, procedure, allergy), data_content (JSON), confidence_score (float 0-1), source_attribution (text), flagged_for_review (boolean), verified_by_user_id (FK), created_at, updated_at. Relationships: many-to-one with ClinicalDocument.
- **MedicalCode**: Represents ICD-10 and CPT code mappings with attributes: code_id (UUID), patient_id (FK), code_type (enum: icd10, cpt), code_value (string), description (text), justification (text), suggested_by_ai (boolean), approved_by_user_id (FK), ai_confidence_score (float), created_at, updated_at. Relationships: many-to-one with Patient.
- **User**: Represents staff and admin accounts with attributes: user_id (UUID), email (unique), password_hash, role (enum: patient, staff, admin), last_login_at, failed_login_attempts (int), account_locked_until (timestamp), mfa_enabled (boolean), created_at, updated_at. Relationships: one-to-many with AuditLogs.
- **AuditLog**: Represents immutable audit trail with attributes: log_id (UUID), user_id (FK), action (enum: login, logout, data_access, data_modify, data_delete), resource_type (string), resource_id (UUID), timestamp (immutable), ip_address (string), user_agent (text). Relationships: many-to-one with User.
- **QueueEntry**: Represents patient arrival queue with attributes: queue_id (UUID), appointment_id (FK), arrival_timestamp (timestamp), wait_time_minutes (computed), priority (enum: normal, urgent), status (enum: waiting, in_visit, completed), created_at, updated_at. Relationships: many-to-one with Appointment.
- **NotificationLog**: Represents notification delivery history with attributes: notification_id (UUID), appointment_id (FK), notification_type (enum: confirmation, reminder_24h, reminder_2h, slot_swap), delivery_channel (enum: email, sms), status (enum: sent, failed, bounced), retry_count (int), sent_at (timestamp), created_at. Relationships: many-to-one with Appointment.

## AI Consideration

**Status:** Applicable

**Rationale:** Spec.md contains 9 [AI-CANDIDATE] tags and 4 [HYBRID] tags across patient intake, document parsing, medical coding, and risk assessment features. AI capabilities are core to the platform's value proposition for clinical efficiency and accuracy.

## AI Requirements

### AI Functional Requirements
- AIR-001: System MUST provide AI conversational intake using natural language understanding to collect patient information with contextual follow-up questions (RAG pattern for medical terminology)
- AIR-002: System MUST automatically parse PDF clinical documents and extract structured data (medications, diagnoses, procedures, allergies) using multimodal LLM with OCR capabilities
- AIR-003: System MUST map clinical diagnoses to ICD-10 codes with justification text explaining rationale for code selection
- AIR-004: System MUST map clinical procedures to CPT codes with justification text explaining rationale for code selection
- AIR-005: System MUST consolidate extracted clinical data from multiple documents into unified patient profile with conflict detection
- AIR-006: System MUST calculate no-show risk score (0-100) based on patient history and appointment characteristics using classification model
- AIR-007: System MUST provide source citations linking extracted data points to specific document sections for verification
- AIR-008: System MUST allow seamless switching between AI conversational intake and manual form at any point without data loss
- AIR-009: System MUST present AI-suggested medical codes to staff for human verification before finalizing
- AIR-010: System MUST fallback to manual workflow when AI confidence score below 80% threshold

### AI Quality Requirements
- AIR-Q01: System MUST maintain medical coding accuracy with >98% AI-human agreement rate measured across evaluation dataset
- AIR-Q02: System MUST achieve data extraction accuracy with >95% precision and recall for structured fields (medication, diagnosis, allergy)
- AIR-Q03: System MUST complete AI conversational intake exchange with <1 second latency at 95th percentile
- AIR-Q04: System MUST complete document parsing with <30 seconds latency for 10-page PDF at 95th percentile
- AIR-Q05: System MUST complete medical coding generation with <5 seconds latency per diagnosis/procedure at 95th percentile
- AIR-Q06: System MUST maintain hallucination rate below 5% for medical code justifications verified against clinical guidelines
- AIR-Q07: System MUST assign confidence scores (0-1) to all extracted data points with calibrated probability distribution
- AIR-Q08: System MUST flag extracted data with confidence score <0.80 for mandatory manual verification
- AIR-Q09: System MUST calculate and display AI-human agreement metrics daily for continuous quality monitoring

### AI Safety Requirements
- AIR-S01: System MUST redact patient identifying information (name, DOB, SSN) from AI model prompts before external API calls
- AIR-S02: System MUST validate AI-generated medical codes against current ICD-10/CPT code libraries to prevent invalid code generation
- AIR-S03: System MUST implement human-in-the-loop verification for all AI-suggested medical codes before submitting to billing
- AIR-S04: System MUST log all AI prompts and responses in audit trail with patient_id correlation for compliance review
- AIR-S05: System MUST implement content filtering to detect and reject inappropriate or harmful AI-generated responses
- AIR-S06: System MUST sanitize AI conversational intake inputs to prevent prompt injection attacks
- AIR-S07: System MUST enforce access control on AI-extracted clinical data matching source document permissions
- AIR-S08: System MUST implement rate limiting on AI API calls to prevent abuse (100 requests per user per hour)
- AIR-S09: System MUST detect and flag potential conflict in extracted clinical data (e.g., medication contraindications) for urgent staff review
- AIR-S10: System MUST validate AI-extracted date ranges ensuring clinical events are chronologically plausible

### AI Operational Requirements
- AIR-O01: System MUST enforce token budget of 4000 input tokens and 1000 output tokens per document parsing request
- AIR-O02: System MUST enforce token budget of 500 input tokens and 200 output tokens per conversational intake exchange
- AIR-O03: System MUST enforce token budget of 2000 input tokens and 500 output tokens per medical coding request
- AIR-O04: System MUST implement circuit breaker for AI model provider failures (open after 5 consecutive failures, retry after 30 seconds)
- AIR-O05: System MUST support model version rollback within 1 hour when accuracy degradation detected
- AIR-O06: System MUST cache frequently used medical terminology embeddings to reduce latency and costs
- AIR-O07: System MUST implement request queuing for document parsing to prevent AI provider rate limit violations
- AIR-O08: System MUST retry failed AI requests up to 3 times with exponential backoff before fallback to manual workflow
- AIR-O09: System MUST monitor AI provider costs daily and alert when spending exceeds budget threshold
- AIR-O10: System MUST support A/B testing for model version upgrades with metric tracking (accuracy, latency, cost)

### AI Architecture Pattern
**Selected Pattern:** Hybrid (RAG + Tool Calling + Classification)

**Rationale:**
- **RAG Pattern** for AI conversational intake and medical terminology grounding: Retrieves relevant medical terminology and intake flow templates from knowledge base to ensure accurate, context-aware patient interactions. Prevents hallucination on medical terms and standardizes data collection across patients.
- **Tool Calling Pattern** for document parsing workflow: Invokes specialized tools for OCR, table extraction, and medical entity recognition to handle various document formats (PDFs, images, scanned forms). Enables structured data extraction pipeline with validation at each step.
- **Classification Model** for no-show risk scoring: Trains lightweight model on historical appointment data (patient demographics, appointment characteristics, past behavior) to predict no-show probability. Enables real-time scoring without external API dependency.
- **Hybrid Justification**: Platform requires multiple AI patterns due to diverse use cases. Conversational intake benefits from grounded responses (RAG), document parsing needs structured extraction pipeline (Tool), and risk scoring requires fast, deterministic predictions (Classification). Single pattern insufficient for medical-grade accuracy and operational efficiency.

### RAG Pipeline Requirements
- AIR-R01: System MUST chunk medical knowledge base documents into 512-token segments with 20% overlap for retrieval
- AIR-R02: System MUST retrieve top-5 chunks with cosine similarity ≥0.75 for conversational intake context
- AIR-R03: System MUST re-rank retrieved chunks using semantic similarity before prompt composition
- AIR-R04: System MUST maintain separate vector indexes for medical terminology, intake templates, and coding guidelines
- AIR-R05: System MUST refresh medical terminology embeddings quarterly when ICD-10/CPT code libraries updated
- AIR-R06: System MUST implement hybrid search combining semantic similarity and keyword matching for medical entity retrieval

## Architecture and Design Decisions

- **Decision #1: Layered Architecture with Vertical Slice for AI Components**: Adopt traditional layered architecture (Presentation → Service → Data) for deterministic workflows (scheduling, queue, authentication) with vertical slice pattern for AI features (conversational intake, document parsing, medical coding) to isolate complex AI pipelines and enable independent iteration. Justifies mixed approach balancing team familiarity with AI component autonomy.

- **Decision #2: Asynchronous Processing for AI Workloads**: Implement message queue (e.g., RabbitMQ, Redis Queue) for document parsing and medical coding workflows to decouple user requests from long-running AI operations. Enables responsive UX (<2s booking) while AI processes run in background (30s document, 5s coding). Supports scalability and fault tolerance with retry mechanisms.

- **Decision #3: PostgreSQL with pgvector Extension for Vector Storage**: Use PostgreSQL with pgvector extension for unified relational and vector storage, eliminating separate vector database. Reduces operational complexity and infrastructure costs (free tier) while supporting RAG retrieval. Tradeoff: pgvector less performant than specialized vector DBs (Pinecone) at >1M embeddings, but sufficient for Phase 1 (estimated <100K medical term embeddings).

- **Decision #4: API Gateway Pattern for AI Provider Abstraction**: Introduce AI Gateway layer abstracting OpenAI, Anthropic, Azure OpenAI providers with unified interface. Enables provider switching, A/B testing, fallback routing, and centralized token budget enforcement. Implements circuit breaker and request queuing per AIR-O requirements. Tradeoff: adds latency (~50ms) but critical for cost control and reliability.

- **Decision #5: CQRS for Audit Log Access**: Separate write model (append-only audit log) from read model (indexed view for queries) to optimize compliance reporting without impacting transactional performance. Audit writes use event sourcing pattern for immutability per HIPAA requirements. Read projections rebuilt from event stream for analytics.

- **Decision #6: Native Windows Deployment with IIS + Windows Services**: Deploy frontend on IIS (static React build) and backend as Windows Services (.NET 8) to meet free infrastructure constraint. Postpone containerization (Docker/Kubernetes) to Phase 2 cloud migration. Tradeoff: limits horizontal scaling but achieves 99.9% uptime with single-server resilience (health checks, auto-restart).

- **Decision #7: Redis for Distributed Caching and Session Management**: Use Upstash Redis (free tier 10K requests/day) for appointment slot caching (5-min TTL), session storage (15-min expiry), and document processing queue. Enables sub-second dashboard response and stateless backend services. Monitor free tier limits and implement cache eviction strategy (LRU).

- **Decision #8: JWT + Refresh Token for Stateless Authentication**: Issue short-lived JWT access tokens (15-min) with long-lived refresh tokens (7 days) stored in HttpOnly cookies. Enables horizontal scaling without shared session state. Audit log captures token issuance/revocation per HIPAA requirements. Tradeoff: token revocation requires blacklist (Redis) for immediate logout.

- **Decision #9: Optimistic Locking for Appointment Booking**: Use version field on Appointment entity with compare-and-swap semantics to prevent double-booking under concurrent load. Retry logic handles conflicts gracefully with user feedback. Avoids pessimistic locks (performance bottleneck) and distributed locks (infrastructure complexity).

- **Decision #10: Feature Flags for Gradual AI Rollout**: Implement feature flags (e.g., LaunchDarkly, Unleash) controlling AI conversational intake, document parsing, and medical coding enablement per user cohort. Enables A/B testing, safe rollback, and gradual staff training. Critical for managing AI accuracy risk in production.

## Technology Stack

| Layer | Technology | Version | Justification (NFR/DR/AIR) |
|-------|------------|---------|----------------------------|
| Frontend | React | 18.x | NFR-047 (mobile responsive), NFR-048 (inline validation), NFR-049 (keyboard nav). Component reusability for Patient/Staff/Admin dashboards. |
| UI Component Library | Material-UI (MUI) | 5.x | NFR-046 (WCAG 2.1 AA accessibility), NFR-050 (consistent UI patterns). Pre-built accessible components reduce development time. |
| State Management | React Query + Zustand | 4.x / 4.x | NFR-004 (sub-second cached views). React Query for server state caching, Zustand for client state. Lighter than Redux for MVP. |
| Mobile | N/A | - | Phase 1 delivers responsive web (NFR-047). Native mobile apps deferred to Phase 2. |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x | NFR-096 (Windows Services deployment), NFR-001 (2s response time). C# strong typing reduces runtime errors. Mature ecosystem for healthcare. |
| API Framework | ASP.NET Core MVC | 8.x | NFR-034 (idempotent endpoints), NFR-038 (OpenAPI docs via Swashbuckle). Built-in middleware for auth, logging, error handling. |
| Authentication | ASP.NET Core Identity + JWT | 8.x | NFR-011 (RBAC), NFR-013 (bcrypt hashing), NFR-014 (session timeout). Integrated with Entity Framework for user storage. |
| Database | PostgreSQL | 16.x | DR-001 to DR-033 (relational integrity, backup, migrations). Open-source, ACID compliance, proven healthcare deployments. pgvector extension for AIR-R requirements. |
| ORM | Entity Framework Core | 8.x | DR-009 to DR-015 (referential integrity, constraints). Type-safe queries, migration tooling. Performance profiling for N+1 detection. |
| AI Model Provider | OpenAI GPT-4o-mini (Primary) + Anthropic Claude 3.5 Sonnet (Fallback) | 2024-07-18 / claude-3-5-sonnet-20241022 | AIR-001 to AIR-006 (conversational intake, document parsing, medical coding). GPT-4o-mini for cost efficiency ($0.15/1M input tokens), Claude for complex medical reasoning fallback. |
| Vector Store | PostgreSQL pgvector | 0.5.x | AIR-R01 to AIR-R06 (RAG retrieval, hybrid search). Unified storage reduces infrastructure. Supports cosine similarity, inner product for 384-dim embeddings. |
| AI Gateway | Custom .NET Service with Polly | N/A / 8.x | AIR-O01 to AIR-O10 (token budget, circuit breaker, retry). Polly for resilience policies. Custom layer for multi-provider routing and cost tracking. |
| Embedding Model | OpenAI text-embedding-3-small | 2024 | AIR-R01 (512-token chunks). 384 dimensions balanced for cost ($0.02/1M tokens) and accuracy. Multilingual support for future expansion. |
| Message Queue | Redis Queue (powered by Upstash Redis) | 7.x | AIR-O07 (document parsing queue), NFR-029 (async processing). Lightweight queue for Phase 1. Upgrade to RabbitMQ if throughput exceeds 1K msgs/min. |
| Caching | Upstash Redis | 7.x | NFR-030 (5-min TTL caching), NFR-004 (sub-second cached views). Free tier (10K requests/day). LRU eviction when limit approached. |
| Testing | xUnit + Moq + Playwright | 2.x / 4.x / 1.x | NFR-036 (80% test coverage). xUnit for unit tests, Moq for mocking, Playwright for E2E UI tests. |
| Infrastructure | Windows Server + IIS | 2022 / 10 | NFR-096 (native Windows deployment). IIS for static frontend hosting, Windows Services for backend APIs. |
| Security | HTTPS (Let's Encrypt), AES-256, bcrypt | - / - / - | NFR-009 (AES-256 at rest), NFR-010 (TLS 1.2+ in transit), NFR-013 (bcrypt password hashing). Let's Encrypt for free SSL certificates. |
| Deployment | PowerShell Scripts + Windows Task Scheduler | - | NFR-039 (feature flags via config), NFR-040 (centralized config). Manual deployment scripts for Phase 1. CI/CD with GitHub Actions in Phase 2. |
| Monitoring | Serilog + Seq (Community Edition) | 8.x / 2024.x | NFR-020 (health checks), NFR-035 (structured logging), NFR-038 (API docs). Seq free tier for structured log search. Upgrade to ELK stack if log volume >10GB/day. |
| Documentation | Swagger (Swashbuckle) + Markdown | 6.x | NFR-038 (OpenAPI 3.0 docs). Auto-generated API docs from controller annotations. Technical docs in Markdown. |
| Email Service | SMTP (SendGrid Free Tier or Gmail SMTP) | - | FR-036 to FR-045 (appointment confirmations, reminders). SendGrid 100 emails/day free tier or Gmail SMTP as fallback. |
| SMS Gateway | Twilio API (Free Trial Credits) | 2023-05 | FR-036 to FR-045 (SMS reminders). Twilio trial $15 credit (~500 SMS). Transition to paid tier post-MVP validation. |

### Alternative Technology Options Considered

| Decision Point | Selected | Rejected | Rationale for Rejection |
|----------------|----------|----------|------------------------|
| **Backend Framework** | .NET 8 | Node.js (Express), Python (FastAPI) | Rejected Node.js due to weaker type safety and healthcare tooling. Rejected Python due to Windows Services deployment complexity and lower maturity in enterprise healthcare. .NET 8 chosen for strong typing, Windows native support (NFR-096), and mature healthcare integrations (HL7, FHIR future). |
| **Database** | PostgreSQL 16 | MySQL 8, MongoDB, SQL Server | Rejected MySQL due to weaker JSON support for clinical data (DR-004). Rejected MongoDB for lack of ACID guarantees critical for appointment booking (DR-002). Rejected SQL Server due to licensing costs. PostgreSQL chosen for open-source, JSONB for semi-structured data, and pgvector for AIR-R requirements. |
| **Vector Database** | PostgreSQL pgvector | Pinecone, Weaviate, Chroma | Rejected Pinecone for $70/month cost (violates NFR budget). Rejected Weaviate/Chroma for operational overhead (separate infrastructure). pgvector chosen for unified storage and free tier (NFR cost optimization), sufficient for <100K embeddings in Phase 1. Transition to Pinecone if vector scale >1M. |
| **AI Model Provider** | OpenAI GPT-4o-mini + Claude 3.5 Sonnet | GPT-4, Google Gemini, Local LLMs | Rejected GPT-4 for 20x cost vs GPT-4o-mini ($3/1M tokens). Rejected Gemini for weaker medical reasoning benchmarks (MedQA). Rejected local LLMs (Llama 3) for inference hardware costs and lower accuracy. GPT-4o-mini chosen for cost ($0.15/1M input tokens) with Claude fallback for complex cases (AIR-O04 circuit breaker). |
| **Message Queue** | Redis Queue | RabbitMQ, Azure Service Bus, AWS SQS | Rejected RabbitMQ for operational overhead (Erlang runtime). Rejected Azure Service Bus and AWS SQS for paid tier requirement. Redis Queue chosen for simplicity (reuses Upstash Redis), sufficient for <1K msgs/min Phase 1 throughput. Upgrade to RabbitMQ if async workflow complexity increases. |
| **Caching Layer** | Upstash Redis | Memcached, In-Memory Cache | Rejected Memcached for lack of persistence (session loss on restart). Rejected in-memory cache for single-server scalability limitation. Upstash Redis chosen for distributed caching (NFR-030), session storage, and message queue unified on single service. |
| **Frontend Framework** | React 18 | Angular 17, Vue 3, Svelte | Rejected Angular for steeper learning curve and verbosity. Rejected Vue for smaller healthcare ecosystem. Rejected Svelte for immature enterprise tooling. React chosen for large component library (MUI accessibility per NFR-046), team familiarity, and React Query caching (NFR-004). |
| **ORM** | Entity Framework Core 8 | Dapper, NHibernate | Rejected Dapper for lack of migration tooling (DR-028). Rejected NHibernate for complex configuration overhead. EF Core chosen for type-safe migrations, LINQ readability, and ASP.NET Core ecosystem integration (NFR-033 data integrity). |

### AI Component Stack
| Component | Technology | Purpose |
|-----------|------------|---------|
| Model Provider | OpenAI GPT-4o-mini (Primary), Anthropic Claude 3.5 Sonnet (Fallback) | LLM inference for conversational intake, document parsing, medical coding. GPT-4o-mini for cost ($0.15/1M input tokens), Claude for medical reasoning quality fallback when GPT confidence <80%. |
| Vector Store | PostgreSQL pgvector 0.5.x | Embedding storage and retrieval for RAG pattern. Supports cosine similarity search for medical terminology, intake templates, coding guidelines. Unified with relational DB to reduce infrastructure. |
| AI Gateway | Custom .NET Service with Polly Resilience | Request routing between OpenAI/Claude, token budget enforcement (AIR-O01-O03), circuit breaker (AIR-O04), cost tracking (AIR-O09). Centralizes prompt templates and response validation. |
| Guardrails | Built-in Validation + Custom Rules | Schema validation for structured outputs (Pydantic/System.Text.Json), content filtering for harmful responses (AIR-S05), PII redaction before API calls (AIR-S01), medical code library validation (AIR-S02). |
| Embedding Model | OpenAI text-embedding-3-small | Generates 384-dim embeddings for medical terminology, clinical notes, and intake transcripts. $0.02/1M tokens, balances cost and accuracy for RAG retrieval (AIR-R02). |
| Prompt Management | Custom Template Engine (Liquid/Handlebars) | Stores versioned prompt templates with variable substitution for conversational intake flows, document parsing instructions, medical coding guidelines. Enables A/B testing and rollback (AIR-O10). |
| Observability | Serilog + Custom Metrics (AIR-Q metrics) | Logs all AI requests/responses with correlation IDs (AIR-S04). Tracks accuracy metrics (AIR-Q01-Q02), latency (AIR-Q03-Q05), confidence score distributions (AIR-Q07), AI-human agreement (AIR-Q09). Daily dashboards for model performance monitoring. |

### Technology Decision Matrix

#### Database Selection (DR-001 to DR-033)

| Metric (from NFR/DR/AIR) | PostgreSQL 16 | MySQL 8 | MongoDB | SQL Server | Rationale |
|--------------------------|---------------|---------|---------|------------|-----------|
| **ACID Compliance** (DR-002 optimistic locking) | 10 | 10 | 7 | 10 | MongoDB weaker multi-document transactions. PostgreSQL/MySQL/SQL Server tie. |
| **JSON Support** (DR-004 clinical data storage) | 10 | 7 | 10 | 8 | PostgreSQL JSONB indexing superior to MySQL JSON. MongoDB native but overkill. |
| **Vector Extension** (AIR-R01-R06 RAG retrieval) | 10 | 0 | 8 | 0 | pgvector mature extension. MongoDB vector search experimental. MySQL/SQL Server lack native support. |
| **Zero Cost** (NFR budget constraint) | 10 | 10 | 10 | 0 | SQL Server licensing $900+/year. PostgreSQL/MySQL/MongoDB open-source. |
| **Healthcare Ecosystem** (HIPAA compliance tools) | 9 | 7 | 5 | 10 | SQL Server strongest healthcare tools but costly. PostgreSQL mature HIPAA deployments, audit extensions. |
| **Backup/PITR** (DR-024, DR-027 point-in-time recovery) | 10 | 9 | 7 | 10 | PostgreSQL WAL archiving robust. MongoDB replica sets less granular. |
| **Weighted Total** | **59/60** | 43/60 | 47/60 | 38/60 | **PostgreSQL wins** on vector support, cost, and JSON indexing. SQL Server excluded by budget. MongoDB lacks ACID for booking. |

#### AI Model Provider Selection (AIR-001 to AIR-010, AIR-Q01-Q09)

| Metric (from NFR/DR/AIR) | OpenAI GPT-4o-mini | Claude 3.5 Sonnet | GPT-4 Turbo | Gemini 1.5 Pro | Rationale |
|--------------------------|-------------------|-------------------|-------------|----------------|-----------|
| **Cost per 1M Input Tokens** (AIR-O09 budget) | 10 ($0.15) | 7 ($3.00) | 0 ($10.00) | 8 ($1.25) | GPT-4o-mini 20x cheaper than GPT-4. Gemini competitive but weaker medical accuracy. |
| **Medical Reasoning Accuracy** (AIR-Q01 >98%) | 8 (MedQA 79%) | 9 (MedQA 86%) | 9 (MedQA 86%) | 7 (MedQA 75%) | Claude strongest medical reasoning. GPT-4o-mini acceptable with human verification (AIR-S03). |
| **Latency** (AIR-Q03-Q05 <1s intake, <5s coding) | 9 (<1s) | 8 (1-2s) | 7 (2-3s) | 9 (<1s) | GPT-4o-mini and Gemini fastest. GPT-4 slower generations. |
| **Context Window** (AIR-O01 token budget 4K input) | 10 (128K) | 10 (200K) | 10 (128K) | 10 (1M) | All models exceed 4K budget requirement. Gemini massive window overkill for MVP. |
| **Structured Output Support** (AIR-Q06 hallucination <5%) | 10 (JSON mode) | 9 (guided gen) | 10 (JSON mode) | 7 (weaker validation) | OpenAI JSON mode enforces schema. Claude guided generation strong. Gemini less reliable. |
| **Weighted Total** | **47/50** | 43/50 | 36/50 | 41/50 | **GPT-4o-mini wins** on cost and latency. Hybrid strategy: use Claude fallback when GPT confidence <80% (AIR-010). |

#### Vector Store Selection (AIR-R01-R06)

| Metric (from NFR/DR/AIR) | pgvector (PostgreSQL) | Pinecone | Weaviate | Chroma | Rationale |
|--------------------------|----------------------|----------|----------|---------|-----------|
| **Cost Monthly** (NFR budget constraint) | 10 ($0) | 0 ($70) | 5 ($25) | 10 ($0) | pgvector and Chroma free. Pinecone/Weaviate paid. |
| **Operational Overhead** (NFR-025 4h RTO) | 10 (unified DB) | 8 (managed) | 6 (self-host) | 7 (in-process) | pgvector reuses PostgreSQL ops. Pinecone managed but separate infra. Weaviate self-host complexity. |
| **Query Performance** (AIR-R02 top-5 retrieval <500ms) | 7 (1-5ms @10K) | 10 (<1ms) | 9 (1ms) | 8 (5ms | Pinecone fastest at scale. pgvector sufficient for <100K embeddings Phase 1. Latency delta <5ms acceptable. |
| **Hybrid Search** (AIR-R06 semantic + keyword) | 10 (PostgreSQL FTS) | 6 (metadata only) | 9 (built-in) | 5 (basic) | pgvector combines vector + FTS in single query. Pinecone weak keyword support. |
| **Scalability** (Phase 2 >1M embeddings) | 5 (degrades >1M) | 10 (billions) | 9 (millions) | 6 (thousands) | pgvector bottleneck at 1M+ vectors. Pinecone scales. Plan migration if growth exceeds 500K. |
| **Weighted Total** | **42/50** | 34/50 | 38/50 | 36/50 | **pgvector wins** for Phase 1 on cost and operational simplicity. Monitor vector count; migrate to Pinecone if >500K embeddings. |

## Technical Requirements

### Technology Choices
- TR-001: System MUST use PostgreSQL 16.x as primary database with ACID compliance and JSONB support (Justification: DR-002 optimistic locking, DR-004 clinical data storage, AIR-R01 vector embeddings)
- TR-002: System MUST use .NET 8 ASP.NET Core Web API for backend services (Justification: NFR-096 Windows Services deployment, NFR-001 2s response time)
- TR-003: System MUST use React 18.x for frontend application with TypeScript (Justification: NFR-047 mobile responsive, NFR-048 inline validation, NFR-050 consistent UI)
- TR-004: System MUST use Entity Framework Core 8.x as ORM with code-first migrations (Justification: DR-009 to DR-015 referential integrity, DR-028 schema versioning)
- TR-005: System MUST use pgvector 0.5.x extension for vector similarity search in PostgreSQL (Justification: AIR-R01 to AIR-R06 RAG retrieval, cost optimization vs separate vector DB)
- TR-006: System MUST use OpenAI GPT-4o-mini as primary LLM with Claude 3.5 Sonnet as fallback (Justification: AIR-Q01 >98% accuracy, AIR-O09 cost budget $0.15/1M tokens)
- TR-007: System MUST use Upstash Redis 7.x for distributed caching and session storage (Justification: NFR-030 5-min TTL caching, NFR-004 sub-second cached views)
- TR-008: System MUST use ASP.NET Core Identity with JWT authentication (Justification: NFR-011 RBAC, NFR-013 bcrypt hashing, NFR-014 15-min session timeout)

### Architecture Patterns
- TR-009: System MUST implement layered architecture (Presentation → Service → Data Access) for deterministic workflows (Justification: NFR-036 80% test coverage, clear separation of concerns)
- TR-010: System MUST implement vertical slice architecture for AI components (conversational intake, document parsing, medical coding) (Justification: AIR-O05 model version rollback, independent iteration)
- TR-011: System MUST implement RESTful API design with hypermedia links (HATEOAS) for resource navigation (Justification: NFR-034 idempotent endpoints, NFR-038 OpenAPI docs)
- TR-012: System MUST implement asynchronous processing with message queue for long-running AI workflows (Justification: AIR-O07 document parsing queue, NFR-001 2s response time)
- TR-013: System MUST implement CQRS pattern for audit log access (Justification: DR-016 7-year retention, NFR-019 99.9% uptime without audit query bottleneck)
- TR-014: System MUST implement API Gateway pattern for AI provider abstraction (Justification: AIR-O04 circuit breaker, AIR-O10 A/B testing, multi-provider fallback)
- TR-015: System MUST implement optimistic locking with version field for appointment booking (Justification: DR-002 prevent double-booking, NFR-001 2s response time)

### Platform Requirements
- TR-016: System MUST deploy backend as Windows Services on Windows Server 2022 (Justification: NFR-096 native Windows deployment, cost optimization)
- TR-017: System MUST deploy frontend as static React build on IIS 10 (Justification: NFR-096 free infrastructure, NFR-047 mobile responsive)
- TR-018: System MUST use HTTPS with TLS 1.2 or higher for all client-server communication (Justification: NFR-010 data in transit encryption, HIPAA compliance)
- TR-019: System MUST use AES-256 encryption for database data at rest (Justification: NFR-009 data at rest encryption, NFR-041 HIPAA Privacy Rule)
- TR-020: System MUST implement health check endpoints exposing /health and /ready routes (Justification: NFR-020 health checks <500ms, NFR-019 99.9% uptime monitoring)
- TR-021: System MUST support feature flags via configuration files for gradual rollout (Justification: NFR-039 feature flags, AIR-O10 A/B testing)
- TR-022: System MUST implement centralized configuration management using appsettings.json with environment overrides (Justification: NFR-040 centralized config, NFR-021 zero-downtime migrations)

### Integration Requirements
- TR-023: System MUST integrate with SMTP service (SendGrid or Gmail) for email delivery (Justification: FR-036 to FR-045 appointment confirmations and reminders)
- TR-024: System MUST integrate with Twilio API for SMS delivery (Justification: FR-036 to FR-045 SMS reminders)
- TR-025: System MUST integrate with OpenAI API for LLM inference with retry and fallback logic (Justification: AIR-O04 circuit breaker, AIR-O08 3 retries)
- TR-026: System MUST integrate with iCal format for calendar synchronization (Google Calendar, Outlook) (Justification: FR-025 calendar sync)
- TR-027: System MUST implement rate limiting middleware for API endpoints (100 requests per minute per user) (Justification: AIR-S08 rate limiting, NFR-031 error rate <0.1%)
- TR-028: System MUST implement request correlation IDs for distributed tracing across services (Justification: NFR-035 structured logging, troubleshooting multi-step workflows)

### Development Standards
- TR-029: System MUST use xUnit for unit testing with Moq for dependency mocking (Justification: NFR-036 80% test coverage)
- TR-030: System MUST use Playwright for end-to-end UI testing (Justification: NFR-036 automated regression testing, NFR-048 inline validation verification)
- TR-031: System MUST use Serilog for structured logging with Seq for log aggregation (Justification: NFR-035 structured logging, NFR-020 health monitoring)
- TR-032: System MUST use Swagger (Swashbuckle) for API documentation generation (Justification: NFR-038 OpenAPI 3.0 docs)
- TR-033: System MUST follow C# coding conventions (PascalCase methods, camelCase parameters, async suffix for async methods) (Justification: NFR-036 maintainability)
- TR-034: System MUST enforce code quality gates (zero compiler warnings, StyleCop rules) in CI pipeline (Justification: NFR-036 maintainability, NFR-031 error rate <0.1%)
- TR-035: System MUST use semantic versioning (MAJOR.MINOR.PATCH) for API releases (Justification: NFR-037 backward compatibility, NFR-039 feature flags)

## Technical Constraints & Assumptions

### Constraints
- **Constraint #1: Free/Open-Source Infrastructure Only**: Must use zero-cost cloud services and open-source technologies due to budget limitations. Eliminates Azure App Service, AWS Lambda, paid AI platforms (Pinecone Standard), and enterprise monitoring tools (Datadog, New Relic). Drives technology choices toward PostgreSQL, Upstash Redis free tier, self-hosted Seq, and GPT-4o-mini pricing. Risk: Free tiers have strict limits (10K Redis requests/day, Twilio $15 trial) that may be exceeded during high usage. Mitigation: Implement aggressive caching (NFR-030) and monitor usage daily (AIR-O09).

- **Constraint #2: Native Windows Deployment Required**: Backend must deploy as Windows Services on Windows Server 2022 with IIS frontend hosting. Eliminates containerized deployment (Docker, Kubernetes) and Linux-based infrastructure in Phase 1. Limits horizontal scaling options but reduces operational complexity. Risk: Single-server architecture creates availability bottleneck. Mitigation: Implement health monitoring (TR-020), automatic service restart, and manual failover procedures. Plan containerization migration in Phase 2.

- **Constraint #3: No Direct EHR Integration in Phase 1**: Platform operates as standalone aggregator without real-time HL7/FHIR feeds from Electronic Health Record systems. Clinical data must be manually uploaded by staff rather than automatically synchronized. Impact: Reduces data freshness and increases staff workload. Drives decision to prioritize AI document parsing (FR-047 to FR-052) to minimize manual data entry. Plan FHIR-compliant API integration in Phase 2.

- **Constraint #4: Dummy Insurance Validation Only**: System performs soft insurance pre-check against dummy records without real-time eligibility verification from payers (Availity, Change Healthcare). Drives requirement FR-033 as [HYBRID] with manual staff review (FR-034). Risk: Staff burden remains high for insurance validation. Mitigation: Provide clear UI indicators for validation failures and prioritize payer API integration in Phase 2.

- **Constraint #5: English Language Only**: All UI text, AI conversational intake, and medical coding operate in English only. Eliminates internationalization (i18n) framework and multilingual LLM requirements in Phase 1. Limits addressable market to English-speaking U.S. healthcare facilities. Plan Spanish language support and i18n infrastructure in Phase 2.

### Assumptions
- **Assumption #1: Patients Have Email and Mobile Phone**: Multi-channel reminder strategy (FR-036 to FR-045) assumes patients provide valid email and SMS-capable phone numbers. If patient lacks email or phone, automated reminders fail. Validation: Track contact information completeness during onboarding. Mitigation: Offer manual phone call reminders for patients without email/SMS (staff workflow).

- **Assumption #2: Staff Will Verify All AI Outputs**: Hybrid AI-human workflow (AIR-S03, FR-064) assumes staff have time and expertise to review AI-suggested medical codes and extracted clinical data. If staff skip verification due to time pressure, accuracy guarantees degrade (AIR-Q01). Validation: Monitor AI-human agreement rates (AIR-Q09) and staff verification completion rates. Mitigation: Implement mandatory verification workflow with blocking UI before code finalization.

- **Assumption #3: Uploaded Clinical Documents Are Legitimate**: Document parsing pipeline (FR-046 to FR-060) assumes uploaded PDFs contain accurate clinical information from verified sources. System does not validate document authenticity or detect fraudulent records. Risk: Malicious staff could upload fabricated documents. Validation: Implement document source attribution (DR-010) and chain of custody tracking. Plan digital signature verification in future phases.

- **Assumption #4: AI Model Providers Maintain SLA Uptime**: Platform depends on OpenAI and Anthropic API availability for core features (conversational intake, document parsing, medical coding). Assumes >99% uptime per provider SLAs. If both providers fail simultaneously, AI workflows halt. Validation: Monitor provider uptime metrics weekly. Mitigation: Implement circuit breaker (AIR-O04) with fallback to manual workflows and queue failed requests for retry.

- **Assumption #5: Medical Terminology Remains Stable**: RAG knowledge base (AIR-R01) for medical terminology and coding guidelines assumes ICD-10/CPT codes have stable definitions with quarterly updates (FR-063). If coding standards change mid-year (e.g., emergency COVID-19 code additions), knowledge base becomes stale. Validation: Subscribe to CMS code update notifications. Mitigation: Implement manual knowledge base update workflow with admin approval before refresh.

- **Assumption #6: Vector Embedding Count Stays Below 100K**: pgvector performance assumptions (Technology Decision Matrix) based on <100K medical term embeddings in Phase 1. If embedding count exceeds 500K (e.g., patient clinical note indexing added), query latency degrades beyond AIR-Q targets. Validation: Monitor embedding count monthly. Mitigation: Plan migration to dedicated vector database (Pinecone, Weaviate) when approaching 500K threshold.

- **Assumption #7: No-Show Risk Prediction Has Historical Data**: Classification model for no-show risk (FR-014) assumes 6+ months of historical appointment data for initial training. If deployed in greenfield facility, model cannot generate predictions until data accumulates. Validation: Check appointment history volume during onboarding. Mitigation: Use rule-based scoring (late arrivals, missed appointments) until ML model has sufficient training data.

## Development Workflow

### Phase 1: Environment Setup and Foundation
1. **Development Environment**: Provision Windows Server 2022 VM with IIS 10, install .NET 8 SDK, PostgreSQL 16, Redis CLI. Configure local development certificates (HTTPS).
2. **Database Foundation**: Initialize PostgreSQL with pgvector extension, create database schema with migrations (Entity Framework Core), configure connection pooling (max 100 connections per TR-008).
3. **Authentication Scaffolding**: Implement ASP.NET Core Identity with JWT authentication, configure password hashing (bcrypt 10 rounds per NFR-013), create User and Role entities with RBAC (Patient, Staff, Admin per NFR-011).
4. **API Foundation**: Setup ASP.NET Core Web API project with Swagger (Swashbuckle) for OpenAPI docs (NFR-038), implement health check endpoints (/health, /ready per TR-020), configure CORS middleware.
5. **Frontend Scaffolding**: Initialize React 18 project with TypeScript, Material-UI component library (NFR-046 accessibility), configure React Query for server state and Zustand for client state (NFR-004 caching).

### Phase 2: Core Deterministic Features
1. **User Management**: Implement user registration with email verification (FR-001), password reset workflow (FR-005), session management with 15-min timeout (FR-003, NFR-014), multi-factor authentication for staff/admin (FR-008).
2. **Appointment Scheduling**: Implement appointment booking with optimistic locking (FR-012, DR-002), slot availability filtering (FR-011), waitlist registration (FR-017), dynamic preferred slot swap background job (FR-019).
3. **Queue Management**: Implement arrival queue dashboard with real-time updates (FR-073), wait time calculation (FR-072), priority adjustment (FR-075), no-show auto-marking after 15 minutes (FR-076).
4. **Notification System**: Integrate SMTP service for email delivery (TR-023), Twilio API for SMS (TR-024), implement reminder scheduling jobs (24h, 2h per FR-037, FR-038), retry logic with exponential backoff (NFR-032, FR-045).

### Phase 3: AI Infrastructure and RAG Pipeline
1. **AI Gateway Foundation**: Implement custom .NET AI Gateway service with Polly for circuit breaker and retry (AIR-O04, AIR-O08), configure OpenAI and Claude API clients with token budget enforcement (AIR-O01-O03), implement cost tracking metrics (AIR-O09).
2. **Vector Store Setup**: Create pgvector indexes in PostgreSQL for medical terminology, intake templates, coding guidelines (AIR-R04), generate embeddings using OpenAI text-embedding-3-small (384 dimensions per Embedding Model selection), implement hybrid search combining vector similarity and PostgreSQL Full-Text Search (AIR-R06).
3. **RAG Knowledge Base**: Populate vector store with ICD-10/CPT code descriptions, medical terminology dictionaries, clinical intake flow templates (chunk size 512 tokens, 20% overlap per AIR-R01), implement quarterly refresh workflow when code libraries updated (FR-063, AIR-R05).
4. **Prompt Management**: Build prompt template engine using Liquid/Handlebars for versioned templates with variable substitution, create prompt templates for conversational intake flows, document parsing instructions, medical coding guidelines. Version control in Git for rollback capability (AIR-O05).

### Phase 4: AI Feature Implementation
1. **Conversational Intake**: Implement AI-assisted conversational intake flow (FR-026) with RAG context retrieval (top-5 chunks per AIR-R02), mandatory field collection (FR-029), optional field collection (FR-030), auto-save every 30 seconds (FR-035), seamless switch to manual form (FR-028).
2. **Document Parsing Pipeline**: Implement async document upload queue using Redis Queue (TR-012), integrate OpenAI GPT-4o-mini for PDF parsing with OCR (FR-047), extract structured clinical data (medications FR-048, diagnoses FR-049, procedures FR-050, allergies FR-051), assign confidence scores (FR-058), flag low-confidence data <80% for review (FR-059, AIR-Q08).
3. **Medical Coding Engine**: Implement ICD-10 code generation with justification (FR-061, AIR-003), CPT code generation with justification (FR-062, AIR-004), validate codes against current libraries (DR-015, AIR-S02), present AI-suggested codes to staff for verification (FR-064, AIR-S03), track AI-human agreement metrics (FR-067, AIR-Q09).
4. **Conflict Detection**: Implement clinical data consolidation across multiple documents (FR-052, AIR-005), detect conflicts using rule-based checks and LLM analysis (medication contraindications, duplicate diagnoses with different dates per FR-053), escalate critical conflicts with urgent flag (AIR-S09), provide side-by-side comparison view for staff resolution (FR-054).

### Phase 5: Testing and Quality Assurance
1. **Unit Testing**: Achieve 80% test coverage for business logic using xUnit and Moq (NFR-036, TR-029), test all CRUD operations with EF Core in-memory database, test AI Gateway with mocked OpenAI/Claude responses.
2. **Integration Testing**: Test end-to-end workflows (booking → confirmation → reminder → arrival → check-in), test AI pipelines with real API calls to staging OpenAI endpoint, validate database transactions and rollback scenarios.
3. **E2E UI Testing**: Implement Playwright test suite (TR-030) covering patient booking flow, AI conversational intake, staff document upload workflow, admin configuration dashboard. Validate accessibility (NFR-046) using axe-core.
4. **Performance Testing**: Load test appointment booking endpoint with 100 concurrent users (NFR-005), validate 2s response time at 95th percentile (NFR-001), test document parsing latency with 10-page PDFs (30s target per AIR-Q04), validate Redis cache hit ratio >80% (NFR-004).

### Phase 6: Security Hardening and Compliance
1. **Security Testing**: Run OWASP dependency scan (npm audit, dotnet list package --vulnerable), implement SQL injection prevention with parameterized queries (NFR-018), XSS prevention with content security policy headers, validate HTTPS enforcement (TR-018).
2. **HIPAA Compliance Audit**: Verify AES-256 encryption at rest (NFR-009, TR-019), TLS 1.2+ in transit (NFR-010, TR-018), immutable audit logs for all PHI access (NFR-012, DR-016), RBAC enforcement (NFR-011), session timeout (NFR-014), PII redaction in logs (NFR-017, AIR-S01).
3. **Penetration Testing**: Conduct manual penetration test on authentication endpoints (brute force, session fixation, CSRF), test authorization bypass attempts, validate rate limiting (TR-027, AIR-S08).

### Phase 7: Deployment and Monitoring
1. **Production Deployment**: Deploy backend as Windows Services using PowerShell scripts, deploy frontend to IIS as static React build, configure Let's Encrypt SSL certificates for HTTPS (TR-018), setup PostgreSQL backup automation (daily at 2 AM per DR-022).
2. **Monitoring Setup**: Configure Serilog structured logging with Seq Community Edition (TR-031), implement custom health check dashboard polling /health endpoint every 60 seconds (NFR-020), setup email alerts for health check failures and error rate spikes (NFR-031 error rate <0.1%).
3. **Operational Runbook**: Document service restart procedures, database backup/restore procedures, AI provider fallback procedures when OpenAI unavailable (switch to Claude per AIR-O04), knowledge base refresh procedures for quarterly ICD-10/CPT updates (AIR-R05).
4. **Post-Launch Monitoring**: Track key metrics daily (appointment booking volume, no-show rate reduction, AI-human agreement rate per AIR-Q09, AI provider costs per AIR-O09), review audit logs weekly for security anomalies (NFR-012), conduct quarterly backup restoration tests (DR-026).
