#!/usr/bin/env python3
"""Tests for the remote state the closing run reads: the last release tag on nuget.org and the last CI run."""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import pathlib
import http.client
import ssl
import subprocess
import sys
import unittest
import urllib.error
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))

import breaking_changes  # noqa: E402
import remote_state  # noqa: E402

spec = importlib.util.spec_from_file_location("gate", ROOT / "scripts" / "gate.py")
gate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gate
spec.loader.exec_module(gate)


def _index(*versions: str) -> tuple[int, str]:
    return 200, json.dumps({"versions": list(versions)})


class LookupTests(unittest.TestCase):
    def test_reads_the_published_versions_lower_cased(self) -> None:
        lookup = remote_state.lookup_package("Tracon.Core", fetcher=lambda url: _index("1.0.0-Preview.6"))

        self.assertEqual(("1.0.0-preview.6",), lookup.versions)

    def test_asks_the_flat_container_for_the_lower_case_id(self) -> None:
        asked: list[str] = []

        def fetcher(url: str) -> tuple[int | None, str]:
            asked.append(url)
            return _index()

        remote_state.lookup_package("Tracon.Core", fetcher=fetcher)

        self.assertEqual(["https://api.nuget.org/v3-flatcontainer/tracon.core/index.json"], asked)

    def test_a_package_nobody_pushed_is_an_answer_not_a_failure(self) -> None:
        lookup = remote_state.lookup_package("Tracon.Core", fetcher=lambda url: (404, ""))

        self.assertEqual((), lookup.versions)
        self.assertFalse(lookup.unreachable)

    def test_no_answer_a_server_error_and_a_broken_body_are_unreachable(self) -> None:
        for answer in ((None, ""), (500, ""), (200, "not json"), (200, "[]"), (200, '{"versions": 3}')):
            with self.subTest(answer=answer):
                self.assertTrue(remote_state.lookup_package("Tracon.Core", fetcher=lambda url, a=answer: a).unreachable)


class ReleaseStateTests(unittest.TestCase):
    def test_every_package_present_is_published(self) -> None:
        state = remote_state.release_state(["A", "B"], "1.0.0", fetcher=lambda url: _index("1.0.0", "0.9.0"))

        self.assertEqual(2, state.published)
        self.assertEqual((), state.missing)
        self.assertEqual((), state.unreachable)

    def test_the_package_the_run_never_pushed_is_missing(self) -> None:
        def fetcher(url: str) -> tuple[int | None, str]:
            return _index("1.0.0") if "/a/" in url else _index("0.9.0")

        state = remote_state.release_state(["A", "B"], "1.0.0", fetcher=fetcher)

        self.assertEqual(("B",), state.missing)
        self.assertEqual(1, state.published)

    def test_a_package_with_no_version_at_all_is_missing(self) -> None:
        state = remote_state.release_state(["A"], "1.0.0", fetcher=lambda url: (404, ""))

        self.assertEqual(("A",), state.missing)

    def test_what_could_not_be_read_is_neither_missing_nor_published(self) -> None:
        state = remote_state.release_state(["A", "B"], "1.0.0", fetcher=lambda url: (None, ""))

        self.assertEqual((), state.missing)
        self.assertEqual(("A", "B"), state.unreachable)
        self.assertEqual(0, state.published)

    def test_a_version_compares_without_regard_to_case(self) -> None:
        state = remote_state.release_state(["A"], "1.0.0-Preview.6", fetcher=lambda url: _index("1.0.0-preview.6"))

        self.assertEqual(1, state.published)


def _nuspec(commit: str | None) -> tuple[int, str]:
    repository = f' commit="{commit}"' if commit else ""
    return 200, (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>A</id>'
        f'<repository type="git" url="https://example.invalid/r"{repository} /></metadata></package>')


