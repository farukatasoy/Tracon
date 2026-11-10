"""Registration entry-point rule, kept in one module for the scripts that need it.

A public member a consumer calls to switch a capability on is a "registration entry point":

- EVERY extension method whose receiver is `Tracon.ITraconBuilder`, whatever its name
  (`Configure`, `RequireCustomBinding` and `RequireProductionProfile` carry no prefix,
  but the chain exists only for registration);
- an `Add`/`Use`/`Map`-prefixed extension whose receiver is one of the other registration types;
- a member of the builder interface itself (`Tracon.ITraconBuilder.Services`).

The C# copy lives in `tests/Tracon.Core.UnitTests/Architecture/CapabilityEntryPoints.cs`
and cannot import this module; the "three names" tests on both sides keep the two in step.
The script that uses this module: `public-surface-inventory.py`.
"""
from __future__ import annotations

import re

BUILDER_RECEIVER = "Tracon.ITraconBuilder"

REGISTRATION_RECEIVERS = frozenset({
    BUILDER_RECEIVER,
    "Microsoft.Extensions.DependencyInjection.IServiceCollection",
    "Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder",
    "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
    "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
})

REGISTRATION_PREFIX = re.compile(r"^(?:Add|Use|Map)")

BUILDER_MEMBER = re.compile(r"^Tracon\.ITraconBuilder\.(?P<name>[A-Za-z0-9_]+)")

# An extension method on an Unshipped line. Leading modifiers (`static`, the `~`
# nullable-oblivious marker) are skipped; the name and the receiver are captured.
EXTENSION_METHOD = re.compile(
    r"^~?(?:[a-z]+ )*[A-Za-z0-9_.<>]+?\.(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
    r"(?:<[^(]*>)?\(this (?P<receiver>[A-Za-z0-9_.]+)")


def is_registration_extension(name: str, receiver: str) -> bool:
    """Is the extension `name` written on type `receiver` a registration entry point?"""
    if receiver == BUILDER_RECEIVER:
        return True
    return receiver in REGISTRATION_RECEIVERS and REGISTRATION_PREFIX.match(name) is not None


def capability_entry_points(line: str) -> str | None:
    """The member name when a `PublicAPI.*.txt` line is a registration entry point, otherwise `None`."""
    member = BUILDER_MEMBER.match(line)
    if member:
        return member.group("name")
    extension = EXTENSION_METHOD.match(line)
    if extension and is_registration_extension(extension.group("name"), extension.group("receiver")):
        return extension.group("name")
    return None
