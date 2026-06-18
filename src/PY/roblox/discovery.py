import glob
import os

from core.cache import get_cached, set_cached
from core.paths import CACHE_TTL, ROBLOX_SEARCH_ROOTS

ROBLOX_EXE = 'RobloxPlayerBeta.exe'


def find_roblox_exe() -> str | None:
    cached = get_cached('roblox_exe', CACHE_TTL)
    if cached and os.path.exists(cached):
        return cached

    for root in ROBLOX_SEARCH_ROOTS:
        matches = glob.glob(os.path.join(root, 'version-*', ROBLOX_EXE))
        if matches:
            latest = max(matches, key=os.path.getmtime)
            set_cached('roblox_exe', latest)
            return latest

    return None


def get_roblox_version(exe: str | None) -> str | None:
    if not exe:
        return None
    try:
        import win32api
        info = win32api.GetFileVersionInfo(exe, '\\')
        ms, ls = info['FileVersionMS'], info['FileVersionLS']
        return f'{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}'
    except Exception:
        folder = os.path.basename(os.path.dirname(exe))
        return folder[len('version-'):] if folder.startswith('version-') else None