class ReleaseCommitsTests(unittest.TestCase):
    def test_reads_the_commit_of_every_package_nuspec_lower_cased(self) -> None:
        state = remote_state.release_commits(["A", "B"], "1.0.0", fetcher=lambda url: _nuspec("ABCDEF1"))

        self.assertEqual({"A": "abcdef1", "B": "abcdef1"}, state.commits)
        self.assertEqual(frozenset({"abcdef1"}), state.distinct)
        self.assertEqual((), state.absent)
        self.assertEqual((), state.unreachable)

    def test_asks_the_flat_container_for_the_lower_case_nuspec_of_that_version(self) -> None:
        seen: list[str] = []

        def fetcher(url: str) -> tuple[int | None, str]:
            seen.append(url)
            return _nuspec("abc")

        remote_state.release_commits(["Tracon.Core"], "1.0.0-Preview.6", fetcher=fetcher)

        self.assertEqual(
            [f"{remote_state.NUGET_FLAT_CONTAINER}/tracon.core/1.0.0-preview.6/tracon.core.nuspec"], seen)

    def test_a_package_nuget_does_not_have_is_absent_not_unreachable(self) -> None:
        def fetcher(url: str) -> tuple[int | None, str]:
            return _nuspec("abc") if "/a/" in url else (404, "")

        state = remote_state.release_commits(["A", "B"], "1.0.0", fetcher=fetcher)

        self.assertEqual(("B",), state.absent)
        self.assertEqual({"A": "abc"}, state.commits)
        self.assertEqual((), state.unreachable)

    def test_a_server_error_a_missing_answer_and_a_broken_body_are_unreachable(self) -> None:
        for answer in ((500, ""), (None, ""), (200, "<not-closed")):
            with self.subTest(answer=answer):
                state = remote_state.release_commits(["A"], "1.0.0", fetcher=lambda url, a=answer: a)

                self.assertEqual(("A",), state.unreachable)
                self.assertEqual({}, state.commits)

    def test_a_nuspec_that_names_no_commit_is_a_package_with_no_commit(self) -> None:
        state = remote_state.release_commits(["A"], "1.0.0", fetcher=lambda url: _nuspec(None))

        self.assertEqual({"A": None}, state.commits)
        self.assertEqual(frozenset({None}), state.distinct)

    def test_foreign_lists_the_packages_that_name_another_commit_or_none(self) -> None:
        answers = {"/a/": "aaa", "/b/": "bbb", "/c/": None}

        def fetcher(url: str) -> tuple[int | None, str]:
            return _nuspec(next(value for key, value in answers.items() if key in url))

        state = remote_state.release_commits(["A", "B", "C"], "1.0.0", fetcher=fetcher)

        self.assertEqual({"B": "bbb", "C": None}, state.foreign("AAA"))
        self.assertEqual({}, remote_state.release_commits(["A"], "1.0.0", fetcher=fetcher).foreign("aaa"))

    def test_after_three_unanswered_packages_the_rest_are_not_asked(self) -> None:
        asked: list[str] = []

        def fetcher(url: str) -> tuple[int | None, str]:
            asked.append(url)
            return None, ""

        state = remote_state.release_commits(list("ABCDEF"), "1.0.0", fetcher=fetcher)

        self.assertEqual(3, len(asked))
        self.assertEqual(tuple("ABCDEF"), state.unreachable)


class FetchTests(unittest.TestCase):
    def test_a_missing_trust_store_falls_back_to_curl(self) -> None:
        failure = urllib.error.URLError(ssl.SSLCertVerificationError("certificate verify failed"))

        with mock.patch.object(remote_state.urllib.request, "urlopen", side_effect=failure), \
                mock.patch.object(remote_state.shutil, "which", return_value="/usr/bin/curl"), \
                mock.patch.object(remote_state, "_fetch_with_curl", return_value=(200, "{}")) as curl:
            self.assertEqual((200, "{}"), remote_state.fetch("https://example.test/index.json"))

        curl.assert_called_once()

    def test_a_certificate_error_without_curl_is_unreachable(self) -> None:
        failure = urllib.error.URLError(ssl.SSLCertVerificationError("certificate verify failed"))

        with mock.patch.object(remote_state.urllib.request, "urlopen", side_effect=failure), \
                mock.patch.object(remote_state.shutil, "which", return_value=None):
            self.assertEqual((None, ""), remote_state.fetch("https://example.test/index.json"))

    def test_a_plain_connection_failure_does_not_try_curl(self) -> None:
        with mock.patch.object(remote_state.urllib.request, "urlopen", side_effect=urllib.error.URLError("no route")), \
                mock.patch.object(remote_state, "_fetch_with_curl") as curl:
            self.assertEqual((None, ""), remote_state.fetch("https://example.test/index.json"))

        curl.assert_not_called()

    def test_an_http_error_is_its_status(self) -> None:
        error = urllib.error.HTTPError("https://example.test", 404, "Not Found", {}, None)  # type: ignore[arg-type]

        with mock.patch.object(remote_state.urllib.request, "urlopen", side_effect=error):
            self.assertEqual((404, ""), remote_state.fetch("https://example.test/index.json"))

    def test_curl_output_is_split_into_status_and_body(self) -> None:
        completed = subprocess.CompletedProcess([], 0, stdout='{"versions":[]}\n200', stderr="")

        with mock.patch.object(remote_state.subprocess, "run", return_value=completed):
            self.assertEqual((200, '{"versions":[]}'), remote_state._fetch_with_curl("https://example.test"))

    def test_curl_with_no_connection_is_unreachable(self) -> None:
        completed = subprocess.CompletedProcess([], 6, stdout="\n000", stderr="")

        with mock.patch.object(remote_state.subprocess, "run", return_value=completed):
            self.assertEqual((None, ""), remote_state._fetch_with_curl("https://example.test"))


