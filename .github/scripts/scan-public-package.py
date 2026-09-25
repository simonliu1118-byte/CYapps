#!/usr/bin/env python3
"""Fail CI when a public package appears to contain deployment secrets or production bindings.

Dependency-free scanner for regular files, directories, ZIP files, and tar archives.
It is a release/artifact safety gate, not a substitute for provider-side secret storage.
"""
from __future__ import annotations

import argparse
import io
import pathlib
import re
import sys
import tarfile
import zipfile
from collections.abc import Iterable

MAX_MEMBER_BYTES = 256 * 1024 * 1024

FORBIDDEN_NAMES = [
    re.compile(r"(^|/)(\.env)(\..+)?$", re.I),
    re.compile(r"(^|/).*(?:service[-_]?account|client[-_]?secret|credentials?).*\.json$", re.I),
    re.compile(r"(^|/).*\.(?:pem|p12|pfx|key)$", re.I),
    re.compile(r"(^|/)wrangler\.deploy(?:\.[^/]+)?$", re.I),
    re.compile(r"(^|/)appsettings\.production\.json$", re.I),
]

# Match actual-looking values, not environment-variable names or placeholders.
TEXT_PATTERNS: list[tuple[str, re.Pattern[str]]] = [
    ("private key", re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", re.I)),
    ("Google OAuth client secret", re.compile(r"\bGOCSPX-[A-Za-z0-9_-]{12,}\b")),
    ("Google refresh token", re.compile(r"\b1//[A-Za-z0-9._/-]{20,}\b")),
    ("GitHub token", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b")),
    ("Cloudflare API token assignment", re.compile(r"(?im)\bCLOUDFLARE_API_TOKEN\b\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9._-]{20,}")),
    ("Cloudflare account id assignment", re.compile(r"(?im)\bCLOUDFLARE_ACCOUNT_ID\b\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[0-9a-f]{32}\b")),
    ("OAuth client secret assignment", re.compile(r"(?im)\b(?:GOOGLE_DRIVE_CLIENT_SECRET|GOOGLE_CLIENT_SECRET|client_secret)\b[\"']?\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9._/-]{16,}")),
    ("token encryption key assignment", re.compile(r"(?im)\b(?:GOOGLE_DRIVE_TOKEN_KEY|GOOGLE_TOKEN_KEY|TOKEN_ENCRYPTION_KEY)\b[\"']?\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9+/=_-]{24,}")),
    ("refresh token assignment", re.compile(r"(?im)\brefresh_token\b[\"']?\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9._/-]{20,}")),
    ("Cloudflare D1 concrete database id", re.compile(r"(?is)[\"']database_id[\"']\s*:\s*[\"'](?!__|<)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[\"']")),
    ("Cloudflare service binding concrete service", re.compile(r"(?is)[\"']service[\"']\s*:\s*[\"'](?!__|<)[A-Za-z0-9][A-Za-z0-9._-]{2,}[\"']")),
]


def normalize_name(name: str) -> str:
    return name.replace("\\", "/").lstrip("./")


def scan_name(name: str, findings: list[str]) -> None:
    normalized = normalize_name(name)
    for pattern in FORBIDDEN_NAMES:
        if pattern.search(normalized):
            findings.append(f"forbidden file name: {normalized}")
            return


def decoded_variants(data: bytes) -> Iterable[str]:
    for encoding in ("utf-8", "utf-16-le", "utf-16-be"):
        try:
            text = data.decode(encoding, errors="ignore")
        except Exception:
            continue
        if text:
            yield text


def scan_bytes(label: str, data: bytes, findings: list[str]) -> None:
    for text in decoded_variants(data):
        for description, pattern in TEXT_PATTERNS:
            if pattern.search(text):
                findings.append(f"{description}: {label}")
                return


def read_limited(stream, label: str) -> bytes:
    data = stream.read(MAX_MEMBER_BYTES + 1)
    if len(data) > MAX_MEMBER_BYTES:
        raise RuntimeError(f"refusing to scan oversized archive member (>256 MiB): {label}")
    return data


def scan_payload(name: str, data: bytes, findings: list[str], parent: str = "") -> None:
    label = f"{parent}{name}"
    try:
        if data.startswith(b"PK\x03\x04"):
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                for info in archive.infolist():
                    if info.is_dir():
                        continue
                    child = normalize_name(info.filename)
                    scan_name(child, findings)
                    if info.file_size > MAX_MEMBER_BYTES:
                        raise RuntimeError(f"refusing to scan oversized nested archive member: {label}!{child}")
                    with archive.open(info, "r") as member:
                        child_data = read_limited(member, f"{label}!{child}")
                    scan_payload(child, child_data, findings, parent=f"{label}!")
                return
    except zipfile.BadZipFile:
        pass
    scan_bytes(label, data, findings)


def scan_zip(path: pathlib.Path, findings: list[str]) -> None:
    with zipfile.ZipFile(path) as archive:
        for info in archive.infolist():
            if info.is_dir():
                continue
            name = normalize_name(info.filename)
            scan_name(name, findings)
            if info.file_size > MAX_MEMBER_BYTES:
                raise RuntimeError(f"refusing to scan oversized archive member (>256 MiB): {path}!{name}")
            with archive.open(info, "r") as member:
                data = read_limited(member, f"{path}!{name}")
            scan_payload(name, data, findings, parent=f"{path}!")


def scan_tar(path: pathlib.Path, findings: list[str]) -> None:
    with tarfile.open(path, mode="r:*") as archive:
        for info in archive.getmembers():
            if not info.isfile():
                continue
            name = normalize_name(info.name)
            scan_name(name, findings)
            if info.size > MAX_MEMBER_BYTES:
                raise RuntimeError(f"refusing to scan oversized archive member (>256 MiB): {path}!{name}")
            member = archive.extractfile(info)
            if member is None:
                continue
            with member:
                data = read_limited(member, f"{path}!{name}")
            scan_payload(name, data, findings, parent=f"{path}!")


def scan_file(path: pathlib.Path, findings: list[str]) -> None:
    scan_name(path.name, findings)
    lower = path.name.lower()
    if lower.endswith(".zip"):
        scan_zip(path, findings)
        return
    if lower.endswith((".tar", ".tar.gz", ".tgz", ".tar.bz2", ".tbz2", ".tar.xz", ".txz")):
        scan_tar(path, findings)
        return
    with path.open("rb") as stream:
        data = read_limited(stream, str(path))
    scan_bytes(str(path), data, findings)


def iter_files(path: pathlib.Path) -> Iterable[pathlib.Path]:
    if path.is_file():
        yield path
    elif path.is_dir():
        yield from (p for p in path.rglob("*") if p.is_file())
    else:
        raise FileNotFoundError(path)


def main() -> int:
    parser = argparse.ArgumentParser(description="Scan public Release/Artifact payloads for deployment secrets and production bindings.")
    parser.add_argument("paths", nargs="+", help="Files or directories that will be published")
    args = parser.parse_args()

    findings: list[str] = []
    try:
        for raw in args.paths:
            root = pathlib.Path(raw)
            for file_path in iter_files(root):
                scan_file(file_path, findings)
    except Exception as exc:
        print(f"PUBLIC_ARTIFACT_SCAN_ERROR: {exc}", file=sys.stderr)
        return 2

    if findings:
        print("PUBLIC_ARTIFACT_SCAN_FAILED", file=sys.stderr)
        for finding in sorted(set(findings)):
            print(f" - {finding}", file=sys.stderr)
        print("Refusing to publish. Remove deployment/runtime credentials or production-bound infrastructure metadata from the package.", file=sys.stderr)
        return 1

    print("Public artifact safety scan passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
