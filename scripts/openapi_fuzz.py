#!/usr/bin/env python3
"""Fuzzes the OpenAPI document of the reference host with Schemathesis.

The one entry point for both the scheduled workflow and a developer's machine: the same command
runs in both places, so a finding from the scheduled run can be replayed locally without
translating anything.

What a run does
    1. Builds `samples/Tracon.Api` (skipped with `--no-build`).
    2. Installs the pinned Schemathesis into a virtual environment under `artifacts/`.
    3. Starts the host on a free loopback port in the `Staging` environment (no user secrets),
       with only the `echo` model provider, the demonstration roles on, and the chosen store.
    4. Optionally seeds the host through its own HTTP API (`--seed-data`), so requests can get
       past "no such resource".
    5. Runs every leg (`--leg`) against `/openapi/v1.json`, one Schemathesis invocation per
       check family, and prints a summary table.
    6. Stops the host and any database container it started.

The exit code is 1 when any gating check found something and 2 when the run could not be set up.
A finding is never silenced by a flag here: what the run accepts is written in
`ACCEPTED_ANSWERS` below, one status per operation, and the summary prints how often each accepted
answer was seen.

Standard library only. Schemathesis itself lives in the virtual environment and is never a
dependency of a shipped package.
"""

from __future__ import annotations

import argparse
import base64
import dataclasses
import hashlib
import json
import os
import pathlib
import re
import secrets
import shutil
import signal
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import zlib
from collections.abc import Callable, Iterable, Mapping, Sequence
from typing import Any

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCRIPTS = ROOT / "scripts"
REQUIREMENTS = SCRIPTS / "openapi-fuzz-requirements.txt"
HOOKS = SCRIPTS / "openapi_fuzz_hooks.py"
SAMPLE_PROJECT = ROOT / "samples" / "Tracon.Api" / "Tracon.Api.csproj"
SAMPLE_OUTPUT = ROOT / "artifacts" / "bin" / "Tracon.Api" / "release"
DEFAULT_OUTPUT = ROOT / "artifacts" / "openapi-fuzz"

SCHEMATHESIS_VERSION = "4.32.7"
"""Must equal the version pinned in the requirements file; a test compares the two."""

DEFAULT_SEED = 20261101
DEFAULT_EXAMPLES = 100

API_PREFIX = "/tracon/api"
ROLE_HEADER = "X-Tracon-Demo-Role"
ROLES = ("admin", "operator", "reader", "anonymous")

# The checks that fail a leg. Each one has been driven to zero findings on the in-memory store, as the admin
# role, before it was listed here.
GATING_CHECKS = (
    "not_a_server_error",
    "unsupported_method",
)
# Measured and printed with a count on every run, never failing a leg. The OpenAPI document is a shipped claim, so
# a response that departs from it is a defect, but the findings below come from the shape of the document itself
# (a nullable member written as `oneOf` of null and a schema that also accepts null, a content type the stored
# file decides, error statuses the document does not list). Fixing them changes the published document and the
# clients generated from it, which is a decision for the maintainers, so the run reports them until it is taken.
REPORT_CHECKS = (
    "status_code_conformance",
    "content_type_conformance",
    "response_schema_conformance",
)

# The one kind of server status the run accepts, per operation: an answer that says "this host cannot do that" and is
# documented as such, from a capability the reference host does not register. Anything else from these operations
# (500, a different 5xx) and the same status from any other operation still fails the run. The summary prints how
# often each entry answered, so an entry that stops being exercised shows up as a zero instead of staying forever.
#
# Adding an entry is a decision about the HTTP contract, not a way to quiet a run. It lives here, in code, and not in
# the Schemathesis configuration file: the configuration applies only the first of several per-operation entries.
ACCEPTED_ANSWERS: Mapping[tuple[str, str], int] = {
    # No vector store is registered: documented as 501 in the OpenAPI document.
    ("GET", "/tracon/api/knowledge/{collection}/documents"): 501,
    ("POST", "/tracon/api/knowledge/{collection}/documents"): 501,
    ("DELETE", "/tracon/api/knowledge/{collection}/documents/{sourceId}"): 501,
    ("POST", "/tracon/api/knowledge/{collection}/search"): 501,
    # No speech provider is registered. voices and speak document the 501; health answers it without documenting it.
    ("GET", "/tracon/api/voice/voices"): 501,
    ("POST", "/tracon/api/voice/speak"): 501,
    ("GET", "/tracon/api/voice/health"): 501,
    # Branching copies rows, which only a SQL store can do: documented as 501.
    ("POST", "/tracon/api/sessions/{sessionId}/branch"): 501,
    # These four connect to the registered MCP server to answer, and the seed registers one at an address that never
    # resolves. "The upstream could not be reached" is the correct 502 (documented), and it does not echo the address.
    ("GET", "/tracon/api/mcp-servers/{name}/prompts"): 502,
    ("POST", "/tracon/api/mcp-servers/{name}/prompts/{prompt}"): 502,
    ("GET", "/tracon/api/mcp-servers/{name}/resources"): 502,
    ("GET", "/tracon/api/mcp-servers/{name}/resources/read"): 502,
}


def accepts_answer(method: str, template: str, status: int) -> bool:
    """Whether a server status from this operation is one the run accepts."""
    return ACCEPTED_ANSWERS.get((method.upper(), template)) == status


def accepted_answer_counts(stats: Mapping[str, Any]) -> dict[str, int]:
    """How often each listed operation gave its accepted answer, zero included.

    A zero is the point: the entry was not exercised in this run, either because the store or the seed makes the
    operation answer something else, or because the entry is stale.
    """
    responses: Mapping[str, Mapping[str, int]] = stats.get("responses", {})

    return {
        f"{method} {template} -> {status}": responses.get(f"{method} {template}", {}).get(str(status), 0)
        for (method, template), status in ACCEPTED_ANSWERS.items()
    }


# What a role may NOT do, read from the answers the leg collected. The roles are nested (reader inside operator inside
# admin), so a lower role answered with 2xx on something above it is an authorization gap that no 5xx would show.
# Only the two ends are checked here, where the expectation needs no list of which endpoint is admin-only:
#   * the reader reads: a 2xx on anything but GET is a gap, except the posts that only compute or search;
#   * the anonymous caller (no role header) is refused everywhere except the routes that are open on purpose.
READER_MAY_POST = (
    "POST /tracon/api/agents/{name}/estimate",  # prices a run without starting one
    "POST /tracon/api/knowledge/{collection}/search",  # reads and writes nothing
    "POST /tracon/a2a/",  # the external surface answers to its own scope and the loopback rule, not to a role
)
ANONYMOUS_MAY_REACH = (
    "GET /tracon/api/meta",  # the console learns the authentication method here, before it can ask for anything
    "GET /tracon/api/mcp-servers/{name}/oauth/callback",  # the provider's redirect carries no role
    "GET /tracon/a2a/",  # the agent card
    "POST /tracon/a2a/",  # see above: loopback is the local operator
)


def role_violations(role: str, stats: Mapping[str, Any]) -> list[str]:
    """Operations that answered 2xx to a role that must not get it, with the statuses seen."""
    responses: Mapping[str, Mapping[str, int]] = stats.get("responses", {})
    found: list[str] = []

    for operation, counts in sorted(responses.items()):
        if not any(code.startswith("2") for code in counts):
            continue

        method = operation.split(" ", 1)[0]
        allowed: tuple[str, ...]

        if role == "reader":
            if method == "GET":
                continue

            allowed = READER_MAY_POST
        elif role == "anonymous":
            allowed = ANONYMOUS_MAY_REACH
        else:
            continue

        if not any(operation.startswith(prefix) for prefix in allowed):
            succeeded = {code: count for code, count in counts.items() if code.startswith("2")}
            found.append(f"{operation} answered {succeeded} to the {role} leg")

    return found


def scope_violations(scopes: Sequence[str], stats: Mapping[str, Any]) -> list[str]:
    """Writes that answered 2xx to a key whose only scopes are read scopes.

    A key narrows the admin role, so a key with `RunsRead` alone must read and nothing else. The names of the read
    scopes end in `Read`; the same few posts that only compute or search are allowed as for the reader role.
    """
    if not scopes or not all(scope.endswith("Read") for scope in scopes):
        return []

    return [
        violation.replace("the reader leg", f"a key with only {'+'.join(scopes)}")
        for violation in role_violations("reader", stats)
    ]


STORES = ("memory", "sqlite", "postgres", "sqlserver")
POSTGRES_IMAGE = "pgvector/pgvector:pg18"
SQLSERVER_IMAGE = "mcr.microsoft.com/mssql/server:2022-latest"
CONTAINER_PREFIX = "tracon-fuzz-"
DATABASE_READY_SECONDS = 240
HOST_READY_SECONDS = 180
BUILD_PARALLELISM = 3

