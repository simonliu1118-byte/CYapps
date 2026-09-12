from __future__ import annotations

import calendar
import json
import locale
import os
import re
import shutil
import sys
import unicodedata
from dataclasses import dataclass
from datetime import date, datetime
from pathlib import Path
from typing import Iterable, Optional

APP_NAME = "志遠記帳系統"
APP_VERSION = "V1.0.26"
APP_RELEASE_DATE = "2026/09/11"
DB_FILENAME = "CYaccounting.db"
BACKUP_PREFIX = "CYaccbkup_"
CLEAR_PASSWORD = "19911118"
AMOUNT_DIGITS = 7
MAX_AMOUNT = 9_999_999


class ConfigError(RuntimeError):
    pass


def app_root() -> Path:
    """Return the portable application root.

    In the packaged build sys.executable is runtime/pythonw.exe, so the root is
    its parent folder's parent. During development, this file lives in app/.
    """
    exe = Path(sys.executable).resolve()
    if exe.parent.name.lower() == "runtime":
        return exe.parent.parent
    return Path(__file__).resolve().parent.parent


def data_dir() -> Path:
    p = app_root() / "Data"
    p.mkdir(parents=True, exist_ok=True)
    return p


def config_path() -> Path:
    return data_dir() / "config.json"


def default_database_path() -> Path:
    return data_dir() / DB_FILENAME


def _read_config_file(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict):
        raise ValueError("設定內容不是有效物件")
    return data


def load_config_with_status() -> tuple[dict, str | None]:
    """Load configuration without silently discarding a damaged custom DB path.

    A valid previous copy is restored automatically.  If neither copy can be
    trusted, startup must stop instead of opening a new default database that
    would make existing records appear to have disappeared.
    """
    p = config_path()
    backup = p.with_suffix(".json.bak")
    if not p.exists():
        if not backup.exists():
            return {}, None
        try:
            recovered = _read_config_file(backup)
            safe_copy(backup, p)
            return recovered, "主設定檔遺失，已由上一版安全設定自動復原。"
        except Exception as exc:
            raise ConfigError(f"主設定檔遺失，且安全設定也無法讀取：{exc}") from exc
    try:
        return _read_config_file(p), None
    except Exception as current_error:
        try:
            recovered = _read_config_file(backup)
        except Exception as backup_error:
            raise ConfigError(
                f"設定檔已損壞，安全設定也無法使用。主設定：{current_error}；安全設定：{backup_error}"
            ) from current_error
        corrupt = p.with_name(f"config.corrupt_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json")
        try:
            os.replace(p, corrupt)
            safe_copy(backup, p)
        except Exception as exc:
            raise ConfigError(f"找到安全設定，但無法完成復原：{exc}") from exc
        return recovered, f"設定檔已損壞，已由上一版安全設定復原。損壞檔保留為 {corrupt.name}。"


def load_config() -> dict:
    config, _warning = load_config_with_status()
    return config


def save_config(config: dict) -> None:
    if not isinstance(config, dict):
        raise ConfigError("設定內容格式不正確，未儲存。")
    p = config_path()
    tmp = p.with_suffix(".json.tmp")
    backup = p.with_suffix(".json.bak")
    with tmp.open("w", encoding="utf-8") as f:
        json.dump(config, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())
    if p.exists():
        try:
            _read_config_file(p)
            safe_copy(p, backup)
        except Exception:
            # Never replace a known-good fallback with a damaged current file.
            pass
    elif not backup.exists():
        safe_copy(tmp, backup)
    os.replace(tmp, p)


def parse_date(text: str) -> Optional[date]:
    try:
        return datetime.strptime(text.strip(), "%Y/%m/%d").date()
    except Exception:
        return None