class GiveUpAndPackageSetTests(unittest.TestCase):
    def test_after_three_unanswered_packages_the_rest_are_not_asked(self) -> None:
        asked: list[str] = []

        def fetcher(url: str) -> tuple[int | None, str]:
            asked.append(url)
            return None, ""

        state = remote_state.release_state(["A", "B", "C", "D", "E", "F"], "1.0.0", fetcher=fetcher)

        self.assertEqual(3, len(asked))
        self.assertEqual(("A", "B", "C", "D", "E", "F"), state.unreachable)

    def test_an_answer_resets_the_count(self) -> None:
        answers = iter([(None, ""), (None, ""), _index("1.0.0"), (None, ""), (None, ""), _index("1.0.0")])

        state = remote_state.release_state(["A", "B", "C", "D", "E", "F"], "1.0.0", fetcher=lambda url: next(answers))

        self.assertEqual(2, state.published)
        self.assertEqual(("A", "B", "D", "E"), state.unreachable)

    def test_a_cut_off_answer_and_a_body_that_is_not_utf8_are_no_answer(self) -> None:
        for failure in (http.client.IncompleteRead(b"x"), UnicodeDecodeError("utf-8", b"\xff", 0, 1, "invalid")):
            with self.subTest(failure=type(failure).__name__), \
                    mock.patch.object(remote_state.urllib.request, "urlopen", side_effect=failure):
                self.assertEqual((None, ""), remote_state.fetch("https://example.test/index.json"))

    def test_the_package_set_is_the_one_the_tag_held(self) -> None:
        def runner(command, **kwargs):  # noqa: ANN001, ANN202
            if command[1] == "ls-tree":
                return subprocess.CompletedProcess(command, 0, stdout="Tracon.Core\nTracon.Hidden\nsamples-only\n", stderr="")

            project = command[2]

            if project.endswith("Tracon.Core/Tracon.Core.csproj"):
                return subprocess.CompletedProcess(command, 0, stdout="<Project />", stderr="")

            if project.endswith("Tracon.Hidden/Tracon.Hidden.csproj"):
                return subprocess.CompletedProcess(command, 0, stdout="<IsPackable>false</IsPackable>", stderr="")

            return subprocess.CompletedProcess(command, 128, stdout="", stderr="fatal: path does not exist")

        self.assertEqual(["Tracon.Core"], remote_state.packable_ids_at(ROOT, "v1.0.0", runner=runner))

    def test_a_tag_git_cannot_read_has_no_set(self) -> None:
        runner = lambda command, **kwargs: subprocess.CompletedProcess(command, 128, stdout="", stderr="fatal")  # noqa: E731

        self.assertIsNone(remote_state.packable_ids_at(ROOT, "v9.9.9", runner=runner))


class RepositorySlugTests(unittest.TestCase):
    def _slug(self, url: str) -> str | None:
        completed = subprocess.CompletedProcess([], 0, stdout=url + "\n", stderr="")

        return remote_state.repository_slug(ROOT, "origin", runner=lambda *args, **kwargs: completed)

    def test_reads_the_owner_and_name_from_every_url_shape(self) -> None:
        self.assertEqual("owner/repo", self._slug("https://github.com/owner/repo.git"))
        self.assertEqual("owner/repo", self._slug("https://github.com/owner/repo"))
        self.assertEqual("owner/repo", self._slug("git@github.com:owner/repo.git"))
        self.assertEqual("owner/repo", self._slug("alias:owner/repo.git"))

    def test_a_remote_that_does_not_exist_has_no_slug(self) -> None:
        completed = subprocess.CompletedProcess([], 2, stdout="", stderr="error: No such remote")

        self.assertIsNone(remote_state.repository_slug(ROOT, "nope", runner=lambda *args, **kwargs: completed))