# Environment variables that would change what the host does. They are removed from the child's
# environment so a developer's own configuration cannot leak into a run.
SCRUBBED_PREFIXES = ("TRACON", "ASPNETCORE_", "DOTNET_ENVIRONMENT", "OTEL_")

DISPOSABLE_COUNT = 12
"""How many throwaway copies of each deletable resource the seed makes; DELETE draws only from them."""

INJECT_PERCENT = 60
BODY_PERCENT = 50

UNREACHABLE_BASE = "https://fuzz-target.invalid"
"""Where the seeded webhooks and MCP servers point. The `.invalid` top-level domain never resolves, so a webhook that
fires or an MCP server the host connects to sends nothing anywhere, and the host answers with its "upstream could not be
reached" error instead of talking to a real site."""

_NOT_SET = object()


class FuzzError(Exception):
    """The run could not be set up or could not continue."""


# --------------------------------------------------------------------------
# Legs
# --------------------------------------------------------------------------


@dataclasses.dataclass(frozen=True)
class Leg:
    """One identity the run presents to the host.

    `role` is the demonstration role header (`anonymous` sends none). `scopes` is empty for a leg
    without an API key; otherwise the leg carries a key with exactly those scopes next to the
    `admin` role, because a key narrows a role and never widens it.
    """

    role: str
    scopes: tuple[str, ...] = ()

    @property
    def name(self) -> str:
        if self.scopes:
            return "scope-" + "+".join(self.scopes)

        return "role-" + self.role


def parse_leg(text: str) -> Leg:
    """Parses `role:<name>` or `scope:<Name>[+<Name>...]`."""
    kind, _, value = text.partition(":")

    if kind == "role" and value in ROLES:
        return Leg(role=value)

    if kind == "scope" and value:
        names = tuple(part for part in value.split("+") if part)

        if names and all(re.fullmatch(r"[A-Za-z]+", name) for name in names):
            return Leg(role="admin", scopes=names)

    raise FuzzError(f"'{text}' is not a leg. Use role:{{{'|'.join(ROLES)}}}, scope:<Scope>[+<Scope>], roles:all or scopes:each.")


def expand_legs(specs: Sequence[str], scope_names: Sequence[str]) -> list[Leg]:
    """Expands `roles:all` and `scopes:each`; keeps order and drops duplicates."""
    legs: list[Leg] = []

    for spec in specs:
        if spec == "roles:all":
            expanded = [Leg(role=role) for role in ROLES]
        elif spec == "scopes:each":
            if not scope_names:
                raise FuzzError("'scopes:each' needs the ApiKeyScope values from the OpenAPI document, and it lists none.")

            expanded = [Leg(role="admin", scopes=(name,)) for name in scope_names]
        else:
            expanded = [parse_leg(spec)]

        for leg in expanded:
            if leg not in legs:
                legs.append(leg)

    return legs


def leg_headers(leg: Leg) -> list[str]:
    """The `-H` values of a leg. The API key is not among them; the hooks add it."""
    return [] if leg.role == "anonymous" else [f"{ROLE_HEADER}: {leg.role}"]


# --------------------------------------------------------------------------
# What the run saw
# --------------------------------------------------------------------------


class RunStats:
    """Per-operation response and check counts, written by the hooks and read by the summary."""

    def __init__(self) -> None:
        self.responses: dict[str, dict[str, int]] = {}
        self.checks: dict[str, dict[str, dict[str, int]]] = {}
        self.failure_statuses: dict[str, dict[str, int]] = {}

    def record_response(self, operation: str, status: int) -> None:
        counts = self.responses.setdefault(operation, {})
        key = str(status)
        counts[key] = counts.get(key, 0) + 1

    def record_check(self, check: str, operation: str, outcome: str, status: int | None = None) -> None:
        per_operation = self.checks.setdefault(check, {}).setdefault(operation, {})
        per_operation[outcome] = per_operation.get(outcome, 0) + 1

        if outcome == "failure" and status is not None:
            by_status = self.failure_statuses.setdefault(check, {})
            by_status[str(status)] = by_status.get(str(status), 0) + 1

    def to_json(self) -> dict[str, Any]:
        return {"responses": self.responses, "checks": self.checks, "failureStatuses": self.failure_statuses}


@dataclasses.dataclass(frozen=True)
class Coverage:
    """How far into the host the requests of a leg got."""

    operations: int
    requests: int
    only_not_found: tuple[str, ...]
    only_refused: tuple[str, ...]
    never_succeeded: tuple[str, ...]
    statuses: Mapping[str, int]
    status_501: Mapping[str, int]


def summarize_coverage(stats: Mapping[str, Any]) -> Coverage:
    """Folds the response counts of a leg into the numbers the summary prints.

    `only_not_found` holds operations that saw 404 and nothing else: a request that never found
    its resource tells nothing about the code behind it. `only_refused` holds operations that saw
    only 401 or 403 (the identity of the leg is not allowed there). `never_succeeded` holds those
    that never saw a 2xx.
    """
    responses: Mapping[str, Mapping[str, int]] = stats.get("responses", {})
    totals: dict[str, int] = {}
    not_found: list[str] = []
    refused: list[str] = []
    never: list[str] = []
    in_501: dict[str, int] = {}
    requests = 0

    for operation, counts in sorted(responses.items()):
        codes = set(counts)
        requests += sum(counts.values())

        for code, count in counts.items():
            totals[code] = totals.get(code, 0) + count

        if codes == {"404"}:
            not_found.append(operation)

        if codes and codes <= {"401", "403"}:
            refused.append(operation)

        if not any(code.startswith("2") for code in codes):
            never.append(operation)

        if "501" in counts:
            in_501[operation] = counts["501"]

    return Coverage(
        operations=len(responses),
        requests=requests,
        only_not_found=tuple(not_found),
        only_refused=tuple(refused),
        never_succeeded=tuple(never),
        statuses=dict(sorted(totals.items())),
        status_501=in_501,
    )


def check_failures(stats: Mapping[str, Any]) -> dict[str, int]:
    """Number of failed check evaluations per check name."""
    failed: dict[str, int] = {}

    for check, per_operation in stats.get("checks", {}).items():
        count = sum(outcomes.get("failure", 0) for outcomes in per_operation.values())

        if count:
            failed[check] = count

    return failed


_TEST_CASES = re.compile(r"(\d+) generated, (?:(\d+) found (\d+) unique failures?|(\d+) passed)")
_FAILURE_LINE = re.compile(r"^\s*❌\s+(ST\d+)\s+(.+?):\s+(\d+)\s*$", re.MULTILINE)
_ERROR_LINE = re.compile(r"^\s*🚫\s+(.+?):\s+(\d+)\s*$", re.MULTILINE)
_CURL_LINE = re.compile(r"^\s{4}(curl .+)$", re.MULTILINE)
_OPERATION_HEADER = re.compile(r"^_{3,} (.+?) _{3,}$", re.MULTILINE)


def parse_failure_groups(log: str) -> dict[str, int]:
    """Failure groups of the Schemathesis summary, for example `{"ST101 Server error": 3}`.

    Operations that could not be tested at all ("Runtime Error: 181") are listed too, under their own title: a run in
    which nothing was tested is a failed run, never a quiet one.
    """
    groups = {f"{code} {title}": int(count) for code, title, count in _FAILURE_LINE.findall(log)}

    for title, count in _ERROR_LINE.findall(log):
        if not title.startswith("Stateful"):
            groups[f"error: {title}"] = int(count)

    return groups


def parse_test_cases(log: str) -> int:
    """How many test cases Schemathesis says it generated, or 0."""
    match = _TEST_CASES.search(log)

    return int(match.group(1)) if match else 0


def extract_repro(log: str, base_url: str) -> list[str]:
    """The `curl` lines of the failures, with the ephemeral host replaced by `$HOST`.

    The port of a run is chosen per run, so a line that names it cannot be pasted a day later.
    `--serve` starts a host and prints the value to put in `HOST`.
    """
    lines: list[str] = []
    operation = ""

    for raw in log.splitlines():
        header = _OPERATION_HEADER.match(raw)

        if header:
            operation = header.group(1).strip()
            continue

        match = _CURL_LINE.match(raw)

        if match:
            line = match.group(1).replace(base_url.rstrip("/"), "$HOST")
            lines.append(f"# {operation}\n{line}" if operation else line)

    return lines


# --------------------------------------------------------------------------
# Seed data
# --------------------------------------------------------------------------

