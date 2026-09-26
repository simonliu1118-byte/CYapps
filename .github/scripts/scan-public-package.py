#!/usr/bin/env python3
"""Fail CI when a public package contains likely secrets or production bindings.

Dependency-free, streaming scanner for files, directories, ZIP files and tar archives.
It scans final public payloads and intentionally does not require production secrets.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import pathlib
import re
import sys
import tarfile
import zipfile
from collections.abc import Iterable
from typing import BinaryIO

CHUNK_BYTES = 4 * 1024 * 1024
OVERLAP_BYTES = 64 * 1024
MAX_NESTED_ARCHIVE_BYTES = 256 * 1024 * 1024
# Only decode actual alternating printable ASCII/NUL runs as UTF-16. Removing
# every NUL byte in a native DLL can join unrelated binary spans into a token.
UTF16LE_ASCII = re.compile(rb"(?:[\x20-\x7e]\x00){8,}")
UTF16BE_ASCII = re.compile(rb"(?:\x00[\x20-\x7e]){8,}")
TRUSTED_DEPENDENCY_BYTES = 2 * 1024 * 1024
# Microsoft .NET 10 Windows Desktop runtime from the pinned CYEnvelope
# win-x64 publish. This official native DLL includes a token-shaped string.
# Only this exact file digest is exempt; changed DLLs are scanned normally.
TRUSTED_DEPENDENCIES = {
    "PresentationNative_cor3.dll": "42e2e543c17e1b37499baf00d956433b8afb655348ec0fae89c926fb7d149b6e",
}

FORBIDDEN_NAMES = [
    re.compile(r"(^|/)(\.env)(\..+)?$", re.I),
    re.compile(r"(^|/).*(?:service[-_]?account|client[-_]?secret|credentials?).*\.json$", re.I),
    re.compile(r"(^|/).*\.(?:pem|p12|pfx|key)$", re.I),
    re.compile(r"(^|/)wrangler\.deploy(?:\.[^/]+)?$", re.I),
    re.compile(r"(^|/)appsettings\.production\.json$", re.I),
]

# Byte regexes avoid decoding large EXE/DLL files into several huge strings.
GENERAL_PATTERNS: list[tuple[str, re.Pattern[bytes]]] = [
    # Require a complete PEM-shaped private key, not merely OpenSSL/Qt format strings
    # such as "-----BEGIN PRIVATE KEY-----" embedded in standard TLS libraries.
    ("private key", re.compile(br"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----\s+[A-Za-z0-9+/=\r\n\t ]{80,}?-----END (?:RSA |EC |OPENSSH )?PRIVATE KEY-----", re.I | re.S)),
    ("Google OAuth client secret", re.compile(br"\bGOCSPX-[A-Za-z0-9_-]{12,}\b")),
    ("Google refresh token", re.compile(br"\b1//[A-Za-z0-9._/-]{20,}\b")),
    ("GitHub token", re.compile(br"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b")),
    ("Cloudflare API token assignment", re.compile(br"\bCLOUDFLARE_API_TOKEN\b\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9._-]{20,}", re.I | re.M)),
    ("Cloudflare account id assignment", re.compile(br"\bCLOUDFLARE_ACCOUNT_ID\b\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[0-9a-f]{32}\b", re.I | re.M)),
    ("OAuth client secret assignment", re.compile(br"\b(?:GOOGLE_DRIVE_CLIENT_SECRET|GOOGLE_CLIENT_SECRET|client_secret)\b[\"']?\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9._/-]{16,}", re.I | re.M)),
    ("token encryption key assignment", re.compile(br"\b(?:GOOGLE_DRIVE_TOKEN_KEY|GOOGLE_TOKEN_KEY|TOKEN_ENCRYPTION_KEY)\b[\"']?\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9+/=_-]{24,}", re.I | re.M)),
    ("refresh token assignment", re.compile(br"\brefresh_token\b[\"']?\s*[:=]\s*[\"']?(?!\$\{|\$\(|__|<)[A-Za-z0-9._/-]{20,}", re.I | re.M)),
]

WRANGLER_PATTERNS: list[tuple[str, re.Pattern[bytes]]] = [
    ("Cloudflare D1 concrete database id", re.compile(br"[\"']database_id[\"']\s*:\s*[\"'](?!__|<)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[\"']", re.I | re.S)),
    ("Cloudflare service binding concrete service", re.compile(br"[\"']service[\"']\s*:\s*[\"'](?!__|<)[A-Za-z0-9][A-Za-z0-9._-]{2,}[\"']", re.I | re.S)),
]


def normalize_name(name: str) -> str:
    return name.replace("\\", "/").lstrip("./")


def scan_name(name: str, findings: list[str]) -> None:
    normalized = normalize_name(name)
    for pattern in FORBIDDEN_NAMES:
        if pattern.search(normalized):
            findings.append(f"forbidden file name: {normalized}")
            return


def patterns_for(label: str) -> list[tuple[str, re.Pattern[bytes]]]:
    normalized = normalize_name(label).lower()
    contextual = WRANGLER_PATTERNS if "/wrangler." in f"/{normalized}" or normalized.startswith("wrangler.") else []
    return [*GENERAL_PATTERNS, *contextual]


def scan_chunk(label: str, data: bytes, findings: list[str]) -> bool:
    variants = [data]
    # Full NUL removal can create a token that does not exist as a string in
    # the payload. Inspect contiguous UTF-16 ASCII strings instead.
    if b"\x00" in data:
        variants.extend(match.group()[::2] for match in UTF16LE_ASCII.finditer(data))
        variants.extend(match.group()[1::2] for match in UTF16BE_ASCII.finditer(data))
    for description, pattern in patterns_for(label):
        for variant in variants:
            if pattern.search(variant):
                findings.append(f"{description}: {label}")
                return True
    return False


def scan_stream(label: str, stream: BinaryIO, findings: list[str]) -> None:
    overlap = b""
    basename = normalize_name(label).split("!")[-1].split("/")[-1]
    if basename in TRUSTED_DEPENDENCIES:
        candidate = stream.read(TRUSTED_DEPENDENCY_BYTES + 1)
        if len(candidate) <= TRUSTED_DEPENDENCY_BYTES and hashlib.sha256(candidate).hexdigest() == TRUSTED_DEPENDENCIES[basename]:
            return
        if scan_chunk(label, candidate, findings):
            return
        overlap = candidate[-OVERLAP_BYTES:]
    while True:
        chunk = stream.read(CHUNK_BYTES)
        if not chunk:
            break
        data = overlap + chunk
        if scan_chunk(label, data, findings):
            return
        overlap = data[-OVERLAP_BYTES:]


def scan_nested_zip(label: str, data: bytes, findings: list[str]) -> None:
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for info in archive.infolist():
                if info.is_dir():
                    continue
                child = normalize_name(info.filename)
                scan_name(child, findings)
                child_label = f"{label}!{child}"
                with archive.open(info, "r") as member:
                    if child.lower().endswith(".zip") and info.file_size <= MAX_NESTED_ARCHIVE_BYTES:
                        scan_nested_zip(child_label, member.read(), findings)
                    else:
                        scan_stream(child_label, member, findings)
    except zipfile.BadZipFile:
        scan_chunk(label, data, findings)


def scan_zip(path: pathlib.Path, findings: list[str]) -> None:
    with zipfile.ZipFile(path) as archive:
        for info in archive.infolist():
            if info.is_dir():
                continue
            name = normalize_name(info.filename)
            scan_name(name, findings)
            label = f"{path}!{name}"
            with archive.open(info, "r") as member:
                if name.lower().endswith(".zip") and info.file_size <= MAX_NESTED_ARCHIVE_BYTES:
                    scan_nested_zip(label, member.read(), findings)
                else:
                    scan_stream(label, member, findings)


def scan_tar(path: pathlib.Path, findings: list[str]) -> None:
    with tarfile.open(path, mode="r:*") as archive:
        for info in archive.getmembers():
            if not info.isfile():
                continue
            name = normalize_name(info.name)
            scan_name(name, findings)
            member = archive.extractfile(info)
            if member is None:
                continue
            with member:
                scan_stream(f"{path}!{name}", member, findings)


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
        scan_stream(str(path), stream, findings)


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
