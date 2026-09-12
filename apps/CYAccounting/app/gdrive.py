from __future__ import annotations

import base64
import hashlib
import json
import os
import re
import secrets
import socket
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from typing import Callable

from util import app_root


DRIVE_SCOPES = [
    "https://www.googleapis.com/auth/drive.readonly",
    "https://www.googleapis.com/auth/drive.file",
]
DRIVE_API = "https://www.googleapis.com/drive/v3"
DRIVE_UPLOAD_API = "https://www.googleapis.com/upload/drive/v3"
GOOGLE_SHEET_MIME = "application/vnd.google-apps.spreadsheet"
XLSX_MIME = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
XLS_MIME = "application/vnd.ms-excel"
XLSM_MIME = "application/vnd.ms-excel.sheet.macroEnabled.12"
FOLDER_MIME = "application/vnd.google-apps.folder"
DEFAULT_BACKUP_FOLDER = "CYAccounting"


class GoogleDriveError(RuntimeError):
    pass


@dataclass
class DriveFile:
    id: str
    name: str
    mime_type: str
    modified_time: str = ""
    size: str = ""

    @property
    def type_label(self) -> str:
        if self.mime_type == GOOGLE_SHEET_MIME:
            return "Google 試算表"
        if self.mime_type == XLSX_MIME:
            return "Excel (.xlsx)"
        if self.mime_type == XLS_MIME:
            return "Excel (.xls)"
        if self.mime_type == XLSM_MIME:
            return "Excel (.xlsm)"
        return self.mime_type


def _b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("ascii").rstrip("=")


def _dpapi_protect(text: str) -> str:
    """Protect a token with Windows DPAPI; use a marked fallback off Windows.

    The production package runs on Windows.  The fallback keeps source tests
    usable on other platforms and is never presented as encryption.
    """
    if not text:
        return ""
    if sys.platform != "win32":
        return "plain:" + base64.b64encode(text.encode("utf-8")).decode("ascii")
    import ctypes
    from ctypes import wintypes

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_byte))]

    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    crypt32.CryptProtectData.argtypes = [
        ctypes.POINTER(DATA_BLOB), wintypes.LPCWSTR, ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(DATA_BLOB),
    ]
    crypt32.CryptProtectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
    kernel32.LocalFree.restype = wintypes.HLOCAL
    raw = text.encode("utf-8")
    buf = ctypes.create_string_buffer(raw)
    in_blob = DATA_BLOB(len(raw), ctypes.cast(buf, ctypes.POINTER(ctypes.c_byte)))
    out_blob = DATA_BLOB()
    if not crypt32.CryptProtectData(
        ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)
    ):
        raise GoogleDriveError("無法使用 Windows 保護 Google 授權資訊。")
    try:
        data = ctypes.string_at(out_blob.pbData, out_blob.cbData)
        return "dpapi:" + base64.b64encode(data).decode("ascii")
    finally:
        kernel32.LocalFree(ctypes.cast(out_blob.pbData, wintypes.HLOCAL))


def _dpapi_unprotect(value: str) -> str:
    if not value:
        return ""
    if value.startswith("plain:"):
        return base64.b64decode(value[6:].encode("ascii")).decode("utf-8")
    if not value.startswith("dpapi:"):
        # Compatibility with a future/plain migration path.  Never silently
        # treat arbitrary data as a valid token if decoding fails.
        return ""
    if sys.platform != "win32":
        return ""
    import ctypes
    from ctypes import wintypes

    class DATA_BLOB(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_byte))]

    crypt32 = ctypes.windll.crypt32
    kernel32 = ctypes.windll.kernel32
    crypt32.CryptUnprotectData.argtypes = [
        ctypes.POINTER(DATA_BLOB), ctypes.POINTER(wintypes.LPWSTR), ctypes.POINTER(DATA_BLOB),
        ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(DATA_BLOB),
    ]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = [wintypes.HLOCAL]
    kernel32.LocalFree.restype = wintypes.HLOCAL
    encrypted = base64.b64decode(value[6:].encode("ascii"))
    buf = ctypes.create_string_buffer(encrypted)
    in_blob = DATA_BLOB(len(encrypted), ctypes.cast(buf, ctypes.POINTER(ctypes.c_byte)))
    out_blob = DATA_BLOB()
    if not crypt32.CryptUnprotectData(
        ctypes.byref(in_blob), None, None, None, None, 0, ctypes.byref(out_blob)
    ):
        return ""
    try:
        return ctypes.string_at(out_blob.pbData, out_blob.cbData).decode("utf-8")
    finally:
        kernel32.LocalFree(ctypes.cast(out_blob.pbData, wintypes.HLOCAL))


