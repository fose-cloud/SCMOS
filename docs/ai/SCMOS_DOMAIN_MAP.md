# SCMOS Domain Map — Phase A

วันที่ตรวจ: 7 กันยายน 2026 (Asia/Bangkok)

Source snapshot: `9b31ab4` บน `azure-dotnet-migration`; รายละเอียดวิธีตรวจและข้อจำกัดอยู่ใน [Current Architecture](SCMOS_CURRENT_ARCHITECTURE.md)

ชื่อหน้าจอและ entity ด้านล่างมาจากโค้ดจริง การมีเมนูไม่ได้แปลว่ามี service หรือข้อมูลจริงครบแล้ว ตารางฐานข้อมูลเป็น EF model ที่พบใน repository; ยังไม่ได้ตรวจ schema drift หรือจำนวนข้อมูลบน Production

## 1. Domain → UI → API → services → tables

Paths ย่อ: UI = `app/scmos/screens/`; API = `server/Scmos.Api/Endpoints/`; Services/Rules/Data = โฟลเดอร์ใต้ `server/Scmos.Api/`

| Domain | Existing UI / status | Existing API and service | Actual mapped tables |
| --- | --- | --- | --- |
| Operations / My Job / Import-Export-Domestic | `Workspace.tsx`, `ImportWorkbook.tsx`, `Postpone.tsx`; active | `/api/jobs`, `/page`, `/changed`; `JobsEndpoints`, `WorkspaceService`, `JobsRepository`, `JobRegisterCache` | `operation_jobs`, `audit_events`, `job_delegations` |
| Workflow / booking / vendor escalation | `WorkflowPanel.tsx`, `Booking.tsx`; Booking/Pre-Run/Verification menu entries were removed but underlying code remains | `/api/workflow/*`; `WorkflowService` | `workflow_events`, `supplier_requests`, `operation_jobs` |
| Shipment tracking / delay | `Monitoring.tsx`, `MonitorBoard.tsx`, `Detail.tsx`, `DelayAnalysis.tsx` | `/api/shipment/*`, `/api/monitor`, `/api/risk`; `MonitoringService`, `MonitorService`, `RiskService` | `shipment_milestones`, `delay_records`, `operation_jobs`, related operational issues |
| Subcontractor Master / onboarding | `Suppliers.tsx`, `SupplierFlows.tsx` | `/api/suppliers/*`; `SupplierService`, `CarrierDirectory` | `suppliers`, `supplier_aliases`, `supplier_contacts`, `supplier_trucks`, `supplier_drivers`, `supplier_evaluations` |
| Carrier-facing work | `CarrierPortal.tsx`; carrier accounts have their own menu | `/api/carrier`, accept/decline; `CarrierService` | `staff`, `suppliers`, `supplier_requests`, `operation_jobs` |
| Capacity / vehicle types | `CapacityBoard.tsx`, `VehicleTypes.tsx` | `/api/capacity`, `/api/vehicle-types`; `CapacityService`, `VehicleTypeService` | `supplier_capacity`, `vehicle_types`, `supplier_trucks`, `type_migration_backup` |
| Rates / procurement / fuel bands | `Rates.tsx`, `RateSheet.tsx`, `ChemoursRates.tsx` | `/api/rates`, `/promote`, `/quotes`, `/api/rate-inquiries/*`; `RateService`, `RateInquiryService` | `rate_lanes`, `rate_prices`, `fuel_bands`, `rate_surcharges`, `rate_inquiries`, `rate_inquiry_lanes`, `rate_inquiry_prices` |
| Quotation / multi-route calculation | `Quotation.tsx`, `QuoteCalculator.tsx`, `QuoteTerms.tsx` | `/api/quote-card/*`, `/api/journeys/*`; `QuoteCardService`, `QuoteSheetService`, `JourneyService`, `RoutingService` | `quote_vehicle_rates`, `quote_extras`, `quote_settings`, `journey_distances`, rate inquiry tables |
| Customer-specific rates / customer reports / receipt | `Loreal.tsx`, `Chemours.tsx`, `ChemoursRates.tsx`, `CargoForm.tsx` | `/api/customer-rates`, `/api/cargo-forms`; `CustomerDocumentService`; reports also consume existing job data | `customer_rate_bands`, `customer_rate_lanes`, `customer_rate_prices`, `cargo_form_templates`, `operation_jobs` |
| KPI / OTD / vendor performance | `Kpi.tsx`, `SupplierPerformance.tsx`, `VendorReport.tsx` | `/api/kpi`, `/measures`, `/excel`; `KpiService`, `KpiEngine`, `CarrierScorecard` | Reads jobs, delays, supplier requests, pre-run checks, incidents, operational issues; not a new KPI fact table |
| Dashboard / management briefing | `Dashboard.tsx`, `Today.tsx`, `ExecutiveBoard.tsx`, `BriefingBand.tsx` | `/api/dashboard/today`, `/rates`, `/briefing`, `/api/notifications`; `DashboardService`, `MonitorService`, `NotificationService` | Reads current operational tables/cache; no standalone AI metric table |
| Incidents / CAR-PAR / daily issues | `Incidents.tsx`, `CarParReport.tsx`, `OperationalIssues.tsx` | `/api/incidents/*`, `/api/issues/*`; `IncidentService`, `OperationalIssueService` | `incident_cases`, `operational_issues`, `documents`, `audit_events` |
| Training / driver compliance | `Training.tsx`, `CustomerTrainingRegister.tsx` | `/api/training/*`; `TrainingService` | `drivers`, `training_courses`, `customer_training_requirements`, `driver_training`, `customer_training_records`, documents and audit |
| Documents / verification / retention | `Documents.tsx`, `Verification.tsx`, `PreRun.tsx` | `/api/documents/*`, `/api/verification/*`, `/api/pre-run/*`, `/api/ai-extract`; `DocumentService`, `VerificationService`, `PreRunService`, `BlobFileStore`, `DocumentExtractor` | `documents`, `pre_run_checks`, `operation_jobs`, `audit_events`; files in private Blob |
| Customers / Job Rotation | `JobRotation.tsx`, customer dropdowns in My Job | `/api/rotation`, `/customers`, `/owners`, `/options`; `RotationService` | `rotation_assignments`, `staff`, `suppliers`; no independent `customers` master entity found |
| Reports / archives / legacy upload data | `ReportCentre.tsx`, `VolumeReport.tsx`, report panels | `/api/reports/*`, `/api/uploads`, `/api/operations`; `MonthlyReportService`, `ReportWriterService`, `ReportArchiveService`, `ReportScheduler`; legacy endpoints also query DbContext | `report_archive`, `report_uploads`, `operation_uploads`, `operation_entries` plus current operational sources |
| Billing / invoices | Billing Control menu and `Panels.tsx` / `BillingAging` placeholder | No invoice-specific API or BillingService found; `KpiEngine.Billing()` returns not measurable | No invoice/payment entity or table found |
| AI assistant / approval | `Assistant.tsx`; real catalogue/queue, rule-based answer; no model chat | `/api/ai/tools`, `/invoke`, `/approvals`, decision/applied; `AiGateway`, `AiPermissions` | `ai_tools`, `approvals`, general `audit_events` |
| Administration / identity / delegation / audit | `Administration.tsx`, `Audit.tsx`, profile delegation controls | `/api/me`, `/api/staff/*`, `/api/delegations/*`, `/api/audit`; `UserAccessor`, `StaffService`, `SignInAccountService`, `DelegationService`, `AuditService` | `staff`, `job_delegations`, `audit_events` |
| ABS / CCS / Outlook / LINE | `ExternalSystem.tsx` with four definitions; not yet connected | UI probes `/api/abs/status`, `/api/ccs/status`, `/api/integrations/microsoft/status`, `/api/integrations/line/status`; handlers not found | No mailbox/message/matching tables found |