class CiStateTests(unittest.TestCase):
    @staticmethod
    def _runner(runs: list[dict[str, str]], gh_code: int = 0):
        def run(command, **kwargs):  # noqa: ANN001, ANN202
            if command[:2] == ["git", "remote"]:
                return subprocess.CompletedProcess(command, 0, stdout="https://github.com/owner/repo.git\n", stderr="")

            return subprocess.CompletedProcess(command, gh_code, stdout=json.dumps(runs), stderr="")

        return run

    def _state(self, runs: list[dict[str, str]], **kwargs):  # noqa: ANN003, ANN202
        return remote_state.ci_state(ROOT, runner=self._runner(runs, **kwargs), which=lambda name: "/usr/bin/gh")

    def test_a_red_run_is_red(self) -> None:
        state = self._state([{"status": "completed", "conclusion": "failure", "url": "u", "headSha": "abcdef0123"}])

        self.assertTrue(state.is_red)
        self.assertEqual("abcdef01", state.sha)

    def test_skipped_runs_decided_nothing_and_the_next_one_counts(self) -> None:
        state = self._state([
            {"status": "completed", "conclusion": "skipped", "url": "1", "headSha": "a"},
            {"status": "completed", "conclusion": "cancelled", "url": "2", "headSha": "b"},
            {"status": "completed", "conclusion": "success", "url": "3", "headSha": "c"},
        ])

        self.assertEqual("3", state.url)
        self.assertFalse(state.is_red)

    def test_a_run_still_going_counts_and_is_not_red(self) -> None:
        state = self._state([{"status": "in_progress", "conclusion": "", "url": "u", "headSha": "a"}])

        self.assertFalse(state.is_red)

    def test_only_skipped_runs_leave_nothing_to_report(self) -> None:
        self.assertIsNone(self._state([{"status": "completed", "conclusion": "skipped", "url": "u", "headSha": "a"}]))

    def test_the_workflow_to_read_is_a_parameter_and_defaults_to_ci(self) -> None:
        commands: list[list[str]] = []

        def run(command, **kwargs):  # noqa: ANN001, ANN202
            commands.append(list(command))

            if command[:2] == ["git", "remote"]:
                return subprocess.CompletedProcess(command, 0, stdout="https://github.com/owner/repo.git\n", stderr="")

            return subprocess.CompletedProcess(command, 0, stdout="[]", stderr="")

        remote_state.ci_state(ROOT, runner=run, which=lambda name: "/usr/bin/gh")
        remote_state.ci_state(ROOT, workflow="stability.yml", runner=run, which=lambda name: "/usr/bin/gh")

        listings = [command for command in commands if command[:3] == ["gh", "run", "list"]]
        self.assertEqual(["ci.yml", "stability.yml"], [command[command.index("--workflow") + 1] for command in listings])
        self.assertTrue(all("-R" in command for command in listings))

    def test_the_branch_defaults_to_next_and_is_a_parameter(self) -> None:
        commands: list[list[str]] = []

        def run(command, **kwargs):  # noqa: ANN001, ANN202
            commands.append(list(command))

            if command[:2] == ["git", "remote"]:
                return subprocess.CompletedProcess(command, 0, stdout="https://github.com/owner/repo.git\n", stderr="")

            return subprocess.CompletedProcess(command, 0, stdout="[]", stderr="")

        remote_state.ci_state(ROOT, runner=run, which=lambda name: "/usr/bin/gh")
        remote_state.ci_state(ROOT, branch="main", runner=run, which=lambda name: "/usr/bin/gh")

        listings = [command for command in commands if command[:3] == ["gh", "run", "list"]]
        self.assertEqual(["next", "main"], [command[command.index("--branch") + 1] for command in listings])

    def test_everything_unreadable_is_none(self) -> None:
        self.assertIsNone(self._state([], gh_code=1))
        self.assertIsNone(self._state([]))
        self.assertIsNone(remote_state.ci_state(ROOT, runner=self._runner([]), which=lambda name: None))


def _run(sha: str, *, event: str = "push", branch: str = "next", repository: str = "owner/repo",
         status: str = "completed", conclusion: str = "success") -> dict[str, object]:
    return {
        "head_sha": sha, "event": event, "head_branch": branch, "status": status, "conclusion": conclusion,
        "html_url": f"https://github.com/owner/repo/actions/runs/{sha}", "head_repository": {"full_name": repository},
    }