def import_oauth_client_json(path: Path, config: dict) -> dict:
    try:
        raw = json.loads(Path(path).read_text(encoding="utf-8"))
    except Exception as exc:
        raise GoogleDriveError(f"無法讀取 OAuth 憑證 JSON：{exc}") from exc
    section = raw.get("installed") or raw.get("web")
    if not isinstance(section, dict):
        raise GoogleDriveError("OAuth 憑證格式不正確，請使用 Google Cloud 的「桌面應用程式」OAuth JSON。")
    required = ("client_id", "auth_uri", "token_uri")
    missing = [key for key in required if not section.get(key)]
    if missing:
        raise GoogleDriveError("OAuth 憑證缺少必要欄位：" + ", ".join(missing))
    client = {
        "client_id": section["client_id"],
        "client_secret": section.get("client_secret", ""),
        "auth_uri": section["auth_uri"],
        "token_uri": section["token_uri"],
        "project_id": section.get("project_id", ""),
    }
    config["google_oauth_client"] = client
    # A newly imported client invalidates tokens issued to a previous client.
    config.pop("google_refresh_token", None)
    config.pop("google_drive_user", None)
    config.pop("google_drive_folder_id", None)
    config.pop("google_drive_last_backup_name", None)
    return client


class _OAuthCallbackHandler(BaseHTTPRequestHandler):
    result: dict[str, str] = {}

    def do_GET(self):  # noqa: N802
        query = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
        type(self).result = {k: v[0] for k, v in query.items() if v}
        body = (
            "<!doctype html><meta charset='utf-8'><title>志遠記帳系統</title>"
            "<style>body{font-family:Segoe UI,Microsoft JhengHei,sans-serif;margin:50px;line-height:1.7}</style>"
            "<h2>Google Drive 授權完成</h2><p>可以關閉此瀏覽器分頁並回到志遠記帳系統。</p>"
        ).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):  # noqa: A003
        return