## 2. Rules, dependencies, candidate AI tools and risk

Candidate tools are **proposals**, not statements that the current AI can execute them. Prefer existing catalogue names where a matching definition already exists. Read-only is low side-effect risk but can still carry high confidentiality risk.

### Operations and shipment monitoring

- Rules: `JobRules`, `JobStatus`, `WorkspaceTabs`, `Formats`, `MonitorRules`, `Notifications`, `Workflow`; normalize IMPORT/EXPORT/DELIVERY and preserve no-date cases.
- Dependencies: identity/operator mapping, current register, delegation for writes, distinct job key, workflow events, existing arrival/plan calculations.
- Reusable tools: `search_shipment`, `query_shipments`, `query_delays`; later typed `getShipmentTimeline` over known event sources.
- Proposed output: key/reference/customer/plan/actual/status plus coded missing-assignment indicators and source timestamp; exclude full job JSON and personal contacts by default.
- Risk: read-only operations LOW action risk / HIGH confidentiality if incorrectly scoped. Status/ETA/assignment changes HIGH and remain off.
- Important: `MonitorRules.Judge` excludes done/cancelled work, skips unreadable dates, looks within two days for missing carrier, and uses one most-serious reason per job. Existing NoTruck rule tests both plate and driver absent, not either absent. Do not silently change that rule to match a generic AI example.
- `RiskService` is a different older rule grouping with a 12-group display limit. Decide the displayed total and basis explicitly before building the morning queue.

