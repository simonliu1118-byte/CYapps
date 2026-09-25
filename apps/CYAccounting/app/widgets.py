from __future__ import annotations

from datetime import date, timedelta
from typing import Callable, Optional

from PySide6.QtCore import QDate, QEvent, QModelIndex, QPoint, QRegularExpression, QTimer, Qt, Signal
from PySide6.QtGui import QBrush, QColor, QFont, QIntValidator, QRegularExpressionValidator, QStandardItem, QStandardItemModel, QTextCharFormat
from PySide6.QtWidgets import (
    QAbstractItemView,
    QAbstractSpinBox,
    QCalendarWidget,
    QComboBox,
    QDateEdit,
    QDialog,
    QDialogButtonBox,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QSpinBox,
    QStyledItemDelegate,
    QTableView,
    QVBoxLayout,
    QWidget,
)

from util import format_amount, normalize_date_input, shift_month, trim_weighted, weighted_units
from theme import CALENDAR_STYLE, CATEGORY_POPUP_STYLE, COMBO_ARROW_PATH


class WeightedLineEdit(QLineEdit):
    """Line edit with IME-safe weighted length limiting.

    Full-width/East Asian characters count as two units. Pre-edit text from an
    IME is never rejected; trimming happens only after committed text changes.
    """

    def __init__(self, max_units: int, parent: QWidget | None = None):
        super().__init__(parent)
        self.max_units = max_units
        self._composing = False
        self._internal = False
        self.textChanged.connect(self._enforce)

    def inputMethodEvent(self, event):  # noqa: N802
        self._composing = bool(event.preeditString())
        super().inputMethodEvent(event)
        self._composing = bool(event.preeditString())
        if not self._composing:
            self._enforce(self.text())

    def _enforce(self, text: str) -> None:
        if self._internal or self._composing:
            return
        if weighted_units(text) <= self.max_units:
            return
        cursor = self.cursorPosition()
        trimmed = trim_weighted(text, self.max_units)
        self._internal = True
        self.setText(trimmed)
        self.setCursorPosition(min(cursor, len(trimmed)))
        self._internal = False


class SmartDateLineEdit(QLineEdit):
    """Date text box that formats YYYYMMDD but deliberately does not validate it."""

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setMaxLength(10)
        self.setValidator(QRegularExpressionValidator(QRegularExpression(r"[0-9/]{0,10}"), self))
        self._formatting = False
        # Use textChanged plus a queued normalization.  This is more reliable
        # than relying only on textEdited when the user replaces a selected
        # slash-formatted date with eight digits.
        self.textChanged.connect(self._schedule_format)
        self.editingFinished.connect(self.normalize_format)

    def _schedule_format(self, _text: str) -> None:
        if not self._formatting:
            QTimer.singleShot(0, self._format_if_complete)

    def _format_if_complete(self) -> None:
        if self._formatting:
            return
        text = self.text()
        normalized = normalize_date_input(text)
        if normalized == text:
            return
        self._formatting = True
        self.setText(normalized)
        self.setCursorPosition(len(normalized))
        self._formatting = False

    def normalize_format(self) -> None:
        if self._formatting:
            return
        normalized = normalize_date_input(self.text())
        if normalized != self.text():
            self._formatting = True
            self.setText(normalized)
            self.setCursorPosition(len(normalized))
            self._formatting = False