# Which pool each path parameter of a route draws from. The first rule that matches a route wins.
# A pool name is a kind of resource; the seed fills the pools and the hooks draw from them.
PATH_POOL_RULES: tuple[tuple[str, Mapping[str, str]], ...] = (
    (r"^/tracon/agui/\{agentName\}$", {"agentName": "agent"}),
    (r"^/tracon/api/agents/\{name\}/versions/\{a\}/diff/\{b\}$", {"name": "agent", "a": "version", "b": "version"}),
    (r"^/tracon/api/agents/\{name\}", {"name": "agent"}),
    (r"^/tracon/api/api-keys/\{id\}$", {"id": "api-key"}),
    (r"^/tracon/api/approvals/rules/\{ruleId\}$", {"ruleId": "approval-rule"}),
    (r"^/tracon/api/approvals/\{id\}", {"id": "approval"}),
    (r"^/tracon/api/attachments/\{id\}$", {"id": "attachment"}),
    (r"^/tracon/api/audit/\{entity\}$", {"entity": "audit-entity"}),
    (r"^/tracon/api/data-subjects/\{id\}", {"id": "data-subject"}),
    (r"^/tracon/api/evals/runs/\{id\}", {"id": "eval-run"}),
    (r"^/tracon/api/evals/\{name\}/cases/from-run/\{runId\}$", {"name": "eval-suite", "runId": "run"}),
    (r"^/tracon/api/evals/\{name\}", {"name": "eval-suite"}),
    (r"^/tracon/api/experiments/\{name\}", {"name": "experiment"}),
    (r"^/tracon/api/jobs/\{id\}", {"id": "job"}),
    (r"^/tracon/api/knowledge/\{collection\}", {"collection": "collection", "sourceId": "collection"}),
    (r"^/tracon/api/mcp-servers/\{name\}", {"name": "mcp-server", "prompt": "mcp-prompt"}),
    (r"^/tracon/api/models/health/\{provider\}$", {"provider": "provider"}),
    (r"^/tracon/api/quotas/\{id\}$", {"id": "quota"}),
    (r"^/tracon/api/retention/\{target\}$", {"target": "retention-target"}),
    (r"^/tracon/api/runs/\{a\}/compare/\{b\}$", {"a": "run", "b": "run"}),
    (r"^/tracon/api/runs/\{runId\}/feedback/\{scoreId\}$", {"runId": "run", "scoreId": "score"}),
    (r"^/tracon/api/runs/\{runId\}", {"runId": "run"}),
    (r"^/tracon/api/schedules/\{name\}", {"name": "schedule"}),
    (r"^/tracon/api/sessions/\{sessionId\}", {"sessionId": "session"}),
    (r"^/tracon/api/skill-script-grants/\{skillName\}$", {"skillName": "skill"}),
    (r"^/tracon/api/skills/\{name\}", {"name": "skill"}),
    (r"^/tracon/api/tenants/\{slug\}$", {"slug": "tenant"}),
    (r"^/tracon/api/tenants/\{tenantId\}/providers/\{provider\}$", {"tenantId": "tenant-id", "provider": "tenant-provider"}),
    (r"^/tracon/api/tenants/\{tenantId\}", {"tenantId": "tenant-id"}),
    (r"^/tracon/api/triggers/\{tenantId\}/\{name\}$", {"tenantId": "tenant-id", "name": "trigger"}),
    (r"^/tracon/api/triggers/\{name\}$", {"name": "trigger"}),
    (r"^/tracon/api/webhooks/\{name\}", {"name": "webhook"}),
    (r"^/tracon/api/workflows/runs/\{runId\}", {"runId": "workflow-run"}),
    (r"^/tracon/api/workflows/\{name\}/(run|graph)$", {"name": "workflow"}),
    (r"^/tracon/api/workflows/\{name\}/versions/\{a\}/diff/\{b\}$", {"name": "workflow-def", "a": "version", "b": "version"}),
    (r"^/tracon/api/workflows/\{name\}", {"name": "workflow-def"}),
    (r"^/tracon/v1/conversations/\{conversationId\}", {"conversationId": "conversation"}),
)

# A request body the host accepts, per route. `{name}` stands for the route's own `name` value, so a
# body that must repeat the name in the path does. Half of the requests to these routes carry the
# generated body, half this one: the generated half finds the crashes, this half reaches the code
# behind the validation.
_AGENT_BODY = {"name": "{name}", "instructions": "Answer briefly.", "model": {"provider": "echo", "model": "echo-1"}}
GOOD_BODIES: Mapping[tuple[str, str], Any] = {
    ("POST", "/tracon/api/agents"): {**_AGENT_BODY, "name": "fuzz-created-{n}"},
    ("PUT", "/tracon/api/agents/{name}"): _AGENT_BODY,
    ("POST", "/tracon/api/agents/validate"): {**_AGENT_BODY, "name": "fuzz-validated"},
    ("POST", "/tracon/agui/{agentName}"): {
        "threadId": "fuzz-thread",
        "runId": "fuzz-run",
        "messages": [{"id": "fuzz-message", "role": "user", "content": "hello"}],
        "tools": [],
        "context": [],
        "state": {},
        "forwardedProps": {},
    },
    ("POST", "/tracon/api/agents/{name}/run"): {"message": "hello"},
    ("POST", "/tracon/api/agents/{name}/estimate"): {"message": "hello"},
    ("POST", "/tracon/api/api-keys"): {"name": "fuzz-created-{n}", "scopes": ["RunsRead"]},
    ("PUT", "/tracon/api/evals/{name}"): {"agentName": "fuzz-agent", "checks": []},
    ("PUT", "/tracon/api/evals/{name}/cases"): [{"query": "hello", "expectedOutput": "hello"}],
    ("POST", "/tracon/api/evals/{name}/run"): {},
    ("PUT", "/tracon/api/experiments/{name}"): {
        "agentName": "fuzz-agent",
        "variants": [{"name": "a", "version": 1, "weight": 50}, {"name": "b", "version": 2, "weight": 50}],
    },
    ("PUT", "/tracon/api/mcp-servers/{name}"): {"endpoint": UNREACHABLE_BASE + "/mcp", "transport": "StreamableHttp", "enabled": True},
    ("PUT", "/tracon/api/quotas"): {"period": "Daily", "maxRuns": 1000, "enabled": True},
    ("PUT", "/tracon/api/retention/{target}"): {"maxAgeDays": 30, "archive": False, "enabled": False},
    ("POST", "/tracon/api/runs/{runId}/feedback"): {"kind": "Stars", "value": 4},
    ("PUT", "/tracon/api/skills/{name}"): {"name": "{name}", "description": "A skill.", "instructions": "Do the thing."},
    ("PUT", "/tracon/api/tenants/{slug}"): {"displayName": "Fuzz tenant"},
    ("PUT", "/tracon/api/workflows/{name}"): {"kind": "Sequential", "agentNames": ["fuzz-agent", "support"], "responseMode": "SingleAnswer"},
    ("PUT", "/tracon/api/tenants/{tenantId}/providers/{provider}"): {"apiKeyConfigurationName": "Tracon:ProviderKeys:fuzz"},
    ("PUT", "/tracon/api/triggers/{name}"): {
        "targetKind": "Agent",
        "targetName": "fuzz-agent",
        "signingSecretConfigurationName": "Tracon:TriggerSecrets:fuzz",
        "payloadMode": "WholeBody",
        "enabled": True,
    },
    ("PUT", "/tracon/api/webhooks/{name}"): {"url": UNREACHABLE_BASE + "/hook", "events": ["run.completed"], "enabled": True},
    ("POST", "/tracon/api/approvals/rules"): {"toolName": "cancel_order"},
    ("POST", "/tracon/v1/chat/completions"): {"model": "fuzz-agent", "messages": [{"role": "user", "content": "hello"}]},
    ("POST", "/tracon/v1/responses"): {"model": "fuzz-agent", "input": "hello"},
}


def pool_kinds_for(template: str) -> Mapping[str, str]:
    """The pool each path parameter of `template` draws from, or an empty mapping."""
    for pattern, kinds in PATH_POOL_RULES:
        if re.match(pattern, template):
            return kinds

    return {}


def stable_hash(value: Any) -> int:
    """A hash of `value` that is the same in every process, unlike `hash()` on strings."""
    return zlib.crc32(json.dumps(value, sort_keys=True, default=str).encode("utf-8"))


@dataclasses.dataclass
class SeedPlan:
    """What the seeded host holds, by kind of resource.

    `stable` values are read, updated and started again and again; `disposable` ones exist to be
    deleted. A DELETE draws only from `disposable`, so the first delete of a run cannot remove the
    resource every later request depends on.
    """

    stable: dict[str, list[str]] = dataclasses.field(default_factory=dict)
    disposable: dict[str, list[str]] = dataclasses.field(default_factory=dict)
    steps: list[dict[str, Any]] = dataclasses.field(default_factory=list)

    def add(self, kind: str, *values: str, disposable: bool = False) -> None:
        target = self.disposable if disposable else self.stable
        bucket = target.setdefault(kind, [])

        for value in values:
            if value and value not in bucket:
                bucket.append(value)

    def to_json(self) -> dict[str, Any]:
        return {
            "stable": self.stable,
            "disposable": self.disposable,
            "steps": self.steps,
            "injectPercent": INJECT_PERCENT,
            "bodyPercent": BODY_PERCENT,
        }


