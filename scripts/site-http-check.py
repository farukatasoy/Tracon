#!/usr/bin/env python3
"""Measures the redirect, error and preview-indexing contract of a local nginx instance."""
import argparse
import http.client
import subprocess
from pathlib import Path
from urllib.parse import urlsplit

parser = argparse.ArgumentParser(
    description='Measures the redirect, error and preview-indexing contract of a local nginx instance.')
parser.add_argument('origin', help='local nginx origin, for example http://127.0.0.1:4175')
args = parser.parse_args()
origin = urlsplit(args.origin)
root = Path(__file__).resolve().parents[1]
site = subprocess.check_output(['node', '--input-type=module', '-e',
    "import {site} from './docs-site/site.config.mjs'; process.stdout.write(site)"], cwd=root, text=True)
host = urlsplit(site).hostname
errors = []
# Third field: the expected Content-Type prefix. The Markdown copies are checked here
# because nginx, not the file, decides their type: the stock mime.types has no .md, and a
# missing default_type serves the copy as application/octet-stream - a check that only
# looks at dist cannot see that.
checks = [('/', 200, 'text/html'), ('/getting-started/', 200, 'text/html'),
          ('/getting-started?ref=seo', 301, None),
          ('/getting-started/index.html', 200, 'text/html'),
          ('/seo-audit-missing-page/', 404, None), ('/404/', 404, None),
          ('/404.html', 404, None), ('/robots.txt', 200, 'text/plain'),
          ('/sitemap-index.xml', 200, 'text/xml'),
          ('/llms.txt', 200, 'text/plain; charset=utf-8'),
          ('/capabilities/index.md', 200, 'text/markdown; charset=utf-8'),
          ('/api/tracon.agentdefinition/index.md', 200, 'text/markdown; charset=utf-8')]
for path, expected, content_type in checks:
    for request_host in [host, 'preview.invalid']:
        connection = http.client.HTTPConnection(origin.hostname, origin.port, timeout=10)
        connection.request('GET', path, headers={'Host': request_host})
        response = connection.getresponse()
        body = response.read().decode()
        if response.status != expected:
            errors.append(f'{path}: {response.status} != {expected}')
        if expected == 301 and response.getheader('Location') != '/getting-started/?ref=seo':
            errors.append(f'{path}: wrong redirect {response.getheader("Location")}')
        if (response.getheader('X-Robots-Tag') == 'noindex') != (request_host != host):
            errors.append(f'{request_host}{path}: wrong X-Robots-Tag')
        if expected == 404 and ('Page not found' not in body or 'content="noindex"' not in body):
            errors.append(f'{path}: error HTML or noindex is missing')
        if content_type and not (response.getheader('Content-Type') or '').startswith(content_type):
            errors.append(f'{path}: Content-Type {response.getheader("Content-Type")} != {content_type}*')
        # A Markdown copy is a near-duplicate of its page and is not in the sitemap; the HTML
        # page owns it. Only this header can say so - the copy's body cannot. Because
        # `add_header` drops the inherited X-Robots-Tag, both headers are checked TOGETHER:
        # if one is added and the other forgotten, the preview host serves indexable Markdown.
        if path.endswith('/index.md'):
            expected_link = f'<{site.rstrip("/")}{path[:-len("index.md")]}>; rel="canonical", </llms.txt>; rel="describedby"'
            if response.getheader('Link') != expected_link:
                errors.append(f'{path}: Link {response.getheader("Link")} != {expected_link}')
        connection.close()
print(f'HTTP: {len(checks) * 2} responses checked; {len(errors)} error(s).')
for error in errors:
    print(error)
raise SystemExit(bool(errors))
