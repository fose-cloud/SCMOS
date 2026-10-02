import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { INVITATION_STATE, bangkokTime, evaluationLink } from "../app/scmos/annualEvaluation.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("an evaluation link carries its token in the fragment, which no server is sent", () => {
  const token = "A".repeat(43);
  assert.equal(evaluationLink("https://scmos.example", token), `https://scmos.example/evaluation#${token}`);
  assert.equal(evaluationLink("https://scmos.example/", token), `https://scmos.example/evaluation#${token}`);
  assert.doesNotMatch(evaluationLink("https://scmos.example", token), /[?&]token=/);
});

test("the page outside SCMOS reads its token from the fragment and sends it only in a header", () => {
  const page = read("app/evaluation/page.tsx");
  assert.match(page, /window\.location\.hash/);
  assert.match(page, /addEventListener\("hashchange"/);
  assert.match(page, /"x-evaluation-token": fromLink/);
  assert.match(page, /"x-evaluation-token": token/);
  assert.match(page, /fetch\("\/api\/external\/evaluation"/);
  assert.match(page, /fetch\("\/api\/external\/evaluation\/submit"/);
  // No sign-in, no SCMOS shell, no API helper that would attach a session.
  assert.doesNotMatch(page, /apiFetch|SCMOSApp|getUser/);
  assert.doesNotMatch(page, /searchParams|[?&]token=/);
  const layout = read("app/evaluation/layout.tsx");
  assert.match(layout, /robots: \{ index: false, follow: false \}/);
  assert.match(layout, /referrer: "no-referrer"/);
});

test("the web proxy passes the evaluation token on, and the API reads the same header", () => {
  const proxy = read("app/api/[...path]/route.ts");
  const names = proxy.match(/const REQUEST_HEADERS = \[([^\]]+)\]/)[1];
  assert.match(names, /"x-evaluation-token"/);
  const endpoints = read("server/Scmos.Api/Endpoints/ExternalEvaluationEndpoints.cs");
  assert.match(endpoints, /TokenHeader = "X-Evaluation-Token"/);
  assert.match(endpoints, /RequireRateLimiting\(RateLimitPolicy\)/);
  assert.match(endpoints, /CacheControl = "no-store"/);
});

test("every link state the API reports has a label", () => {
  const rules = read("server/Scmos.Api/Rules/AnnualEvaluationRules.cs");
  const states = [...rules.matchAll(/public const string Invitation\w+ = "([a-z-]+)";/g)].map((match) => match[1]);
  const helper = read("server/Scmos.Api/Rules/EvaluationInvitations.cs");
  states.push(helper.match(/public const string Expired = "([a-z-]+)";/)[1]);
  assert.ok(states.length >= 6);
  for (const state of states) assert.ok(INVITATION_STATE[state], state);
});

test("times are shown in Bangkok", () => {
  assert.equal(bangkokTime("2026-10-01T16:59:59Z"), "2026-10-01 23:59");
  assert.equal(bangkokTime("2026-10-01T17:00:00Z"), "2026-10-02 00:00");
  assert.equal(bangkokTime(null), "—");
});

test("the admin screen makes, lists and changes links only through the campaign's routes", () => {
  const screen = read("app/scmos/screens/EvaluationEvaluators.tsx");
  assert.match(screen, /\/api\/annual-evaluations\/\$\{campaignId\}\/evaluators/);
  for (const route of ['send("/evaluators"', 'send("/invitations"', "/sent`", "/extend`", "/renew`", "/revoke`"]) assert.ok(screen.includes(route), route);
  assert.match(screen, /evaluationLink\(origin, one\.token\)/);
  // A link is kept on screen only until dismissed; nothing stores it.
  assert.doesNotMatch(screen, /localStorage|sessionStorage/);
  const campaign = read("app/scmos/screens/AnnualEvaluation.tsx");
  assert.match(campaign, /tab === "evaluators" && <EvaluationEvaluators/);
  // New links are held by the campaign, not the tab, and leaving with some on screen asks first.
  assert.match(campaign, /issued=\{issued\} setIssued=\{setIssued\}/);
  assert.match(campaign, /<Back onBack=\{leave\} \/>/);
  assert.doesNotMatch(screen, /useState<IssuedLink\[\]>/);
});