### Vendors, carrier portal, capacity and vehicles

- Rules: alias resolution in `CarrierDirectory`, `CarrierAssignment`, `Workflow`, vehicle normalization and capability checks. Names are not stable identity; aliases cannot be arbitrarily merged by a model.
- Dependencies: supplier master, vehicles, capacity date/type, historical requests, pre-run response and operational evidence.
- Tools: `search_supplier`, `query_capacity`; vendor performance adapter reuses `CarrierScorecard`/`KpiEngine`.
- Risk: master/performance reads MEDIUM confidentiality; recommendations are proposals; assigning a vendor, registering/merging/deleting vendors or changing master values is HIGH/RESTRICTED and not enabled.
- Carrier users must not receive competitors' lanes, prices or assignments. An empty supplier/operator mapping must not mean all suppliers.

### Rates and quotations

- Rules: `FuelLadder`, `QuoteBasis`, `QuoteCalculation`, `RateVehicles`, `JourneyKey`, `RateService`, `QuoteSheetService`. Preserve existing per-line rounding and current fuel basis.
- Dependencies: equipment code, route, customer, carrier, service, DG status, diesel band, surcharge source and rate provenance. Spreadsheet parser/UI preview is not the final backend write authority.
- Tools: `query_rates`; explicit read-only comparison adapter. Do not run `/promote` or `/save-to-sheet` as a read tool.
- Risk: HIGH commercial sensitivity even for reads; require `ViewRates`. `QuoteToSheet` permits a particular user save operation, not AI permission to edit the rate book.
- Keep buying/selling/customer-specific rates and inquiry/calculated/promoted sources separate. A stored price is not proof of an approved, effective contract; validity/approval evidence may be UNKNOWN. Reply with no supported approved rate instead of inventing a price.
- The latest rate UI supports multiple routes/vehicle types and missing quotation lines. A missing cost is not zero. Cargo forms are documents, not rate authority.

### KPI, OTD, reports and management

- Rules: `KpiMeasures`, `KpiEngine`, `CarrierScorecard`, `MonthlyReport`, `ReportCommentary`, `Briefing`. Reuse denominators, measurable-data flags, configured/customer-specific thresholds and evidence windows.
- Actual measure enum contains six: OnTimeDelivery, Delay, Accident, CarPar, Billing, SupplierPerformance. Older prose saying eight does not override the code.
- Dependencies: plan/arrival timestamps, supplier request responses, pre-run records, recorded delays/incidents/issues; some existing TS report helpers present different report views and must be reconciled against their source, not replaced wholesale.
- Tools: `calculate_kpi`, `analyze_kpi`, management read summary. Return scope, period, numerator, denominator, measurable status, calculation version and evidence references.
- Risk: MEDIUM/HIGH confidentiality; dashboards may permit aggregates while denying person-level or supplier details. Aggregate permission is not permission to reveal underlying rows.
- `Briefing` is deterministic prose over facts already counted. It is a reusable morning-brief starting point; label FACT/CALCULATED, not an LLM review.
- Billing KPI has no invoice source. Present N/A, not 0%, 100% or an invented number of exceptions. Building an invoice module is outside read-only AI work.

### Incidents, operational issues and CAR/PAR

- Rules: existing issue classifications, `Workflow`/incident transitions, `AuditActions` and CloseCarPar checks.
- Dependencies: issue/case IDs, job linkage, evidence documents, due dates and authorized responsible person.
- Tools: `query_incidents`, case/timeline read, labeled draft questions or summaries. Root cause produced by AI must remain AI SUGGESTED until human verification.
- Risk: HIGH for sensitive incident content and case closure. Drafting must not call save/advance/close handlers; no automatic CAR/PAR closure.

### Training, driver compliance, documents and knowledge

