from __future__ import annotations

import sys
import tempfile
import unittest
import urllib.parse
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "app"))

from gdrive import DriveFile, GoogleDriveClient, XLSX_MIME


class FakeDriveClient(GoogleDriveClient):
    def __init__(self, pages):
        super().__init__({"google_drive_folder_id": "folder"})
        self.pages = pages
        self.deleted = []

    def _request(self, method, url, **kwargs):
        if method == "DELETE":
            self.deleted.append(urllib.parse.unquote(url.rsplit("/", 1)[-1]))
            return {}
        query = urllib.parse.parse_qs(urllib.parse.urlparse(url).query)
        token = query.get("pageToken", [""])[0]
        return self.pages[token]


class GoogleDriveTests(unittest.TestCase):
    def test_cleanup_reads_all_pages_and_only_deletes_exact_backup_names(self):
        first = [
            {"id": f"new-{i}", "name": f"CYaccbkup_202609{30-i:02d}.db", "createdTime": f"2026-09-{30-i:02d}T00:00:00Z"}
            for i in range(30)
        ]
        second = [
            {"id": "old-1", "name": "CYaccbkup_20260801.db", "createdTime": "2026-08-01T00:00:00Z"},
            {"id": "old-2", "name": "CYaccbkup_20260701.db", "createdTime": "2026-07-01T00:00:00Z"},
            {"id": "similar", "name": "CYaccbkup_notes.db", "createdTime": "2020-01-01T00:00:00Z"},
        ]
        client = FakeDriveClient({
            "": {"files": first, "nextPageToken": "page-2"},
            "page-2": {"files": second},
        })
        client.cleanup_backups(30)
        self.assertEqual(client.deleted, ["old-1", "old-2"])

    def test_spreadsheet_listing_reads_later_pages(self):
        client = FakeDriveClient({
            "": {"files": [{"id": "1", "name": "A.xlsx", "mimeType": XLSX_MIME}], "nextPageToken": "next"},
            "next": {"files": [{"id": "2", "name": "B.xlsx", "mimeType": XLSX_MIME}]},
        })
        files = client.list_spreadsheet_files()
        self.assertEqual([item.name for item in files], ["A.xlsx", "B.xlsx"])

    def test_refresh_token_is_used_without_reauthorizing(self):
        config = {
            "google_oauth_client": {
                "client_id": "client", "client_secret": "secret",
                "auth_uri": "https://example.invalid/auth", "token_uri": "https://example.invalid/token",
            },
            "google_refresh_token": "plain:cmVmcmVzaA==",
        }
        client = GoogleDriveClient(config)
        calls = []
        client._post_form = lambda url, data: calls.append((url, data)) or {"access_token": "access", "expires_in": 3600}
        self.assertEqual(client._token(), "access")
        self.assertEqual(client._token(), "access")
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][1]["refresh_token"], "refresh")

    def test_drive_download_writes_returned_bytes(self):
        root = Path(tempfile.mkdtemp())
        client = GoogleDriveClient({})
        client._request = lambda *args, **kwargs: b"xlsx-bytes"
        target = client.download_spreadsheet(DriveFile("id", "book.xlsx", XLSX_MIME), root / "book.xlsx")
        self.assertEqual(target.read_bytes(), b"xlsx-bytes")


if __name__ == "__main__":
    unittest.main()
