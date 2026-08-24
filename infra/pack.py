#!/usr/bin/env python3
"""
Builds the deployment zips.

    python infra/pack.py api    -> publish/            -> api.zip
    python infra/pack.py web    -> .next/standalone/   -> web.zip

This exists because `Compress-Archive` does not work for this.

Windows PowerShell writes zip entries with **backslash** separators, so a nested
file arrives at the Linux App Service as one filename containing a backslash:

    rsync: failed to stat "/home/site/wwwroot/es\\Microsoft.Data.SqlClient.resources.dll"

The first real deployment half-extracted and failed on exactly that. It failed
loudly, which was luck — the same half-written wwwroot could as easily have
produced an app that starts and then misbehaves in a way nobody traces back to
the zip.

`zipfile` always writes forward slashes. It also lets the API package drop the
Windows and macOS runtime folders, which are dead weight on a Linux host and were
three quarters of the upload.
"""

import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TARGETS = {
    "api": (os.path.join(ROOT, "publish"), "api.zip"),
    "web": (os.path.join(ROOT, ".next", "standalone"), "web.zip"),
}


def skip_dirs(target, root, dirs):
    """Runtimes for other operating systems. App Service Linux needs none of them."""
    if target != "api" or not root.endswith("runtimes"):
        return dirs
    return [d for d in dirs if not (d.startswith("win") or d.startswith("osx"))]


# The delivered plan, with real customer names, driver names and driver phone
# numbers in it. It is git-ignored, seeds an empty database locally, and has no
# business being served as a static file: App Service Login lets every signed-in
# person fetch it, and static files never reach the API that decides who may
# read the register. Deployed, the register lives in the database.
# The migration bundle is built into publish/ when somebody applies a migration
# by hand, and `dotnet publish` does not clean it out. It is a self-contained
# executable that alters the database schema, it is a hundred and sixty
# megabytes, and it has no business riding along to a web server — it quadrupled
# the API package the first time it happened, unnoticed.
NEVER_DEPLOY = {"public/data/ops.json", "migrate", "migrate.exe"}


def pack(target):
    source, name = TARGETS[target]
    if not os.path.isdir(source):
        raise SystemExit(
            f"No {source}.\n"
            + ("  Run: dotnet publish server/Scmos.Api -c Release -o publish"
               if target == "api" else
               "  Run: npm run build, then copy .next/static and public into .next/standalone")
        )

    out = os.path.join(ROOT, name)
    if os.path.exists(out):
        os.remove(out)

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for root, dirs, files in os.walk(source):
            dirs[:] = skip_dirs(target, root, dirs)
            for filename in files:
                full = os.path.join(root, filename)
                entry = os.path.relpath(full, source).replace(os.sep, "/")
                if entry in NEVER_DEPLOY:
                    print(f"  skipped {entry} — real operational data, not a static asset")
                    continue
                archive.write(full, entry)
                count += 1

    # The whole point of the file, asserted rather than assumed.
    with zipfile.ZipFile(out) as archive:
        names = archive.namelist()
        bad = [n for n in names if "\\" in n]
        if bad:
            raise SystemExit(f"{len(bad)} entries contain a backslash — the zip is unusable on Linux")

        leaked = sorted(NEVER_DEPLOY.intersection(names))
        if leaked:
            raise SystemExit(f"{leaked} is in the package — it must never be served as a static file")

        expected = "Scmos.Api.dll" if target == "api" else "server.js"
        if not any(n.endswith(expected) for n in names):
            raise SystemExit(f"{name} has no {expected} — the app would not start")

    size = round(os.path.getsize(out) / 1024 / 1024)
    print(f"{name}  {count} files  {size} MB")
    print(f"  az webapp deploy --name <app> --resource-group rg-scmos --src-path {out} --type zip")


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else ""
    if which not in TARGETS:
        raise SystemExit("Usage: python infra/pack.py [api|web]")
    pack(which)