class MonthSpinBox(QWidget):
    """Editable YYYY/MM selector with separate previous/next month buttons.

    The historical class name is retained so existing dialogs can reuse it.
    """

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self._last_valid = date.today().strftime("%Y/%m")
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(4)

        self.prev_button = QPushButton("<")
        self.prev_button.setObjectName("monthNavButton")
        self.prev_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.prev_button.setFixedWidth(38)
        self.prev_button.setToolTip("上一個月")

        self.line_edit = QLineEdit(self._last_valid)
        self.line_edit.setObjectName("monthLineEdit")
        self.line_edit.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.line_edit.setMaxLength(7)
        self.line_edit.setFixedWidth(98)
        self.line_edit.setValidator(
            QRegularExpressionValidator(QRegularExpression(r"[0-9]{0,4}/?[0-9]{0,2}"), self.line_edit)
        )

        self.next_button = QPushButton(">")
        self.next_button.setObjectName("monthNavButton")
        self.next_button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.next_button.setFixedWidth(38)
        self.next_button.setToolTip("下一個月")

        layout.addWidget(self.prev_button)
        layout.addWidget(self.line_edit)
        layout.addWidget(self.next_button)

        self.prev_button.clicked.connect(lambda: self._step(-1))
        self.next_button.clicked.connect(lambda: self._step(1))
        self.line_edit.editingFinished.connect(self.validate_month)
        self.setFixedWidth(182)

    @staticmethod
    def _normalise(text: str) -> str | None:
        import re

        match = re.fullmatch(r"\s*(\d{4})/(\d{1,2})\s*", text or "")
        if not match:
            return None
        year, month = int(match.group(1)), int(match.group(2))
        if not 1900 <= year <= 2199 or not 1 <= month <= 12:
            return None
        return f"{year:04d}/{month:02d}"

    def _set_error(self, error: bool) -> None:
        self.line_edit.setProperty("monthError", error)
        self.line_edit.style().unpolish(self.line_edit)
        self.line_edit.style().polish(self.line_edit)
        self.line_edit.setToolTip("月份格式必須為 YYYY/MM" if error else "")

    def validate_month(self) -> bool:
        value = self._normalise(self.line_edit.text())
        if value is None:
            self._set_error(True)
            self.line_edit.setFocus()
            self.line_edit.selectAll()
            return False
        self._last_valid = value
        self.line_edit.setText(value)
        self._set_error(False)
        return True

    def _step(self, delta: int) -> None:
        current = self._normalise(self.line_edit.text()) or self._last_valid
        self.set_month(shift_month(current, delta))

    def wheelEvent(self, event):  # noqa: N802
        delta = event.angleDelta().y()
        if delta:
            self._step(-1 if delta > 0 else 1)
            event.accept()
            return
        super().wheelEvent(event)

    def month(self) -> str:
        if not self.validate_month():
            raise ValueError("月份格式錯誤")
        return self._last_valid

    def set_month(self, month: str) -> None:
        value = self._normalise(month)
        if value is None:
            raise ValueError(f"Invalid month: {month}")
        self._last_valid = value
        self.line_edit.setText(value)
        self._set_error(False)


class CategoryComboBox(QComboBox):
    """Flat combo visually grouped by disabled category headings."""

    def __init__(self, parent: QWidget | None = None):
        super().__init__(parent)
        self.setModel(QStandardItemModel(self))
        self.setMaxVisibleItems(18)
        self.setEditable(True)
        self.setObjectName("categoryComboBox")
        self.lineEdit().setReadOnly(True)
        # The embedded line edit must not paint its own frame over the combo box.
        # Keep one complete outer frame and an explicit frame around the popup list.
        self.lineEdit().setFrame(False)
        self.lineEdit().setStyleSheet(
            "QLineEdit { border: 0; border-radius: 0; background: transparent; "
            "padding: 0; min-height: 0px; }"
        )
        self.view().setStyleSheet(CATEGORY_POPUP_STYLE)
        self.currentIndexChanged.connect(self._sync_display)

    def set_tree(self, tree: list[dict], preserve: str | None = None) -> None:
        current = preserve if preserve is not None else self.current_value()
        model = QStandardItemModel(self)
        for group in tree:
            g = QStandardItem(group["name"])
            g.setEnabled(False)
            font = g.font(); font.setBold(True); g.setFont(font)
            g.setForeground(QBrush(QColor("#667085")))
            g.setBackground(QBrush(QColor("#F1F3F5")))
            g.setData(None, Qt.ItemDataRole.UserRole)
            model.appendRow(g)
            for cat in group.get("categories", []):
                item = QStandardItem("　" + cat["name"])
                item.setData(cat["name"], Qt.ItemDataRole.UserRole)
                model.appendRow(item)
        self.setModel(model)
        idx = -1
        if current:
            for i in range(model.rowCount()):
                if model.item(i).data(Qt.ItemDataRole.UserRole) == current:
                    idx = i; break
        if idx < 0:
            for i in range(model.rowCount()):
                if model.item(i).isEnabled() and model.item(i).data(Qt.ItemDataRole.UserRole):
                    idx = i; break
        self.setCurrentIndex(idx)
        self._sync_display()

    def _sync_display(self, *_):
        value = self.current_value()
        if value:
            self.setToolTip(value)
            if self.isEditable():
                self.lineEdit().setText(value)

    def current_value(self) -> str:
        return self.currentData(Qt.ItemDataRole.UserRole) or ""

    def set_current_value(self, value: str) -> None:
        model = self.model()
        for i in range(model.rowCount()):
            if model.index(i, 0).data(Qt.ItemDataRole.UserRole) == value:
                self.setCurrentIndex(i)
                return


