from __future__ import annotations

import os
import sys

import sqlite3
from datetime import date, datetime
from pathlib import Path
from typing import Callable

from PySide6.QtCore import QDate, QRegularExpression, QSize, Qt
from PySide6.QtGui import QIcon, QIntValidator, QRegularExpressionValidator
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QCheckBox,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QFileDialog,
    QFormLayout,
    QFrame,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QInputDialog,
    QLabel,
    QLineEdit,
    QListWidget,
    QListWidgetItem,
    QMessageBox,
    QPushButton,
    QSpinBox,
    QTabWidget,
    QTreeWidget,
    QTreeWidgetItem,
    QVBoxLayout,
    QWidget,
)

from db import Database, DatabaseError
from background import start_background_task
from util import (
    APP_NAME,
    APP_VERSION,
    MAX_AMOUNT,
    app_root,
    DB_FILENAME,
    format_amount,
    format_date,
    month_to_index,
    shift_month,
    normalize_name,
    parse_date,
    weighted_units,
    save_config,
)
from widgets import CategoryComboBox, DatePickerDialog, MonthSpinBox, SmartDateLineEdit, WeightedLineEdit
from gdrive import GoogleDriveClient, GoogleDriveError, import_oauth_client_json, newest_local_backup, upload_backup_and_cleanup


def no_tab_button(text: str, parent=None) -> QPushButton:
    b = QPushButton(text, parent)
    b.setFocusPolicy(Qt.FocusPolicy.NoFocus)
    return b


class NameDialog(QDialog):
    def __init__(self, title: str, label: str, initial: str = "", max_units: int = 16, parent=None):
        super().__init__(parent)
        self.setWindowTitle(title)
        self.setModal(True)
        layout = QVBoxLayout(self)
        layout.addWidget(QLabel(label))
        self.edit = WeightedLineEdit(max_units)
        self.edit.setText(initial)
        self.edit.selectAll()
        layout.addWidget(self.edit)
        self.error = QLabel("")
        self.error.setStyleSheet("color:#c62828;font-weight:bold;")
        layout.addWidget(self.error)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.button(QDialogButtonBox.StandardButton.Ok).setText("確定")
        buttons.button(QDialogButtonBox.StandardButton.Cancel).setText("取消")
        buttons.accepted.connect(self._accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)
        self.resize(340, 145)

    def _accept(self):
        if not normalize_name(self.edit.text()):
            self.error.setText("名稱不可空白")
            self.edit.setFocus()
            return
        self.accept()

    def value(self) -> str:
        return normalize_name(self.edit.text())


