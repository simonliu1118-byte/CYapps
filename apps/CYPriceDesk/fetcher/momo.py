"""momo search: keyword validation, one request, result parsing.

Moved from the sandbox ScraplingProbe app.py as-is. Caching, throttling and the
cooldown after a block are handled by the Worker (D1), not here.
"""
import re
import unicodedata
from datetime import datetime, timezone

import never_primp as primp

ENDPOINT = "https://apisearch.momoshop.com.tw/momoSearchCloud/moec/textSearch"
PRICE_RE = re.compile(r"^\s*(?:NT\$|\$)?\s*([1-9]\d{0,2}(?:,\d{3})*|[1-9]\d{0,6})\s*$")


def normalize_keyword(raw):
    keyword = " ".join(str(raw).split())
    if not 2 <= len(keyword) <= 80 or any(ord(c) < 32 for c in keyword):
        raise ValueError("關鍵字必須為 2～80 個字元")
    return keyword


def parse_price(raw):
    if raw is None or isinstance(raw, bool):
        return None
    found = PRICE_RE.fullmatch(str(raw))
    if not found:
        return None
    amount = int(found.group(1).replace(",", ""))
    return amount if 0 < amount <= 10000000 else None


def parse_results(data, keyword):
    if not isinstance(data, dict) or data.get("success") is not True:
        return "provider_refused", []
    search = data.get("rtnSearchData")
    goods = search.get("goodsInfoList") if isinstance(search, dict) else None
    if not isinstance(goods, list):
        return "format_changed", []
    seen, found = set(), []
    tokens = unicodedata.normalize("NFKC", keyword).casefold().split()
    model_terms = [t for t in tokens if any(c.isdigit() for c in t) and len(t) >= 3]
    for item in goods:
        if not isinstance(item, dict):
            continue
        code = str(item.get("goodsCode") or "").strip()
        name = str(item.get("goodsName") or "").strip()
        # momo marketplace (3P) goods use TP-prefixed identifiers, not numeric codes.
        # Do not drop these legitimate offers when building a competitor price list.
        if not re.fullmatch(r"(?:\d{5,14}|TP\d{10,18})", code) or code in seen or not name:
            continue
        # Preserve the products momo returned; relevance is only an ordering hint.
        normalized = unicodedata.normalize("NFKC", name).casefold()
        if all(token in normalized for token in tokens):
            relevance, match_label = 3, "完全符合"
        elif any(token in normalized for token in model_terms):
            relevance, match_label = 2, "型號相關"
        elif any(token in normalized for token in tokens):
            relevance, match_label = 1, "部分相關"
        else:
            relevance, match_label = 0, "momo 推薦"
        seen.add(code)
        price = parse_price(item.get("SALE_PRICE"))
        if price is None:
            price = parse_price(item.get("goodsPrice"))
        found.append({"title": name[:220], "code": code, "price": price,
                      "relevance": relevance, "match_label": match_label,
                      "url": ("https://www.momoshop.com.tw/goods/GoodsDetail.jsp?i_code=" + code
                              if code.startswith("TP") else
                              "https://www.momoshop.com.tw/product/" + code)})
        if len(found) == 20:
            break
    return ("ok" if any(row["price"] is not None for row in found)
            else "no_price" if found else "no_match"), found


def fetch_once(keyword, page=1):
    """One ordinary request; no retries on denial or verification."""
    payload = {
        "host": "ecmobile", "flag": "searchEngine",
        "data": {
            "maxPage": 30, "cateLevel": -1, "serviceCode": "MT01", "platform": 16,
            "has3P": "Y", "NAM": "N", "china": "N", "cp": "N", "first": "N",
            "freeze": "N", "prefere": "N", "stockYN": "N", "superstore": "N",
            "threeHours": "N", "tomorrow": "N", "tvshop": "N", "video": "N",
            "cycle": "N", "cod": "N", "superstorePay": "N",
            "moCoinFeedback": "N", "superstoreFree": "N", "discount": "N",
            "isBrandSeriesPage": False, "isShowAdShop": False,
            "curRecommendedWordsCnt": 0, "searchValue": keyword, "curPage": page
        }
    }
    report = {"status": "network_error", "results": [], "page": page,
              "raw_count": 0, "checked_at": datetime.now(timezone.utc).isoformat()}
    try:
        with primp.Client(
            impersonate="chrome_142", impersonate_os="windows",
            timeout=18.0, http2_only=True,
            headers={"content-type": "application/json",
                     "origin": "https://m.momoshop.com.tw",
                     "referer": "https://m.momoshop.com.tw/"}
        ) as client:
            response = client.post(ENDPOINT, json=payload)
            if response.status_code in (403, 429):
                report["status"] = "blocked"
                return report
            if response.status_code != 200:
                report["status"] = "http_error"
                return report
            if any(x in response.text[:1400].lower() for x in
                   ("captcha", "access denied", "流量驗證")):
                report["status"] = "blocked"
                return report
            try:
                data = response.json()
            except ValueError:
                report["status"] = "format_changed"
                return report
            section = data.get("rtnSearchData") if isinstance(data, dict) else None
            raw = section.get("goodsInfoList") if isinstance(section, dict) else None
            report["raw_count"] = len(raw) if isinstance(raw, list) else 0
            report["status"], report["results"] = parse_results(data, keyword)
    except Exception as exc:
        report["error_type"] = type(exc).__name__
    return report
