import json

from core.paths import SETTINGS_FILE


def _load_settings() -> dict:
    try:
        with open(SETTINGS_FILE, 'r') as f:
            return json.load(f)
    except Exception:
        return {}


def _save_settings(data: dict) -> None:
    with open(SETTINGS_FILE, 'w') as f:
        json.dump(data, f, indent=2)


def get_setting(key: str, default=None):
    return _load_settings().get(key, default)


def set_setting(key: str, value) -> None:
    settings = _load_settings()
    settings[key] = value
    _save_settings(settings)