def save_seed(plan: SeedPlan, path: pathlib.Path) -> None:
    path.write_text(json.dumps(plan.to_json(), indent=1, sort_keys=True), encoding="utf-8")


def load_seed(path: pathlib.Path) -> SeedPlan:
    data = json.loads(path.read_text(encoding="utf-8"))

    return SeedPlan(stable=data.get("stable", {}), disposable=data.get("disposable", {}), steps=data.get("steps", []))


@dataclasses.dataclass(frozen=True)
class SeedChoice:
    """What the hooks put into a generated case. A `None` path means "leave it"."""

    path_parameters: dict[str, Any] | None
    has_body: bool
    body: Any = None


def choose_seed(
    plan: SeedPlan,
    *,
    method: str,
    template: str,
    path_parameters: Mapping[str, Any] | None,
) -> SeedChoice:
    """Decides, from the case alone, whether to swap in seeded values.

    The decision is a function of the generated case, never of a random source, so a failing
    case that Schemathesis shrinks or replays is rebuilt the same way.
    """
    verb = method.upper()
    digest = stable_hash([verb, template, path_parameters or {}])
    kinds = pool_kinds_for(template)
    merged: dict[str, Any] | None = None

    if kinds and path_parameters is not None and digest % 100 < INJECT_PERCENT:
        candidate = dict(path_parameters)
        changed = False

        for position, (parameter, kind) in enumerate(sorted(kinds.items())):
            pool = plan.disposable.get(kind, []) if verb == "DELETE" else plan.stable.get(kind, [])

            if pool and parameter in candidate:
                candidate[parameter] = pool[(digest >> (position * 4 + 3)) % len(pool)]
                changed = True

        merged = candidate if changed else None

    template_body = GOOD_BODIES.get((verb, template))

    if template_body is None or (digest >> 11) % 100 >= BODY_PERCENT:
        return SeedChoice(path_parameters=merged, has_body=False)

    values = merged if merged is not None else dict(path_parameters or {})

    return SeedChoice(path_parameters=merged, has_body=True, body=_render_body(template_body, values, digest))


def _render_body(template: Any, values: Mapping[str, Any], digest: int) -> Any:
    """Replaces `{name}` and `{n}` markers in the strings of a body template."""
    if isinstance(template, str):
        text = template.replace("{n}", f"{digest:08x}")

        for key, value in values.items():
            text = text.replace("{" + key + "}", str(value))

        return text

    if isinstance(template, list):
        return [_render_body(item, values, digest) for item in template]

    if isinstance(template, dict):
        return {key: _render_body(item, values, digest) for key, item in template.items()}

    return template