def _runs_answer(*runs: dict[str, object]) -> tuple[int, str]:
    return 200, json.dumps({"workflow_runs": list(runs)})


class SlugOfTests(unittest.TestCase):
    def test_reads_the_owner_and_name_of_every_url_shape_without_asking_git(self) -> None:
        for url in ("https://github.com/owner/repo.git", "https://github.com/owner/repo", "git@github.com:owner/repo.git"):
            self.assertEqual("owner/repo", remote_state.slug_of(url))

    def test_text_that_names_no_repository_has_no_slug(self) -> None:
        self.assertIsNone(remote_state.slug_of(""))
        self.assertIsNone(remote_state.slug_of("/tmp/some/bare.git"))


class CiRunsTests(unittest.TestCase):
    def test_asks_the_runs_api_for_one_workflow_and_one_branch_without_pull_requests(self) -> None:
        urls: list[str] = []

        def fetcher(url: str) -> tuple[int, str]:
            urls.append(url)
            return _runs_answer()

        remote_state.ci_runs("owner/repo", "next", fetcher=fetcher)

        self.assertEqual(1, len(urls))
        self.assertIn("/repos/owner/repo/actions/workflows/ci.yml/runs", urls[0])
        self.assertIn("branch=next", urls[0])
        self.assertIn("exclude_pull_requests=true", urls[0])

    def test_reads_what_decides_a_run(self) -> None:
        runs = remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: _runs_answer(_run("abc123", event="workflow_dispatch")))

        self.assertEqual(
            [remote_state.CiRun("abc123", "workflow_dispatch", "next", "owner/repo", "completed", "success",
                                "https://github.com/owner/repo/actions/runs/abc123")],
            runs,
        )

    def test_a_run_whose_head_repository_is_gone_has_none(self) -> None:
        body = _run("abc123")
        body["head_repository"] = None

        runs = remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: _runs_answer(body))

        self.assertEqual("", runs[0].repository)

    def test_no_answer_a_server_error_and_a_broken_body_are_unreadable(self) -> None:
        self.assertIsNone(remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: (None, "")))
        self.assertIsNone(remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: (403, "rate limit")))
        self.assertIsNone(remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: (200, "not json")))
        self.assertIsNone(remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: (200, json.dumps({"runs": []}))))


class GreenShasTests(unittest.TestCase):
    def _green(self, *runs: dict[str, object]) -> set[str]:
        parsed = remote_state.ci_runs("owner/repo", "next", fetcher=lambda url: _runs_answer(*runs))

        return remote_state.green_shas(parsed, slug="owner/repo", branch="next")

    def test_a_completed_success_of_a_push_is_green(self) -> None:
        self.assertEqual({"a"}, self._green(_run("a")))

    def test_a_dispatch_run_is_green_too(self) -> None:
        self.assertEqual({"a"}, self._green(_run("a", event="workflow_dispatch")))

    def test_only_a_success_decides_green(self) -> None:
        runs = [_run("f", conclusion="failure"), _run("c", conclusion="cancelled"), _run("s", conclusion="skipped"),
                _run("t", conclusion="timed_out"), _run("n", conclusion="neutral")]

        self.assertEqual(set(), self._green(*runs))

    def test_a_run_that_has_not_finished_is_not_green_whatever_its_conclusion_says(self) -> None:
        self.assertEqual(set(), self._green(_run("a", status="in_progress", conclusion=""), _run("b", status="queued", conclusion="success")))

    def test_a_pull_request_run_is_not_green(self) -> None:
        self.assertEqual(set(), self._green(_run("a", event="pull_request")))

    def test_a_fork_run_with_the_same_branch_name_is_not_green(self) -> None:
        self.assertEqual(set(), self._green(_run("a", repository="stranger/repo")))

    def test_a_run_of_another_branch_is_not_green(self) -> None:
        self.assertEqual(set(), self._green(_run("a", branch="main")))

    def test_the_repository_compares_without_regard_to_case(self) -> None:
        self.assertEqual({"a"}, self._green(_run("a", repository="Owner/Repo")))

    def test_one_failed_run_of_a_sha_that_another_run_passed_does_not_unmake_it(self) -> None:
        self.assertEqual({"a"}, self._green(_run("a", conclusion="failure"), _run("a")))

    def test_nothing_read_is_nothing_green(self) -> None:
        self.assertEqual(set(), remote_state.green_shas(None, slug="owner/repo", branch="next"))


