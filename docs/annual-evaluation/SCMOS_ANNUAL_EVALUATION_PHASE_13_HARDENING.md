# SCMOS Carrier Annual Evaluation — Phase 13: Security, Tests, UAT

2 October 2026 · covers spec sections 35 and 38–43 across Phases 1–12

## What Phase 13 changed

**Writes that land whole.** Three writes saved piece by piece. A failure part-way through left the data inconsistent:

| Write | Before | Now |
|---|---|---|
| Snapshot (`POST /{id}/generate-snapshot`) | saved carrier by carrier — a failure left some carriers on new evidence, the rest on old | one transaction: every carrier's new version or none |
| Calculation (`POST /{id}/calculate`) | saved carrier by carrier | one transaction: every carrier's new result or none |
| External submission (`POST /api/external/evaluation/submit`) | response and "submitted" first, answers in a second save — a failure between them left a link marked submitted with no answers, which its evaluator could never send again | response, answers and the link's state in one transaction; on failure the link stays open and the evaluator is told to send again |

All three run inside the API's execution strategy (`EnableRetryOnFailure`). A retry starts from a clean change tracker. A failed attempt leaves nothing tracked for the audit row's save to write by accident (`Services/EvaluationWrites.cs`). A duplicate-key clash between two requests at the same moment answers 409, not 500.

**Logging (§43)**, through the API's existing `ILogger` (App Service logs):

| Event | Level | Holds |
|---|---|---|
| Snapshot / calculation failed | Error | campaign code, carrier count, user, exception |
| Snapshot / calculation met a concurrent one | Warning | campaign code, user |
| Snapshot / calculation done | Information | campaign code, count, user, elapsed ms |
| External submission refused (unknown, revoked, expired, closed, already sent, invalid answers) | Warning | status, the link's **row id** (or "unknown"), the reason |
| External submission failed | Error | link row id, campaign code, exception |
| External request over the rate limit | Warning | path only |
| AI summary not made (other than not permitted / switched off) | Warning | campaign id, carrier row, user, code |
| 2025 workbook unreadable | Warning | file name, user |

No log line holds an evaluation token or its hash. A check asserts this over every line the services write.

No email and no OTP exist, by decision of 1 Oct 2026: SCMOS sends no email. An admin copies each link and sends it. Spec §24's OTP and §43's "email failures / OTP failures" have nothing to log.

## Already in place (verified, not changed)

- **Indexes (§42).** Campaign code (unique) and year. Carrier: campaign+supplier (unique) and supplier. Token hash (unique). Invitation campaign+status and evaluator+carrier. Response by invitation (unique) and by carrier+department. Answer by response+question (unique). Department code (unique). Snapshot and result by carrier+version (unique). Every evaluation query filters on the leading column of one of these. No loop issues a query per row.
- **Dashboards read stored figures.** The board and results read saved snapshots and results; nothing recalculates history on load.
- **Authorization (§38)**, mapped into the existing capability framework:

  | Spec permission | SCMOS capability | Roles |
  |---|---|---|
  | View | `ViewAnnualEvaluation` | Operation and above |
  | Manage, Configure | `ManageAnnualEvaluation` | Supervisor and above |
  | Review | `ManageAnnualEvaluation` or `DecideAnnualEvaluation` | a supervisor records the decisions while under review |
  | Approve, Finalize | `DecideAnnualEvaluation` | Assistant Manager and above (Assistant Manager, Manager, Administrator) |

  Decided by the user on 4 Oct 2026: a supervisor may record decisions, and the approval is the Assistant Manager's or above.
  "Suspend new allocation" and "inactive" are listed for whoever finalizes, never applied to the supplier's status.

  Carrier (Subcontractor) accounts reach none of it.
- **Privacy (§39).** The external page receives a whitelisted DTO (`ExternalView`): no ids, no other carrier, no rates, no notes, no records. The AI summary's facts carry no evaluator name and no internal pricing note.
- **Tokens (§23).** 256-bit random tokens. Only the SHA-256 is stored. Expiry, revocation, one submission and a per-link rate limit. No sequential identifier on any external route.
- **Migrations (§41).** EF migrations only. Each new table has its `Down()`. Nothing existing is renamed or dropped.

## Test coverage (§40)

`tests/Scmos.Ai.Checks` (pure checks run in CI; SQL checks against LocalDB with `--write-local-db --local-db`):

