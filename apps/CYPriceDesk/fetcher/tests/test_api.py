"""Signature checks for the fetcher API; momo/Feebee lookups are replaced by fixtures (no network)."""
import json
import sys
import time
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from api.index import app, sign  # noqa: E402

SECRET = "test-secret"
BODY = json.dumps({"keyword": "羅技 M331", "page": 1}, ensure_ascii=False, separators=(",", ":")).encode()
MOMO_REPORT = {"status": "ok", "raw_count": 1, "checked_at": "2026-11-16T00:00:00+00:00", "page": 1,
               "results": [{"title": "羅技 M331 黑", "code": "9956761", "price": 699,
                            "relevance": 3, "match_label": "完全符合",
                            "url": "https://www.momoshop.com.tw/product/9956761"}]}


def post(headers, body=BODY, path="/v1/momo"):
    return app.test_client().post(path, data=body, headers=headers, content_type="application/json")


def signed_headers(timestamp=None, body=BODY, secret=SECRET):
    timestamp = str(int(time.time()) if timestamp is None else timestamp)
    return {"x-cypd-timestamp": timestamp, "x-cypd-signature": sign(secret, timestamp, body)}


class FetcherApiTests(unittest.TestCase):
    def test_valid_signature_is_processed(self):
        with patch.dict("os.environ", {"CYPD_FETCHER_SECRET": SECRET}), \
             patch("momo.fetch_once", return_value=MOMO_REPORT) as fetch:
            response = post(signed_headers())
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.get_json()["results"][0]["price"], 699)
        fetch.assert_called_once_with("羅技 M331", 1)

    def test_bad_or_stale_signature_is_rejected(self):
        with patch.dict("os.environ", {"CYPD_FETCHER_SECRET": SECRET}), patch("momo.fetch_once") as fetch:
            wrong = post(signed_headers(secret="other-secret"))
            stale = post(signed_headers(timestamp=int(time.time()) - 301))
            missing = post({})
        self.assertEqual([wrong.status_code, stale.status_code, missing.status_code], [401, 401, 401])
        fetch.assert_not_called()

    def test_unset_secret_returns_503(self):
        with patch.dict("os.environ", {}, clear=True):
            self.assertEqual(post(signed_headers()).status_code, 503)

    def test_signature_matches_worker_vector(self):
        # Same secret / timestamp / body as tests/worker-search.test.js.
        self.assertEqual(sign(SECRET, 1700000000, BODY),
                         "8812112ccbd89f91d690718ca58c7b05960d5cda9471f8a99bcc2b1aaf6dc91d")

    def test_get_is_405_and_unknown_path_is_404(self):
        client = app.test_client()
        self.assertEqual(client.get("/v1/momo").status_code, 405)
        self.assertEqual(client.get("/").status_code, 405)
        with patch.dict("os.environ", {"CYPD_FETCHER_SECRET": SECRET}):
            self.assertEqual(post(signed_headers(), path="/v1/other").status_code, 404)


if __name__ == "__main__":
    unittest.main()
