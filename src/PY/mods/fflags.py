import json
import os

from core.settings import get_setting, set_setting
from roblox.discovery import find_roblox_exe

SETTINGS_KEY = 'fflags'
CLIENT_SETTINGS_DIRNAME = 'ClientSettings'
CLIENT_SETTINGS_FILENAME = 'ClientAppSettings.json'


def _load() -> dict:
    return dict(get_setting(SETTINGS_KEY, {}) or {})


def _save(flags: dict) -> None:
    set_setting(SETTINGS_KEY, flags)


def _coerce_value(value):
    return value.strip() if isinstance(value, str) else value


def list_fflags() -> list:
    return [{'name': k, 'value': v} for k, v in sorted(_load().items())]


def set_fflag(name: str, value) -> tuple:
    name = (name or '').strip()
    if not name:
        return False, 'Flag name cannot be empty.'

    flags = _load()
    flags[name] = _coerce_value(value)
    _save(flags)
    return True, f"Set '{name}'."


def remove_fflag(name: str) -> tuple:
    flags = _load()
    if name not in flags:
        return False, f"'{name}' is not set."

    del flags[name]
    _save(flags)
    return True, f"Removed '{name}'."


def import_json(raw_text: str) -> tuple:
    try:
        data = json.loads(raw_text)
    except (json.JSONDecodeError, TypeError) as e:
        return False, f'Invalid JSON: {e}', 0

    if not isinstance(data, dict):
        return False, 'JSON must be a flat object of flag name to value.', 0

    flags = _load()
    count = 0
    for key, value in data.items():
        if isinstance(value, (dict, list)):
            continue
        flags[str(key).strip()] = _coerce_value(value)
        count += 1

    if count == 0:
        return False, 'No valid flags found in that JSON.', 0

    _save(flags)
    return True, f'Imported {count} flag(s).', count


def clear_all() -> None:
    _save({})


def resync_to_live() -> tuple:
    exe = find_roblox_exe()
    if not exe:
        return False, 'Roblox installation not found.'

    version_dir = os.path.dirname(exe)
    settings_dir = os.path.join(version_dir, CLIENT_SETTINGS_DIRNAME)
    settings_path = os.path.join(settings_dir, CLIENT_SETTINGS_FILENAME)

    try:
        os.makedirs(settings_dir, exist_ok=True)
        with open(settings_path, 'w', encoding='utf-8') as f:
            json.dump(_load(), f, indent=2)
    except OSError as e:
        return False, f'Could not write ClientAppSettings.json: {e}'

    return True, 'FFlags synced.'
