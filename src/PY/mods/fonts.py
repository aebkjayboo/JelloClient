import glob
import json
import os
import shutil

from core.paths import FONTS_CACHE_DIR, STAGED_FAMILIES_DIR, STAGED_FONT
from core.settings import get_setting, set_setting
from mods.base import assert_writable, hashes_match, safe_id
from roblox.discovery import find_roblox_exe

CUSTOM_FONT_REL = os.path.join('content', 'fonts', 'CustomFont.ttf')
FAMILIES_DIR_REL = os.path.join('content', 'fonts', 'families')
CUSTOM_FONT_ASSET_ID = 'rbxasset://fonts/CustomFont.ttf'

BUILTIN_FONTS = []


def _is_ttf(path: str) -> bool:
    return bool(path) and path.lower().endswith('.ttf') and os.path.isfile(path)


def _cache_path(font_id: str) -> str:
    return os.path.join(FONTS_CACHE_DIR, f'{safe_id(font_id)}.ttf')


def _resolve_font_source(font_id: str) -> str | None:
    for f in BUILTIN_FONTS:
        if f.get('id') == font_id and _is_ttf(f.get('path', '')):
            return f['path']

    cached = _cache_path(font_id)
    return cached if _is_ttf(cached) else None


def _live_paths() -> tuple[str, str, str] | None:
    exe = find_roblox_exe()
    if not exe:
        return None

    version_dir = os.path.dirname(exe)
    font_target = os.path.join(version_dir, CUSTOM_FONT_REL)
    families_dir = os.path.join(version_dir, FAMILIES_DIR_REL)
    return version_dir, font_target, families_dir


def _rewrite_family_jsons(live_families_dir: str) -> None:
    if not os.path.isdir(live_families_dir):
        return

    os.makedirs(STAGED_FAMILIES_DIR, exist_ok=True)

    for json_path in glob.glob(os.path.join(live_families_dir, '*.json')):
        filename = os.path.basename(json_path)
        staged_path = os.path.join(STAGED_FAMILIES_DIR, filename)
        if os.path.exists(staged_path):
            continue

        try:
            with open(json_path, 'r', encoding='utf-8') as f:
                family = json.load(f)
        except Exception:
            continue

        changed = False
        for face in family.get('faces', []):
            if face.get('assetId') != CUSTOM_FONT_ASSET_ID:
                face['assetId'] = CUSTOM_FONT_ASSET_ID
                changed = True

        if changed:
            with open(staged_path, 'w', encoding='utf-8') as f:
                json.dump(family, f, indent=2)


def _clear_staged_families() -> None:
    if os.path.isdir(STAGED_FAMILIES_DIR):
        shutil.rmtree(STAGED_FAMILIES_DIR)
    os.makedirs(STAGED_FAMILIES_DIR, exist_ok=True)


def _sync_dir(staged_dir: str, live_dir: str) -> None:
    if not os.path.isdir(staged_dir):
        return

    for staged_file in glob.glob(os.path.join(staged_dir, '*')):
        if not os.path.isfile(staged_file):
            continue

        live_file = os.path.join(live_dir, os.path.basename(staged_file))
        if hashes_match(staged_file, live_file):
            continue

        os.makedirs(live_dir, exist_ok=True)
        assert_writable(live_file)
        shutil.copyfile(staged_file, live_file)


def list_fonts() -> list[dict]:
    fonts = [{'id': 'default', 'name': 'Default', 'custom': False}]

    for f in BUILTIN_FONTS:
        if _is_ttf(f.get('path', '')):
            fonts.append({'id': f['id'], 'name': f['name'], 'custom': False})

    cached = sorted(glob.glob(os.path.join(FONTS_CACHE_DIR, '*.ttf')), key=os.path.getmtime, reverse=True)
    for path in cached:
        font_id = os.path.splitext(os.path.basename(path))[0]
        fonts.append({'id': font_id, 'name': font_id.replace('_', ' '), 'custom': True})

    return fonts


def get_selected_font_id() -> str:
    return get_setting('font_id', 'default')


def add_font(source_path: str) -> tuple[bool, str, dict | None]:
    if not _is_ttf(source_path):
        return False, 'Only .ttf font files are allowed.', None

    name = os.path.splitext(os.path.basename(source_path))[0]
    font_id = safe_id(name)
    dest = _cache_path(font_id)

    i = 2
    while os.path.exists(dest):
        try:
            if os.path.samefile(source_path, dest):
                break
        except OSError:
            pass
        font_id = safe_id(f'{name}_{i}')
        dest = _cache_path(font_id)
        i += 1

    shutil.copyfile(source_path, dest)
    set_setting('font_id', font_id)
    return True, 'Font cached.', {'id': font_id, 'name': name, 'custom': True}


def apply_selected_font(font_id: str | None = None) -> tuple[bool, str]:
    font_id = font_id if font_id is not None else get_selected_font_id()

    live = _live_paths()
    if not live:
        return False, 'Roblox installation not found.'

    _, live_font_target, live_families_dir = live

    if not font_id or font_id == 'default':
        if os.path.exists(STAGED_FONT):
            assert_writable(STAGED_FONT)
            os.remove(STAGED_FONT)

        _clear_staged_families()

        if os.path.exists(live_font_target):
            assert_writable(live_font_target)
            os.remove(live_font_target)

        set_setting('font_id', 'default')
        return True, 'Font reset to default.'

    source = _resolve_font_source(font_id)
    if not source:
        return False, 'Selected font is missing from cache.'

    if not hashes_match(source, STAGED_FONT):
        os.makedirs(os.path.dirname(STAGED_FONT), exist_ok=True)
        assert_writable(STAGED_FONT)
        shutil.copyfile(source, STAGED_FONT)

    _rewrite_family_jsons(live_families_dir)

    if not hashes_match(STAGED_FONT, live_font_target):
        os.makedirs(os.path.dirname(live_font_target), exist_ok=True)
        assert_writable(live_font_target)
        shutil.copyfile(STAGED_FONT, live_font_target)

    _sync_dir(STAGED_FAMILIES_DIR, live_families_dir)

    set_setting('font_id', font_id)
    return True, 'Font applied.'


def resync_to_live() -> tuple[bool, str]:
    return apply_selected_font(get_selected_font_id())
