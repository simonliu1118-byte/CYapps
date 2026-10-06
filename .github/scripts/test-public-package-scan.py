#!/usr/bin/env python3
"""Regression checks for public package token detection."""
from __future__ import annotations

import importlib.util
import io
import pathlib

path = pathlib.Path(__file__).with_name("scan-public-package.py")
spec = importlib.util.spec_from_file_location("public_package_scanner", path)
assert spec and spec.loader
scanner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scanner)

# Synthetic strings are deliberately not real credentials.
sample = b"1//" + b"A" * 28
for label, payload in (
    ("UTF-8", b"prefix " + sample + b" suffix"),
    ("UTF-16LE", b" \x00" + b"".join(bytes((v, 0)) for v in sample) + b" \x00"),
    ("UTF-16BE", b"\x00 " + b"".join(bytes((0, v)) for v in sample) + b"\x00 "),
):
    findings: list[str] = []
    assert scanner.scan_chunk(label, payload, findings), label
    assert findings == [f"Google refresh token: {label}"], findings

# NUL removal would concatenate these unrelated spans into the sample token,
# even though they are neither raw ASCII nor a contiguous UTF-16 string.
false_positive = b"prefix " + b"1//" + b"\x00" * 9 + b"A" * 28 + b" suffix"
findings = []
assert not scanner.scan_chunk("native-runtime.dll", false_positive, findings)
assert findings == []

# Binary tables in official OpenCV native DLLs (OpenCvSharp4.runtime.win
# 4.13.0.20260627) contain "1//" followed by "/" and "." runs. A base64url
# refresh token never contains those characters, so these must not block.
for blob in (
    b"r/IG5dJ/1/////H69JtLIH9NJf3S6uq9ISh0CxdL8gt46iO2",
    b"89::;<=>1//..//0225555566667789:;<==>>>>1//..///",
):
    findings = []
    assert not scanner.scan_chunk("OpenCvSharpExtern.dll", blob, findings), blob
    assert findings == []

# Pinning an official dependency is an exact-byte exception. A modified file
# with the same name must still fail the ordinary token scan.
findings = []
scanner.scan_stream("package.zip!PresentationNative_cor3.dll", io.BytesIO(sample), findings)
assert findings == ["Google refresh token: package.zip!PresentationNative_cor3.dll"]
print("Public package scanner regressions passed.")
