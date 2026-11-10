#!/usr/bin/env python3
"""Checks the built HTML and sitemap contract, and optionally the nginx HTTP boundary."""
import argparse
from collections import defaultdict
from html.parser import HTMLParser
import json
import re
from pathlib import Path
import subprocess
import sys
import urllib.parse
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


class Page(HTMLParser):
    def __init__(self, text):
        super().__init__(convert_charrefs=True)
        self.tags = []
        self.title = ''
        self.in_title = False
        self.feed(text)

    def handle_starttag(self, tag, attrs):
        self.tags.append((tag, dict(attrs)))
        if tag == 'title':
            self.in_title = True

    def handle_endtag(self, tag):
        if tag == 'title':
            self.in_title = False

    def handle_data(self, data):
        if self.in_title:
            self.title += data

    def values(self, tag, key, value, attr):
        return [a.get(attr, '') for t, a in self.tags if t == tag and a.get(key) == value]


def robots_groups(robots):
    """Splits robots.txt into groups of (user-agent list, rule list)."""
    groups, agents, rules = [], [], []
    for line in robots.splitlines():
        line = line.split('#')[0].strip()
        if not line or ':' not in line:
            continue
        field, value = (part.strip() for part in line.split(':', 1))
        field = field.lower()
        if field == 'user-agent':
            if rules:
                groups.append((agents, rules))
                agents, rules = [], []
            agents.append(value)
        elif field in ('allow', 'disallow'):
            rules.append((field, value))
    if agents:
        groups.append((agents, rules))
    return groups


def check_robots(robots, site, preview):
    """Looks for a rule that closes the whole site and for a missing sitemap announcement.

    Closing a sub-path is legitimate - the pagefind index is not a page - so searching the
    text for 'Disallow: /' is not enough: it would also match the '/pagefind/index/' line and
    make it impossible to write a crawler policy. The rules are read group by group; only
    the exact value '/' closes the site.
    """
    errors = []
    for agents, rules in robots_groups(robots):
        for field, value in rules:
            if field == 'disallow' and value == '/':
                errors.append(f"robots.txt blocks the whole site for {', '.join(agents)}")
    if not preview and f'Sitemap: {site}sitemap-index.xml' not in robots:
        errors.append('robots.txt does not announce the sitemap')
    return errors