- Rules: `TrainingRules`, `DocumentChecklist`, `PreRun`, `Retention`, `BlobPaths` and document access guards.
- Existing training bands: >60 days VALID; 31–60 ATTENTION; 1–30 EXPIRING_SOON; today/past EXPIRED. Unreadable expiry returns unknown, not automatically expired. Required course absent is MISSING.
- Dependencies: customer course requirements, driver identity/certificates and private file metadata. `drivers` and `supplier_drivers` are distinct models; do not silently merge them.
- Tools: scoped compliance expiry read, document metadata/search and permitted extraction. General `read_document` must not accept arbitrary Blob paths or bypass `DocumentService` authorization.
- Risk: HIGH PII/confidentiality. Prefer expiry/status metadata to identity documents or phone numbers. Retention deletion and permissions changes are RESTRICTED for AI.
- Knowledge V1 should use document ID, source, version if available, updated date and reference plus the same access filter. No vector database or large RAG system is necessary for discovery. Missing version metadata must remain UNKNOWN.

### Customers and Job Rotation

- Rules: `RotationService`, `rotationCustomers.ts`; customer choices come from rotation data, carriers from supplier master, owners from staff. Customer labels in different reports may require existing normalization.
- Dependencies: rotation row, active staff, supplier options and assignment capability.
- Tools: customer/rotation read context, explain the current assignment plan without mutating it.
- Risk: MEDIUM reads / HIGH changes. Do not invent a new Customer table just to satisfy an example entity list; do not reassign work automatically.

### Existing AI and human approval

- Rules: `AiPermissions` allow/approval/deny and forbidden actions, role/capability controls, plus endpoint audits.
- Dependencies: tool roster, user scope, approval payload and general audit. No current live executor in `/invoke`.
- Next safe increment: typed tool definitions + explicit schema/capability/scope/handler/audit adapters; server-side registry selects only authorized read tools. Unknown tool = denied.
- Risk: HIGH platform-wide if handlers inherit only the current generic Allow flag. Approval listing and applied-state behavior require a separate approved security/execution design before writes.
- Agent categories already present are operation/document/kpi/supplier/safety/management. Register future specialist IDs through a mapping rather than renaming database categories and breaking old rows.

### Administration, external integrations and events

- Current Graph use in staff/sign-in services is identity administration, not permission to read/send Outlook mail.
- ABS/CCS/Outlook/LINE menus are placeholders with live status probes and a list of missing requirements. New model chat must not claim these systems are connected.
- Future event envelope can hold ID, type, entity key, source, timestamp, actor and schema version. A future outbox/idempotent worker is a proposal, not a deployed Service Bus.
- Tools for changing permissions, deleting data, arbitrary SQL and modifying audit history are prohibited. Do not expose staff administrative methods to the AI registry.
- No external communication, Graph/LINE credentials, account provisioning or new Azure resources are authorized merely by adding interfaces.

## 3. First read-only pilot acceptance

Question: “Show today's high-risk shipments.” / “วันนี้มีงานอะไรเสี่ยงบ้าง”

1. Server resolves the current recognized user and permitted row/field scope. Blank restricted scope returns no data/refusal, never team-wide data.
2. Explicit date uses Thailand time and existing date rules; records without usable dates are reported as data-quality exceptions, not guessed into today.
3. Existing operational rule adapter calculates findings. No model-created score replaces approved SCMOS rules.
4. The UI receives real evidence keys, reasons, total matches, limited examples and calculation timestamp. Totals are computed before pagination/display caps.
5. Model receives only allowed evidence projection; tools reject extra fields, unknown names, arbitrary SQL and all mutation attempts.
6. Answer labels FACT/CALCULATED/AI SUGGESTED/UNKNOWN appropriately. Provider failure leaves deterministic core pages working.
7. Successful/refused/failed calls are audited with a run identifier before enabling user-facing live tool execution.
8. Test cross-user, carrier, management-only, missing owner, stale request, invalid model output, timeout and injected job notes. Compare returned evidence with the existing SCMOS view at the same scope/time.

## 4. Explicit non-goals for the initial increment

No invoice backend, no database replacement, no generalized SQL tool, no new authentication, no model-driven vendor assignment, no rate edits, no automatic incident closure, no Outlook/LINE sending, no production deletion and no role changes. Read-only AI still needs authorization and audit; it is not automatically safe just because it does not update a job.