class PromotionStateTests(unittest.TestCase):
    @staticmethod
    def _fetcher(commits: list[str], *runs: dict[str, object]):
        def fetch(url: str) -> tuple[int, str]:
            if "/compare/" in url:
                return 200, json.dumps({"ahead_by": len(commits), "commits": [{"sha": sha} for sha in commits]})

            return _runs_answer(*runs)

        return fetch

    def test_main_level_with_next_is_not_behind(self) -> None:
        state = remote_state.promotion_state("owner/repo", fetcher=self._fetcher([]))

        self.assertEqual(remote_state.Promotion(0, None), state)

    def test_the_newest_green_commit_ahead_of_main_is_the_one_to_promote(self) -> None:
        state = remote_state.promotion_state(
            "owner/repo", fetcher=self._fetcher(["old", "mid", "new"], _run("old"), _run("mid"), _run("new", conclusion="failure")))

        self.assertEqual(remote_state.Promotion(3, "mid"), state)

    def test_main_behind_next_with_nothing_green_has_nothing_to_promote(self) -> None:
        state = remote_state.promotion_state("owner/repo", fetcher=self._fetcher(["a", "b"], _run("a", conclusion="failure")))

        self.assertEqual(remote_state.Promotion(2, None), state)

    def test_the_compare_asks_main_against_next(self) -> None:
        urls: list[str] = []

        def fetch(url: str) -> tuple[int, str]:
            urls.append(url)
            return self._fetcher([])(url)

        remote_state.promotion_state("owner/repo", fetcher=fetch)

        self.assertTrue(any("/repos/owner/repo/compare/main...next" in url for url in urls))

    def test_an_unreadable_compare_or_run_list_is_no_answer(self) -> None:
        self.assertIsNone(remote_state.promotion_state("owner/repo", fetcher=lambda url: (None, "")))
        self.assertIsNone(remote_state.promotion_state("owner/repo", fetcher=lambda url: (200, json.dumps({"x": 1}))))


class GithubHeadersTests(unittest.TestCase):
    def test_a_token_in_the_environment_is_sent_only_to_the_api_host(self) -> None:
        with mock.patch.dict("os.environ", {"GITHUB_TOKEN": "t0k"}, clear=True):
            self.assertEqual("Bearer t0k", remote_state.github_headers("https://api.github.com/repos/o/r")["Authorization"])
            self.assertNotIn("Authorization", remote_state.github_headers("https://api.nuget.org/v3/index.json"))

    def test_without_a_token_the_request_is_anonymous(self) -> None:
        with mock.patch.dict("os.environ", {}, clear=True):
            self.assertNotIn("Authorization", remote_state.github_headers("https://api.github.com/repos/o/r"))
            self.assertIn("Accept", remote_state.github_headers("https://api.github.com/repos/o/r"))