class Seeder:
    """Fills the host through its own HTTP API. A step that fails is recorded and skipped."""

    def __init__(self, base_url: str, headers: Mapping[str, str]) -> None:
        self.base = base_url.rstrip("/")
        self.headers = dict(headers)
        self.plan = SeedPlan()
        self.token = uuid.uuid4().hex[:8]

    def call(
        self,
        method: str,
        path: str,
        body: Any = _NOT_SET,
        *,
        headers: Mapping[str, str] | None = None,
        content: bytes | None = None,
        content_type: str | None = None,
        timeout: float = 60,
    ) -> tuple[int, Any]:
        data = content
        merged = dict(self.headers)

        if body is not _NOT_SET:
            data = json.dumps(body).encode("utf-8")
            merged["Content-Type"] = "application/json"

        if content_type:
            merged["Content-Type"] = content_type

        merged.update(headers or {})
        request = urllib.request.Request(self.base + path, data=data, method=method, headers=merged)

        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                raw = response.read()
                status = response.status
        except urllib.error.HTTPError as error:
            raw = error.read()
            status = error.code

        try:
            return status, json.loads(raw) if raw else None
        except ValueError:
            return status, raw.decode("utf-8", "replace")

    def step(self, name: str, action: Callable[[], str]) -> bool:
        try:
            detail = action()
            self.plan.steps.append({"step": name, "ok": True, "detail": detail})

            return True
        except Exception as error:  # noqa: BLE001 - a seed step must never end the run
            self.plan.steps.append({"step": name, "ok": False, "detail": f"{type(error).__name__}: {error}"})

            return False

    def _expect(self, label: str, result: tuple[int, Any], ok: Iterable[int] = (200, 201, 202, 204)) -> Any:
        status, payload = result

        if status not in tuple(ok):
            snippet = json.dumps(payload)[:160] if not isinstance(payload, str) else payload[:160]
            raise FuzzError(f"{label} answered {status}: {snippet}")

        return payload

    def named(self, prefix: str, index: int | None = None) -> str:
        return f"{prefix}-{self.token}" if index is None else f"{prefix}-{self.token}-{index}"

    def _put_each(self, kind: str, route: str, builder: Callable[[str], Any], prefix: str) -> None:
        """Creates one stable and `DISPOSABLE_COUNT` disposable resources with PUT."""
        stable = self.named(prefix)
        self._expect(kind, self.call("PUT", f"{API_PREFIX}/{route}/{stable}", builder(stable)))
        self.plan.add(kind, stable)

        for index in range(DISPOSABLE_COUNT):
            name = self.named(prefix, index)
            self._expect(kind, self.call("PUT", f"{API_PREFIX}/{route}/{name}", builder(name)))
            self.plan.add(kind, name, disposable=True)

    def run(self) -> SeedPlan:
        agent = "fuzz-agent"
        echo = {"provider": "echo", "model": "echo-1"}

        def definition(name: str, instructions: str = "Answer briefly.") -> dict[str, Any]:
            return {"name": name, "instructions": instructions, "model": echo}

        def agents() -> str:
            status, payload = self.call("GET", f"{API_PREFIX}/agents")
            code = [item["name"] for item in payload if item.get("origin") == "Code"] if status == 200 else []

            self.call("DELETE", f"{API_PREFIX}/agents/{agent}")
            self._expect("agent", self.call("POST", f"{API_PREFIX}/agents", definition(agent)))
            self._expect("agent", self.call("PUT", f"{API_PREFIX}/agents/{agent}", definition(agent, "Answer very briefly.")))
            self.plan.add("agent", agent, *[name for name in ("support", "summarizer", "router") if name in code])
            self.plan.add("version", "1", "2")

            for index in range(DISPOSABLE_COUNT):
                name = self.named("fuzz-del-agent", index)
                self._expect("agent", self.call("POST", f"{API_PREFIX}/agents", definition(name)))
                self.plan.add("agent", name, disposable=True)

            return f"{agent} v2 + {DISPOSABLE_COUNT} disposable, {len(code)} code-defined agents seen"

        self.step("agents", agents)

        def runs() -> str:
            made = []

            for index in range(2):
                session = self.named("fuzz-session", index)
                payload = self._expect(
                    "agent run",
                    self.call(
                        "POST",
                        f"{API_PREFIX}/agents/{agent}/run",
                        {"message": f"hello {index}", "sessionId": session},
                        headers={"Idempotency-Key": f"{self.token}-run-{index}"},
                    ),
                )
                made.append(payload["runId"])
                self.plan.add("run", payload["runId"])
                self.plan.add("session", session)

            for index in range(DISPOSABLE_COUNT):
                session = self.named("fuzz-del-session", index)
                payload = self._expect(
                    "agent run",
                    self.call(
                        "POST",
                        f"{API_PREFIX}/agents/{agent}/run",
                        {"message": "bye", "sessionId": session},
                        headers={"Idempotency-Key": f"{self.token}-del-{index}"},
                    ),
                )
                self.plan.add("session", session, disposable=True)
                self.plan.add("run", payload["runId"], disposable=True)

            return f"{len(made)} runs, {1 + DISPOSABLE_COUNT} sessions"

        self.step("runs", runs)

        def feedback() -> str:
            run_id = self.plan.stable["run"][0]
            payload = self._expect("feedback", self.call("POST", f"{API_PREFIX}/runs/{run_id}/feedback", {"kind": "Stars", "value": 4}))
            identifier = payload.get("id") if isinstance(payload, dict) else None
            self.plan.add("score", str(identifier))

            return f"score {identifier}"

        self.step("feedback", feedback)

        def api_keys() -> str:
            for index in range(DISPOSABLE_COUNT + 1):
                payload = self._expect("api key", self.call("POST", f"{API_PREFIX}/api-keys", {"name": self.named("fuzz-key", index), "scopes": ["RunsRead"]}))
                self.plan.add("api-key", payload["record"]["id"], disposable=index > 0)

            return f"{DISPOSABLE_COUNT + 1} keys"

        self.step("api keys", api_keys)

        def workflow_definitions() -> str:
            # A workflow defined in code has no editable definition (GET, DELETE, versions answer 404 for it), so the
            # routes that read or change a definition draw from definitions saved here.
            body = {"kind": "Sequential", "agentNames": [agent, "support"], "responseMode": "SingleAnswer"}
            reversed_body = {**body, "agentNames": ["support", agent]}
            stable = self.named("fuzz-wf")
            self._expect("workflow", self.call("PUT", f"{API_PREFIX}/workflows/{stable}", body))
            self._expect("workflow", self.call("PUT", f"{API_PREFIX}/workflows/{stable}", reversed_body))
            self.plan.add("workflow-def", stable)

            for index in range(DISPOSABLE_COUNT):
                name = self.named("fuzz-del-wf", index)
                self._expect("workflow", self.call("PUT", f"{API_PREFIX}/workflows/{name}", body))
                self.plan.add("workflow-def", name, disposable=True)

            return f"{stable} v2 + {DISPOSABLE_COUNT} disposable"

        self.step("workflow definitions", workflow_definitions)
        self.step("webhooks", lambda: self._put_each("webhook", "webhooks", lambda _: {"url": UNREACHABLE_BASE + "/hook", "events": ["run.completed"], "enabled": True}, "fuzz-hook") or "webhooks")
        self.step(
            "triggers",
            lambda: self._put_each(
                "trigger",
                "triggers",
                lambda _: {"targetKind": "Agent", "targetName": agent, "signingSecretConfigurationName": "Tracon:TriggerSecrets:fuzz", "payloadMode": "WholeBody", "enabled": True},
                "fuzz-trigger",
            )
            or "triggers",
        )
        self.step("mcp servers", lambda: self._put_each("mcp-server", "mcp-servers", lambda _: {"endpoint": UNREACHABLE_BASE + "/mcp", "transport": "StreamableHttp", "enabled": True}, "fuzz-mcp") or "mcp servers")
        self.step("skills", lambda: self._put_each("skill", "skills", lambda name: {"name": name, "description": "A skill.", "instructions": "Do the thing."}, "fuzz-skill") or "skills")
        self.step(
            "experiments",
            lambda: self._put_each(
                "experiment",
                "experiments",
                lambda _: {"agentName": agent, "variants": [{"name": "a", "version": 1, "weight": 50}, {"name": "b", "version": 2, "weight": 50}]},
                "fuzz-exp",
            )
            or "experiments",
        )

        def evals() -> str:
            self._put_each("eval-suite", "evals", lambda _: {"agentName": agent, "checks": []}, "fuzz-suite")
            suite = self.plan.stable["eval-suite"][0]
            self._expect("eval cases", self.call("PUT", f"{API_PREFIX}/evals/{suite}/cases", [{"query": "hello", "expectedOutput": "hello"}]))
            payload = self._expect("eval run", self.call("POST", f"{API_PREFIX}/evals/{suite}/run", {}))
            self.plan.add("eval-run", payload["id"])
            self.plan.add("job", payload.get("jobId", ""))

            return f"suite {suite}, run {payload['id']}"

        self.step("evals", evals)

        def retention() -> str:
            status, payload = self.call("GET", f"{API_PREFIX}/retention/preview?target=jobs")
            targets = ["jobs", "traces", "run_events", "tool_invocations", "webhook_deliveries", "idempotency_keys"]

            for index, target in enumerate(targets):
                self._expect("retention", self.call("PUT", f"{API_PREFIX}/retention/{target}", {"maxAgeDays": 30, "archive": False, "enabled": False}))
                self.plan.add("retention-target", target, disposable=index > 0)

            return f"{len(targets)} targets (preview answered {status})"

        self.step("retention", retention)

        def tenants() -> str:
            self.plan.add("tenant-id", "default")

            for index in range(DISPOSABLE_COUNT):
                slug = self.named("fuzz-tenant", index)
                self._expect("tenant", self.call("PUT", f"{API_PREFIX}/tenants/{slug}", {"displayName": "Fuzz tenant"}))
                self.plan.add("tenant", slug, disposable=True)

            self._expect("tenant", self.call("PUT", f"{API_PREFIX}/tenants/{self.named('fuzz-tenant')}", {"displayName": "Fuzz tenant"}))
            self.plan.add("tenant", self.named("fuzz-tenant"))

            for index, provider in enumerate(["openai", "anthropic", "google", "azure-openai", "openrouter"]):
                self._expect(
                    "tenant provider",
                    self.call("PUT", f"{API_PREFIX}/tenants/default/providers/{provider}", {"apiKeyConfigurationName": "Tracon:ProviderKeys:fuzz"}),
                )
                self.plan.add("tenant-provider", provider, disposable=index > 0)

            return "tenants and providers"

        self.step("tenants", tenants)

        def quotas() -> str:
            payload = self._expect("quota", self.call("PUT", f"{API_PREFIX}/quotas", {"period": "Daily", "maxRuns": 100000, "enabled": True}))
            self.plan.add("quota", payload["id"])

            return f"quota {payload['id']}"

        self.step("quotas", quotas)

        def approval_rules() -> str:
            payload = self._expect("approval rule", self.call("POST", f"{API_PREFIX}/approvals/rules", {"toolName": "cancel_order"}))
            self.plan.add("approval-rule", payload["id"])

            return f"rule {payload['id']}"

        self.step("approval rules", approval_rules)

        def workflows() -> str:
            status, payload = self.call("GET", f"{API_PREFIX}/workflows")
            listed = payload if status == 200 else []
            names = [item["name"] for item in listed]
            self.plan.add("workflow", *names)
            started = 0

            # Runs are started on the workflows defined in code, with the one parameter the sample declares.
            for item in [item for item in listed if item.get("origin") == "Code"][:3]:
                name = item["name"]
                parameters = {"productionId": "fuzz"} if any(parameter["name"] == "productionId" for parameter in item.get("parameters", [])) else {}
                run_id = self._first_run_id(f"{API_PREFIX}/workflows/{name}/run", {"message": "go", "parameters": parameters})

                if run_id:
                    self.plan.add("workflow-run", run_id)
                    self.plan.add("run", run_id)
                    started += 1

            # A queued run of the human-review workflow stops at its review step and waits there: a pending request for
            # the routes that list, answer and resume it, and a run that is still alive for the ones that cancel it.
            if any(item["name"] == "review-and-revise" for item in listed):
                status, payload = self.call(
                    "POST",
                    f"{API_PREFIX}/workflows/review-and-revise/run",
                    {"message": "write the episode", "parameters": {"productionId": "fuzz"}},
                    headers={"Prefer": "respond-async"},
                )

                if status == 202 and isinstance(payload, dict) and payload.get("runId"):
                    self.plan.add("workflow-run", payload["runId"])
                    self.plan.add("run", payload["runId"])
                    started += 1

            return f"{len(names)} workflows, {started} runs started"

        self.step("workflows", workflows)

        def attachments() -> str:
            boundary = uuid.uuid4().hex
            png = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
            content = (
                f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"seed.png\"\r\nContent-Type: image/png\r\n\r\n".encode()
                + png
                + f"\r\n--{boundary}--\r\n".encode()
            )
            payload = self._expect(
                "attachment",
                self.call("POST", f"{API_PREFIX}/attachments", content=content, content_type=f"multipart/form-data; boundary={boundary}"),
            )
            self.plan.add("attachment", payload["id"])

            return f"attachment {payload['id']}"

        self.step("attachments", attachments)

        def conversations() -> str:
            payload = self._expect("conversation", self.call("POST", "/tracon/v1/conversations", {}))
            self.plan.add("conversation", payload["id"])

            return f"conversation {payload['id']}"

        self.step("conversations", conversations)

        self.plan.add("provider", "echo")
        self.plan.add("data-subject", "fuzz-user")
        self.plan.add("collection", "fuzz-docs")
        self.plan.add("audit-entity", "agent")

        return self.plan

    def _first_run_id(self, path: str, body: Any) -> str | None:
        """Starts a streaming run, reads its first frame for the run id and hangs up."""
        data = json.dumps(body).encode("utf-8")
        headers = {**self.headers, "Content-Type": "application/json", "Accept": "text/event-stream"}
        request = urllib.request.Request(self.base + path, data=data, method="POST", headers=headers)

        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                text = ""
                deadline = time.monotonic() + 15

                while time.monotonic() < deadline and "runId" not in text:
                    chunk = response.read(256)

                    if not chunk:
                        break

                    text += chunk.decode("utf-8", "replace")
        except (urllib.error.URLError, OSError):
            return None

        match = re.search(r'"runId"\s*:\s*"([0-9a-fA-F-]{36})"', text)

        return match.group(1) if match else None


# --------------------------------------------------------------------------
# Processes
# --------------------------------------------------------------------------


def run_command(command: Sequence[str], *, env: Mapping[str, str] | None = None, cwd: pathlib.Path | None = None, check: bool = True, quiet: bool = False) -> subprocess.CompletedProcess[str]:
    """Runs a command and raises `FuzzError` with its output when `check` is set and it fails."""
    result = subprocess.run(  # noqa: S603 - the commands are built here, from constants and checked inputs
        list(command),
        env=dict(env) if env is not None else None,
        cwd=cwd,
        capture_output=True,
        text=True,
        check=False,
    )

    if check and result.returncode != 0:
        tail = (result.stdout + result.stderr).strip()[-1500:]
        raise FuzzError(f"'{' '.join(command[:3])}...' exited with {result.returncode}:\n{tail}")

    if not quiet and result.stdout.strip():
        print(result.stdout.rstrip())

    return result


