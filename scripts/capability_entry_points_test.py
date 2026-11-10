#!/usr/bin/env python3
"""Tests for the registration entry-point rule."""
from __future__ import annotations

import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import capability_entry_points as rule  # noqa: E402

# One fixture: the scripts share this module, so the rule's behaviour is proven here.
# The expected result of each line sits next to it.
LINES = [
    ("Tracon.ITraconBuilder.Services.get -> Microsoft.Extensions.DependencyInjection.IServiceCollection!", "Services"),
    ("static Tracon.TraconBuilderExtensions.Configure(this Tracon.ITraconBuilder! builder, System.Action<Tracon.TraconOptions!>! configure) -> Tracon.ITraconBuilder!", "Configure"),
    ("static Tracon.TraconBuilderExtensions.RequireCustomBinding<T>(this Tracon.ITraconBuilder! builder) -> Tracon.ITraconBuilder!", "RequireCustomBinding"),
    ("static Tracon.TraconBuilderExtensions.RequireProductionProfile(this Tracon.ITraconBuilder! builder, System.Action<Tracon.TraconProductionProfileOptions!>? configure = null) -> Tracon.ITraconBuilder!", "RequireProductionProfile"),
    ("static Tracon.PostgreSqlBuilderExtensions.UsePostgreSql(this Tracon.ITraconBuilder! builder) -> Tracon.ITraconBuilder!", "UsePostgreSql"),
    ("static Tracon.TraconServiceCollectionExtensions.AddTracon(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> Tracon.ITraconBuilder!", "AddTracon"),
    ("~static Tracon.TraconEndpointRouteBuilderExtensions.MapTracon(this Microsoft.AspNetCore.Routing.IEndpointRouteBuilder! endpoints) -> void", "MapTracon"),
    # A name without the prefix is NOT an entry point on a receiver other than the builder.
    ("static Tracon.ServiceCollectionHelpers.Configure(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> void", None),
    # A receiver that is not a registration type.
    ("static Tracon.StringExtensions.AddSuffix(this string! value) -> string!", None),
    # A static method that is not an extension.
    ("static Tracon.Helper.AddNumbers(int a, int b) -> int", None),
    ("Tracon.TraconBuilderExtensions", None),
]


class RuleTests(unittest.TestCase):
    def test_every_fixture_line_yields_its_expected_result(self):
        for line, expected in LINES:
            with self.subTest(line=line):
                self.assertEqual(rule.capability_entry_points(line), expected)

    def test_builder_receiver_ignores_the_name_prefix(self):
        self.assertTrue(rule.is_registration_extension("Configure", "Tracon.ITraconBuilder"))
        self.assertFalse(rule.is_registration_extension(
            "Configure", "Microsoft.Extensions.DependencyInjection.IServiceCollection"))


if __name__ == "__main__":
    unittest.main()
