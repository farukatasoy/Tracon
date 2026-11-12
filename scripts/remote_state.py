"""Remote state the local gates cannot see.

A closing run that is green on this machine says nothing about two things that live elsewhere:
whether the last release tag actually reached nuget.org, and what the last CI run of the branch
said. A release run that failed on a flaky test stayed unnoticed for ten days because no local
gate looked at either.

Each question has three answers, never two. "Missing" and "red" fail or warn; "unreachable"
only warns, because a closing run on a train or behind a broken proxy must still be able to
finish. A check that turns the network into a hard dependency would be switched off the first
time it got in the way.
"""

from __future__ import annotations

import dataclasses
import http.client
import json
import os
import pathlib
import re
import shutil
import ssl
import subprocess
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
from collections.abc import Callable, Sequence

NUGET_FLAT_CONTAINER = "https://api.nuget.org/v3-flatcontainer"
LOOKUP_TIMEOUT_SECONDS = 15
GIVE_UP_AFTER_UNREACHABLE = 3
"""Consecutive packages nuget.org could not answer for before the rest are not asked: 21 lookups of 15 seconds each is five minutes of waiting for a network that is not there."""
CI_TIMEOUT_SECONDS = 25

GITHUB_API = "https://api.github.com"
CI_BRANCH = "next"
"""The branch the public CI decides on: the mirror pushes here and `main` only follows a green commit of it."""
PROMOTED_BRANCH = "main"
GREEN_EVENTS = frozenset({"push", "workflow_dispatch"})
"""The events whose run can make a commit green; a pull request run proves a different tree."""

Fetcher = Callable[[str], "tuple[int | None, str]"]
"""Reads a URL and returns the HTTP status and the body; the status is `None` when nothing answered."""
Runner = Callable[..., subprocess.CompletedProcess[str]]


@dataclasses.dataclass(frozen=True)
class PackageLookup:
    """What nuget.org said about one package."""

    package_id: str
    versions: tuple[str, ...] | None
    """The published versions, lower-cased; `None` when the lookup itself failed."""

    @property
    def unreachable(self) -> bool:
        return self.versions is None


@dataclasses.dataclass(frozen=True)
class CiState:
    """The last CI run of a branch."""

    status: str
    conclusion: str
    url: str
    sha: str

    @property
    def is_red(self) -> bool:
        return self.status == "completed" and self.conclusion in {"failure", "timed_out", "startup_failure"}


def _request(url: str, headers: dict[str, str] | None) -> urllib.request.Request:
    """A request whose `Authorization` header does not follow a redirect.

    `urllib` copies a request's headers onto the request it makes after a redirect, to whatever host the redirect names,
    and does not copy the unredirected ones. A credential goes in the second kind.
    """
    request = urllib.request.Request(url, headers={name: value for name, value in (headers or {}).items() if name.lower() != "authorization"})

    for name, value in (headers or {}).items():
        if name.lower() == "authorization":
            request.add_unredirected_header(name, value)

    return request


def fetch(url: str, headers: dict[str, str] | None = None) -> tuple[int | None, str]:
    """Reads a URL: Python's own TLS first, `curl` when this machine's Python has no trust store.

    A Python installed without its certificates fails every HTTPS call with a verification error.
    Reading that as "no network" would switch the check off on exactly the machines that have one.
    """
    try:
        with urllib.request.urlopen(_request(url, headers), timeout=LOOKUP_TIMEOUT_SECONDS) as response:
            return response.status, response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        return error.code, ""
    except urllib.error.URLError as error:
        if isinstance(error.reason, ssl.SSLCertVerificationError) and shutil.which("curl"):
            return _fetch_with_curl(url, headers)
        return None, ""
    except (TimeoutError, OSError, http.client.HTTPException, UnicodeDecodeError):
        # A cut-off answer (IncompleteRead) and a body that is not UTF-8 are no answer either.
        return None, ""