def main():
    parser = argparse.ArgumentParser(
        description='Checks the built HTML and sitemap contract, and optionally the nginx HTTP boundary.')
    parser.add_argument('--dist', type=Path, default=ROOT / 'docs-site/dist')
    parser.add_argument('--preview', action='store_true')
    args = parser.parse_args()
    site = subprocess.check_output(['node', '--input-type=module', '-e',
        "import {siteUrl} from './docs-site/site.config.mjs'; process.stdout.write(siteUrl)"], cwd=ROOT, text=True)
    errors = []
    titles, descriptions = defaultdict(list), defaultdict(list)
    urls, links = set(), defaultdict(set)
    files = list(args.dist.rglob('*.html'))
    if not files:
        errors.append('no HTML found')
    for file in files:
        path = file.relative_to(args.dist).as_posix()
        route = '/' if path == 'index.html' else '/' + path.removesuffix('index.html')
        page = Page(file.read_text(encoding='utf-8'))
        error_page = path == '404.html'
        expected = urllib.parse.urljoin(site, route)
        def check(condition, message):
            if not condition:
                errors.append(f'{path}: {message}')
        check(sum(t == 'h1' for t, _ in page.tags) == 1, 'exactly one H1 is required')
        # The template chrome prints its own heading above the H1; the page structure starts at the H1.
        headings = [int(t[1]) for t, _ in page.tags if len(t) == 2 and t[0] == 'h' and t[1].isdigit()]
        headings = headings[headings.index(1):] if 1 in headings else headings
        skip = next((f'h{a}→h{b}' for a, b in zip(headings, headings[1:]) if b > a + 1), '')
        check(not skip, f'heading level jumps: {skip}')
        robots = page.values('meta', 'name', 'robots', 'content')
        check(any('noindex' in v for v in robots) == (error_page or args.preview), 'noindex does not match the environment or error page')
        canonical = page.values('link', 'rel', 'canonical', 'href')
        check(canonical == ([] if error_page else [expected]), 'canonical mismatch')
        # llmstxt.org suggests two link relations: `alternate` to the Markdown copy and
        # `describedby` to the enclosing llms.txt. The copy EXISTING is not enough - the
        # address it declares in its own body must be the page's canonical. When the copies
        # were first generated, the home page's copy was written under `/index/index.md`
        # with an address that did not exist, and no check that looked from inside the site
        # could see it. Addresses are root-relative inside the site; only the canonical is absolute.
        check(page.values('link', 'rel', 'describedby', 'href') == [urllib.parse.urlparse(site).path + 'llms.txt'],
              'the llms.txt describedby link is missing')
        alternate = [href for href in page.values('link', 'rel', 'alternate', 'href')
                     if href.endswith('.md')]
        if path == 'index.html' or error_page:
            check(not alternate, 'this page must not have a markdown copy')
        else:
            markdown = args.dist / route.lstrip('/') / 'index.md'
            check(alternate == [route + 'index.md'], 'markdown alternate link mismatch')
            if not markdown.is_file():
                check(False, 'the markdown copy was not generated')
            else:
                declared = re.search(r'^> Page: (\S+)$', markdown.read_text(encoding='utf-8'), re.M)
                check(declared is not None and declared.group(1) == expected,
                      f'the markdown copy declares the wrong address: {declared.group(1) if declared else "none"}')
        if error_page:
            continue
        urls.add(expected)
        titles[page.title].append(path)
        desc = page.values('meta', 'name', 'description', 'content')
        check(len(desc) == 1 and bool(desc[0].strip()), 'description is empty or repeated')
        descriptions[desc[0] if desc else ''].append(path)
        check(bool(page.title.strip()), 'title is empty')
        check(page.values('meta', 'property', 'og:url', 'content') == [expected], 'og:url mismatch')
        check(page.values('meta', 'property', 'og:description', 'content') == desc, 'OG description mismatch')
        for key, value in [('og:image', 'property'), ('twitter:image', 'name')]:
            images = page.values('meta', value, key, 'content')
            check(len(images) == 1 and images[0].startswith(site) and
                  (args.dist / images[0].removeprefix(site)).is_file(), f'{key} not found')
        for tag, attrs in page.tags:
            if tag == 'a' and attrs.get('href'):
                target = urllib.parse.urljoin(expected, attrs['href']).split('#')[0].split('?')[0]
                if target.startswith(site) and target != expected:
                    links[expected].add(target)
    for name, groups in [('title', titles), ('description', descriptions)]:
        for value, paths in groups.items():
            if len(paths) > 1:
                errors.append(f'Duplicate {name}: {value}: {paths}')
    sitemap_urls = []
    for file in args.dist.glob('sitemap-*.xml'):
        tree = ET.parse(file)
        if tree.getroot().tag.endswith('urlset'):
            sitemap_urls.extend(e.text for e in tree.iter() if e.tag.endswith('}loc'))
    if set(sitemap_urls) != urls or len(sitemap_urls) != len(urls):
        errors.append('The sitemap and the canonical HTML set are not equal')
    reachable, pending = set(), [site]
    while pending:
        url = pending.pop()
        if url not in reachable:
            reachable.add(url)
            pending.extend(links[url] & urls - reachable)
    if urls - reachable:
        errors.append(f'Pages that the home page does not reach: {sorted(urls - reachable)}')
    home = (args.dist / 'index.html').read_text(encoding='utf-8')
    blocks = re.findall(r'<script[^>]*type="application/ld\+json"[^>]*>(.*?)</script>', home, re.S)
    data = [json.loads(block) for block in blocks]
    if data != [{'@context': 'https://schema.org', '@type': 'WebSite', 'name': 'Tracon', 'url': site}]:
        errors.append('The home page WebSite structured data does not match')
    robots = (args.dist / 'robots.txt').read_text(encoding='utf-8')
    errors.extend(check_robots(robots, site, args.preview))
    print(f'SEO: {len(files)} HTML, {len(sitemap_urls)} sitemap URL, {len(urls & reachable)} reachable page(s); {len(errors)} error(s).')
    for error in errors[:30]:
        print(error)
    return bool(errors)


if __name__ == '__main__':
    sys.exit(main())
