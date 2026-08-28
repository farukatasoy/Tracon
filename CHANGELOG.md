# Changelog

All notable changes to AgentPrism are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.0-preview.1] - 2026-08-28

### Added

- Initial public preview release of AgentPrism, a production-grade agent
  control plane for the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/).
- Model provider adapters for OpenAI (Chat Completions and Responses),
  Anthropic (Claude), Google (Gemini), and Azure OpenAI, plus an
  `IModelProvider` extension point for any other provider — with per-tenant
  bring-your-own-key (BYOK) support and per-tenant egress policy.
- Persistence backends for PostgreSQL, SQL Server, and SQLite, each with
  embedded migrations; the runtime (`AgentPrism.Core`) needs no database.
- An embedded React console — 30 screens across 36 routes (Dashboard,
  Agents, Skills, Playground, Sessions, Runs, Workflows, Jobs, Evals,
  Experiments, Approvals, Tools, Models, MCP, Triggers, Audit, Diagnostics,
  Settings) — with zero JavaScript dependency in the consuming project.
- A management HTTP API alongside OpenAI-compatible endpoints
  (`/v1/responses`, `/v1/chat/completions`, `/v1/conversations`), multi-tenant
  by default.
- Run recording with spans, metrics, and cost; a tamper-evident audit trail
  with a verifiable hash chain; data subject export and erasure.
- Extension points for custom tools, model providers, run stores, run
  judges, agent sources, and scheduled job handlers, each with a published
  behavior-contract test suite (`AgentPrism.Testing.Contracts.Xunit`) so a
  third-party implementation can be verified against the same tests
  AgentPrism's own implementations run.
- Workflow execution with five orchestration patterns, checkpoints, resume,
  and human-in-the-loop approval.
- MCP tool discovery from remote servers, and agent exposure over MCP and
  A2A.
- A typed management client generated from the OpenAPI document, published
  both as a NuGet package (`AgentPrism.Client`) and an npm package
  (`@agentprism/client`).
- A `dotnet new agentprism-api` project template and an `agentprism` global
  CLI tool (`migrate`, `migrate status`, `health`).
- Native AOT and trimming compatibility for `AgentPrism.Abstractions`,
  `AgentPrism.Core`, `AgentPrism.PostgreSql`, `AgentPrism.OpenAI`,
  `AgentPrism.Anthropic`, `AgentPrism.Google`, `AgentPrism.Azure`, and
  `AgentPrism.Voice`.

[Unreleased]: https://github.com/farukatasoy/AgentPrism/compare/v1.0.0-preview.1...HEAD
[1.0.0-preview.1]: https://github.com/farukatasoy/AgentPrism/releases/tag/v1.0.0-preview.1
