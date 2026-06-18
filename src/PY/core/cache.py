import json
import time

from core.paths import CACHE_FILE


def load_cache() -> dict:
    try:
        with open(CACHE_FILE, 'r') as f:
            return json.load(f)
    except Exception:
        return {}


def save_cache(data: dict) -> None:
    try:
        with open(CACHE_FILE, 'w') as f:
            json.dump(data, f)
    except Exception:
        pass


def get_cached(key: str, ttl: float) -> object | None:
    entry = load_cache().get(key)
    if isinstance(entry, dict) and (time.time() - entry.get('ts', 0)) < ttl:
        return entry.get('value')
    return None


def set_cached(key: str, value) -> None:
    cache = load_cache()
    cache[key] = {'value': value, 'ts': time.time()}
    save_cache(cache)


def record_launch() -> dict:
    cache = load_cache()
    stats = cache.get('launch_stats', {'count': 0, 'last_run': None})
    stats['count'] += 1
    stats['last_run'] = time.time()
    cache['launch_stats'] = stats
    save_cache(cache)
    return stats


def get_launch_stats() -> dict:
    return load_cache().get('launch_stats', {'count': 0, 'last_run': None})