class GoogleDriveClient:
    def __init__(self, config: dict):
        self.config = config
        self._access_token = ""
        self._expires_at = 0.0

    @property
    def has_client(self) -> bool:
        client = self.config.get("google_oauth_client")
        return isinstance(client, dict) and bool(client.get("client_id") and client.get("token_uri"))

    @property
    def is_connected(self) -> bool:
        return bool(self.has_client and _dpapi_unprotect(self.config.get("google_refresh_token", "")))

    @property
    def user_label(self) -> str:
        user = self.config.get("google_drive_user")
        if isinstance(user, dict):
            email = str(user.get("emailAddress") or "").strip()
            name = str(user.get("displayName") or "").strip()
            if email and name:
                return f"{name}（{email}）"
            return email or name
        return ""

    def disconnect(self) -> None:
        for key in (
            "google_refresh_token",
            "google_drive_user",
            "google_drive_folder_id",
            "google_drive_last_backup_name",
        ):
            self.config.pop(key, None)
        self._access_token = ""
        self._expires_at = 0.0

    def authorize(self, pump_events: Callable[[], None] | None = None, timeout_seconds: int = 180) -> None:
        if not self.has_client:
            raise GoogleDriveError("請先匯入 Google OAuth 桌面應用程式憑證 JSON。")
        client = self.config["google_oauth_client"]
        _OAuthCallbackHandler.result = {}
        try:
            server = HTTPServer(("127.0.0.1", 0), _OAuthCallbackHandler)
        except OSError as exc:
            raise GoogleDriveError(f"無法建立本機 OAuth 回呼服務：{exc}") from exc
        server.timeout = 0.2
        port = server.server_port
        redirect_uri = f"http://127.0.0.1:{port}/"
        state = secrets.token_urlsafe(24)
        verifier = secrets.token_urlsafe(64)
        challenge = _b64url(hashlib.sha256(verifier.encode("ascii")).digest())
        params = {
            "client_id": client["client_id"],
            "redirect_uri": redirect_uri,
            "response_type": "code",
            "scope": " ".join(DRIVE_SCOPES),
            "access_type": "offline",
            "prompt": "consent",
            "state": state,
            "code_challenge": challenge,
            "code_challenge_method": "S256",
        }
        auth_url = client["auth_uri"] + "?" + urllib.parse.urlencode(params)
        if not webbrowser.open(auth_url):
            server.server_close()
            raise GoogleDriveError("無法開啟瀏覽器進行 Google 授權。")
        deadline = time.monotonic() + timeout_seconds
        try:
            while time.monotonic() < deadline and not _OAuthCallbackHandler.result:
                server.handle_request()
                if pump_events:
                    pump_events()
        finally:
            server.server_close()
        result = dict(_OAuthCallbackHandler.result)
        if not result:
            raise GoogleDriveError("Google 授權等待逾時。")
        if result.get("state") != state:
            raise GoogleDriveError("Google 授權回傳狀態不符，已取消連線。")
        if result.get("error"):
            raise GoogleDriveError("Google 授權未完成：" + result.get("error", "unknown_error"))
        code = result.get("code")
        if not code:
            raise GoogleDriveError("Google 授權沒有回傳授權碼。")
        token_data = {
            "client_id": client["client_id"],
            "code": code,
            "code_verifier": verifier,
            "grant_type": "authorization_code",
            "redirect_uri": redirect_uri,
        }
        if client.get("client_secret"):
            token_data["client_secret"] = client["client_secret"]
        token = self._post_form(client["token_uri"], token_data)
        refresh = token.get("refresh_token")
        if not refresh:
            raise GoogleDriveError("Google 未提供可持續使用的授權權杖，請重新授權。")
        self.config["google_refresh_token"] = _dpapi_protect(str(refresh))
        self._access_token = str(token.get("access_token") or "")
        self._expires_at = time.time() + int(token.get("expires_in", 3600)) - 60
        try:
            self.config["google_drive_user"] = self.about_user()
        except Exception:
            self.config["google_drive_user"] = {}

    @staticmethod
    def _post_form(url: str, data: dict) -> dict:
        body = urllib.parse.urlencode(data).encode("utf-8")
        req = urllib.request.Request(url, data=body, headers={"Content-Type": "application/x-www-form-urlencoded"})
        try:
            with urllib.request.urlopen(req, timeout=30) as res:
                return json.loads(res.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", "replace")
            raise GoogleDriveError(f"Google OAuth 連線失敗（HTTP {exc.code}）：{detail[:300]}") from exc
        except Exception as exc:
            raise GoogleDriveError(f"Google OAuth 連線失敗：{exc}") from exc

    def _token(self) -> str:
        if self._access_token and time.time() < self._expires_at:
            return self._access_token
        if not self.has_client:
            raise GoogleDriveError("尚未設定 Google OAuth 憑證。")
        refresh = _dpapi_unprotect(self.config.get("google_refresh_token", ""))
        if not refresh:
            raise GoogleDriveError("尚未連結 Google Drive。")
        client = self.config["google_oauth_client"]
        data = {
            "client_id": client["client_id"],
            "refresh_token": refresh,
            "grant_type": "refresh_token",
        }
        if client.get("client_secret"):
            data["client_secret"] = client["client_secret"]
        token = self._post_form(client["token_uri"], data)
        access = str(token.get("access_token") or "")
        if not access:
            raise GoogleDriveError("Google 授權已失效，請到設定重新連結。")
        self._access_token = access
        self._expires_at = time.time() + int(token.get("expires_in", 3600)) - 60
        return access

    def _request(
        self,
        method: str,
        url: str,
        *,
        data: bytes | None = None,
        content_type: str | None = None,
        expect_json: bool = True,
        timeout: int = 60,
    ):
        headers = {"Authorization": f"Bearer {self._token()}"}
        if content_type:
            headers["Content-Type"] = content_type
        req = urllib.request.Request(url, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as res:
                payload = res.read()
                if expect_json:
                    return json.loads(payload.decode("utf-8")) if payload else {}
                return payload
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", "replace")
            if exc.code == 401:
                self._access_token = ""
                self._expires_at = 0
            raise GoogleDriveError(f"Google Drive API 錯誤（HTTP {exc.code}）：{detail[:400]}") from exc
        except Exception as exc:
            raise GoogleDriveError(f"Google Drive 連線失敗：{exc}") from exc

    def about_user(self) -> dict:
        query = urllib.parse.urlencode({"fields": "user(displayName,emailAddress)"})
        data = self._request("GET", f"{DRIVE_API}/about?{query}")
        return data.get("user") or {}

    def list_spreadsheet_files(self, search: str = "") -> list[DriveFile]:
        mime_query = (
            f"(mimeType='{GOOGLE_SHEET_MIME}' or mimeType='{XLSX_MIME}' or mimeType='{XLSM_MIME}' or mimeType='{XLS_MIME}')"
        )
        q = f"trashed=false and {mime_query}"
        if search.strip():
            safe = search.strip().replace("'", "\\'")
            q += f" and name contains '{safe}'"
        raw_files = self._list_all_files(q, "modifiedTime desc", "id,name,mimeType,modifiedTime,size")
        return [
            DriveFile(
                id=str(item.get("id", "")),
                name=str(item.get("name", "")),
                mime_type=str(item.get("mimeType", "")),
                modified_time=str(item.get("modifiedTime", "")),
                size=str(item.get("size", "")),
            )
            for item in raw_files
            if item.get("id") and item.get("name")
        ]

    def _list_all_files(self, q: str, order_by: str, file_fields: str) -> list[dict]:
        """Read every Drive result page while guarding against a repeated token."""
        files: list[dict] = []
        page_token = ""
        seen_tokens: set[str] = set()
        while True:
            params = {
                "q": q,
                "pageSize": "100",
                "orderBy": order_by,
                "spaces": "drive",
                "fields": f"nextPageToken,files({file_fields})",
            }
            if page_token:
                params["pageToken"] = page_token
            data = self._request("GET", f"{DRIVE_API}/files?{urllib.parse.urlencode(params)}")
            files.extend(item for item in data.get("files", []) if isinstance(item, dict))
            next_token = str(data.get("nextPageToken") or "")
            if not next_token:
                break
            if next_token in seen_tokens:
                raise GoogleDriveError("Google Drive 分頁資料異常，已停止讀取以避免重複處理。")
            seen_tokens.add(next_token)
            page_token = next_token
        return files

    def download_spreadsheet(self, file: DriveFile, destination: Path) -> Path:
        destination = Path(destination)
        destination.parent.mkdir(parents=True, exist_ok=True)
        if file.mime_type == GOOGLE_SHEET_MIME:
            params = urllib.parse.urlencode({"mimeType": XLSX_MIME})
            url = f"{DRIVE_API}/files/{urllib.parse.quote(file.id)}/export?{params}"
        else:
            url = f"{DRIVE_API}/files/{urllib.parse.quote(file.id)}?alt=media"
        data = self._request("GET", url, expect_json=False, timeout=120)
        destination.write_bytes(data)
        return destination

    def _find_or_create_backup_folder(self) -> str:
        folder_id = str(self.config.get("google_drive_folder_id") or "").strip()
        if folder_id:
            return folder_id
        safe_name = DEFAULT_BACKUP_FOLDER.replace("'", "\\'")
        q = f"trashed=false and mimeType='{FOLDER_MIME}' and name='{safe_name}'"
        params = {"q": q, "pageSize": "10", "fields": "files(id,name)"}
        try:
            data = self._request("GET", f"{DRIVE_API}/files?{urllib.parse.urlencode(params)}")
            files = data.get("files", [])
            if files:
                folder_id = str(files[0]["id"])
        except GoogleDriveError:
            folder_id = ""
        if not folder_id:
            body = json.dumps({"name": DEFAULT_BACKUP_FOLDER, "mimeType": FOLDER_MIME}).encode("utf-8")
            data = self._request(
                "POST",
                f"{DRIVE_API}/files?fields=id",
                data=body,
                content_type="application/json; charset=UTF-8",
            )
            folder_id = str(data.get("id") or "")
        if not folder_id:
            raise GoogleDriveError("無法建立 Google Drive 備份資料夾。")
        self.config["google_drive_folder_id"] = folder_id
        return folder_id

    def _start_resumable_upload(self, url: str, method: str, metadata: dict, size: int, mime_type: str) -> str:
        headers = {
            "Authorization": f"Bearer {self._token()}",
            "Content-Type": "application/json; charset=UTF-8",
            "X-Upload-Content-Type": mime_type,
            "X-Upload-Content-Length": str(size),
        }
        body = json.dumps(metadata, ensure_ascii=False).encode("utf-8")
        req = urllib.request.Request(url, data=body, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=60) as res:
                location = res.headers.get("Location") or ""
                if not location:
                    raise GoogleDriveError("Google Drive 未回傳續傳上傳位置。")
                return location
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", "replace")
            raise GoogleDriveError(f"Google Drive 建立上傳工作失敗（HTTP {exc.code}）：{detail[:400]}") from exc
        except GoogleDriveError:
            raise
        except Exception as exc:
            raise GoogleDriveError(f"Google Drive 建立上傳工作失敗：{exc}") from exc

    def _finish_resumable_upload(self, session_url: str, payload: bytes, mime_type: str) -> dict:
        headers = {
            "Content-Type": mime_type,
            "Content-Length": str(len(payload)),
        }
        req = urllib.request.Request(session_url, data=payload, headers=headers, method="PUT")
        try:
            with urllib.request.urlopen(req, timeout=180) as res:
                body = res.read()
                return json.loads(body.decode("utf-8")) if body else {}
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", "replace")
            raise GoogleDriveError(f"Google Drive 上傳備份失敗（HTTP {exc.code}）：{detail[:400]}") from exc
        except Exception as exc:
            raise GoogleDriveError(f"Google Drive 上傳備份失敗：{exc}") from exc

    @staticmethod
    def _multipart(metadata: dict, payload: bytes, mime_type: str) -> tuple[bytes, str]:
        boundary = "===============CYAccounting_" + secrets.token_hex(12)
        meta = json.dumps(metadata, ensure_ascii=False).encode("utf-8")
        body = (
            f"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n".encode("ascii")
            + meta
            + f"\r\n--{boundary}\r\nContent-Type: {mime_type}\r\n\r\n".encode("ascii")
            + payload
            + f"\r\n--{boundary}--\r\n".encode("ascii")
        )
        return body, f"multipart/related; boundary={boundary}"

    def upload_backup(self, backup_path: Path) -> str:
        backup_path = Path(backup_path)
        if not backup_path.exists():
            raise GoogleDriveError("找不到要同步的本機備份檔。")
        folder_id = self._find_or_create_backup_folder()
        name = backup_path.name
        safe = name.replace("'", "\\'")
        q = f"trashed=false and name='{safe}' and '{folder_id}' in parents"
        params = {"q": q, "pageSize": "10", "fields": "files(id,name)"}
        data = self._request("GET", f"{DRIVE_API}/files?{urllib.parse.urlencode(params)}")
        existing = data.get("files", [])
        payload = backup_path.read_bytes()
        mime_type = "application/x-sqlite3"
        if existing:
            file_id = str(existing[0]["id"])
            session = self._start_resumable_upload(
                f"{DRIVE_UPLOAD_API}/files/{urllib.parse.quote(file_id)}?uploadType=resumable&fields=id",
                "PATCH", {}, len(payload), mime_type,
            )
        else:
            session = self._start_resumable_upload(
                f"{DRIVE_UPLOAD_API}/files?uploadType=resumable&fields=id",
                "POST", {"name": name, "parents": [folder_id]}, len(payload), mime_type,
            )
        result = self._finish_resumable_upload(session, payload, mime_type)
        file_id = str(result.get("id") or (existing[0].get("id") if existing else ""))
        self.config["google_drive_last_backup_name"] = name
        self.config["google_drive_last_backup_date"] = time.strftime("%Y/%m/%d")
        return file_id

    def cleanup_backups(self, keep: int = 30) -> None:
        folder_id = self._find_or_create_backup_folder()
        q = f"trashed=false and '{folder_id}' in parents and name contains 'CYaccbkup_'"
        files = [
            item for item in self._list_all_files(q, "createdTime desc", "id,name,createdTime")
            if re.fullmatch(r"CYaccbkup_\d{8}\.db", str(item.get("name") or ""))
        ]
        files.sort(
            key=lambda item: (str(item.get("createdTime") or ""), str(item.get("name") or "")),
            reverse=True,
        )
        for item in files[max(0, int(keep)):]:
            file_id = item.get("id")
            if file_id:
                try:
                    self._request("DELETE", f"{DRIVE_API}/files/{urllib.parse.quote(str(file_id))}")
                except GoogleDriveError:
                    # Retention cleanup must never turn a successful upload into
                    # a failed backup operation.
                    pass


def upload_backup_and_cleanup(backup_path: Path, config: dict, keep: int = 30) -> dict:
    """Upload one verified local backup using an isolated config snapshot."""
    snapshot = dict(config)
    client = GoogleDriveClient(snapshot)
    if not client.is_connected:
        raise GoogleDriveError("Google Drive 尚未完成授權。")
    client.upload_backup(Path(backup_path))
    client.cleanup_backups(keep)
    return snapshot


def newest_local_backup(backup_dir: Path) -> Path | None:
    files = sorted(Path(backup_dir).glob("CYaccbkup_*.db"), key=lambda p: p.name, reverse=True)
    return files[0] if files else None
