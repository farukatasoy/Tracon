#!/usr/bin/env python3
"""Üretilen HTML ve sitemap sözleşmesini, isteğe bağlı nginx HTTP sınırını denetler."""
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
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
        errors.append('HTML bulunamadı')
    for file in files:
        path = file.relative_to(args.dist).as_posix()
        route = '/' if path == 'index.html' else '/' + path.removesuffix('index.html')
        page = Page(file.read_text(encoding='utf-8'))
        error_page = path == '404.html'
        expected = urllib.parse.urljoin(site, route)
        def check(condition, message):
            if not condition:
                errors.append(f'{path}: {message}')
        check(sum(t == 'h1' for t, _ in page.tags) == 1, 'tek H1 gerekli')
        # Şablon kromu H1'in üstünde kendi başlığını basar; sayfa yapısı H1'den başlar.
        headings = [int(t[1]) for t, _ in page.tags if len(t) == 2 and t[0] == 'h' and t[1].isdigit()]
        headings = headings[headings.index(1):] if 1 in headings else headings
        skip = next((f'h{a}→h{b}' for a, b in zip(headings, headings[1:]) if b > a + 1), '')
        check(not skip, f'başlık seviyesi atlıyor: {skip}')
        robots = page.values('meta', 'name', 'robots', 'content')
        check(any('noindex' in v for v in robots) == (error_page or args.preview), 'noindex ortam/hata sayfasıyla uyumsuz')
        canonical = page.values('link', 'rel', 'canonical', 'href')
        check(canonical == ([] if error_page else [expected]), 'canonical uyumsuz')
        if error_page:
            continue
        urls.add(expected)
        titles[page.title].append(path)
        desc = page.values('meta', 'name', 'description', 'content')
        check(len(desc) == 1 and bool(desc[0].strip()), 'description boş veya çoklu')
        descriptions[desc[0] if desc else ''].append(path)
        check(bool(page.title.strip()), 'title boş')
        check(page.values('meta', 'property', 'og:url', 'content') == [expected], 'og:url uyumsuz')
        check(page.values('meta', 'property', 'og:description', 'content') == desc, 'OG description uyumsuz')
        for key, value in [('og:image', 'property'), ('twitter:image', 'name')]:
            images = page.values('meta', value, key, 'content')
            check(len(images) == 1 and images[0].startswith(site) and
                  (args.dist / images[0].removeprefix(site)).is_file(), f'{key} bulunamadı')
        for tag, attrs in page.tags:
            if tag == 'a' and attrs.get('href'):
                target = urllib.parse.urljoin(expected, attrs['href']).split('#')[0].split('?')[0]
                if target.startswith(site) and target != expected:
                    links[expected].add(target)
    for name, groups in [('title', titles), ('description', descriptions)]:
        for value, paths in groups.items():
            if len(paths) > 1:
                errors.append(f'Tekrarlanan {name}: {value}: {paths}')
    sitemap_urls = []
    for file in args.dist.glob('sitemap-*.xml'):
        tree = ET.parse(file)
        if tree.getroot().tag.endswith('urlset'):
            sitemap_urls.extend(e.text for e in tree.iter() if e.tag.endswith('}loc'))
    if set(sitemap_urls) != urls or len(sitemap_urls) != len(urls):
        errors.append('Sitemap ile canonical HTML kümesi eşit değil')
    reachable, pending = set(), [site]
    while pending:
        url = pending.pop()
        if url not in reachable:
            reachable.add(url)
            pending.extend(links[url] & urls - reachable)
    if urls - reachable:
        errors.append(f'Ana sayfadan erişilmeyen sayfalar: {sorted(urls - reachable)}')
    home = (args.dist / 'index.html').read_text(encoding='utf-8')
    blocks = re.findall(r'<script[^>]*type="application/ld\+json"[^>]*>(.*?)</script>', home, re.S)
    data = [json.loads(block) for block in blocks]
    if data != [{'@context': 'https://schema.org', '@type': 'WebSite', 'name': 'Tracon', 'url': site}]:
        errors.append('Ana sayfa WebSite structured data uyumsuz')
    robots = (args.dist / 'robots.txt').read_text(encoding='utf-8')
    if 'Disallow: /' in robots or (not args.preview and f'Sitemap: {site}sitemap-index.xml' not in robots):
        errors.append('robots.txt taramayı veya sitemap keşfini engelliyor')
    print(f'SEO: {len(files)} HTML, {len(sitemap_urls)} sitemap URL, {len(urls & reachable)} erişilebilir sayfa; {len(errors)} hata.')
    for error in errors[:30]:
        print(error)
    return bool(errors)


if __name__ == '__main__':
    sys.exit(main())
