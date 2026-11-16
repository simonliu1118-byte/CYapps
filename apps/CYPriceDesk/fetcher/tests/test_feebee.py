"""Offline regression tests for the Feebee parser (moved from the sandbox site tests)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unittest

from feebee import make_search_url, parse_all_results, parse_feebee_results


def card(title, price, shop="Coupang 酷澎", href="/p/sample"):
    return (f'<div><a href="{href}">{title}</a><span>{price}</span>'
            f'<span>{shop}</span><a href="/go/x">前往購買</a></div>')


class FeebeeTests(unittest.TestCase):
    def setUp(self):
        self.url = make_search_url("3m 1530-1")

    def test_fixed_host_and_encoding(self):
        self.assertEqual(self.url, "https://feebee.com.tw/s/3m%201530-1/")

    def test_extract_only_coupang_prices(self):
        html = (card("3M 通氣膠帶 12捲", "259", "momo購物網") +
                card("3M 1530-1 紙膠帶 1個", "179") +
                card("3M 1530-1 紙膠帶 12捲 3盒", "395"))
        state, rows = parse_feebee_results(html, self.url)
        self.assertEqual(state, "ok")
        self.assertEqual([x["price"] for x in rows], [179, 395])
        self.assertEqual(rows[0]["title"], "3M 1530-1 紙膠帶 1個")
        self.assertTrue(rows[0]["url"].startswith("https://feebee.com.tw/p/"))

    def test_first_item_must_not_use_site_header_as_title(self):
        html = ('<header><a href="/">Feebee 飛比價格</a></header>'
                '<nav><a href="/s/3m/">熱門搜尋</a></nav>'
                + card("3M 1530-1 透氣紙膠帶 1個", "179"))
        status, rows = parse_feebee_results(html, self.url)
        self.assertEqual(status, "ok")
        self.assertEqual(rows[0]["title"], "3M 1530-1 透氣紙膠帶 1個")

    def test_missing_product_link_rejects_card(self):
        html = ('<header><a href="/">Feebee 飛比價格</a></header>'
                '<div>3M 1530-1 1個</div><span>175</span>'
                '<span>Coupang 酷澎</span><a>前往購買</a>')
        status, rows = parse_feebee_results(html, self.url)
        self.assertEqual((status, rows), ("no_price", []))

    def test_reject_promo_and_title_numbers(self):
        html = ('<a href="/p/x">3M 1530-1 一盒12捲</a>'
                '<span>促銷 99 元</span><span>Coupang 酷澎</span>'
                '<a>前往購買</a>')
        self.assertEqual(parse_feebee_results(html, self.url), ("no_price", []))

    def test_reject_other_merchants(self):
        self.assertEqual(parse_feebee_results(
            card("3M 1530-1 一盒", "250", "蝦皮商城"), self.url), ("no_match", []))

    def test_untrusted_link_falls_back_to_feebee_search(self):
        state, rows = parse_feebee_results(
            card("3M 1530-1 一盒", "300", href="javascript:alert(1)"), self.url)
        self.assertEqual(state, "ok")
        self.assertEqual(rows[0]["url"], self.url)

    def test_shopee_and_coupang_share_one_html_parser(self):
        html = (card("3M 1530-1 十二捲", "300", "蝦皮商城 - 瑞昌藥局") +
                card("3M 1530-1 一個裝", "175", "Coupang 酷澎") +
                card("3M 1530-1 十二入", "230", "蝦皮購物 - 亮亮生活") +
                card("3M 1530-1 十二入", "99", "momo購物網"))
        all_rows = parse_all_results(html, self.url)
        self.assertEqual([r["price"] for r in all_rows["shopee"][1]], [300, 230])
        self.assertEqual([r["price"] for r in all_rows["coupang"][1]], [175])
        self.assertEqual([r["merchant"] for r in all_rows["shopee"][1]],
                         ["蝦皮商城", "蝦皮購物"])
        self.assertEqual(all_rows["shopee"][1][1]["seller"], "亮亮生活")



if __name__ == "__main__":
    unittest.main()
