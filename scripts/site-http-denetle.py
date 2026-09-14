#!/usr/bin/env python3
"""Yerel nginx örneğinin redirect, hata ve preview indeksleme sözleşmesini ölçer."""
import argparse
import http.client
import subprocess
from pathlib import Path
from urllib.parse import urlsplit

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('origin', help='Yerel nginx origin, ör. http://127.0.0.1:4175')
args = parser.parse_args()
origin = urlsplit(args.origin)
root = Path(__file__).resolve().parents[1]
site = subprocess.check_output(['node', '--input-type=module', '-e',
    "import {site} from './docs-site/site.config.mjs'; process.stdout.write(site)"], cwd=root, text=True)
host = urlsplit(site).hostname
errors = []
# Üçüncü alan: beklenen Content-Type öneki. Markdown kopyası burada denetlenir çünkü
# tipi dosya değil nginx belirler: stok mime.types'ta .md yoktur ve eksik bir
# default_type kopyayı application/octet-stream olarak yollar — dist'e bakan hiçbir
# kapı bunu göremez.
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
            errors.append(f'{path}: yanlış redirect {response.getheader("Location")}')
        if (response.getheader('X-Robots-Tag') == 'noindex') != (request_host != host):
            errors.append(f'{request_host}{path}: yanlış X-Robots-Tag')
        if expected == 404 and ('Page not found' not in body or 'content="noindex"' not in body):
            errors.append(f'{path}: hata HTML/noindex eksik')
        if content_type and not (response.getheader('Content-Type') or '').startswith(content_type):
            errors.append(f'{path}: Content-Type {response.getheader("Content-Type")} != {content_type}*')
        connection.close()
print(f'HTTP: {len(checks) * 2} yanıt denetlendi; {len(errors)} hata.')
for error in errors:
    print(error)
raise SystemExit(bool(errors))
