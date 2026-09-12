from __future__ import annotations

from typing import Callable

from PySide6.QtCore import QAbstractTableModel, QModelIndex, Qt, Signal
from PySide6.QtGui import QBrush, QColor

from util import format_amount


class LedgerTableModel(QAbstractTableModel):
    HEADERS = [
        "日期", "帳戶", "收支", "收入科目", "收入摘要", "收入金額",
        "支出科目", "支出摘要", "支出金額", "餘額",
    ]

    FIELD_BY_COLUMN = {
        0: "tx_date",
        1: "account_name",
        3: "category_name",
        4: "summary",
        5: "amount",
        6: "category_name",
        7: "summary",
        8: "amount",
    }

    edit_failed = Signal(str)
    edit_succeeded = Signal(int)

    def __init__(self, edit_callback: Callable[[int, str, object], tuple[bool, str]], parent=None):
        super().__init__(parent)
        self.rows: list[dict] = []
        self.placeholder_rows = 16
        self.locked = False
        self.account_view = False
        self.edit_callback = edit_callback

    def set_rows(self, rows: list[dict], placeholder_rows: int | None = None) -> None:
        self.beginResetModel()
        self.rows = rows
        if placeholder_rows is not None:
            self.placeholder_rows = max(1, placeholder_rows)
        self.endResetModel()

    def set_locked(self, locked: bool) -> None:
        self.locked = locked
        if self.rowCount():
            self.dataChanged.emit(self.index(0, 0), self.index(self.rowCount()-1, self.columnCount()-1))

    def row_data(self, row: int) -> dict | None:
        return self.rows[row] if 0 <= row < len(self.rows) else None

    def rowCount(self, parent=QModelIndex()):  # noqa: N802
        return max(len(self.rows), self.placeholder_rows)

    def columnCount(self, parent=QModelIndex()):  # noqa: N802
        return len(self.HEADERS)

    def headerData(self, section, orientation, role=Qt.ItemDataRole.DisplayRole):  # noqa: N802
        if role == Qt.ItemDataRole.DisplayRole and orientation == Qt.Orientation.Horizontal:
            if section == 1 and self.account_view:
                return "帳戶 ▲"
            if section == 9 and self.account_view:
                return "帳戶餘額"
            return self.HEADERS[section]
        return None

    def set_account_view(self, enabled: bool) -> None:
        enabled = bool(enabled)
        if self.account_view == enabled:
            return
        self.account_view = enabled
        self.headerDataChanged.emit(Qt.Orientation.Horizontal, 1, 9)
        if self.rowCount():
            self.dataChanged.emit(self.index(0, 9), self.index(self.rowCount() - 1, 9))

    def data(self, index, role=Qt.ItemDataRole.DisplayRole):  # noqa: N802
        if not index.isValid():
            return None
        r = self.row_data(index.row())
        col = index.column()
        if role == Qt.ItemDataRole.BackgroundRole:
            return QBrush(QColor("#ffffff" if index.row() % 2 == 0 else "#f2f5f8"))
        if r is None:
            return None
        if role == Qt.ItemDataRole.UserRole:
            return int(r["id"])
        if role == Qt.ItemDataRole.DisplayRole:
            return self._display_value(r, col)
        if role == Qt.ItemDataRole.TextAlignmentRole:
            if col in (5, 8, 9):
                return int(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
            return int(Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter)
        if role == Qt.ItemDataRole.ToolTipRole and col == 9:
            if self.account_view:
                return f"{r.get('account_name', '')}：{format_amount(r.get('account_balance', 0))}"
            balances = r.get("account_balances", {})
            lines = [f"{name}：{format_amount(value)}" for name, value in sorted(balances.items())]
            lines.append("────────────")
            lines.append(f"總餘額：{format_amount(r.get('balance', 0))}")
            return "\n".join(lines)
        return None

    def _display_value(self, r: dict, col: int):
        kind = r["kind"]
        if col == 0:
            return r["tx_date"]
        if col == 1:
            return r["account_name"]
        if col == 2:
            return "收入" if kind == "income" else "支出"
        if col == 3:
            return r["category_name"] if kind == "income" else ""
        if col == 4:
            return r["summary"] if kind == "income" else ""
        if col == 5:
            return format_amount(r["amount"]) if kind == "income" else ""
        if col == 6:
            return r["category_name"] if kind == "expense" else ""
        if col == 7:
            return r["summary"] if kind == "expense" else ""
        if col == 8:
            return format_amount(r["amount"]) if kind == "expense" else ""
        if col == 9:
            key = "account_balance" if self.account_view else "balance"
            return format_amount(r.get(key, 0))
        return ""

    def raw_value(self, row: int, col: int):
        r = self.row_data(row)
        if not r:
            return ""
        if col == 0:
            return r["tx_date"]
        if col == 1:
            return r["account_name"]
        if col in (3, 6):
            return r["category_name"]
        if col in (4, 7):
            return r["summary"]
        if col in (5, 8):
            return r["amount"]
        return ""

    def flags(self, index):
        if not index.isValid():
            return Qt.ItemFlag.NoItemFlags
        r = self.row_data(index.row())
        if r is None:
            return Qt.ItemFlag.ItemIsEnabled
        flags = Qt.ItemFlag.ItemIsEnabled | Qt.ItemFlag.ItemIsSelectable
        if self.locked:
            return flags
        col = index.column()
        kind = r["kind"]
        editable = col in (0, 1)
        editable = editable or (kind == "income" and col in (3, 4, 5))
        editable = editable or (kind == "expense" and col in (6, 7, 8))
        if editable:
            flags |= Qt.ItemFlag.ItemIsEditable
        return flags

    def setData(self, index, value, role=Qt.ItemDataRole.EditRole):  # noqa: N802
        if role != Qt.ItemDataRole.EditRole or not index.isValid():
            return False
        r = self.row_data(index.row())
        if not r or self.locked:
            return False
        field = self.FIELD_BY_COLUMN.get(index.column())
        if not field:
            return False
        ok, message = self.edit_callback(int(r["id"]), field, value)
        if ok:
            self.edit_succeeded.emit(int(r["id"]))
            return True
        self.edit_failed.emit(message)
        return False