def scrubbed_environment(extra: Mapping[str, str] | None = None) -> dict[str, str]:
    """The current environment without anything that would change what the host does."""
    environment = {key: value for key, value in os.environ.items() if not key.upper().startswith(SCRUBBED_PREFIXES)}
    environment.update(extra or {})

    return environment


def venv_executable(venv: pathlib.Path, name: str) -> pathlib.Path:
    folder, suffix = ("Scripts", ".exe") if os.name == "nt" else ("bin", "")

    return venv / folder / f"{name}{suffix}"


def ensure_venv(venv: pathlib.Path) -> pathlib.Path:
    """Creates the virtual environment from the hash-pinned requirements, once per requirements file."""
    if sys.version_info < (3, 11):
        raise FuzzError(f"Schemathesis {SCHEMATHESIS_VERSION} needs Python 3.11 or later; this is {sys.version.split()[0]}.")

    wanted = hashlib.sha256(REQUIREMENTS.read_bytes()).hexdigest()
    stamp = venv / ".requirements-sha256"
    schemathesis = venv_executable(venv, "schemathesis")

    if schemathesis.exists() and stamp.exists() and stamp.read_text(encoding="utf-8").strip() == wanted:
        return schemathesis

    print(f"Installing Schemathesis {SCHEMATHESIS_VERSION} into {venv} ...")
    shutil.rmtree(venv, ignore_errors=True)
    run_command([sys.executable, "-m", "venv", str(venv)], quiet=True)
    python = venv_executable(venv, "python")
    run_command([str(python), "-m", "pip", "install", "--disable-pip-version-check", "--no-input", "--quiet", "--require-hashes", "-r", str(REQUIREMENTS)], quiet=True)
    stamp.write_text(wanted, encoding="utf-8")

    return schemathesis


def build_host(with_ui: bool) -> None:
    command = ["dotnet", "build", str(SAMPLE_PROJECT), "-c", "Release", f"-m:{BUILD_PARALLELISM}", "--nologo", "-v:q"]

    if not with_ui:
        # The console is not part of the HTTP surface the document describes, and building it needs Node.
        command.append("-p:TraconFrontendEnabled=false")

    print("Building the reference host ...")
    run_command(command, env=scrubbed_environment({"MSBUILDDISABLENODEREUSE": "1"}), quiet=True)


def free_port() -> int:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))

        return probe.getsockname()[1]


class Host:
    """The reference host as a child process."""

    def __init__(self, store_environment: Mapping[str, str], log_path: pathlib.Path) -> None:
        self.port = free_port()
        self.base_url = f"http://127.0.0.1:{self.port}"
        self.log_path = log_path
        self.environment = scrubbed_environment(
            {
                # Staging loads no user secrets, so a developer's real provider keys and shared
                # database never reach a run.
                "ASPNETCORE_ENVIRONMENT": "Staging",
                "Tracon__Demo__Roles__Enabled": "true",
                "Tracon__Ui__AuthToken": "",
                "Tracon__PostgreSql__ConnectionString": "",
                "Tracon__SqlServer__ConnectionString": "",
                "Tracon__Sqlite__ConnectionString": "",
                # Request logging at Information is most of a fuzz run's output and none of its findings: a crash is
                # logged at Error, which this keeps.
                "Logging__LogLevel__Default": "Warning",
                **store_environment,
            }
        )
        self.process: subprocess.Popen[bytes] | None = None

    def start(self) -> None:
        dll = SAMPLE_OUTPUT / "Tracon.Api.dll"

        if not dll.exists():
            raise FuzzError(f"{dll} does not exist; run without --no-build first.")

        log = self.log_path.open("wb")
        self.process = subprocess.Popen(  # noqa: S603
            ["dotnet", str(dll), "--contentRoot", str(SAMPLE_OUTPUT), "--urls", self.base_url],
            env=self.environment,
            stdout=log,
            stderr=subprocess.STDOUT,
            cwd=SAMPLE_OUTPUT,
        )
        deadline = time.monotonic() + HOST_READY_SECONDS

        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise FuzzError(f"The host exited with {self.process.returncode} before it was ready; see {self.log_path}.")

            if fetch_status(f"{self.base_url}/openapi/v1.json") == 200:
                return

            time.sleep(1)

        raise FuzzError(f"The host was not ready after {HOST_READY_SECONDS} s; see {self.log_path}.")

    def stop(self) -> None:
        process, self.process = self.process, None

        if process is None or process.poll() is not None:
            return

        process.terminate()

        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=15)


def fetch_status(url: str) -> int | None:
    try:
        with urllib.request.urlopen(url, timeout=5) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code
    except (urllib.error.URLError, OSError):
        return None


def fetch_json(url: str, headers: Mapping[str, str] | None = None) -> Any:
    request = urllib.request.Request(url, headers=dict(headers or {}))

    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read())


# --------------------------------------------------------------------------
# Stores
# --------------------------------------------------------------------------


def content_protection_key() -> dict[str, str]:
    """A throwaway key for the sample's content protection.

    The sample turns content protection on, and a SQL store then needs the raw key its configuration names; the
    in-memory store does not. Without it every agent run answers 502 and an attachment upload 500, so the leg would
    measure the missing key and not the store. The key lives in the host's environment for one run and nowhere else.
    """
    return {"Tracon__ContentProtection__RawKeys__sample": base64.b64encode(secrets.token_bytes(32)).decode("ascii")}


class Store:
    """The persistence of a run: nothing to start for `memory` and `sqlite`, a container otherwise."""

    def __init__(self, kind: str, output: pathlib.Path, external_connection_variable: str | None) -> None:
        self.kind = kind
        self.output = output
        self.external = external_connection_variable
        self.container: str | None = None
        self.image: str | None = None

    def start(self) -> dict[str, str]:
        """Starts what the store needs and returns the environment that points the host at it."""
        if self.kind == "memory":
            return {}

        if self.kind == "sqlite":
            database = self.output / "store.db"

            for suffix in ("", "-wal", "-shm"):
                pathlib.Path(str(database) + suffix).unlink(missing_ok=True)

            return {"Tracon__Sqlite__ConnectionString": f"Data Source={database}", **content_protection_key()}

        key = "Tracon__PostgreSql__ConnectionString" if self.kind == "postgres" else "Tracon__SqlServer__ConnectionString"

        if self.external:
            value = os.environ.get(self.external, "")

            if not value:
                raise FuzzError(f"--database-env names {self.external}, which is empty or unset.")

            return {key: value, **content_protection_key()}

        return {key: self._start_container(), **content_protection_key()}

    def _start_container(self) -> str:
        if shutil.which("docker") is None:
            raise FuzzError(f"The {self.kind} store needs Docker, or a connection string in an environment variable (--database-env).")

        self.container = CONTAINER_PREFIX + uuid.uuid4().hex[:8]
        port = free_port()
        password = "Fz" + secrets.token_hex(8) + "!9"

        if self.kind == "postgres":
            self.image = POSTGRES_IMAGE
            inner, environment = 5432, {"POSTGRES_PASSWORD": password, "POSTGRES_USER": "tracon", "POSTGRES_DB": "tracon"}
        else:
            self.image = SQLSERVER_IMAGE
            inner, environment = 1433, {"ACCEPT_EULA": "Y", "MSSQL_SA_PASSWORD": password}

        # The values reach the container through the client's own environment, so the password is
        # not on a command line that `ps` can show.
        command = ["docker", "run", "-d", "--rm", "--name", self.container, "-p", f"127.0.0.1:{port}:{inner}"]

        for name in environment:
            command += ["-e", name]

        run_command([*command, self.image], env=scrubbed_environment(environment), quiet=True)
        self._wait_ready(password)

        if self.kind == "postgres":
            return f"Host=127.0.0.1;Port={port};Database=tracon;Username=tracon;Password={password}"  # SYNTHETIC-CREDENTIAL: a throwaway password for a container that lives for one run

        return f"Server=127.0.0.1,{port};Database=master;User Id=sa;Password={password};TrustServerCertificate=true"  # SYNTHETIC-CREDENTIAL: a throwaway password for a container that lives for one run

    def _wait_ready(self, password: str) -> None:
        """Waits for a real query, three times in a row.

        The PostgreSQL image starts a temporary server that listens on a socket only, runs its
        init scripts and shuts it down; a readiness probe that passes in that window is followed by
        "the database system is shutting down". SQL Server accepts the login a few seconds before
        it takes queries.
        """
        assert self.container is not None
        deadline = time.monotonic() + DATABASE_READY_SECONDS
        consecutive = 0

        while time.monotonic() < deadline:
            if self.kind == "postgres":
                probe = ["docker", "exec", self.container, "psql", "-h", "127.0.0.1", "-U", "tracon", "-d", "tracon", "-tAc", "SELECT 1"]
                result = run_command(probe, check=False, quiet=True)
            else:
                result = None

                for tool in ("/opt/mssql-tools18/bin/sqlcmd", "/opt/mssql-tools/bin/sqlcmd"):
                    probe = ["docker", "exec", "-e", "SQLCMDPASSWORD", self.container, tool, "-C", "-S", "localhost", "-U", "sa", "-h", "-1", "-Q", "SET NOCOUNT ON; SELECT 1"]
                    result = run_command(probe, env=scrubbed_environment({"SQLCMDPASSWORD": password}), check=False, quiet=True)

                    if result.returncode == 0:
                        break

            if result is not None and result.returncode == 0 and result.stdout.strip().startswith("1"):
                consecutive += 1

                if consecutive >= 3:
                    return
            else:
                consecutive = 0

            time.sleep(1)

        raise FuzzError(f"The {self.kind} container was not ready after {DATABASE_READY_SECONDS} s.")

    def stop(self) -> None:
        container, self.container = self.container, None

        if container:
            run_command(["docker", "rm", "-f", container], check=False, quiet=True)


