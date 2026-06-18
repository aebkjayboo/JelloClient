import os

APPDATA = os.environ.get('APPDATA', '')
LOCAL_APP_DATA = os.environ.get('LOCALAPPDATA', '')

APP_DIR = os.path.join(APPDATA, 'JelloClient')
MODS_DIR = os.path.join(APP_DIR, 'Modifications')

CACHE_FILE = os.path.join(APP_DIR, 'cache.json')
SETTINGS_FILE = os.path.join(APP_DIR, 'settings.json')

CACHE_TTL = 3600

FONTS_ROOT = os.path.join(MODS_DIR, 'fonts')
FONTS_CACHE_DIR = os.path.join(FONTS_ROOT, 'cache')
FONTS_STAGED_ROOT = os.path.join(FONTS_ROOT, 'staged')
STAGED_FONT = os.path.join(FONTS_STAGED_ROOT, 'content', 'fonts', 'CustomFont.ttf')
STAGED_FAMILIES_DIR = os.path.join(FONTS_STAGED_ROOT, 'content', 'fonts', 'families')

ROBLOX_SEARCH_ROOTS = [
    os.path.join(LOCAL_APP_DATA, 'Bloxstrap', 'Versions'),
    os.path.join(LOCAL_APP_DATA, 'Roblox', 'Versions'),
]

for _dir in (APP_DIR, MODS_DIR, FONTS_CACHE_DIR, STAGED_FAMILIES_DIR, os.path.dirname(STAGED_FONT)):
    os.makedirs(_dir, exist_ok=True)