def normalize_date_input(text: str) -> str:
    """Normalize date *format only* without validating whether the date exists.

    Slash characters are ignored while determining whether the user has entered
    a complete YYYYMMDD value.  This deliberately supports partial replacement
    of an existing date, e.g. selecting ``02/09`` in ``2026/02/09`` and typing
    ``0209`` leaves ``2026/0209`` temporarily, which is then normalized to
    ``2026/02/09``.  Calendar validity remains the sole responsibility of
    :func:`parse_date`.
    """
    raw = (text or "").strip()
    digits = raw.replace("/", "")
    if re.fullmatch(r"\d{8}", digits):
        return f"{digits[:4]}/{digits[4:6]}/{digits[6:8]}"
    return raw


def sync_version_marker() -> None:
    """Keep exactly one portable version marker in the application root."""
    root = app_root()
    current = f"{APP_VERSION}.txt"
    pattern = re.compile(r"^V\d+\.\d+\.\d+\.txt$")
    try:
        for path in root.iterdir():
            if path.is_file() and pattern.fullmatch(path.name) and path.name != current:
                path.unlink(missing_ok=True)
        marker = root / current
        content = f"Version: {APP_VERSION}\nDate: {APP_RELEASE_DATE}\n"
        if not marker.exists() or marker.read_text(encoding="utf-8", errors="replace") != content:
            marker.write_text(content, encoding="utf-8")
    except Exception:
        # Version housekeeping must never prevent the accounting program from starting.
        pass


def format_date(d: date) -> str:
    return d.strftime("%Y/%m/%d")


def month_key_from_date(d: date) -> str:
    return d.strftime("%Y/%m")


def parse_month(month: str) -> tuple[int, int]:
    m = re.fullmatch(r"(\d{4})/(\d{2})", month.strip())
    if not m:
        raise ValueError(f"Invalid month: {month}")
    y, mo = int(m.group(1)), int(m.group(2))
    if not 1 <= mo <= 12:
        raise ValueError(f"Invalid month: {month}")
    return y, mo


def month_to_index(month: str) -> int:
    y, m = parse_month(month)
    return y * 12 + (m - 1)


def index_to_month(index: int) -> str:
    y, m0 = divmod(index, 12)
    return f"{y:04d}/{m0 + 1:02d}"


def shift_month(month: str, delta: int) -> str:
    return index_to_month(month_to_index(month) + delta)


def month_bounds(month: str) -> tuple[str, str]:
    y, m = parse_month(month)
    last = calendar.monthrange(y, m)[1]
    return f"{y:04d}/{m:02d}/01", f"{y:04d}/{m:02d}/{last:02d}"


def weighted_units(text: str) -> int:
    units = 0
    for ch in text:
        if unicodedata.east_asian_width(ch) in ("W", "F", "A"):
            units += 2
        else:
            units += 1
    return units


def trim_weighted(text: str, max_units: int) -> str:
    out: list[str] = []
    used = 0
    for ch in text:
        w = 2 if unicodedata.east_asian_width(ch) in ("W", "F", "A") else 1
        if used + w > max_units:
            break
        out.append(ch)
        used += w
    return "".join(out)


def normalize_name(text: str) -> str:
    return text.strip()


def format_amount(value: int | None) -> str:
    if value is None:
        return ""
    return f"{int(value):,}"


def try_set_zh_tw_locale() -> None:
    candidates = [
        "Chinese_Taiwan.950",
        "zh_TW.UTF-8",
        "zh_TW.utf8",
        "Chinese (Traditional)_Taiwan.950",
    ]
    for name in candidates:
        try:
            locale.setlocale(locale.LC_COLLATE, name)
            return
        except Exception:
            continue


def text_sort_key(text: str) -> str:
    try:
        return locale.strxfrm(text or "")
    except Exception:
        return text or ""


def is_month_locked(month: str, locked_through: str | None) -> bool:
    if not locked_through:
        return False
    return month_to_index(month) <= month_to_index(locked_through)


def safe_copy(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_suffix(dst.suffix + ".tmp")
    shutil.copy2(src, tmp)
    os.replace(tmp, dst)


@dataclass(frozen=True)
class SaveResult:
    ok: bool
    message: str
    transaction_id: int | None = None
