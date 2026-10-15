#!/usr/bin/env python3
"""Availability check for candidate product names.

Usage:  scripts/check-name.py Stemma Palimpsest Diptych
        GITHUB_TOKEN=... scripts/check-name.py Stemma      (higher GitHub rate limit)
        scripts/check-name.py --json Stemma

Checks (all best effort, network required):
  * GitHub repositories and users with that exact name
  * package registries: npm, PyPI, crates.io, NuGet
  * domains: .com .org .io .dev .app (DNS lookup)
  * trademarks: tries TMview's search service (classes 9 and 42, filed/registered);
    that service is not a documented API and often refuses scripts, so the script
    always prints the ready-made search links for TMview, EUIPO, USPTO and WIPO.

A script cannot replace a legal trademark search: treat "free" as "no obvious conflict".
"""
import json
import os
import socket
import sys
import urllib.error
import urllib.parse
import urllib.request

UA = {"User-Agent": "Mozilla/5.0 (name-check script)"}


def get(url, headers=None, data=None, timeout=20):
    h = dict(UA)
    h.update(headers or {})
    req = urllib.request.Request(url, headers=h, data=data)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:  # network down, timeout, TLS...
        return None, str(e)


def github(name):
    h = {"Accept": "application/vnd.github+json"}
    tok = os.environ.get("GITHUB_TOKEN")
    if tok:
        h["Authorization"] = "Bearer " + tok
    q = urllib.parse.quote(name + " in:name")
    s, body = get(f"https://api.github.com/search/repositories?q={q}&sort=stars&per_page=5", h)
    if s != 200:
        return {"status": "unknown", "detail": f"HTTP {s} (set GITHUB_TOKEN to avoid rate limits)"}
    items = json.loads(body).get("items", [])
    exact = [i for i in items if i["name"].lower() == name.lower()]
    top = [f'{i["full_name"]} ({i["stargazers_count"]}★)' for i in (exact or items)[:3]]
    us, _ = get(f"https://api.github.com/users/{urllib.parse.quote(name)}", h)
    return {
        "status": "taken" if exact else "free",
        "detail": ("exact: " if exact else "similar: ") + (", ".join(top) or "none")
        + ("; user/org exists" if us == 200 else ""),
    }


def registry(name):
    n = urllib.parse.quote(name.lower())
    out = {}
    for label, url in (
        ("npm", f"https://registry.npmjs.org/{n}"),
        ("pypi", f"https://pypi.org/pypi/{n}/json"),
        ("crates", f"https://crates.io/api/v1/crates/{n}"),
        ("nuget", f"https://api.nuget.org/v3-flatcontainer/{n}/index.json"),
    ):
        s, _ = get(url)
        out[label] = "taken" if s == 200 else "free" if s == 404 else "unknown"
    return out


def domains(name):
    out = {}
    for tld in ("com", "org", "io", "dev", "app"):
        host = f"{name.lower()}.{tld}"
        try:
            socket.gethostbyname(host)
            out[host] = "in use"
        except socket.gaierror:
            out[host] = "no DNS (probably free)"
        except Exception:
            out[host] = "unknown"
    return out


def tmview(name):
    body = json.dumps({
        "page": "1", "pageSize": "30", "criteria": "C", "basicSearch": name,
        "fOffices": [], "fNiceClass": ["9", "42"], "fTMStatus": ["Filed", "Registered"],
    }).encode()
    s, text = get(
        "https://www.tmdn.org/tmview/api/search/results",
        {"Content-Type": "application/json", "Accept": "application/json",
         "Origin": "https://www.tmdn.org", "Referer": "https://www.tmdn.org/tmview/"},
        data=body,
    )
    if s != 200:
        return {"status": "unknown", "detail": f"service refused ({s}); use the links"}
    try:
        tms = json.loads(text).get("tradeMarks", [])
    except ValueError:
        return {"status": "unknown", "detail": "unreadable answer; use the links"}
    rows = []
    for t in tms:
        rows.append(
            f'{t.get("markVerbalElementText", "?")} | {t.get("tmOffice", "?")} {t.get("applicationNumber", "")}'
            f' | {t.get("applicantName", "?")} | class {t.get("niceClass", "?")} | {t.get("tradeMarkStatus", "?")}'
        )
    return {"status": "conflict?" if rows else "none found", "results": rows}


def links(name):
    q = urllib.parse.quote(name)
    return {
        "TMview (9,42)": f"https://www.tmdn.org/tmview/#/tmview/results?page=1&pageSize=30&criteria=B&basicSearch={q}&fNiceClass=9,42&fTMStatus=Filed,Registered",
        "EUIPO": f"https://euipo.europa.eu/eSearch/#basic/1+1+1+1/100+100+100+100/{q}",
        "USPTO": f"https://tmsearch.uspto.gov/search/search-results?query={q}",
        "WIPO Global Brand DB": f"https://branddb.wipo.int/en/quicksearch?by=brandName&v={q}&rows=30&sort=score%20desc",
        "Web": f"https://www.google.com/search?q=%22{q}%22+software",
    }


def main(argv):
    as_json = "--json" in argv
    names = [a for a in argv if not a.startswith("--")]
    if not names:
        print(__doc__)
        return 2
    report = {}
    for name in names:
        report[name] = {
            "github": github(name),
            "registries": registry(name),
            "domains": domains(name),
            "trademarks": tmview(name),
            "links": links(name),
        }
    if as_json:
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return 0
    for name, r in report.items():
        print(f"\n=== {name} ===")
        g = r["github"]
        print(f"GitHub     : {g['status']} - {g['detail']}")
        print("Registries : " + ", ".join(f"{k}={v}" for k, v in r["registries"].items()))
        print("Domains    : " + ", ".join(f"{k}: {v}" for k, v in r["domains"].items()))
        t = r["trademarks"]
        print(f"Trademarks : {t['status']}" + (f" - {t['detail']}" if "detail" in t else ""))
        for row in t.get("results", []):
            print("   " + row)
        print("Check by hand:")
        for k, v in r["links"].items():
            print(f"   {k}: {v}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
