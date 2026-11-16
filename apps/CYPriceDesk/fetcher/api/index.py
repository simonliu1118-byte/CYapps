"""Price fetcher for CYPriceDesk (Vercel Python runtime).

No web pages and no cache: it only answers HMAC-signed requests from the Worker.
Caching, throttling and the block cooldown live in the Worker (D1).
"""
import hashlib
import hmac
import json
import os
import sys
import time
from functools import wraps

from flask import Flask, jsonify, request

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import feebee  # noqa: E402
import momo  # noqa: E402

MAX_SKEW_SECONDS = 300

app = Flask(__name__)


def sign(secret, timestamp, body):
    """hex HMAC-SHA256 over `timestamp + "." + raw body`; the Worker computes the same."""
    message = str(timestamp).encode() + b"." + body
    return hmac.new(secret.encode(), message, hashlib.sha256).hexdigest()


def signed(view):
    @wraps(view)
    def wrapper():
        secret = os.environ.get("CYPD_FETCHER_SECRET", "")
        if not secret:
            return jsonify(error="fetcher secret not configured"), 503
        timestamp = request.headers.get("x-cypd-timestamp", "")
        signature = request.headers.get("x-cypd-signature", "")
        try:
            fresh = abs(time.time() - int(timestamp)) <= MAX_SKEW_SECONDS
        except ValueError:
            fresh = False
        expected = sign(secret, timestamp, request.get_data())
        if not fresh or not hmac.compare_digest(signature, expected):
            return jsonify(error="unauthorized"), 401
        return view()
    return wrapper


@app.before_request
def post_only():
    if request.method != "POST":
        return jsonify(error="method not allowed"), 405
    return None


@app.after_request
def no_store(response):
    response.headers["Cache-Control"] = "no-store"
    return response


def read_body():
    try:
        data = json.loads(request.get_data() or b"{}")
    except ValueError:
        data = None
    return data if isinstance(data, dict) else {}


@app.post("/v1/momo")
@signed
def momo_search():
    body = read_body()
    try:
        keyword = momo.normalize_keyword(body.get("keyword", ""))
        page = body.get("page", 1)
        if isinstance(page, bool) or not isinstance(page, int) or not 1 <= page <= 5:
            raise ValueError("page must be 1 to 5")
    except ValueError as exc:
        return jsonify(error=str(exc)), 400
    report = momo.fetch_once(keyword, page)
    return jsonify({key: report[key] for key in ("status", "raw_count", "results", "checked_at")})


@app.post("/v1/feebee")
@signed
def feebee_search():
    body = read_body()
    try:
        keyword = momo.normalize_keyword(body.get("keyword", ""))
    except ValueError as exc:
        return jsonify(error=str(exc)), 400
    report = feebee.fetch_once(keyword)
    out = {"status": report["status"], "source_url": report["source_url"], "checked_at": report["checked_at"]}
    if report["status"] == "ok":
        out["markets"] = {market: {"status": state, "results": rows}
                          for market, (state, rows) in report["markets"].items()}
    return jsonify(out)