| Spec item | Where |
|---|---|
| System / human weight validation, KPI boundaries, N/A, department weighting, multiple evaluators, final score, rounding, insufficient data | `EvaluationScoringChecks`, `EvaluationReviewChecks`, `AnnualEvaluationChecks` |
| Snapshot immutability, campaign locking | `AnnualEvaluationChecks` |
| Token expiry, revocation, wrong carrier, duplicate submission | `EvaluationInvitationChecks`; concurrent duplicate in `EvaluationHardeningChecks` |
| OTP expiry / attempt limit | not applicable — no OTP (decision of 1 Oct 2026) |
| Role authorization | rules in `AnnualEvaluationChecks`; services in `EvaluationHardeningChecks` |
| Historical records | `LegacyEvaluationChecks`; the old screen's rows kept in `EvaluationReviewChecks` |
| Critical E2E flow (create → carriers → snapshot → evaluator → link → open → view → submit → aggregate → calculate → review → finalize) | `EvaluationHardeningChecks` through the services, with the audit trail of each step (§35) |
| Writes that fail part-way, logs without tokens | `EvaluationHardeningChecks` |
| AI summary held to its evidence | `EvaluationSummaryChecks` |

The project has no browser E2E harness. The flow runs through the same services the endpoints call; the routes, the web proxy and the external page were tested live on a local stack.

## UAT — สำหรับทีม Subcontract Management (หลัง deploy)

ทำบนแคมเปญทดลองก่อน (เช่นปี 2027) แล้ว **เก็บถาวร** เมื่อเสร็จ — อย่าใช้แคมเปญจริงทดสอบ

1. **สร้างแคมเปญ** (Supervisor ขึ้นไป) → ตั้งช่วงคะแนนและเกณฑ์ → เพิ่มผู้ขนส่ง → **เตรียมข้อมูล** → สร้าง snapshot — ผู้ขนส่งทุกรายมี snapshot ครั้งที่ 1
2. **ผู้ประเมิน** → เพิ่มผู้ประเมิน 1 คนต่อแผนก → สร้างลิงก์ → คัดลอกลิงก์ (แสดงครั้งเดียว)
3. **เปิดรับการประเมิน** → เปิดลิงก์ในหน้าต่างไม่ระบุตัวตน — เห็นเฉพาะผู้ขนส่งของลิงก์นั้น ไม่มีราคา/หมายเหตุภายใน
4. ส่งแบบประเมิน (ให้คะแนน 1–2 ต้องมีเหตุผล) → ส่งซ้ำ — ต้องขึ้นว่า "ส่งได้ครั้งเดียว"
5. ยกเลิกลิงก์หนึ่งลิงก์ → เปิดลิงก์นั้น — ต้องขึ้นว่า "ถูกยกเลิกแล้ว"
6. **คำนวณคะแนน** → ผลการประเมิน — คะแนน System / Department / รวม ตรงกับที่คิดด้วยมือ 1 ราย
7. ผู้ขนส่ง 1 ราย → **สรุปด้วย AI** (เมื่อเปิด `AI__EvaluationAiEnabled`) — ทุกบรรทัดมี [F#] และกดดูข้อเท็จจริงได้
8. ปิดรับ → **การพิจารณา** → ตัดสินทุกราย (ทุกอย่างยกเว้น "ใช้งานต่อ" ต้องมีเหตุผล) → Assistant Manager ขึ้นไปอนุมัติ (Supervisor บันทึกการตัดสินได้ แต่อนุมัติไม่ได้) → สรุปผล — ผลเข้าทะเบียนผู้ขนส่ง (Supplier Register → ประวัติการประเมิน); ผู้ขนส่งที่ตัดสิน "ระงับการจ่ายงานใหม่" หรือ "เลิกใช้งาน" ต้องยังมีสถานะเดิมในทะเบียน — เปลี่ยนเองที่ Supplier Register ถ้าต้องการ
9. บัญชี Operation: เห็นผลได้ แต่ปุ่มสร้าง/คำนวณ/ตัดสินต้องไม่ทำงาน
10. **ประวัติการประเมิน** → นำเข้า `Subcontractor Evaluation.xlsx` ปี 2025 และตาราง ISO & Q-Mark — ตรวจตัวอย่างก่อนกดนำเข้า; รายที่จับคู่ไม่ได้ต้องเลือกเอง

## Settings the owner turns on (Azure Portal → API → Environment variables)

- `AI__EvaluationAiEnabled = true` — the AI summary button (needs `AI__Enabled = true`, already on). Off by default.