class DatePickerDialog(QDialog):
    def __init__(self, initial: date, parent: QWidget | None = None):
        super().__init__(parent)
        self.setWindowTitle("選擇日期")
        self.setModal(True)
        self.setFixedSize(430, 360)
        self.selected_date: date | None = None
        self._formatted_dates: list[QDate] = []

        outer = QVBoxLayout(self)
        outer.setContentsMargins(10, 10, 10, 10)
        outer.setSpacing(8)
        nav = QHBoxLayout()
        self.btn_prev_year = QPushButton("<<")
        self.btn_prev_month = QPushButton("<")
        self.month_label = QLabel()
        self.month_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        font = self.month_label.font()
        font.setBold(True)
        font.setPointSize(font.pointSize() + 1)
        self.month_label.setFont(font)
        self.btn_next_month = QPushButton(">")
        self.btn_next_year = QPushButton(">>")
        for b in (self.btn_prev_year, self.btn_prev_month, self.btn_next_month, self.btn_next_year):
            b.setFixedWidth(48)
            b.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        nav.addWidget(self.btn_prev_year)
        nav.addWidget(self.btn_prev_month)
        nav.addWidget(self.month_label, 1)
        nav.addWidget(self.btn_next_month)
        nav.addWidget(self.btn_next_year)
        outer.addLayout(nav)

        self.calendar = QCalendarWidget()
        self.calendar.setNavigationBarVisible(False)
        self.calendar.setGridVisible(True)
        self.calendar.setFirstDayOfWeek(Qt.DayOfWeek.Monday)
        self.calendar.setVerticalHeaderFormat(QCalendarWidget.VerticalHeaderFormat.NoVerticalHeader)
        self.calendar.setSelectedDate(QDate(initial.year, initial.month, initial.day))
        self.calendar.setCurrentPage(initial.year, initial.month)
        self.calendar.setStyleSheet(CALENDAR_STYLE)
        sat = self.calendar.weekdayTextFormat(Qt.DayOfWeek.Saturday)
        sat.setForeground(QBrush(QColor("#2c8b57")))
        self.calendar.setWeekdayTextFormat(Qt.DayOfWeek.Saturday, sat)
        sun = self.calendar.weekdayTextFormat(Qt.DayOfWeek.Sunday)
        sun.setForeground(QBrush(QColor("#d64b4b")))
        self.calendar.setWeekdayTextFormat(Qt.DayOfWeek.Sunday, sun)
        outer.addWidget(self.calendar)

        self.btn_prev_year.clicked.connect(lambda: self._shift(years=-1))
        self.btn_next_year.clicked.connect(lambda: self._shift(years=1))
        self.btn_prev_month.clicked.connect(lambda: self._shift(months=-1))
        self.btn_next_month.clicked.connect(lambda: self._shift(months=1))
        self.calendar.currentPageChanged.connect(self._update_page)
        self.calendar.clicked.connect(self._choose_date)
        self._update_page(initial.year, initial.month)

    def _shift(self, years: int = 0, months: int = 0):
        y = self.calendar.yearShown() + years
        m = self.calendar.monthShown() + months
        while m < 1:
            y -= 1
            m += 12
        while m > 12:
            y += 1
            m -= 12
        self.calendar.setCurrentPage(y, m)

    def _update_page(self, year: int, month: int):
        self.month_label.setText(f"{year:04d} 年 {month:02d} 月")
        self._apply_month_formats(year, month)

    def _apply_month_formats(self, year: int, month: int) -> None:
        # Clear formats from the previously displayed 6-week grid.
        for qd in self._formatted_dates:
            self.calendar.setDateTextFormat(qd, QTextCharFormat())
        self._formatted_dates.clear()

        first = date(year, month, 1)
        # QCalendarWidget paints a leading previous-month week when the first
        # day of the month lands exactly on the configured first weekday.
        # The old code started at that month's day 1 and therefore missed e.g.
        # 2025/11/24-30 while showing December 2025 (Monday-first calendar).
        natural_start = first - timedelta(days=first.weekday())
        grid_start = natural_start - timedelta(days=7 if first.weekday() == 0 else 0)
        for offset in range(42):
            current = grid_start + timedelta(days=offset)
            qd = QDate(current.year, current.month, current.day)
            fmt = QTextCharFormat()
            if current.month != month:
                fmt.setBackground(QBrush(QColor("#F1F3F5")))
                fmt.setForeground(QBrush(QColor("#98A2B3")))
            elif current.weekday() == 5:
                fmt.setForeground(QBrush(QColor("#2c8b57")))
            elif current.weekday() == 6:
                fmt.setForeground(QBrush(QColor("#d64b4b")))
            self.calendar.setDateTextFormat(qd, fmt)
            self._formatted_dates.append(qd)

    def _choose_date(self, qd: QDate):
        self.calendar.setSelectedDate(qd)
        self.selected_date = date(qd.year(), qd.month(), qd.day())
        # Keep the selection visible briefly so the click has clear feedback.
        QTimer.singleShot(110, self.accept)