# --------------------------------------------------------------------------
# Legs against a running host
# --------------------------------------------------------------------------


@dataclasses.dataclass
class LegResult:
    leg: Leg
    passes: dict[str, int]
    failures: dict[str, int]
    exit_codes: dict[str, int]
    coverage: Coverage
    test_cases: int
    gate_failed: bool
    notes: list[str]
    violations: list[str] = dataclasses.field(default_factory=list)
    failure_statuses: dict[str, dict[str, int]] = dataclasses.field(default_factory=dict)
    accepted: dict[str, int] = dataclasses.field(default_factory=dict)


@dataclasses.dataclass(frozen=True)
class Settings:
    store: str
    examples: int
    report_examples: int
    seed: int
    workers: int
    phases: str | None
    only: str | None
    gating: tuple[str, ...]
    reporting: tuple[str, ...]
    seed_data: bool
    output: pathlib.Path
    schemathesis: pathlib.Path
    base_url: str


def create_scoped_key(base_url: str, scopes: Sequence[str]) -> str:
    """Creates an API key with exactly `scopes` and returns its plaintext. The admin role makes the request."""
    request = urllib.request.Request(
        f"{base_url}{API_PREFIX}/api-keys",
        data=json.dumps({"name": "fuzz-" + "+".join(scopes)[:80], "scopes": list(scopes)}).encode("utf-8"),
        method="POST",
        headers={"Content-Type": "application/json", ROLE_HEADER: "admin"},
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read())["plaintextKey"]
    except urllib.error.HTTPError as error:
        raise FuzzError(f"Creating a key with scopes {','.join(scopes)} answered {error.code}: {error.read()[:200]!r}") from error


def schemathesis_command(settings: Settings, leg: Leg, *, checks: Sequence[str], examples: int, directory: pathlib.Path, junit: str) -> list[str]:
    command = [
        str(settings.schemathesis),
        "run",
        f"{settings.base_url}/openapi/v1.json",
        "--checks",
        ",".join(checks),
        "--generation-database",
        "none",
        "--seed",
        str(settings.seed),
        "--max-examples",
        str(examples),
        "--workers",
        str(settings.workers),
        "--no-color",
        "--report",
        "junit",
        "--report-dir",
        str(directory),
        "--report-junit-path",
        str(directory / junit),
    ]

    if settings.phases:
        command += ["--phases", settings.phases]

    if settings.only:
        command += ["--include-path-regex", settings.only]

    for header in leg_headers(leg):
        command += ["-H", header]

    return command


def run_leg(settings: Settings, leg: Leg, seed_file: pathlib.Path | None) -> LegResult:
    directory = settings.output / leg.name
    shutil.rmtree(directory, ignore_errors=True)
    directory.mkdir(parents=True)
    stats_path = directory / "stats.json"
    notes: list[str] = []
    environment = scrubbed_environment(
        {
            "SCHEMATHESIS_HOOKS": str(HOOKS),
            "TRACON_FUZZ_STATS": str(stats_path),
            "PYTHONUTF8": "1",
            "PYTHONPATH": str(SCRIPTS),
        }
    )

    if seed_file is not None:
        environment["TRACON_FUZZ_SEED_FILE"] = str(seed_file)

    if leg.scopes:
        environment["TRACON_FUZZ_API_KEY"] = create_scoped_key(settings.base_url, leg.scopes)

    passes: dict[str, int] = {}
    failures: dict[str, int] = {}
    exit_codes: dict[str, int] = {}
    test_cases = 0
    merged = RunStats()

    # One invocation per check family. Schemathesis stops generating for an operation at its first
    # failure, so a family that finds something early would hide what the others would have found
    # in the same operation.
    families = [("gate", settings.gating, settings.examples), ("report", settings.reporting, settings.report_examples)]

    for family, checks, examples in families:
        if not checks:
            continue

        print(f"  [{leg.name}] {family}: {', '.join(checks)} ({examples} examples per operation) ...", flush=True)
        started = time.monotonic()
        command = schemathesis_command(settings, leg, checks=checks, examples=examples, directory=directory, junit=f"{family}.xml")
        # Run from the output directory: Schemathesis keeps a cache folder next to its working directory.
        result = run_command(command, env=environment, cwd=directory, check=False, quiet=True)
        log = result.stdout + result.stderr
        (directory / f"{family}.log").write_text(log, encoding="utf-8")
        exit_codes[family] = result.returncode
        test_cases += parse_test_cases(log)
        groups = parse_failure_groups(log)

        for group, count in groups.items():
            failures[f"{family}: {group}"] = count

        elapsed = time.monotonic() - started
        print(f"  [{leg.name}] {family}: exit {result.returncode}, {sum(groups.values())} finding(s), {elapsed:.0f} s", flush=True)

        if result.returncode not in (0, 1):
            notes.append(f"{family} run ended with exit code {result.returncode}, which is neither a pass nor a finding; see {family}.log")

        # The hooks overwrite the stats file at exit, so fold it in after each invocation.
        if stats_path.exists():
            current = json.loads(stats_path.read_text(encoding="utf-8"))
            _merge(merged, current)
            stats_path.unlink()

    stats = merged.to_json()
    (directory / "stats.json").write_text(json.dumps(stats, indent=1, sort_keys=True), encoding="utf-8")
    coverage = summarize_coverage(stats)
    counted = check_failures(stats)

    for check, count in counted.items():
        passes[check] = count

    violations = scope_violations(leg.scopes, stats) if leg.scopes else role_violations(leg.role, stats)
    gate_failed = (
        exit_codes.get("gate", 0) != 0
        or any(code not in (0, 1) for code in exit_codes.values())
        or any(": error: " in key for key in failures)
        or bool(violations)
    )
    write_repro(directory, settings, leg, seed_file is not None)

    return LegResult(
        leg=leg,
        passes=passes,
        failures=failures,
        exit_codes=exit_codes,
        coverage=coverage,
        test_cases=test_cases,
        gate_failed=gate_failed,
        notes=notes,
        violations=violations,
        failure_statuses=dict(stats.get("failureStatuses", {})),
        accepted=accepted_answer_counts(stats),
    )


def _merge(target: RunStats, current: Mapping[str, Any]) -> None:
    for operation, counts in current.get("responses", {}).items():
        for status, count in counts.items():
            bucket = target.responses.setdefault(operation, {})
            bucket[status] = bucket.get(status, 0) + count

    for check, per_operation in current.get("checks", {}).items():
        for operation, outcomes in per_operation.items():
            for outcome, count in outcomes.items():
                bucket = target.checks.setdefault(check, {}).setdefault(operation, {})
                bucket[outcome] = bucket.get(outcome, 0) + count

    for check, by_status in current.get("failureStatuses", {}).items():
        for status, count in by_status.items():
            bucket = target.failure_statuses.setdefault(check, {})
            bucket[status] = bucket.get(status, 0) + count


