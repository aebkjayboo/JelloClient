import ctypes
import ctypes.wintypes
import threading

MUTEX_NAME = 'ROBLOX_singletonEvent'

_mutex_thread: threading.Thread | None = None
_mutex_stop = threading.Event()


def _mutex_worker() -> None:
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)

    create_mutex = kernel32.CreateMutexW
    create_mutex.argtypes = [ctypes.wintypes.LPVOID, ctypes.wintypes.BOOL, ctypes.wintypes.LPCWSTR]
    create_mutex.restype = ctypes.wintypes.HANDLE

    mutex = create_mutex(None, True, MUTEX_NAME)
    if not mutex:
        return

    _mutex_stop.wait()
    kernel32.ReleaseMutex(mutex)
    kernel32.CloseHandle(mutex)


def is_mutex_alive() -> bool:
    return _mutex_thread is not None and _mutex_thread.is_alive()


def start_mutex() -> None:
    global _mutex_thread
    if is_mutex_alive():
        return
    _mutex_stop.clear()
    _mutex_thread = threading.Thread(target=_mutex_worker, daemon=True, name='mutex-worker')
    _mutex_thread.start()


def stop_mutex() -> None:
    _mutex_stop.set()
    if _mutex_thread:
        _mutex_thread.join(timeout=2)