class TransactionDelegate(QStyledItemDelegate):
    """Inline editor delegate for the ledger table."""

    def __init__(self, account_provider: Callable[[], list[str]], category_provider: Callable[[str], list[str]], parent=None):
        super().__init__(parent)
        self.account_provider = account_provider
        self.category_provider = category_provider

    @staticmethod
    def _compact_editor(editor):
        # Inline editors should visually stay inside the table cell instead of
        # inheriting the application's larger 11pt input-field typography.
        font = editor.font()
        font.setPointSizeF(9.0)
        editor.setFont(font)
        if isinstance(editor, QLineEdit):
            editor.setStyleSheet(
                "QLineEdit { font-size: 9pt; min-height: 0px; padding: 0px 3px; "
                "border-radius: 0px; }"
            )
        elif isinstance(editor, QComboBox):
            editor.setStyleSheet(
                "QComboBox { font-size: 9pt; min-height: 0px; padding: 0px 13px 0px 3px; "
                "border-radius: 0px; } "
                "QComboBox::drop-down { subcontrol-origin: padding; subcontrol-position: top right; "
                "width: 12px; border-left: 1px solid #aeb9c5; background:#f1f4f7; } "
                f'QComboBox::down-arrow {{ image: url("{COMBO_ARROW_PATH}"); width: 8px; height: 6px; }} '
                "QComboBox QAbstractItemView { font-size: 9pt; }"
            )
        return editor

    def createEditor(self, parent, option, index):  # noqa: N802
        col = index.column()
        row_data = index.model().row_data(index.row())
        if not row_data:
            return None
        kind = row_data["kind"]
        if col == 0:
            # A plain line editor fits the table cell reliably.  QDateEdit's
            # embedded calendar button used to squeeze YYYY/MM/DD and corrupt
            # the inline rendering on narrow date columns.
            ed = SmartDateLineEdit(parent)
            return self._compact_editor(ed)
        if col == 1:
            cb = QComboBox(parent)
            names = self.account_provider()
            current = row_data["account_name"]
            if current not in names:
                names = [current] + names
            cb.addItems(names)
            return self._compact_editor(cb)
        if col in (3, 6):
            expected = "income" if col == 3 else "expense"
            if kind != expected:
                return None
            cb = QComboBox(parent)
            names = self.category_provider(kind)
            current = row_data["category_name"]
            if current not in names:
                names = [current] + names
            cb.addItems(names)
            return self._compact_editor(cb)
        if col in (4, 7):
            expected = "income" if col == 4 else "expense"
            if kind != expected:
                return None
            return self._compact_editor(WeightedLineEdit(40, parent))
        if col in (5, 8):
            expected = "income" if col == 5 else "expense"
            if kind != expected:
                return None
            ed = QLineEdit(parent)
            ed.setValidator(QRegularExpressionValidator(QRegularExpression(r"[0-9]{0,7}"), ed))
            return self._compact_editor(ed)
        return None

    def setEditorData(self, editor, index):  # noqa: N802
        value = index.model().raw_value(index.row(), index.column())
        if isinstance(editor, QComboBox):
            i = editor.findText(str(value))
            editor.setCurrentIndex(max(i, 0))
        elif isinstance(editor, QLineEdit):
            editor.setText(str(value))
            editor.selectAll()

    def setModelData(self, editor, model, index):  # noqa: N802
        if isinstance(editor, QComboBox):
            value = editor.currentText()
        else:
            value = editor.text()
        model.setData(index, value, Qt.ItemDataRole.EditRole)
