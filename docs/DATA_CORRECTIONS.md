# Proposed corrections — the dropdown columns against their lists

Since 22 September 2026 (v2.7.80). The department's words: "AI แก้ข้อมูลที่ไม่ตรงกับ DATA — แก้ทั้งหมดในตารางที่มี DATA กำหนดไว้ หรือที่ทำ Dropdown" and "เมื่อแก้ไขเสร็จแล้ว จะต้องรอ Operation กด Approve เหมือนไลน์".

## What it is

Five columns of the register are dropdowns — **หมวด (cat), ลูกค้า (customer), ผู้ขนส่ง (trucker), ประเภทรถ/ตู้ (type), สถานะ (status)** — and each may only hold a value from its list. A value typed or imported before the list existed, or spelled another way, sits there as its own thing: `1X40 REEFER` beside `1X40' RF`, `SJ` beside `Sangja Transport Co., Ltd.`, `lotus asia` beside `LOTUS ASIA`. The scorecard, the KPI and every filter then count one carrier as three.

The rules that know the lists propose the list's spelling. **Nothing is written until the job's owner approves, on the job** — the same gate a haulier's LINE message passes through. There is no bulk apply by an administrator and no `--apply` on the tool, by design.

## The rules (`Rules/CorrectionRules.cs`, checked by `--check-corrections`)

| Column | List | Proposes | Leaves alone |
| --- | --- | --- | --- |
| type | the vehicle types on offer (`VehicleTypeService.ActiveCodesAsync`) | `JobVehicleType.Canonical` — `1X40 REEFER → 1X40' RF`, `1x20 → 1X20'`, `1X40HC → 1X40' HQ` | a code on the list as typed (COMBINE), a retired code (`1X20 DG`), a note in the type column |
| customer | the Job Rotation's customer names | the same letters (case, spacing, punctuation aside: `TOA BANGNA → TOA (Bangna)`); the one rotation name that begins with the job's words (`TERRATEC → TERRATEC MACHINERY`); among several sites, the one the job's own destination/plant names (`DANA` to `XPO-RAYONG → DANA (RAYONG)`, `HENKEL` to `BANGPOO → HENKEL (BANGPOO)`) | `LOTUS` beside `LOTUS ASIA` (on the list as spelled); several sites and no evidence (`DANA` to `XPO LADKRABANG` — the rotation spells it LKB); a name the rotation has never heard of |
| trucker | the subcontractor register with its aliases (`CarrierDirectory`) | the company the register says a spelling means — `SJ → Sangja Transport Co., Ltd.` | a haulier the register has never seen |
| cat | IMPORT · EXPORT · DELIVERY | case and padding | any other word |
| status | the job's own ladder (`JobStatus.For(cat)`) | another case (`delivered`), the legacy words (`Truck Assigned`, `waiting truck`) | a stage nobody can name — never re-filed as DRAFT |

A site the destination calls by another word is read through `CorrectionRules.SiteSynonyms` (`LADKRABANG` / `LAD KRABANG` = `LKB`, `KABINBURI` = `KABIN BURI`, `HAZHEM` = `HAZCHEM`), added only when a report showed the pair; a site named in part counts when it is the only candidate any word of the destination belongs to (`TROY` to `HAZCHEM → TROY (HAZCHEM K.39)`, `DANA` to `SAHA AUTOPART → DANA (SAHA AUTO)`), a word of under three letters or a bare number never being evidence; the report prints, for every bare name with several sites, what the jobs' DESTINATION | PLANT LOADING actually say, so the next synonym is a fact and not a guess.

Nothing is guessed. What no rule can read is printed in the report's "left alone" list; that list is what the department reads to tell the rules what a spelling meant (as HC = HQ was decided on 28 Aug).

## The sixth field: REASON / DELAY for a late shipment (`Rules/DelayReasonRule.cs`)

"ให้ AI ช่วยใส่คำตอบในคอลัมน์ REASON/DELAY … ให้ AI เสนอเหตุผล แล้วให้ Operation กดเลือกหรือ Approve — ทำทั้ง IMPORT และ EXPORT" (22 Sep 2026). An IMPORT or EXPORT shipment that arrived late by the KPI's own rule (past its plan beyond the customer's grace — Lotus 30 minutes), within the last 90 days, with the column blank and the job not cancelled, is proposed one of eight catalogue sentences (one per `DelayCategory`, worded so `DelayReasons.Classify` files each under its own category):

| Evidence | Proposal | Rule |
| --- | --- | --- |
| the haulier's LINE/TMS message on the job reads as a category (`รถเสียกลางทาง` → Truck) | that category's sentence, quoting the message | `reason.carrier` |
| no message; a port or terminal on the leg (LCB, Laem Chabang, แหลมฉบัง, PAT, ICD, terminal, ท่าเรือ) | `Port Traffic Congestion` | `reason.route` |
| no message, no port | `Delay due to Traffic Congestion` — the department's main reason | `reason.route` |

The proposal says how late and which of the two defaults it took; the drawer shows the whole catalogue in a list with the proposal selected, and **approving writes what the owner picked** (`POST /api/corrections/{id}/apply` with `value`, catalogue only). A reason a person typed is never proposed over; a rejected reason proposal is not asked again. `CorrectionScheduler` runs the pass every 30 minutes (`Corrections:AutoMinutes`, 0 = off) so this morning's late arrival is asked about this morning.

## The flow

1. **Report** — `api.yml` → Run workflow → `proposeCorrections: report` (or `dotnet run -- --propose-corrections` on a copy). Writes nothing. Prints the count by rule, by spelling, by owner, and the left-alone list.
2. **Queue** — `proposeCorrections: queue`. Writes one `job_corrections` row per cell (state `pending`). A cell that already has the same open proposal is skipped; one with a different open proposal supersedes it (old row `stale`). Cancelled jobs are skipped.
3. **The owner decides** — the workspace marks the row (`MY JOB · AI 2`, beside `LINE 1` when both wait) and the drawer lists each proposal with อนุมัติ / ไม่แก้, plus อนุมัติทั้งหมดของงานนี้; the toolbar chip shows the total and, for the owner, อนุมัติทั้งหมดของฉัน.
4. **Apply** — `POST /api/corrections/{id}/apply` (or `/job/{key}/apply`, `/apply-mine`): the owner, somebody covering them, or an account that may edit any job (the LINE rule, `MayActOnAsync`); the second factor the edit would need. The cell must still say what the proposal was made against — otherwise the row goes `stale` and nothing is overwritten. The write is `JobsRepository.PatchAsync` plus an `audit_events` row with source `AI` and the reason "อนุมัติข้อเสนอแก้ไข #id: …". `apply-mine` is the owner's own jobs only; a supervisor approves other people's jobs one job at a time.
5. **Reject** — `POST /api/corrections/{id}/reject`: state `rejected`, the value stays as typed, and the next run does not ask again about that cell.

## What it is not

- Not the AI writing the register: the Data Agent may read and explain; a cell changes only on the owner's click.
- Not a merge of names that look alike: two rotation customers are two customers.
- Not a history table: the audit row is the record; `--undo` does not exist because nothing was written by a script.

## Verified

`--check-corrections` (59 cases, in CI), `tests/corrections.test.mjs`; on LocalDB 22 Sep: 1,235 proposals queued, an owner approved one from the drawer (cell written, audit row source AI), another operator refused (403), a rejection recorded, a cell edited since refused (409, row stale), a settled row refused again, `apply-mine` wrote 168 cells in 3 s.