def _fetch_with_curl(url: str, headers: dict[str, str] | None = None) -> tuple[int | None, str]:
    header_arguments = [argument for name, value in (headers or {}).items() for argument in ("--header", f"{name}: {value}")]

    try:
        completed = subprocess.run(
            ["curl", "--silent", "--max-time", str(LOOKUP_TIMEOUT_SECONDS), *header_arguments, "--write-out", "\n%{http_code}", url],
            capture_output=True,
            text=True,
            timeout=LOOKUP_TIMEOUT_SECONDS + 5,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None, ""

    body, _, status = completed.stdout.rpartition("\n")

    return (int(status), body) if status.isdigit() and status != "000" else (None, "")


def github_headers(url: str) -> dict[str, str]:
    """The headers of a GitHub API read: the JSON media type, and the token of the environment for the API host only.

    The token raises the rate limit of an anonymous read (60 an hour). It is offered to the API host only, and `fetch` sends it
    so that a redirect does not carry it elsewhere. A run in GitHub Actions has `GITHUB_TOKEN`; a developer's shell may have `GH_TOKEN`.
    """
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "tracon-remote-state"}
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")

    if token and urllib.parse.urlsplit(url).hostname == urllib.parse.urlsplit(GITHUB_API).hostname:
        headers["Authorization"] = f"Bearer {token}"

    return headers


def fetch_github(url: str) -> tuple[int | None, str]:
    """`fetch` with the GitHub headers."""
    return fetch(url, github_headers(url))


def lookup_package(package_id: str, *, fetcher: Fetcher = fetch) -> PackageLookup:
    """Reads the published versions of a package from the anonymous flat container."""
    status, body = fetcher(f"{NUGET_FLAT_CONTAINER}/{package_id.lower()}/index.json")

    if status == 404:
        # A package nobody has pushed is an answer, not a failure to ask.
        return PackageLookup(package_id, ())

    if status != 200:
        return PackageLookup(package_id, None)

    try:
        document = json.loads(body)
    except ValueError:
        return PackageLookup(package_id, None)

    versions = document.get("versions") if isinstance(document, dict) else None

    if not isinstance(versions, list):
        return PackageLookup(package_id, None)

    return PackageLookup(package_id, tuple(str(version).lower() for version in versions))


def packable_ids_at(root: pathlib.Path, ref: str, *, runner: Runner = subprocess.run) -> list[str] | None:
    """The packable projects as `ref` had them, or `None` when git cannot say.

    The set a release pushed is the set its tag held. A project added to the working tree since is not part of that
    release, and counting it would turn every later closing run red until the next tag.
    """
    def git(*arguments: str) -> subprocess.CompletedProcess[str] | None:
        try:
            return runner(["git", *arguments], cwd=root, capture_output=True, text=True, timeout=30, check=False)
        except (OSError, subprocess.TimeoutExpired):
            return None

    listing = git("ls-tree", "--name-only", f"{ref}:src")

    if listing is None or listing.returncode != 0:
        return None

    ids: list[str] = []

    for name in listing.stdout.split():
        project = git("show", f"{ref}:src/{name}/{name}.csproj")

        if project is None or project.returncode != 0 or "<IsPackable>false</IsPackable>" in project.stdout:
            continue

        ids.append(name)

    return ids


@dataclasses.dataclass(frozen=True)
class ReleaseState:
    """Whether a release version reached nuget.org."""

    version: str
    missing: tuple[str, ...]
    unreachable: tuple[str, ...]
    published: int


def release_state(
    package_ids: Sequence[str],
    version: str,
    *,
    fetcher: Fetcher = fetch,
) -> ReleaseState:
    """Looks every package of `version` up and sorts the answers."""
    missing: list[str] = []
    unreachable: list[str] = []
    published = 0
    wanted = version.lower()

    silent = 0

    for package_id in package_ids:
        if silent >= GIVE_UP_AFTER_UNREACHABLE:
            unreachable.append(package_id)
            continue

        lookup = lookup_package(package_id, fetcher=fetcher)

        if lookup.unreachable:
            silent += 1
            unreachable.append(package_id)
            continue

        silent = 0

        if wanted in (lookup.versions or ()):
            published += 1
        else:
            missing.append(package_id)

    return ReleaseState(version, tuple(missing), tuple(unreachable), published)


@dataclasses.dataclass(frozen=True)
class ReleaseCommits:
    """The commit every package of one version carries, as nuget.org's nuspec names it.

    Three answers per package, never two: it carries a commit, it is not there, or nuget.org could not say.
    """

    version: str
    commits: dict[str, str | None]
    """Package id -> the commit its nuspec names (lower-cased); `None` when the nuspec names no commit."""
    absent: tuple[str, ...]
    unreachable: tuple[str, ...]

    @property
    def distinct(self) -> frozenset[str | None]:
        """Every commit the published packages name; one element when the release is clean."""
        return frozenset(self.commits.values())

    def foreign(self, commit: str) -> dict[str, str | None]:
        """The published packages whose nuspec names a commit other than `commit`."""
        wanted = commit.lower()
        return {package: named for package, named in self.commits.items() if named != wanted}