class AccountManagerDialog(QDialog):
    changed = None

    def __init__(self, db: Database, parent=None):
        super().__init__(parent)
        self.db = db
        self.setWindowTitle("帳戶管理")
        self.setModal(True)
        # Keep the manager only as wide as the five compact action buttons.
        self.resize(340, 400)
        self.setFixedWidth(340)
        outer = QVBoxLayout(self)
        note = QLabel("帳戶名稱上限8個中文字；點擊第一欄可設為預設帳戶。\n修改或刪除帳戶不會變更歷史記帳資料。")
        note.setWordWrap(True)
        note.setStyleSheet("color:#5d6875;")
        outer.addWidget(note)

        self.list = QTreeWidget()
        self.list.setColumnCount(2)
        self.list.setHeaderHidden(True)
        self.list.setRootIsDecorated(False)
        self.list.setIndentation(0)
        self.list.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        self.list.setColumnWidth(0, 34)
        self.list.header().setStretchLastSection(True)
        self.list.setStyleSheet("QTreeWidget::item { padding: 4px 3px; }")
        self.list.setToolTip("點擊第一欄可設為預設帳戶")
        self.list.itemClicked.connect(self._item_clicked)
        self.list.itemDoubleClicked.connect(self._item_double_clicked)
        outer.addWidget(self.list, 1)

        actions = QHBoxLayout()
        actions.setSpacing(6)
        # Put the rarely destructive action last and keep ordering controls first.
        specs = (
            ("↑", lambda: self.move(-1), 38),
            ("↓", lambda: self.move(1), 38),
            ("新增", self.add, 72),
            ("修改", self.modify, 72),
            ("刪除", self.delete, 72),
        )
        for text, fn, width in specs:
            button = no_tab_button(text)
            button.setMinimumWidth(width)
            button.clicked.connect(fn)
            actions.addWidget(button)
        actions.addStretch(1)
        outer.addLayout(actions)
        self.refresh()

    def refresh(self, select_id: int | None = None):
        self.list.clear()
        for row in self.db.accounts():
            item = QTreeWidgetItem(["★" if row["is_default"] else "", row["name"]])
            item.setData(0, Qt.ItemDataRole.UserRole, row["id"])
            item.setTextAlignment(0, Qt.AlignmentFlag.AlignCenter)
            # Slightly taller rows make the compact two-column list easier to scan.
            item.setSizeHint(0, QSize(0, 30))
            item.setSizeHint(1, QSize(0, 30))
            if row["is_default"]:
                font = item.font(0)
                font.setBold(True)
                item.setFont(0, font)
                item.setToolTip(0, "目前預設帳戶")
            else:
                item.setToolTip(0, "點擊設為預設帳戶")
            self.list.addTopLevelItem(item)
            if select_id == row["id"]:
                self.list.setCurrentItem(item)
        if not self.list.currentItem() and self.list.topLevelItemCount():
            self.list.setCurrentItem(self.list.topLevelItem(0))

    def selected_id(self) -> int | None:
        item = self.list.currentItem()
        return int(item.data(0, Qt.ItemDataRole.UserRole)) if item else None

    def _item_clicked(self, item: QTreeWidgetItem, column: int):
        if column != 0:
            return
        aid = int(item.data(0, Qt.ItemDataRole.UserRole))
        row = next((a for a in self.db.accounts() if a["id"] == aid), None)
        if row and not row["is_default"]:
            self.db.set_default_account(aid)
            self.refresh(aid)

    def _item_double_clicked(self, item: QTreeWidgetItem, column: int):
        if column == 0:
            self._item_clicked(item, column)
        else:
            self.modify()

    def _existing_names(self, exclude_id: int | None = None) -> set[str]:
        return {a["name"] for a in self.db.accounts() if a["id"] != exclude_id}

    def add(self):
        dlg = NameDialog("新增帳戶", "帳戶名稱：", max_units=16, parent=self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        name = dlg.value()
        if name in self._existing_names():
            QMessageBox.warning(self, APP_NAME, "已有相同名稱的帳戶。")
            return
        try:
            self.db.add_account(name)
            self.refresh()
        except sqlite3.IntegrityError:
            QMessageBox.warning(self, APP_NAME, "已有相同名稱的帳戶。")

    def modify(self):
        aid = self.selected_id()
        if aid is None:
            return
        row = next(a for a in self.db.accounts() if a["id"] == aid)
        dlg = NameDialog("修改帳戶", "帳戶名稱：", row["name"], 16, self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        name = dlg.value()
        if name in self._existing_names(aid):
            QMessageBox.warning(self, APP_NAME, "已有相同名稱的帳戶。")
            return
        self.db.rename_account(aid, name)
        self.refresh(aid)

    def delete(self):
        aid = self.selected_id()
        if aid is None:
            return
        row = next(a for a in self.db.accounts() if a["id"] == aid)
        if len(self.db.accounts()) <= 1:
            QMessageBox.warning(self, APP_NAME, "帳戶至少需要保留一個。")
            return
        if QMessageBox.question(
            self, APP_NAME, f"確定刪除帳戶「{row['name']}」嗎？\n歷史記帳資料不會被刪除。"
        ) != QMessageBox.StandardButton.Yes:
            return
        try:
            self.db.delete_account(aid)
            self.refresh()
        except DatabaseError as e:
            QMessageBox.warning(self, APP_NAME, str(e))

    def move(self, delta: int):
        aid = self.selected_id()
        if aid is not None:
            self.db.move_account(aid, delta)
            self.refresh(aid)


class CategoryPage(QWidget):
    def __init__(self, db: Database, kind: str, parent=None):
        super().__init__(parent)
        self.db = db
        self.kind = kind
        outer = QVBoxLayout(self)
        count_row = QHBoxLayout()
        count_row.addStretch(1)
        self.favorite_count_label = QLabel("")
        self.favorite_count_label.setStyleSheet("color:#5d6875;font-weight:bold;")
        count_row.addWidget(self.favorite_count_label)
        outer.addLayout(count_row)
        self.tree = QTreeWidget()
        self.tree.setColumnCount(2)
        self.tree.setHeaderHidden(True)
        self.tree.setSelectionMode(QAbstractItemView.SelectionMode.SingleSelection)
        # Keep the favorite marker in a true fixed first column.  The tree
        # hierarchy/indentation belongs to column 1; otherwise Qt indents
        # child items inside column 0 and the star is shifted or clipped.
        self.tree.setTreePosition(1)
        self.tree.setColumnWidth(0, 30)
        self.tree.header().setStretchLastSection(True)
        self.tree.setToolTip("點擊科目前方第一欄可設定或取消常用科目（最多10個）")
        self.tree.itemClicked.connect(self._item_clicked)
        self.tree.itemDoubleClicked.connect(self._item_double_clicked)
        outer.addWidget(self.tree, 1)

        # Two compact action rows keep the manager narrow.  Ordering controls
        # stay on the far left and the destructive action remains last.
        first_row = QHBoxLayout()
        first_row.setSpacing(6)
        first_specs = (
            ("↑", lambda: self.move(-1), 40),
            ("↓", lambda: self.move(1), 40),
            ("新增科目", self.add_category, 84),
            ("修改", self.modify, 62),
            ("刪除", self.delete, 62),
        )
        for text, fn, width in first_specs:
            button = no_tab_button(text)
            button.setMinimumWidth(width)
            button.clicked.connect(fn)
            first_row.addWidget(button)
        first_row.addStretch(1)
        outer.addLayout(first_row)

        second_row = QHBoxLayout()
        second_row.setSpacing(6)
        second_specs = (
            ("新增大分類", self.add_group, 104),
            ("移至其他大分類", self.move_to_group, 132),
        )
        for text, fn, width in second_specs:
            button = no_tab_button(text)
            button.setMinimumWidth(width)
            button.clicked.connect(fn)
            second_row.addWidget(button)
        second_row.addStretch(1)
        outer.addLayout(second_row)
        self.refresh()

    def refresh(self, selected: tuple[str, int] | None = None):
        self.tree.clear()
        tree_data = self.db.category_tree(self.kind)
        favorite_count = sum(
            1 for g in tree_data for c in g["categories"] if int(c.get("is_favorite", 0))
        )
        self.favorite_count_label.setText(f"常用科目：{favorite_count} / 10")
        for g in tree_data:
            gi = QTreeWidgetItem(["", g["name"]])
            gi.setData(0, Qt.ItemDataRole.UserRole, ("group", g["id"]))
            f = gi.font(1); f.setBold(True); gi.setFont(1, f)
            self.tree.addTopLevelItem(gi)
            for c in g["categories"]:
                favorite = bool(int(c.get("is_favorite", 0)))
                ci = QTreeWidgetItem(["★" if favorite else "", c["name"]])
                ci.setData(0, Qt.ItemDataRole.UserRole, ("category", c["id"]))
                ci.setTextAlignment(0, Qt.AlignmentFlag.AlignCenter)
                ci.setToolTip(0, "點擊取消常用科目" if favorite else "點擊設為常用科目")
                gi.addChild(ci)
                if selected == ("category", c["id"]):
                    self.tree.setCurrentItem(ci)
            gi.setExpanded(True)
            if selected == ("group", g["id"]):
                self.tree.setCurrentItem(gi)
        if not self.tree.currentItem() and self.tree.topLevelItemCount():
            self.tree.setCurrentItem(self.tree.topLevelItem(0))

    def _item_clicked(self, item: QTreeWidgetItem, column: int):
        selected = item.data(0, Qt.ItemDataRole.UserRole)
        if column != 0 or not selected or selected[0] != "category":
            return
        category_id = int(selected[1])
        current = next(
            (c for g in self.db.category_tree(self.kind) for c in g["categories"] if c["id"] == category_id),
            None,
        )
        if not current:
            return
        try:
            self.db.set_category_favorite(category_id, not bool(int(current.get("is_favorite", 0))))
            self.refresh(("category", category_id))
        except DatabaseError as exc:
            QMessageBox.warning(self, APP_NAME, str(exc))

    def _item_double_clicked(self, item: QTreeWidgetItem, column: int):
        # First column is reserved for the favorite star; double-clicking it
        # must not unexpectedly open the rename dialog.
        if column == 1:
            self.modify()

    def selected(self) -> tuple[str, int] | None:
        item = self.tree.currentItem()
        return item.data(0, Qt.ItemDataRole.UserRole) if item else None

    def existing_groups(self, exclude: int | None = None) -> set[str]:
        return {g["name"] for g in self.db.category_tree(self.kind) if g["id"] != exclude}

    def existing_categories(self, exclude: int | None = None) -> set[str]:
        return {c["name"] for g in self.db.category_tree(self.kind) for c in g["categories"] if c["id"] != exclude}

    def add_group(self):
        dlg = NameDialog("新增大分類", "大分類名稱：", max_units=16, parent=self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        if dlg.value() in self.existing_groups():
            QMessageBox.warning(self, APP_NAME, "已有相同名稱的大分類。")
            return
        self.db.add_group(self.kind, dlg.value())
        self.refresh()

    def add_category(self):
        groups = self.db.category_tree(self.kind)
        if not groups:
            QMessageBox.warning(self, APP_NAME, "請先建立大分類。")
            return
        selected = self.selected()
        selected_group = None
        if selected:
            if selected[0] == "group":
                selected_group = selected[1]
            else:
                for g in groups:
                    if any(c["id"] == selected[1] for c in g["categories"]):
                        selected_group = g["id"]
                        break
        dlg = QDialog(self); dlg.setWindowTitle("新增科目")
        form = QFormLayout(dlg)
        name = WeightedLineEdit(16)
        combo = QComboBox()
        for g in groups:
            combo.addItem(g["name"], g["id"])
        if selected_group:
            combo.setCurrentIndex(max(0, combo.findData(selected_group)))
        form.addRow("科目名稱：", name); form.addRow("所屬大分類：", combo)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.button(QDialogButtonBox.StandardButton.Ok).setText("確定")
        buttons.button(QDialogButtonBox.StandardButton.Cancel).setText("取消")
        buttons.accepted.connect(dlg.accept); buttons.rejected.connect(dlg.reject); form.addRow(buttons)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        value = normalize_name(name.text())
        if not value:
            QMessageBox.warning(self, APP_NAME, "科目名稱不可空白。")
            return
        if value in self.existing_categories():
            QMessageBox.warning(self, APP_NAME, "同名科目不可出現在不同大分類。")
            return
        self.db.add_category(self.kind, int(combo.currentData()), value)
        self.refresh()

    def modify(self):
        selected = self.selected()
        if not selected:
            return
        typ, item_id = selected
        if typ == "group":
            row = next(g for g in self.db.category_tree(self.kind) if g["id"] == item_id)
            dlg = NameDialog("修改大分類", "大分類名稱：", row["name"], 16, self)
            if dlg.exec() != QDialog.DialogCode.Accepted:
                return
            if dlg.value() in self.existing_groups(item_id):
                QMessageBox.warning(self, APP_NAME, "已有相同名稱的大分類。")
                return
            self.db.rename_group(item_id, dlg.value())
        else:
            row = next(c for g in self.db.category_tree(self.kind) for c in g["categories"] if c["id"] == item_id)
            dlg = NameDialog("修改科目", "科目名稱：", row["name"], 16, self)
            if dlg.exec() != QDialog.DialogCode.Accepted:
                return
            if dlg.value() in self.existing_categories(item_id):
                QMessageBox.warning(self, APP_NAME, "同名科目不可出現在不同大分類。")
                return
            self.db.rename_category(item_id, dlg.value())
        self.refresh(selected)

    def delete(self):
        selected = self.selected()
        if not selected:
            return
        item_type, item_id = selected
        if item_type == "group":
            group = next(g for g in self.db.category_tree(self.kind) if g["id"] == item_id)
            if group["categories"]:
                QMessageBox.warning(self, APP_NAME, "此大分類仍包含科目，請先移動或刪除其中的科目。")
                return
            message = f"確定要刪除大分類「{group['name']}」嗎？"
        else:
            category = next(c for g in self.db.category_tree(self.kind) for c in g["categories"] if c["id"] == item_id)
            message = f"確定要刪除科目「{category['name']}」嗎？\n已存入的歷史記帳資料不會受到影響。"
        answer = QMessageBox.question(
            self,
            APP_NAME,
            message,
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No,
        )
        if answer != QMessageBox.StandardButton.Yes:
            return
        try:
            if item_type == "group":
                self.db.delete_group(item_id)
            else:
                self.db.delete_category(item_id)
            self.refresh()
        except DatabaseError as e:
            QMessageBox.warning(self, APP_NAME, str(e))

    def move(self, delta: int):
        selected = self.selected()
        if not selected:
            return
        if selected[0] == "group":
            self.db.move_group(selected[1], delta)
        else:
            self.db.move_category(selected[1], delta)
        self.refresh(selected)

    def move_to_group(self):
        selected = self.selected()
        if not selected or selected[0] != "category":
            return
        groups = self.db.category_tree(self.kind)
        names = [g["name"] for g in groups]
        choice, ok = QInputDialog.getItem(self, "移至其他大分類", "選擇大分類：", names, 0, False)
        if not ok:
            return
        gid = next(g["id"] for g in groups if g["name"] == choice)
        self.db.move_category_to_group(selected[1], gid)
        self.refresh(selected)


class CategoryManagerDialog(QDialog):
    def __init__(self, db: Database, parent=None):
        super().__init__(parent)
        self.db = db
        self.setWindowTitle("收入支出科目管理")
        self.setModal(True)
        # The first action row defines the useful width; remove the empty right side.
        self.resize(400, 520)
        self.setFixedWidth(400)
        outer = QVBoxLayout(self)
        note = QLabel("大分類與科目名稱上限8個中文字；科目名稱不可重複。歷史資料不會隨科目改名或刪除而變更。")
        note.setWordWrap(True); note.setStyleSheet("color:#5d6875;")
        outer.addWidget(note)
        tabs = QTabWidget()
        tabs.addTab(CategoryPage(db, "income"), "收入科目")
        tabs.addTab(CategoryPage(db, "expense"), "支出科目")
        outer.addWidget(tabs, 1)


class OpeningBalanceDialog(QDialog):
    def __init__(self, db: Database, month: str, parent=None, initial_values: dict[str, int] | None = None):
        super().__init__(parent)
        self.db = db
        self.month = month
        self.setWindowTitle(f"設定 {month} 期初餘額")
        self.setModal(True)
        self.setFixedWidth(320)

        outer = QVBoxLayout(self)
        outer.setContentsMargins(14, 12, 14, 12)
        outer.setSpacing(8)
        note = QLabel("每個帳戶分開設定；可輸入負數。空白代表尚未設定。")
        note.setStyleSheet("color:#5d6875;")
        note.setWordWrap(True)
        outer.addWidget(note)

        accounts = list(db.relevant_accounts_for_month(month))
        grid = QGridLayout()
        grid.setHorizontalSpacing(12)
        grid.setVerticalSpacing(7)
        account_header = QLabel("帳戶名稱")
        balance_header = QLabel("期初餘額")
        hf = account_header.font(); hf.setBold(True)
        account_header.setFont(hf); balance_header.setFont(hf)
        grid.addWidget(account_header, 0, 0)
        grid.addWidget(balance_header, 0, 1)
        grid.setColumnStretch(0, 1)

        existing = db.opening_balances(month)
        if initial_values is not None:
            existing = dict(initial_values)
        self.edits: dict[str, QLineEdit] = {}
        for row, (name, current) in enumerate(accounts, start=1):
            label = QLabel(name + ("" if current else "（歷史帳戶）"))
            edit = QLineEdit()
            edit.setMinimumWidth(145)
            edit.setValidator(QRegularExpressionValidator(QRegularExpression(r"-?[0-9]{0,15}"), edit))
            if name in existing:
                edit.setText(str(existing[name]))
            else:
                edit.setPlaceholderText("未設定")
            edit.textChanged.connect(self.update_total)
            grid.addWidget(label, row, 0)
            grid.addWidget(edit, row, 1)
            self.edits[name] = edit
        outer.addLayout(grid)

        self.total = QLabel()
        f = self.total.font(); f.setBold(True); self.total.setFont(f)
        outer.addWidget(self.total)
        self.error = QLabel("")
        self.error.setStyleSheet("color:#c62828;font-weight:bold;")
        self.error.setWordWrap(True)
        outer.addWidget(self.error)

        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
        buttons.button(QDialogButtonBox.StandardButton.Save).setText("儲存")
        buttons.button(QDialogButtonBox.StandardButton.Cancel).setText("取消")
        buttons.accepted.connect(self.save)
        buttons.rejected.connect(self.reject)
        outer.addWidget(buttons)
        self.update_total()

        # Compact for a few accounts; grow naturally as accounts are added.
        visible_rows = max(1, len(accounts))
        self.setFixedHeight(min(620, 220 + visible_rows * 42))

    def values(self) -> dict[str, int | None]:
        out = {}
        for name, edit in self.edits.items():
            text = edit.text().strip()
            out[name] = int(text) if text not in ("", "-") else None
        return out

    def update_total(self):
        total = 0
        for e in self.edits.values():
            try:
                total += int(e.text())
            except Exception:
                pass
        self.total.setText(f"合計：{format_amount(total)}")

    def save(self):
        try:
            self.db.set_opening_balances(self.month, self.values())
            self.accept()
        except Exception as e:
            self.error.setText(str(e))


class CarryForwardDialog(QDialog):
    def __init__(self, target_month: str, previous_month: str, values: dict[str, int], parent=None):
        super().__init__(parent)
        self.setWindowTitle("自動帶入期初餘額")
        self.setModal(True)
        self.values = values
        outer = QVBoxLayout(self)
        title = QLabel(f"{target_month} 尚未設定期初餘額，是否將 {previous_month} 的各帳戶期末餘額帶入？")
        title.setWordWrap(True)
        outer.addWidget(title)
        grid = QGridLayout()
        total = 0
        for row, (name, amount) in enumerate(values.items()):
            grid.addWidget(QLabel(name), row, 0)
            val = QLabel(format_amount(amount)); val.setAlignment(Qt.AlignmentFlag.AlignRight)
            grid.addWidget(val, row, 1)
            total += amount
        sep = QFrame(); sep.setFrameShape(QFrame.Shape.HLine)
        outer.addLayout(grid); outer.addWidget(sep)
        total_label = QLabel(f"合計：{format_amount(total)}")
        f = total_label.font(); f.setBold(True); total_label.setFont(f)
        outer.addWidget(total_label)
        buttons = QDialogButtonBox()
        yes = buttons.addButton("確認帶入", QDialogButtonBox.ButtonRole.AcceptRole)
        no = buttons.addButton("暫不帶入", QDialogButtonBox.ButtonRole.RejectRole)
        yes.clicked.connect(self.accept); no.clicked.connect(self.reject)
        outer.addWidget(buttons)
        self.resize(430, 300)


class EditTransactionDialog(QDialog):
    def __init__(self, db: Database, tx: dict, parent=None):
        super().__init__(parent)
        self.db = db; self.tx = tx
        self.setWindowTitle("編輯記帳資料")
        self.setModal(True)
        form = QFormLayout(self)
        date_row = QHBoxLayout()
        self.date_edit = SmartDateLineEdit()
        self.date_edit.setText(tx["tx_date"])
        cal = no_tab_button("")
        cal.setIcon(QIcon(str(app_root() / "app" / "resources" / "calendar.png")))
        cal.setToolTip("選擇日期")
        cal.clicked.connect(self.pick_date)
        date_row.addWidget(self.date_edit); date_row.addWidget(cal)
        form.addRow("日期：", date_row)
        self.account = QComboBox()
        names = [a["name"] for a in db.accounts()]
        if tx["account_name"] not in names:
            names.insert(0, tx["account_name"])
        self.account.addItems(names); self.account.setCurrentText(tx["account_name"])
        form.addRow("帳戶：", self.account)
        form.addRow("收支：", QLabel("收入" if tx["kind"] == "income" else "支出"))
        self.category = QComboBox()
        cats = db.category_names(tx["kind"])
        if tx["category_name"] not in cats:
            cats.insert(0, tx["category_name"])
        self.category.addItems(cats); self.category.setCurrentText(tx["category_name"])
        form.addRow(("收入" if tx["kind"] == "income" else "支出") + "科目：", self.category)
        self.summary = WeightedLineEdit(40); self.summary.setText(tx["summary"])
        form.addRow(("收入" if tx["kind"] == "income" else "支出") + "摘要：", self.summary)
        self.amount = QLineEdit(str(tx["amount"])); self.amount.setValidator(QRegularExpressionValidator(QRegularExpression(r"[0-9]{0,7}"), self.amount))
        form.addRow(("收入" if tx["kind"] == "income" else "支出") + "金額：", self.amount)
        self.error = QLabel(""); self.error.setStyleSheet("color:#c62828;font-weight:bold;")
        form.addRow(self.error)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
        buttons.button(QDialogButtonBox.StandardButton.Save).setText("儲存修改")
        buttons.button(QDialogButtonBox.StandardButton.Cancel).setText("取消")
        buttons.accepted.connect(self.save); buttons.rejected.connect(self.reject); form.addRow(buttons)
        self.resize(460, 390)

    def pick_date(self):
        d = parse_date(self.date_edit.text()) or date.today()
        dlg = DatePickerDialog(d, self)
        if dlg.exec() == QDialog.DialogCode.Accepted and dlg.selected_date:
            self.date_edit.setText(format_date(dlg.selected_date))

    def save(self):
        try:
            amount = int(self.amount.text())
        except Exception:
            self.error.setText("金額必須大於0")
            return
        if amount > MAX_AMOUNT and amount != int(self.tx["amount"]):
            self.error.setText("金額最多7位數")
            return
        ok, msg = self.db.update_transaction_full(
            self.tx["id"], self.date_edit.text(), self.account.currentText(),
            self.category.currentText(), self.summary.text(), amount,
        )
        if ok:
            self.accept()
        else:
            self.error.setText(msg)


class SettingsDialog(QDialog):
    def __init__(
        self,
        db: Database,
        config: dict,
        change_db_callback: Callable[[Path], tuple[bool, str]],
        restore_callback: Callable[[Path], tuple[bool, str]],
        clear_callback: Callable[[], None],
        settings_changed_callback: Callable[[], None],
        parent=None,
    ):
        super().__init__(parent)
        self.db = db
        self.config = config
        self.change_db_callback = change_db_callback
        self.restore_callback = restore_callback
        self.clear_callback = clear_callback
        self.settings_changed_callback = settings_changed_callback
        self.setWindowTitle("系統設定")
        self.setObjectName("settingsDialog")
        self.setModal(True)
        self.resize(700, 650)
        self.setStyleSheet(
            "QDialog#settingsDialog QPushButton, QDialog#settingsDialog QLineEdit, "
            "QDialog#settingsDialog QComboBox, QDialog#settingsDialog QSpinBox {"
            "min-height:21px; max-height:24px; padding-top:0px; padding-bottom:0px;}"
            "QDialog#settingsDialog QGroupBox {padding-top:9px; padding-bottom:7px;}"
        )
        outer = QVBoxLayout(self)

        db_box = QGroupBox("資料庫儲存位置")
        db_layout = QGridLayout(db_box)
        self.db_path = QLineEdit(str(db.path.parent)); self.db_path.setReadOnly(True)
        browse = no_tab_button("瀏覽"); browse.clicked.connect(self.browse_db)
        open_folder = no_tab_button("開啟資料資料夾"); open_folder.clicked.connect(self.open_data_folder)
        apply_db = no_tab_button("儲存並套用"); apply_db.clicked.connect(self.apply_db)
        db_layout.addWidget(self.db_path, 0, 0); db_layout.addWidget(browse, 0, 1)
        db_layout.addWidget(open_folder, 1, 0, Qt.AlignmentFlag.AlignRight); db_layout.addWidget(apply_db, 1, 1)
        outer.addWidget(db_box)

        lock_box = QGroupBox("資料鎖定")
        lock_layout = QHBoxLayout(lock_box)
        lock_layout.setContentsMargins(12, 10, 12, 10)
        lock_layout.setSpacing(8)
        current = db.locked_through()
        self.current_lock = QLabel(current or "未設定")
        self.lock_month = MonthSpinBox()
        self.lock_month.set_month(current or date.today().strftime("%Y/%m"))
        apply_lock = no_tab_button("套用鎖定年月"); apply_lock.clicked.connect(self.apply_lock)
        lock_layout.addWidget(QLabel("目前鎖定至："))
        lock_layout.addWidget(self.current_lock)
        lock_layout.addSpacing(12)
        lock_layout.addWidget(self.lock_month)
        lock_layout.addWidget(apply_lock)
        lock_layout.addStretch(1)
        outer.addWidget(lock_box)

        ledger_box = QGroupBox("記帳資料表")
        ledger_layout = QGridLayout(ledger_box)
        ledger_layout.setContentsMargins(12, 8, 12, 8)
        ledger_layout.setHorizontalSpacing(8)
        ledger_layout.setVerticalSpacing(5)
        ledger_layout.addWidget(QLabel("切換到記帳資料表時："), 0, 0)
        self.ledger_position = QComboBox()
        self.ledger_position.addItem("最新的一筆", "latest")
        self.ledger_position.addItem("最舊的一筆", "oldest")
        current_position = self.config.get("ledger_entry_position", "latest")
        index = self.ledger_position.findData(current_position)
        self.ledger_position.setCurrentIndex(index if index >= 0 else 0)
        self.ledger_position.currentIndexChanged.connect(self._ledger_position_changed)
        ledger_layout.addWidget(self.ledger_position, 0, 1)
        ledger_layout.addWidget(QLabel("Excel 匯出預設位置："), 1, 0)
        self.export_path = QLineEdit(str(self.config.get("export_directory") or Path.home()))
        self.export_path.setReadOnly(True)
        browse_export = no_tab_button("瀏覽")
        browse_export.clicked.connect(self.browse_export_directory)
        ledger_layout.addWidget(self.export_path, 1, 1, 1, 2)
        ledger_layout.addWidget(browse_export, 1, 3)
        ledger_layout.setColumnStretch(2, 1)
        outer.addWidget(ledger_box)

        summary_box = QGroupBox("常用摘要")
        summary_layout = QHBoxLayout(summary_box)
        summary_layout.setContentsMargins(12, 7, 12, 7)
        summary_layout.setSpacing(5)
        summary_layout.addWidget(QLabel("統計依據："))
        self.common_summary_basis = QComboBox()
        self.common_summary_basis.addItem("帳務日期", "tx_date")
        self.common_summary_basis.addItem("近期輸入", "created_at")
        current_basis = self.config.get("common_summary_basis", "tx_date") or "tx_date"
        basis_index = self.common_summary_basis.findData(current_basis)
        self.common_summary_basis.setCurrentIndex(basis_index if basis_index >= 0 else 0)
        summary_layout.addWidget(self.common_summary_basis)
        summary_layout.addWidget(QLabel("，最近"))
        self.common_summary_recent = QLineEdit(str(int(self.config.get("common_summary_recent_n", 100) or 100)))
        self.common_summary_recent.setValidator(QIntValidator(20, 1000, self.common_summary_recent))
        self.common_summary_recent.setFixedWidth(58)
        summary_layout.addWidget(self.common_summary_recent)
        summary_layout.addWidget(QLabel("筆，至少出現"))
        self.common_summary_min = QLineEdit(str(int(self.config.get("common_summary_min_count", 3) or 3)))
        self.common_summary_min.setValidator(QIntValidator(2, 50, self.common_summary_min))
        self.common_summary_min.setFixedWidth(46)
        summary_layout.addWidget(self.common_summary_min)
        summary_layout.addWidget(QLabel("次"))
        summary_layout.addStretch(1)
        self.common_summary_basis.currentIndexChanged.connect(self._summary_settings_changed)
        self.common_summary_recent.editingFinished.connect(self._summary_settings_changed)
        self.common_summary_min.editingFinished.connect(self._summary_settings_changed)
        outer.addWidget(summary_box)

        backup_box = QGroupBox("備份與還原")
        backup_layout = QHBoxLayout(backup_box)
        backup_layout.setContentsMargins(12, 10, 12, 10)
        backup_layout.setSpacing(16)
        last = self.config.get("last_auto_backup_date") or "尚未備份"
        self.last_backup = QLabel(last)
        info = QVBoxLayout()
        info.setSpacing(5)
        last_row = QHBoxLayout()
        last_row.setSpacing(5)
        last_row.addWidget(QLabel("上次自動備份："))
        last_row.addWidget(self.last_backup)
        last_row.addStretch(1)
        info.addLayout(last_row)
        info.addWidget(QLabel("自動備份：每3天於程式啟動時檢查，保留最新30份。"))
        backup_layout.addLayout(info, 1)
        buttons = QVBoxLayout()
        buttons.setSpacing(6)
        manual = no_tab_button("建立備份"); manual.clicked.connect(self.manual_backup)
        restore = no_tab_button("還原備份"); restore.clicked.connect(self.restore)
        manual.setMinimumWidth(112); restore.setMinimumWidth(112)
        buttons.addWidget(manual)
        buttons.addWidget(restore)
        backup_layout.addLayout(buttons)
        outer.addWidget(backup_box)

        drive_box = QGroupBox("Google Drive")
        drive_layout = QGridLayout(drive_box)
        drive_layout.setContentsMargins(12, 8, 12, 8)
        drive_layout.setHorizontalSpacing(7)
        drive_layout.setVerticalSpacing(5)
        self.google_client = GoogleDriveClient(self.config)
        self.google_status = QLabel()
        self._refresh_google_status()
        drive_layout.addWidget(QLabel("狀態："), 0, 0)
        drive_layout.addWidget(self.google_status, 0, 1, 1, 3)
        oauth_btn = no_tab_button("匯入 OAuth 憑證")
        oauth_btn.setToolTip("匯入 Google Cloud『桌面應用程式』OAuth client JSON")
        oauth_btn.clicked.connect(self.import_google_oauth)
        self.google_connect_btn = no_tab_button("連結 Google Drive")
        self.google_connect_btn.clicked.connect(self.connect_google_drive)
        self.google_disconnect_btn = no_tab_button("中斷連結")
        self.google_disconnect_btn.clicked.connect(self.disconnect_google_drive)
        drive_layout.addWidget(oauth_btn, 1, 0)
        drive_layout.addWidget(self.google_connect_btn, 1, 1)
        drive_layout.addWidget(self.google_disconnect_btn, 1, 2)
        drive_layout.addWidget(QLabel("備份資料夾：CYAccounting"), 1, 3)
        self.google_sync_check = QCheckBox("本機自動備份完成後同步至 Google Drive")
        self.google_sync_check.setChecked(bool(self.config.get("google_drive_sync_enabled", False)))
        self.google_sync_check.toggled.connect(self._google_sync_toggled)
        self.google_sync_now_btn = no_tab_button("立即同步")
        self.google_sync_now_btn.clicked.connect(self.sync_google_now)
        drive_layout.addWidget(self.google_sync_check, 2, 0, 1, 3)
        drive_layout.addWidget(self.google_sync_now_btn, 2, 3)
        self.google_last = QLabel()
        self._refresh_google_status()
        drive_layout.addWidget(self.google_last, 3, 0, 1, 4)
        outer.addWidget(drive_box)

        info_box = QGroupBox("程式資訊")
        info_layout = QGridLayout(info_box)
        info_layout.addWidget(QLabel("程式名稱："), 0, 0)
        info_layout.addWidget(QLabel(APP_NAME), 0, 1)
        info_layout.addWidget(QLabel("版本："), 1, 0)
        info_layout.addWidget(QLabel(APP_VERSION), 1, 1)
        clear = no_tab_button("清除所有記帳資料與期初餘額")
        clear.setObjectName("dangerButton")
        clear.clicked.connect(self.clear_data)
        info_layout.setColumnStretch(2, 1)
        info_layout.addWidget(clear, 0, 3, 2, 1, Qt.AlignmentFlag.AlignVCenter)
        outer.addWidget(info_box)
        outer.addStretch()
        close = no_tab_button("關閉"); close.clicked.connect(self.accept)
        row = QHBoxLayout(); row.addStretch(); row.addWidget(close); outer.addLayout(row)

    def _ledger_position_changed(self, _index: int) -> None:
        value = self.ledger_position.currentData() or "latest"
        self.config["ledger_entry_position"] = value

    def _summary_settings_changed(self) -> None:
        try:
            recent = int(self.common_summary_recent.text())
        except Exception:
            recent = int(self.config.get("common_summary_recent_n", 100) or 100)
        try:
            minimum = int(self.common_summary_min.text())
        except Exception:
            minimum = int(self.config.get("common_summary_min_count", 3) or 3)
        recent = max(20, min(1000, recent))
        minimum = max(2, min(50, minimum))
        self.common_summary_recent.setText(str(recent))
        self.common_summary_min.setText(str(minimum))
        self.config["common_summary_basis"] = self.common_summary_basis.currentData() or "tx_date"
        self.config["common_summary_recent_n"] = recent
        self.config["common_summary_min_count"] = minimum
        save_config(self.config)
        self.settings_changed_callback()

    def browse_export_directory(self):
        initial = self.export_path.text().strip() or str(Path.home())
        folder = QFileDialog.getExistingDirectory(self, "選擇 Excel 匯出預設位置", initial)
        if folder:
            self.export_path.setText(folder)
            self.config["export_directory"] = folder
            save_config(self.config)

    def _refresh_google_status(self):
        if not hasattr(self, "google_client"):
            return
        if self.google_client.is_connected:
            user = self.google_client.user_label or "已授權帳戶"
            status = f"已連結：{user}"
        elif self.google_client.has_client:
            status = "已載入 OAuth 憑證，尚未連結 Google Drive"
        else:
            status = "尚未設定 OAuth 憑證"
        if hasattr(self, "google_status"):
            self.google_status.setText(status)
        if hasattr(self, "google_last"):
            last = self.config.get("google_drive_last_backup_date") or "尚未同步"
            self.google_last.setText(f"上次雲端備份：{last}")
        if hasattr(self, "google_connect_btn"):
            self.google_connect_btn.setEnabled(self.google_client.has_client)
        if hasattr(self, "google_disconnect_btn"):
            self.google_disconnect_btn.setEnabled(self.google_client.is_connected)
        if hasattr(self, "google_sync_now_btn"):
            self.google_sync_now_btn.setEnabled(self.google_client.is_connected)

    def import_google_oauth(self):
        path, _ = QFileDialog.getOpenFileName(self, "匯入 Google OAuth 憑證", str(Path.home()), "JSON 檔案 (*.json)")
        if not path:
            return
        try:
            import_oauth_client_json(Path(path), self.config)
            save_config(self.config)
            self.google_client = GoogleDriveClient(self.config)
            self._refresh_google_status()
            QMessageBox.information(self, APP_NAME, "OAuth 憑證已載入。接著按「連結 Google Drive」完成帳戶授權。")
        except GoogleDriveError as exc:
            QMessageBox.warning(self, APP_NAME, str(exc))

    def connect_google_drive(self):
        try:
            self.google_client.authorize(QApplication.processEvents)
            save_config(self.config)
            self._refresh_google_status()
            QMessageBox.information(self, APP_NAME, "Google Drive 已連結。")
        except GoogleDriveError as exc:
            QMessageBox.warning(self, APP_NAME, str(exc))

    def disconnect_google_drive(self):
        if QMessageBox.question(
            self, APP_NAME, "確定中斷此程式的 Google Drive 連結嗎？",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
            QMessageBox.StandardButton.Cancel,
        ) != QMessageBox.StandardButton.Yes:
            return
        self.google_client.disconnect()
        self.config["google_drive_sync_enabled"] = False
        if hasattr(self, "google_sync_check"):
            self.google_sync_check.setChecked(False)
        save_config(self.config)
        self._refresh_google_status()

    def _google_sync_toggled(self, checked: bool):
        if checked and not self.google_client.is_connected:
            self.google_sync_check.blockSignals(True)
            self.google_sync_check.setChecked(False)
            self.google_sync_check.blockSignals(False)
            QMessageBox.information(self, APP_NAME, "請先完成 Google Drive 授權。")
            return
        self.config["google_drive_sync_enabled"] = bool(checked)
        save_config(self.config)

    def sync_google_now(self):
        if not self.google_client.is_connected:
            QMessageBox.information(self, APP_NAME, "尚未連結 Google Drive。")
            return
        backup_dir = self.db.path.parent / "AutoBackup"
        backup_dir.mkdir(parents=True, exist_ok=True)
        target = backup_dir / f"CYaccbkup_{date.today().strftime('%Y%m%d')}.db"
        try:
            # Refresh today's verified local snapshot first; only the network
            # portion runs in the background so the live DB connection never
            # crosses threads.
            self.db.backup_to(target)
        except Exception as exc:
            QMessageBox.warning(self, APP_NAME, f"建立本機備份失敗：{exc}")
            return
        self.google_sync_now_btn.setEnabled(False)
        self.google_sync_now_btn.setText("同步中…")
        snapshot = dict(self.config)

        def task():
            return upload_backup_and_cleanup(target, snapshot, 30)

        def finished(updated, exc):
            self.google_sync_now_btn.setText("立即同步")
            self.google_sync_now_btn.setEnabled(self.google_client.is_connected)
            if exc is not None:
                QMessageBox.warning(self, APP_NAME, f"Google Drive 同步失敗：{exc}")
                return
            if isinstance(updated, dict):
                for key, value in updated.items():
                    if key.startswith("google_"):
                        self.config[key] = value
            save_config(self.config)
            self.google_client = GoogleDriveClient(self.config)
            self._refresh_google_status()
            QMessageBox.information(self, APP_NAME, f"Google Drive 同步完成：\n{target.name}")

        self._background_google_sync = start_background_task(task, finished)

    def open_data_folder(self):
        folder = str(self.db.path.parent)
        try:
            if sys.platform == "win32":
                os.startfile(folder)
            else:
                import subprocess
                subprocess.Popen(["xdg-open", folder])
        except Exception as exc:
            QMessageBox.warning(self, APP_NAME, f"無法開啟資料資料夾：{exc}")

    def browse_db(self):
        folder = QFileDialog.getExistingDirectory(self, "選擇資料庫儲存資料夾", self.db_path.text())
        if folder:
            self.db_path.setText(folder)

    def apply_db(self):
        target = Path(self.db_path.text())
        if target.resolve() == self.db.path.parent.resolve():
            return
        ok, msg = self.change_db_callback(target)
        if ok:
            QMessageBox.information(self, APP_NAME, "資料庫位置已更新。")
            self.db_path.setText(str(self.db.path.parent))
        else:
            QMessageBox.warning(self, APP_NAME, msg)

    def apply_lock(self):
        try:
            new = self.lock_month.month()
        except ValueError:
            return
        old = self.db.locked_through()
        if new == old:
            return
        if old and month_to_index(new) < month_to_index(old):
            text = (f"確定將鎖定年月由 {old} 改為 {new} 嗎？\n\n"
                    f"{shift_month(new, 1)} 至 {old} 將恢復可編輯狀態。請確認這是刻意的解除鎖定操作。")
            answer = QMessageBox.warning(
                self, APP_NAME, text,
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
                QMessageBox.StandardButton.Cancel,
            )
        else:
            text = f"確定將記帳資料鎖定至 {new} 嗎？\n{new} 及之前的資料將無法新增、修改、刪除或變更期初餘額。"
            answer = QMessageBox.question(
                self, APP_NAME, text,
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
                QMessageBox.StandardButton.Cancel,
            )
        if answer != QMessageBox.StandardButton.Yes:
            return
        self.db.set_locked_through(new)
        self.current_lock.setText(new)
        self.settings_changed_callback()

    def manual_backup(self):
        default = f"CYaccbkup_{date.today().strftime('%Y%m%d')}.db"
        path, _ = QFileDialog.getSaveFileName(self, "建立備份", str(Path.home() / default), "SQLite 資料庫 (*.db)")
        if not path:
            return
        try:
            self.db.backup_to(Path(path))
            QMessageBox.information(self, APP_NAME, f"備份完成：\n{path}")
        except Exception as e:
            QMessageBox.warning(self, APP_NAME, f"備份失敗：{e}")

    def restore(self):
        path, _ = QFileDialog.getOpenFileName(self, "選擇備份檔", str(self.db.path.parent), "SQLite 資料庫 (*.db)")
        if not path:
            return
        if QMessageBox.question(self, APP_NAME, "還原備份將覆蓋目前的記帳資料，是否繼續？") != QMessageBox.StandardButton.Yes:
            return
        ok, msg = self.restore_callback(Path(path))
        if ok:
            QMessageBox.information(self, APP_NAME, "備份還原完成。")
            self.settings_changed_callback()
        else:
            QMessageBox.warning(self, APP_NAME, f"還原失敗：{msg}")

    def clear_data(self):
        first, accepted = QInputDialog.getText(
            self, "清除記帳資料與期初餘額",
            "這會重置整個本機帳本：交易、期初、帳戶、科目、鎖帳與本機設定。\n既有備份會保留。第一次確認：請輸入 DELETE。",
            QLineEdit.EchoMode.Normal,
        )
        if not accepted:
            return
        if first != "DELETE":
            QMessageBox.warning(self, APP_NAME, "文字不正確，沒有清除任何資料。")
            return
        second, accepted = QInputDialog.getText(
            self, "最後確認清除",
            "最後一次確認：請再次輸入 DELETE。\n本機帳本將從頭開始；Google Drive 上的既有備份不受影響。",
            QLineEdit.EchoMode.Normal,
        )
        if not accepted:
            return
        if second != "DELETE":
            QMessageBox.warning(self, APP_NAME, "第二次文字不正確，沒有清除任何資料。")
            return
        try:
            self.clear_callback()
        except Exception as exc:
            QMessageBox.warning(self, APP_NAME, f"清除失敗：{exc}")
            return
        self.google_client = GoogleDriveClient(self.config)
        self._refresh_google_status()
        self.google_sync_check.blockSignals(True)
        self.google_sync_check.setChecked(False)
        self.google_sync_check.blockSignals(False)
        QMessageBox.information(self, APP_NAME, "本機帳本已重置；重置前的本機備份已保留。")
        self.settings_changed_callback()
