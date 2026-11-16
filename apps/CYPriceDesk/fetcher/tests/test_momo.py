"""Offline checks for momo price parsing (moved from the sandbox site tests)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import unittest

from momo import normalize_keyword, parse_results


def payload():
    return {"success": True, "rtnSearchData": {"goodsInfoList": [
        {"goodsCode": "9956761", "goodsName": "羅技 M331 黑", "SALE_PRICE": "699"},
        {"goodsCode": "4318322", "goodsName": "羅技 M331 白", "SALE_PRICE": "599"},
        {"goodsCode": "4318322", "goodsName": "羅技 M331 白", "SALE_PRICE": "599"}]}}


class MomoParseTests(unittest.TestCase):
    def test_keyword_validation(self):
        self.assertEqual(normalize_keyword(" 羅技   M331 "), "羅技 M331")
        with self.assertRaises(ValueError):
            normalize_keyword(" ")

    def test_real_provider_shape(self):
        status, rows = parse_results(payload(), "羅技 M331")
        self.assertEqual(status, "ok")
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0]["price"], 699)

    def test_3m_search_keeps_official_nonliteral_matches(self):
        data = {"success": True, "rtnSearchData": {"goodsInfoList": [
            {"goodsCode":"1234567", "goodsName":"3M Micropore 1530-0 通氣膠帶", "SALE_PRICE":"259"},
            {"goodsCode":"1234568", "goodsName":"Nexcare 1530-1 紙膠帶", "SALE_PRICE":"190"},
            {"goodsCode":"1234569", "goodsName":"3M 透氣膠帶 1吋", "SALE_PRICE":"54"},
            {"goodsCode":"1234570", "goodsName":"醫療膠帶 12入", "SALE_PRICE":"160"}]}}
        status, rows = parse_results(data, "3m 1530")
        self.assertEqual(status, "ok")
        self.assertEqual(len(rows), 4)
        self.assertEqual([r["relevance"] for r in rows], [3, 2, 1, 0])

    def test_actual_3m_1530_marketplace_ids_kept(self):
        data = {"success": True, "rtnSearchData": {"goodsInfoList": [
            {"goodsCode":"14011391", "goodsName":"3M 1530-0 通氣膠帶 6 組", "SALE_PRICE":"259"},
            {"goodsCode":"TP00013930000165", "goodsName":"3M 1530-0 紙膠帶 12 入", "SALE_PRICE":"$190"},
            {"goodsCode":"TP00065910000469", "goodsName":"3M Nexcare 1530CP 膠帶", "SALE_PRICE":"$54"},
            {"goodsCode":"TP00052130002280", "goodsName":"來而康 1530-0 透氣膠帶", "SALE_PRICE":"$406"},
            {"goodsCode":"TP00071080000060", "goodsName":"3M Nexcare 1530CP-1", "SALE_PRICE":"$69"},
            {"goodsCode":"TP00094060000571", "goodsName":"3M 1530 盒裝", "SALE_PRICE":"$100"}]}}
        status, rows = parse_results(data, "3m 1530")
        self.assertEqual(status, "ok")
        self.assertEqual(len(rows), 6)
        self.assertEqual([r["price"] for r in rows], [259,190,54,406,69,100])
        self.assertTrue(rows[1]["url"].endswith("GoodsDetail.jsp?i_code=TP00013930000165"))
        self.assertTrue(rows[0]["url"].endswith("/product/14011391"))

    def test_explicit_provider_failure(self):
        self.assertEqual(parse_results({"success": False}, "羅技 M331"), ("provider_refused", []))



if __name__ == "__main__":
    unittest.main()
