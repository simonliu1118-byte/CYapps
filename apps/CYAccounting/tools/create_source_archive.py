from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import subprocess
import tarfile
from pathlib import Path


APP_PATH = "apps/CYAccounting/"
WORKFLOW_PATHS = (
    ".github/workflows/cyaccounting-build.yml",
    ".github/workflows/cyaccounting-release.yml",
)
EXCLUDED_PATHS = {f"{APP_PATH}SOURCE_SHA256.txt"}


def repository_root() -> Path:
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        check=True,
        capture_output=True,
        text=True,
    )
    return Path(result.stdout.strip()).resolve()


def tracked_source_files(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files", "--", APP_PATH, *WORKFLOW_PATHS],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    return sorted(
        path for path in result.stdout.splitlines()
        if path and path not in EXCLUDED_PATHS
    )


def create_archive(root: Path, output: Path) -> str:
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for relative in tracked_source_files(root):
                    payload = (root / relative).read_bytes()
                    info = tarfile.TarInfo(relative)
                    info.size = len(payload)
                    info.mode = 0o644
                    info.uid = 0
                    info.gid = 0
                    info.uname = ""
                    info.gname = ""
                    info.mtime = 0
                    archive.addfile(info, io.BytesIO(payload))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + ".sha256").write_text(
        f"{digest}  {output.name}\n", encoding="ascii"
    )
    return digest


def main() -> int:
    parser = argparse.ArgumentParser(description="建立可重現的 CYAccounting 原始碼封存檔")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = repository_root()
    output = args.output if args.output.is_absolute() else (Path.cwd() / args.output)
    digest = create_archive(root, output.resolve())
    print(f"SOURCE_SHA256={digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
