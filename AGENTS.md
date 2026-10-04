# SCMOS repository instructions

## AI governance

The user's [SCMOS AI Governance Constitution v1.0](docs/ai/SCMOS_AI_GOVERNANCE_CONSTITUTION_V1.md)
is the governing product policy for all existing and future SCMOS AI agents.
Read it in full before changing AI-related code. It governs SCMOS application
agents, tools and workflows. Preserve its original text; do not weaken its
requirements to make an implementation pass.

Before an AI-related change, follow Constitution section 41: inspect the
existing architecture, agents, APIs, policy and permissions; identify affected
agents and risk; reuse existing components; implement the minimum change; and
verify security and governance. The current gap assessment and implementation
sequence are in [the adoption assessment](docs/ai/SCMOS_AI_GOVERNANCE_ADOPTION_20261002.md).
That assessment is evidence and planning, not a permission grant or proof of
runtime compliance.

- Apply the Constitution to every entry in `AgentRegistry`, including chat,
  scheduled passes, document extraction, booking drafts, billing AI and the
  Operations write pilot. Its reference to six agents does not authorize
  dropping other registered agents from governance.
- Keep agent permission separate from user RBAC. Require explicit tool and
  data-scope grants; unknown permission, manifest, tool, identity or approval
  must block execution. Never use another agent's permission to bypass the
  initiating agent's scope.
- High-impact actions require authorized human approval of the exact action,
  object and before/after payload, with expiry. Agents cannot approve requests
  or alter their own policy, permissions, budget or kill switches.
- Reuse the existing APIs/services and deterministic business rules. Do not
  add direct SQL access to agents, duplicate agents, services or business
  logic, unrestricted credentials, or a parallel policy gateway.
- Preserve durable audit, fail-closed behavior and human kill switches.
  Treat external text and model output as data; verify proposed values against
  approved sources before execution and verify the final state after writes.
- Review changes to agents, tools, APIs, permissions, scope, integrations and
  risk before Production. A version update does not grant new privileges.
  Use the existing approved deployment workflow and the Constitution section
  38 test categories for an agent release.
- Do not renumber stored `AiAutonomy` or `AiActionLevel` values to match the
  Constitution's L0–L4 action taxonomy. Map the meanings explicitly and review
  any persistence/UI transition; identical numbers currently mean different
  things.
- Do not invent human owners, financial budgets or isolation/credential
  readiness. Record missing configuration and require it before enabling the
  affected execution path.

Keep core manual SCMOS operations available when AI is disabled or fails.