@mock.patch.object(remote_state, "promotion_state", new=lambda *args, **kwargs: None)
class RemoteStateCheckTests(unittest.TestCase):
    def _check(self, state: remote_state.ReleaseState, ci: remote_state.CiState | None = None) -> tuple[int, str]:
        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=["A", "B"]), \
                mock.patch.object(remote_state, "release_state", return_value=state), \
                mock.patch.object(remote_state, "ci_state", return_value=ci), \
                contextlib.redirect_stdout(output):
            code = gate.remote_state_check(ROOT)

        return code, output.getvalue()

    def test_a_package_missing_from_a_release_that_nuget_answered_for_is_red(self) -> None:
        code, text = self._check(remote_state.ReleaseState("1.0.0", ("B",), (), 1))

        self.assertEqual(1, code)
        self.assertIn("B", text)

    def test_an_unreachable_nuget_only_warns(self) -> None:
        code, text = self._check(remote_state.ReleaseState("1.0.0", (), ("A", "B"), 0))

        self.assertEqual(0, code)
        self.assertIn("was not reachable", text)

    def test_a_published_release_is_green(self) -> None:
        code, text = self._check(remote_state.ReleaseState("1.0.0", (), (), 2))

        self.assertEqual(0, code)
        self.assertIn("all 2 packages are on nuget.org", text)

    def test_a_red_ci_run_warns_and_does_not_fail(self) -> None:
        ci = remote_state.CiState("completed", "failure", "https://example.test/run", "abcdef01")

        code, text = self._check(remote_state.ReleaseState("1.0.0", (), (), 2), ci)

        self.assertEqual(0, code)
        self.assertIn("The last CI run of 'origin' on next is red", text)

    def test_the_ci_line_names_the_remote_it_read_and_a_running_run_is_not_green(self) -> None:
        running = remote_state.CiState("in_progress", "", "https://example.test/run", "abcdef01")
        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=["A"]), \
                mock.patch.object(remote_state, "release_state", return_value=remote_state.ReleaseState("1.0.0", (), (), 1)), \
                mock.patch.object(remote_state, "ci_state", return_value=running), \
                contextlib.redirect_stdout(output):
            gate.remote_state_check(ROOT, "mirror")

        text = output.getvalue()

        self.assertIn("'mirror'", text)
        self.assertNotIn("✅ 'mirror'", text)

    def _check_with_stability(
        self, ci: remote_state.CiState | None, stability: remote_state.CiState | None,
    ) -> tuple[int, str, list[str]]:
        workflows: list[str] = []

        def ci_state(root, remote="origin", branch="main", *, workflow="ci.yml", **kwargs):  # noqa: ANN001, ANN202
            workflows.append(workflow)
            return stability if workflow == gate.STABILITY_WORKFLOW else ci

        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=["A"]), \
                mock.patch.object(remote_state, "release_state", return_value=remote_state.ReleaseState("1.0.0", (), (), 1)), \
                mock.patch.object(remote_state, "ci_state", side_effect=ci_state), \
                contextlib.redirect_stdout(output):
            code = gate.remote_state_check(ROOT, "mirror")

        return code, output.getvalue(), workflows

    def test_a_red_stability_run_warns_that_no_new_phase_opens_and_does_not_fail(self) -> None:
        green = remote_state.CiState("completed", "success", "https://example.test/ci", "abcdef01")
        red = remote_state.CiState("completed", "failure", "https://example.test/stability", "abcdef01")

        code, text, workflows = self._check_with_stability(green, red)

        self.assertEqual(0, code)
        self.assertEqual(["ci.yml", "stability.yml"], workflows)
        self.assertIn("The last stability run of 'mirror' is red", text)
        self.assertIn("https://example.test/stability", text)
        self.assertIn("fix the instability first", text)
        self.assertIn("✅ The last CI run of 'mirror' on next is green", text)

    def test_a_green_stability_run_is_reported_and_a_missing_one_is_only_information(self) -> None:
        green = remote_state.CiState("completed", "success", "u", "abcdef01")

        _, text, _ = self._check_with_stability(green, green)
        _, missing, _ = self._check_with_stability(green, None)

        self.assertIn("✅ The last stability run of 'mirror' is green", text)
        self.assertIn("ℹ️ No decided stability run could be read for 'mirror'", missing)
        self.assertNotIn("fix the instability first", missing)

    def test_a_stability_run_still_going_is_not_green(self) -> None:
        running = remote_state.CiState("in_progress", "", "u", "abcdef01")

        _, text, _ = self._check_with_stability(running, running)

        self.assertNotIn("✅ The last stability run of 'mirror'", text)
        self.assertIn("The last stability run of 'mirror' has not decided yet", text)

    def test_a_tag_tree_git_cannot_read_falls_back_to_the_working_tree_with_a_warning(self) -> None:
        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=None), \
                mock.patch.object(gate, "packable_project_ids", return_value=["A"]), \
                mock.patch.object(remote_state, "release_state", return_value=remote_state.ReleaseState("1.0.0", (), (), 1)), \
                mock.patch.object(remote_state, "ci_state", return_value=None), \
                contextlib.redirect_stdout(output):
            code = gate.remote_state_check(ROOT)

        self.assertEqual(0, code)
        self.assertIn("from the working tree", output.getvalue())

    def test_an_unresolvable_release_tag_skips_nuget_with_a_warning(self) -> None:
        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", side_effect=breaking_changes.BaselineError("no tag")), \
                mock.patch.object(remote_state, "release_state") as state, \
                mock.patch.object(remote_state, "ci_state", return_value=None), \
                contextlib.redirect_stdout(output):
            code = gate.remote_state_check(ROOT)

        self.assertEqual(0, code)
        state.assert_not_called()


