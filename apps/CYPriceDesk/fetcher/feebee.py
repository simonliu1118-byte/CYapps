"""One Feebee HTML search and parser, two separately presented merchant markets.

Moved from the sandbox ScraplingProbe feebee_search.py as-is. Caching, throttling
and the cooldown after a block are handled by the Worker (D1), not here.

All displayed prices are Feebee product-level references, never live checkout
or individual SKU prices. Anonymous, no price history, no retries or proxies.
"""
import re
from datetime import datetime, timezone
from html.parser import HTMLParser
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urljoin, urlparse
from urllib.request import Request, urlopen

BASE = "https://feebee.com.tw"
MERCHANT_RE = re.compile(r"Coupang\s*酷澎|酷澎\s*Coupang", re.I)
SHOPEE_RE = re.compile(r"^(蝦皮商城|蝦皮購物)(?:\s*[-－—]\s*(.+))?$")
MARKETS = ("coupang", "shopee")
PRICE_RE = re.compile(r"^(?:NT)?\$?\s*([1-9]\d{0,2}(?:,\d{3})*|[1-9]\d{0,6})\s*(?:元)?$")
IGNORE = {"推薦商品", "找同款商品", "前往購買", "促銷", "Coupang 酷澎"}


def make_search_url(keyword):
    keyword = " ".join(str(keyword).split())
    if not 2 <= len(keyword) <= 80 or any(ord(c) < 32 for c in keyword):
        raise ValueError("keyword must be 2 to 80 characters")
    return BASE + "/s/" + quote(keyword, safe="") + "/"


def _safe_link(href, fallback):
    if not href:
        return fallback
    url = urljoin(BASE + "/", href)
    parsed = urlparse(url)
    return url if parsed.scheme == "https" and parsed.hostname in (
        "feebee.com.tw", "www.feebee.com.tw") else fallback


class VisibleNodes(HTMLParser):
    """Visible text nodes with their enclosing anchor's URL."""
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.parts, self.links, self.skip = [], [], 0

    def handle_starttag(self, tag, attrs):
        if tag in ("script", "style", "noscript", "svg"):
            self.skip += 1
        if tag == "a":
            self.links.append(dict(attrs).get("href", ""))

    def handle_endtag(self, tag):
        if tag in ("script", "style", "noscript", "svg"):
            self.skip = max(0, self.skip - 1)
        if tag == "a" and self.links:
            self.links.pop()

    def handle_data(self, data):
        if self.skip:
            return
        text = " ".join(data.split()).strip()
        if text:
            self.parts.append((text[:300], self.links[-1] if self.links else ""))


def _merchant(text):
    """Return (market, merchant label, seller, type) for exact shop markers."""
    if MERCHANT_RE.fullmatch(text):
        return ("coupang", "Coupang 酷澎", "", "coupang")
    match = SHOPEE_RE.fullmatch(text)
    if match:
        label, seller = match.group(1), (match.group(2) or "").strip()[:100]
        return ("shopee", label, seller,
                "mall" if label == "蝦皮商城" else "marketplace")
    return None


def _card(parts, search_url):
    tagged = [(i, info) for i, (text, _) in enumerate(parts)
              if (info := _merchant(text)) is not None]
    if len(tagged) != 1:
        return None
    merchant_idx, (market, merchant, seller, shop_type) = tagged[0]
    price_idx = merchant_idx - 1
    if price_idx < 1:
        return None
    match = PRICE_RE.fullmatch(parts[price_idx][0])
    if match is None:
        return None
    price = int(match.group(1).replace(",", ""))
    if not 0 < price <= 10000000:
        return None
    # A site header or unrelated ad is never a valid product title.
    # Use only the latest nearby linked text from this purchase listing.
    candidates = [(text, href) for text, href in
                  parts[max(0, price_idx - 12):price_idx]
                  if href and len(text) >= 8 and text not in IGNORE
                  and "Feebee" not in text and "飛比價格" not in text
                  and not _merchant(text) and not PRICE_RE.fullmatch(text)
                  and "精選商品優惠價格" not in text and "相關搜尋" not in text]
    if not candidates:
        return None
    title, href = candidates[-1]
    return {"title": title[:220], "price": price,
            "url": _safe_link(href, search_url), "source": "飛比／" + merchant,
            "market": market, "merchant": merchant, "seller": seller,
            "shop_type": shop_type}


def parse_all_results(html, search_url):
    """Parse once; separate merchants without combining product/price statistics."""
    parser = VisibleNodes()
    parser.feed(html)
    rows = {market: [] for market in MARKETS}
    seen = {market: set() for market in MARKETS}
    mentions = {market: 0 for market in MARKETS}
    parts = []
    for text, href in parser.parts:
        marker = _merchant(text)
        if marker:
            mentions[marker[0]] += 1
        if text == "前往購買":
            row = _card(parts, search_url)
            if row:
                market = row["market"]
                identity = (row["merchant"], row["seller"], row["title"],
                            row["price"], row["url"])
                if identity not in seen[market] and len(rows[market]) < 20:
                    seen[market].add(identity)
                    rows[market].append(row)
            parts = []
        else:
            parts.append((text, href))
    return {
        market: ("ok" if rows[market] else
                 "no_price" if mentions[market] else "no_match", rows[market])
        for market in MARKETS
    }


def parse_feebee_results(html, search_url, market="coupang"):
    """Compatibility entrypoint; market affects filtering, not the HTML request."""
    if market not in MARKETS:
        raise ValueError("unsupported market")
    return parse_all_results(html, search_url)[market]


def fetch_once(keyword):
    search_url = make_search_url(keyword)
    report = {"status": "network_error", "results": [], "source_url": search_url,
              "checked_at": datetime.now(timezone.utc).isoformat()}
    req = Request(search_url, headers={
        "User-Agent": "Mozilla/5.0 (compatible; PriceResearchPrototype/0.1)",
        "Accept": "text/html,application/xhtml+xml",
        "Accept-Language": "zh-TW,zh;q=0.9",
    })
    try:
        with urlopen(req, timeout=18) as response:
            target = urlparse(response.geturl())
            if response.status != 200:
                report["status"] = "http_error"
                return report
            if target.scheme != "https" or target.hostname not in (
                    "feebee.com.tw", "www.feebee.com.tw"):
                report["status"] = "unexpected_redirect"
                return report
            html = response.read(1500000).decode("utf-8", "replace")
        if any(x in html[:3000].lower() for x in ("captcha", "access denied", "流量驗證")):
            report["status"] = "blocked"
            return report
        report["status"] = "ok"
        report["markets"] = parse_all_results(html, search_url)
    except HTTPError as error:
        report["status"] = "blocked" if error.code in (403, 429) else "http_error"
    except (URLError, TimeoutError, OSError, ValueError) as error:
        report["error_type"] = type(error).__name__
    return report