def nuspec_commit(body: str) -> str | None:
    """The `commit` attribute of a nuspec's `<repository>` element, lower-cased; `None` when it names none.

    Raises `ValueError` for a body that is not a nuspec document.
    """
    try:
        root = ET.fromstring(body)
    except ET.ParseError as error:
        raise ValueError(f"the nuspec is not XML: {error}") from error

    for element in root.iter():
        if element.tag.rpartition("}")[2] == "repository":
            commit = element.get("commit")
            return commit.lower() if commit else None

    return None


def release_commits(
    package_ids: Sequence[str],
    version: str,
    *,
    fetcher: Fetcher = fetch,
) -> ReleaseCommits:
    """Reads the nuspec of every package of `version` and sorts the answers.

    After `GIVE_UP_AFTER_UNREACHABLE` unanswered packages in a row the rest are not asked, as in `release_state`.
    """
    commits: dict[str, str | None] = {}
    absent: list[str] = []
    unreachable: list[str] = []
    silent = 0
    wanted = version.lower()

    for package_id in package_ids:
        if silent >= GIVE_UP_AFTER_UNREACHABLE:
            unreachable.append(package_id)
            continue

        lowered = package_id.lower()
        status, body = fetcher(f"{NUGET_FLAT_CONTAINER}/{lowered}/{wanted}/{lowered}.nuspec")

        if status == 404:
            silent = 0
            absent.append(package_id)
            continue

        commit: str | None = None
        answered = status == 200

        if answered:
            try:
                commit = nuspec_commit(body)
            except ValueError:
                answered = False

        if not answered:
            silent += 1
            unreachable.append(package_id)
            continue

        silent = 0
        commits[package_id] = commit

    return ReleaseCommits(version, commits, tuple(absent), tuple(unreachable))


def slug_of(url: str) -> str | None:
    """`owner/name` of a repository URL, from an https URL, an ssh URL or an ssh alias; `None` when it names none.

    A path on disk (`/tmp/mirror.git`) names no repository: the tests and a rehearsal point at one.
    """
    text = url.strip()

    if "://" in text:
        path = urllib.parse.urlsplit(text).path
    elif ":" in text and not text.startswith("/"):
        path = text.partition(":")[2]
    else:
        return None

    segments = path.strip("/").removesuffix(".git").split("/")

    return "/".join(segments) if len(segments) == 2 and all(segments) else None


def repository_slug(root: pathlib.Path, remote: str, *, runner: Runner = subprocess.run) -> str | None:
    """`owner/name` of a git remote, from an https URL, an ssh URL or an ssh alias; `None` when there is none."""
    try:
        completed = runner(
            ["git", "remote", "get-url", remote], cwd=root, capture_output=True, text=True, timeout=10, check=False)
    except (OSError, subprocess.TimeoutExpired):
        return None

    return slug_of(completed.stdout) if completed.returncode == 0 else None


