"""Schemathesis hooks of the OpenAPI fuzz run (scripts/openapi_fuzz.py).

This module is loaded by Schemathesis inside the run's own virtual environment through the
`SCHEMATHESIS_HOOKS` variable. It does three things and nothing else:

* Writes the bearer token of a scoped API key into every request, so the token never reaches a
  command line or a report.
* Replaces some generated path parameters and request bodies with ones the seeded host really
  holds, so a request can get past "no such resource" into the code behind it. The rest stay
  generated, because the generated half is what finds the crashes.
* Drops the one server status per operation that `openapi_fuzz.ACCEPTED_ANSWERS` lists, and the schema failure of
  an event-stream response (a stream has no schema the document can state).
* Counts every response and every check outcome per operation and writes the counts at exit.
  Schemathesis reports failures, not the ground it covered; the counts say which operations
  never saw anything but a 404 and which never saw a 2xx.

The decisions are pure functions in `openapi_fuzz`, which this module imports from its own
directory, so they can be tested without Schemathesis installed.
"""

from __future__ import annotations

import atexit
import json
import os
import pathlib
import sys
import threading

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import openapi_fuzz  # noqa: E402
import schemathesis  # noqa: E402
from schemathesis.core.failures import ServerError  # noqa: E402
from schemathesis.openapi.checks import JsonSchemaError  # noqa: E402

_STATS_PATH = os.environ.get("TRACON_FUZZ_STATS", "")
_SEED_PATH = os.environ.get("TRACON_FUZZ_SEED_FILE", "")
_API_KEY = os.environ.get("TRACON_FUZZ_API_KEY", "")

_LOCK = threading.Lock()
_DRAWN = threading.local()
_STATS = openapi_fuzz.RunStats()
_SEED = openapi_fuzz.load_seed(pathlib.Path(_SEED_PATH)) if _SEED_PATH else None


def _flush() -> None:
    if _STATS_PATH:
        with _LOCK:
            pathlib.Path(_STATS_PATH).write_text(json.dumps(_STATS.to_json(), indent=1, sort_keys=True), encoding="utf-8")


atexit.register(_flush)


@schemathesis.hook
def before_call(context, case, kwargs):  # noqa: ANN001
    if _API_KEY:
        # `kwargs` are the transport's own arguments and already carry `headers` from the case; the case is the place.
        case.headers = {**(case.headers or {}), "Authorization": f"Bearer {_API_KEY}"}


@schemathesis.hook
def map_path_parameters(context, path_parameters):  # noqa: ANN001
    """Swaps a seeded value into the generated path parameters.

    🚨 This and `map_body` are two hooks on purpose, and not one `map_case`. A `map_case` hook of ANY body, an identity
    function included, makes Schemathesis 4.32.7 fail the Hypothesis health check "Too many generated examples are
    filtered out" for every operation whose only input is a path string, once the run draws 20 examples or fewer
    (measured: 17 operations on the in-memory store, 2 of 3 on `/agents/{name}`; with no hook, or with
    `map_path_parameters`/`map_body`/`map_query` as identity functions, none). The harness counted the check as an error
    and failed the leg for a reason that is not the host's.
    """
    if _SEED is None:
        return path_parameters

    operation = context.operation
    choice = openapi_fuzz.choose_seed(_SEED, method=operation.method, template=operation.path, path_parameters=path_parameters)
    chosen = choice.path_parameters if choice.path_parameters is not None else path_parameters
    _DRAWN.template = operation.path
    _DRAWN.path_parameters = chosen

    return chosen


@schemathesis.hook
def map_body(context, body):  # noqa: ANN001
    """Swaps a seeded body in; it renders `{name}` with the path parameters drawn for the same case.

    The path parameters of a case are drawn, and mapped, before its body, so `_DRAWN` holds this case's values. An
    operation with no path parameter never sets it, and a stale value of another operation is not used.
    """
    if _SEED is None:
        return body

    operation = context.operation
    drawn = getattr(_DRAWN, "path_parameters", None) if getattr(_DRAWN, "template", None) == operation.path else None
    choice = openapi_fuzz.choose_seed(
        _SEED,
        method=operation.method,
        template=operation.path,
        path_parameters=drawn if drawn is not None else {},
        variation=body if isinstance(body, (dict, list, str, int, float, bool, type(None))) else None,
    )

    return choice.body if choice.has_body else body


@schemathesis.hook
def filter_failure(context, failure, case, response):  # noqa: ANN001
    """Keeps every failure except a listed operation's listed server status and the shape of an event stream."""
    if isinstance(failure, ServerError):
        return not openapi_fuzz.accepts_answer(case.operation.method, case.operation.path, response.status_code)

    # OpenAPI 3.1 has no way to describe the shape of an event stream: the document says `type: string` for the body,
    # and Schemathesis checks every event object against it ("SSE event violates schema"). The schema of a stream
    # cannot hold, so the schema check does not apply to one; the status and the content type of the same response
    # are still checked. The names of the events are pinned by a test of their own (SseEventNameSnapshotTests).
    if isinstance(failure, JsonSchemaError) and openapi_fuzz.is_event_stream(response.headers):
        return False

    return True


@schemathesis.hook
def after_call(context, case, response):  # noqa: ANN001
    with _LOCK:
        _STATS.record_response(case.operation.label, response.status_code)


@schemathesis.hook
def after_validate(context, case, response, results):  # noqa: ANN001
    with _LOCK:
        for result in results:
            _STATS.record_check(result.name, case.operation.label, result.status.name.lower(), response.status_code)