def write_repro(directory: pathlib.Path, settings: Settings, leg: Leg, seeded: bool) -> None:
    """Writes the short replay file of a leg: the command and the `curl` line of every finding."""
    lines = [
        f"# Replay of leg '{leg.name}' (seed {settings.seed}, {settings.examples} examples, store {settings.store}).",
        "# Same host, same generators; a finding that depends on data the run created needs --seed-data too.",
        "python3 scripts/openapi_fuzz.py "
        + " ".join(
            [
                f"--store {settings.store}",
                f"--leg {leg.name.replace('role-', 'role:', 1).replace('scope-', 'scope:', 1)}",
                f"--seed {settings.seed}",
                f"--examples {settings.examples}",
            ]
            + (["--seed-data"] if seeded else [])
        ),
        "",
        "# Single requests. Start a host with `python3 scripts/openapi_fuzz.py --serve` and export HOST first.",
    ]

    for family in ("gate", "report"):
        log_path = directory / f"{family}.log"

        if log_path.exists():
            curls = extract_repro(log_path.read_text(encoding="utf-8"), settings.base_url)

            if curls:
                lines += ["", f"# {family} findings"] + curls

    (directory / "repro.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


# --------------------------------------------------------------------------
# Summary
# --------------------------------------------------------------------------


def format_summary(settings: Settings, results: Sequence[LegResult], seed_steps: Sequence[Mapping[str, Any]]) -> str:
    """Markdown summary, also written to the job summary of a workflow run."""
    rows = [
        f"## OpenAPI fuzz: store `{settings.store}`, seed {settings.seed}, {settings.examples} examples per operation",
        "",
        f"Gating checks: {', '.join(settings.gating) or 'none'}. Reported, not gating: {', '.join(settings.reporting) or 'none'}.",
        "",
        "| Leg | Gate | Findings | Requests | Operations | Only 404 | Only 401/403 | No 2xx |",
        "|---|---|---|---|---|---|---|---|",
    ]

    for result in results:
        findings = sum(count for key, count in result.failures.items() if key.startswith("gate:"))
        reported = sum(count for key, count in result.failures.items() if key.startswith("report:"))
        verdict = "FAIL" if result.gate_failed else "pass"
        rows.append(
            f"| `{result.leg.name}` | {verdict} | {findings} gating, {reported} reported | {result.coverage.requests} | "
            f"{result.coverage.operations} | {len(result.coverage.only_not_found)} | {len(result.coverage.only_refused)} | "
            f"{len(result.coverage.never_succeeded)} |"
        )

    for result in results:
        if result.failures or result.violations or result.failure_statuses:
            rows += ["", f"### `{result.leg.name}` findings"]
            rows += [f"- {key}: {count}" for key, count in sorted(result.failures.items())]

        if result.failure_statuses:
            rows += [
                f"- {check} failures by received status: "
                + ", ".join(f"{status} x{count}" for status, count in sorted(by_status.items()))
                for check, by_status in sorted(result.failure_statuses.items())
            ]

        rows += [f"- role boundary: {violation}" for violation in result.violations]
        rows += [f"- note: {note}" for note in result.notes]

    accepted: dict[str, int] = {}

    for result in results:
        for entry, count in result.accepted.items():
            accepted[entry] = accepted.get(entry, 0) + count

    if accepted:
        rows += ["", "### Accepted answers (a capability the host does not register, or the unreachable MCP server the seed registers)"]
        rows += [f"- {entry}: {count}" for entry, count in sorted(accepted.items())]

    if seed_steps:
        failed = [step for step in seed_steps if not step["ok"]]
        rows += ["", f"### Seed: {len(seed_steps) - len(failed)} of {len(seed_steps)} steps succeeded"]
        rows += [f"- failed: {step['step']}: {step['detail']}" for step in failed]

    return "\n".join(rows) + "\n"


# --------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0], formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--store", choices=STORES, default="memory", help="persistence of the host (default: memory)")
    parser.add_argument("--database-env", metavar="VARIABLE", help="read the connection string for --store postgres|sqlserver from this environment variable instead of starting a container")
    parser.add_argument("--leg", action="append", metavar="LEG", help="role:<admin|operator|reader|anonymous>, scope:<Scope>[+<Scope>], roles:all or scopes:each (repeatable; default role:admin)")
    parser.add_argument("--examples", type=int, default=DEFAULT_EXAMPLES, help=f"examples per operation for the gating checks (default {DEFAULT_EXAMPLES})")
    parser.add_argument("--report-examples", type=int, default=None, help="examples per operation for the reported checks (default: --examples)")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED, help=f"Schemathesis seed (default {DEFAULT_SEED})")
    parser.add_argument("--workers", type=int, default=1, help="Schemathesis workers (default 1: deterministic order)")
    parser.add_argument("--phases", help="Schemathesis phases, comma-separated (default: all)")
    parser.add_argument("--only", metavar="REGEX", help="fuzz only the operations whose path matches this regular expression")
    parser.add_argument("--checks", help="override the gating checks, comma-separated")
    parser.add_argument("--report-checks", help="override the reported checks, comma-separated ('' for none)")
    parser.add_argument("--gate-only", action="store_true", help="skip the reported checks; the leg runs the gating checks only")
    parser.add_argument("--seed-data", action="store_true", help="seed the host through its HTTP API and draw path parameters and bodies from it")
    parser.add_argument("--allow-seed-failures", action="store_true", help="run the legs even when a seed step failed")
    parser.add_argument("--no-build", action="store_true", help="use the host already built under artifacts/")
    parser.add_argument("--with-ui", action="store_true", help="build the console too (needs Node.js); the default leaves it out")
    parser.add_argument("--serve", action="store_true", help="start the host, print its address, and wait for Ctrl+C instead of running legs")
    parser.add_argument("--output", type=pathlib.Path, default=DEFAULT_OUTPUT, help="directory for the environment, logs and reports")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    output: pathlib.Path = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    host: Host | None = None
    store = Store(args.store, output, args.database_env)

    def terminate(_signal: int, _frame: Any) -> None:
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, terminate)

    try:
        schemathesis = ensure_venv(output / "venv")

        if not args.no_build:
            build_host(args.with_ui)

        environment = store.start()
        host = Host(environment, output / "host.log")
        host.start()
        print(f"Host ready at {host.base_url} (store {args.store}, image {store.image or 'none'}).")

        seed_file: pathlib.Path | None = None
        seed_steps: list[dict[str, Any]] = []

        if args.seed_data:
            print("Seeding the host through its API ...")
            plan = Seeder(host.base_url, {ROLE_HEADER: "admin"}).run()
            seed_file = output / "seed.json"
            save_seed(plan, seed_file)
            seed_steps = plan.steps

            for step in plan.steps:
                print(f"  seed {'ok  ' if step['ok'] else 'FAIL'} {step['step']}: {step['detail']}")

            broken = [step["step"] for step in plan.steps if not step["ok"]]

            if broken and not args.allow_seed_failures:
                raise FuzzError(
                    f"The seed failed at: {', '.join(broken)}. A leg on a half-filled host measures the missing data, "
                    "not the host; fix the cause or pass --allow-seed-failures to run anyway."
                )

        if args.serve:
            print(f"export HOST={host.base_url}   # then: curl -H '{ROLE_HEADER}: admin' $HOST{API_PREFIX}/agents")

            if seed_file is not None:
                print(f"The seed pools are in {seed_file}; the hooks read them through TRACON_FUZZ_SEED_FILE.")

            print("Ctrl+C stops the host.")

            while True:
                time.sleep(3600)

        document = fetch_json(f"{host.base_url}/openapi/v1.json")
        scope_names = document.get("components", {}).get("schemas", {}).get("ApiKeyScope", {}).get("enum", [])
        legs = expand_legs(args.leg or ["role:admin"], scope_names)

        for leg in legs:
            unknown = [scope for scope in leg.scopes if scope_names and scope not in scope_names]

            if unknown:
                raise FuzzError(f"{', '.join(unknown)} is not an ApiKeyScope. The document lists: {', '.join(scope_names)}.")

        gating = tuple(args.checks.split(",")) if args.checks else GATING_CHECKS
        reporting = () if args.gate_only else tuple(part for part in (args.report_checks if args.report_checks is not None else ",".join(REPORT_CHECKS)).split(",") if part)
        settings = Settings(
            store=args.store,
            examples=args.examples,
            report_examples=args.report_examples or args.examples,
            seed=args.seed,
            workers=args.workers,
            phases=args.phases,
            only=args.only,
            gating=gating,
            reporting=reporting,
            seed_data=args.seed_data,
            output=output,
            schemathesis=schemathesis,
            base_url=host.base_url,
        )
        results = [run_leg(settings, leg, seed_file) for leg in legs]
    except FuzzError as error:
        print(f"openapi-fuzz: {error}", file=sys.stderr)

        return 2
    except KeyboardInterrupt:
        print("openapi-fuzz: interrupted", file=sys.stderr)

        return 130
    finally:
        if host is not None:
            host.stop()

        store.stop()

    summary = format_summary(settings, results, seed_steps)
    (output / "summary.md").write_text(summary, encoding="utf-8")
    print()
    print(summary)
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")

    if step_summary:
        with open(step_summary, "a", encoding="utf-8") as handle:
            handle.write(summary)

    failed = [result.leg.name for result in results if result.gate_failed]

    if failed:
        print(f"openapi-fuzz: gate failed on {len(failed)} leg(s): {', '.join(failed)}. Replay files: {output}/<leg>/repro.txt", file=sys.stderr)

        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
