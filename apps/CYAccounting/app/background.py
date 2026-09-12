from __future__ import annotations

from collections.abc import Callable

from PySide6.QtCore import QObject, QThread, Signal, Slot
from PySide6.QtWidgets import QApplication


class _TaskThread(QThread):
    completed = Signal(object, object)

    def __init__(self, task: Callable[[], object], parent: QObject):
        super().__init__(parent)
        self.task = task
        self.delivery_pending = True

    def run(self) -> None:
        try:
            self.completed.emit(self.task(), None)
        except Exception as exc:
            self.completed.emit(None, exc)


class _Relay(QObject):
    def __init__(
        self,
        thread: _TaskThread,
        callback: Callable[[object, object], None],
        parent: QObject,
    ):
        super().__init__(parent)
        self.thread = thread
        self.callback = callback

    @Slot(object, object)
    def dispatch(self, result: object, error: object) -> None:
        try:
            self.callback(result, error)
        finally:
            self.thread.delivery_pending = False
            # Release references captured by a finished dialog/window callback.
            self.callback = lambda _result, _error: None


def start_background_task(
    task: Callable[[], object],
    callback: Callable[[object, object], None],
) -> QThread:
    """Run one blocking operation off the GUI thread and report safely on it."""
    app = QApplication.instance()
    if app is None:
        raise RuntimeError("QApplication 尚未建立")
    tasks = getattr(app, "_cy_background_tasks", None)
    if tasks is None:
        tasks = []
        app._cy_background_tasks = tasks
    else:
        # Finished threads are safe to release only after their GUI callback
        # has consumed the result.
        tasks[:] = [
            item for item in tasks
            if item[0].isRunning() or bool(item[0].delivery_pending)
        ]
    thread = _TaskThread(task, app)
    relay = _Relay(thread, callback, app)
    tasks.append((thread, relay))
    thread.completed.connect(relay.dispatch)
    thread.start()
    return thread


def background_tasks_running() -> bool:
    app = QApplication.instance()
    tasks = getattr(app, "_cy_background_tasks", []) if app is not None else []
    return any(
        thread.isRunning() or bool(thread.delivery_pending)
        for thread, _relay in list(tasks)
    )