@mock.patch.object(remote_state, "repository_slug", new=lambda *args, **kwargs: "owner/repo")
class PromotionLinesTests(unittest.TestCase):
    """`remote-state` says how far `main` is behind `next` and what the next promotion would move it to; information, never a failure."""

    def _check(self, promotion: remote_state.Promotion | None) -> tuple[int, str]:
        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=["A"]), \
                mock.patch.object(remote_state, "release_state", return_value=remote_state.ReleaseState("1.0.0", (), (), 1)), \
                mock.patch.object(remote_state, "ci_state", return_value=None), \
                mock.patch.object(remote_state, "promotion_state", return_value=promotion) as asked, \
                contextlib.redirect_stdout(output):
            code = gate.remote_state_check(ROOT)

        self.assertEqual([mock.call("owner/repo")], asked.call_args_list)

        return code, output.getvalue()

    def test_main_level_with_next_is_green(self) -> None:
        code, text = self._check(remote_state.Promotion(0, None))

        self.assertEqual(0, code)
        self.assertIn("✅ main is level with next", text)

    def test_main_behind_next_names_the_commit_the_next_promotion_moves_it_to(self) -> None:
        code, text = self._check(remote_state.Promotion(3, "abcdef0123456789"))

        self.assertEqual(0, code)
        self.assertIn("main is 3 commit(s) behind next", text)
        self.assertIn("awaiting promotion is abcdef01", text)

    def test_main_behind_next_with_nothing_green_says_nothing_can_be_promoted(self) -> None:
        code, text = self._check(remote_state.Promotion(2, None))

        self.assertEqual(0, code)
        self.assertIn("main is 2 commit(s) behind next", text)
        self.assertIn("nothing can be promoted", text)

    def test_an_unreadable_answer_is_information_and_does_not_fail_the_check(self) -> None:
        code, text = self._check(None)

        self.assertEqual(0, code)
        self.assertIn("How far main is behind next on 'origin' could not be read", text)

    def test_a_remote_with_no_repository_name_is_not_asked(self) -> None:
        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=["A"]), \
                mock.patch.object(remote_state, "release_state", return_value=remote_state.ReleaseState("1.0.0", (), (), 1)), \
                mock.patch.object(remote_state, "ci_state", return_value=None), \
                mock.patch.object(remote_state, "repository_slug", return_value=None), \
                mock.patch.object(remote_state, "promotion_state") as asked, \
                contextlib.redirect_stdout(output):
            code = gate.remote_state_check(ROOT)

        self.assertEqual(0, code)
        asked.assert_not_called()
        self.assertIn("could not be read", output.getvalue())

    def test_the_ci_line_names_the_branch_it_read_and_the_stability_line_reads_main(self) -> None:
        branches: list[str] = []

        def ci_state(root, remote="origin", branch=remote_state.CI_BRANCH, *, workflow="ci.yml", **kwargs):  # noqa: ANN001, ANN202
            branches.append(f"{workflow}:{branch}")
            return remote_state.CiState("completed", "success", "u", "abcdef0123456789")

        output = io.StringIO()

        with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                mock.patch.object(remote_state, "packable_ids_at", return_value=["A"]), \
                mock.patch.object(remote_state, "release_state", return_value=remote_state.ReleaseState("1.0.0", (), (), 1)), \
                mock.patch.object(remote_state, "ci_state", side_effect=ci_state), \
                mock.patch.object(remote_state, "promotion_state", return_value=None), \
                contextlib.redirect_stdout(output):
            gate.remote_state_check(ROOT)

        self.assertEqual(["ci.yml:next", f"{gate.STABILITY_WORKFLOW}:main"], branches)
        self.assertIn("The last CI run of 'origin' on next is green", output.getvalue())


class ClosingRunTests(unittest.TestCase):
    def test_the_closing_run_reads_the_remote_right_after_the_scan_and_before_the_tests(self) -> None:
        rendered = [command.display for command in gate.closing_commands("abc123", site=False, performance=False)]
        scan = next(index for index, text in enumerate(rendered) if text.endswith(" scan"))
        remote = next(index for index, text in enumerate(rendered) if text.endswith(" remote-state"))
        tests = next(index for index, text in enumerate(rendered) if "dotnet test" in text)

        self.assertLess(scan, remote)
        self.assertLess(remote, tests)

    def test_a_wrapper_gets_its_own_entry_point_and_remote(self) -> None:
        extension = gate.Extension(program="tools/wrapper.py", ci_remote="mirror")
        rendered = [command.display for command in gate.closing_commands("abc123", site=False, extension=extension)]

        self.assertIn("python3 tools/wrapper.py remote-state", rendered)


if __name__ == "__main__":
    unittest.main()