def ci_state(
    root: pathlib.Path,
    remote: str = "origin",
    branch: str = CI_BRANCH,
    *,
    workflow: str = "ci.yml",
    runner: Runner = subprocess.run,
    which: Callable[[str], str | None] = shutil.which,
) -> CiState | None:
    """The last run of `workflow` on `branch` of `remote` that decided something, or `None` when it cannot be read.

    A skipped or cancelled run decided nothing (a repository that mirrors its code elsewhere skips every
    run), so the newest run that finished with a verdict, or is still running, is the one that counts.
    """
    if which("gh") is None:
        return None

    slug = repository_slug(root, remote, runner=runner)

    if slug is None:
        return None

    try:
        completed = runner(
            [
                "gh", "run", "list", "-R", slug, "--branch", branch, "--workflow", workflow, "--limit", "10",
                "--json", "status,conclusion,url,headSha",
            ],
            cwd=root,
            capture_output=True,
            text=True,
            timeout=CI_TIMEOUT_SECONDS,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None

    if completed.returncode != 0:
        return None

    try:
        runs = json.loads(completed.stdout)
    except ValueError:
        return None

    if not isinstance(runs, list):
        return None

    for latest in runs:
        if not isinstance(latest, dict):
            continue

        conclusion = str(latest.get("conclusion", ""))

        if str(latest.get("status", "")) == "completed" and conclusion in {"skipped", "cancelled", "neutral", "stale"}:
            continue

        return CiState(
            status=str(latest.get("status", "")),
            conclusion=conclusion,
            url=str(latest.get("url", "")),
            sha=str(latest.get("headSha", ""))[:8],
        )

    return None


@dataclasses.dataclass(frozen=True)
class CiRun:
    """One run of a workflow, with the fields that decide whether its commit counts as green."""

    sha: str
    event: str
    branch: str
    repository: str
    """`owner/name` of the repository the run's commit came from; empty when GitHub no longer knows it."""
    status: str
    conclusion: str
    url: str


def ci_runs(
    slug: str,
    branch: str,
    *,
    workflow: str = "ci.yml",
    fetcher: Fetcher = fetch_github,
) -> list[CiRun] | None:
    """The newest runs of `workflow` on `branch` of `slug`, from the public Actions API; `None` when it could not be read.

    Only the newest 100 runs are read. `exclude_pull_requests` does not drop the runs of pull requests (it empties the
    `pull_requests` list of each run), so a caller decides by `event`, as `green_shas` does. The anonymous limit is 60 reads an
    hour, so a caller that runs often sets `GITHUB_TOKEN`.
    """
    query = urllib.parse.urlencode({"branch": branch, "per_page": 100, "exclude_pull_requests": "true"})
    status, body = fetcher(f"{GITHUB_API}/repos/{slug}/actions/workflows/{workflow}/runs?{query}")

    if status != 200:
        return None

    try:
        document = json.loads(body)
    except ValueError:
        return None

    entries = document.get("workflow_runs") if isinstance(document, dict) else None

    if not isinstance(entries, list):
        return None

    runs: list[CiRun] = []

    for entry in entries:
        if not isinstance(entry, dict):
            continue

        repository = entry.get("head_repository")

        runs.append(CiRun(
            sha=str(entry.get("head_sha", "")),
            event=str(entry.get("event", "")),
            branch=str(entry.get("head_branch", "")),
            repository=str(repository.get("full_name", "")) if isinstance(repository, dict) else "",
            status=str(entry.get("status", "")),
            conclusion=str(entry.get("conclusion") or ""),
            url=str(entry.get("html_url", "")),
        ))

    return runs


def green_shas(runs: Sequence[CiRun] | None, *, slug: str, branch: str) -> set[str]:
    """The commits with a run that passed, on `branch` of `slug` itself.

    Only a completed `success` of a push or a manual run counts. A run that failed, was cancelled or skipped, is
    still going, belongs to a pull request, ran in a fork whose branch has the same name, or ran on another branch
    decides nothing: a commit is green because one run of its own passed, not because the others did not fail.
    """
    wanted = slug.lower()

    return {
        run.sha
        for run in runs or ()
        if run.status == "completed"
        and run.conclusion == "success"
        and run.event in GREEN_EVENTS
        and run.branch == branch
        and run.repository.lower() == wanted
    }


@dataclasses.dataclass(frozen=True)
class Promotion:
    """How far the promoted branch is behind the CI branch, and the newest commit it could move to."""

    behind: int
    candidate: str | None
    """The newest green commit between the two branches; `None` when there is none."""


def promotion_state(
    slug: str,
    *,
    fetcher: Fetcher = fetch_github,
    base: str = PROMOTED_BRANCH,
    head: str = CI_BRANCH,
    workflow: str = "ci.yml",
) -> Promotion | None:
    """Reads, without a clone, how many commits `base` is behind `head` and which green commit would be promoted.

    `None` when the compare or the run list could not be read: a closing run offline must still finish.
    """
    status, body = fetcher(f"{GITHUB_API}/repos/{slug}/compare/{base}...{head}")

    if status != 200:
        return None

    try:
        document = json.loads(body)
    except ValueError:
        return None

    commits = document.get("commits") if isinstance(document, dict) else None
    behind = document.get("ahead_by") if isinstance(document, dict) else None

    if not isinstance(commits, list) or not isinstance(behind, int):
        return None

    runs = ci_runs(slug, head, workflow=workflow, fetcher=fetcher)

    if runs is None:
        return None

    green = green_shas(runs, slug=slug, branch=head)
    ahead = [str(commit.get("sha", "")) for commit in commits if isinstance(commit, dict)]
    candidate = next((sha for sha in reversed(ahead) if sha in green), None)

    return Promotion(behind, candidate)
