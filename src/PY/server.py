import atexit
import collections
import datetime
import os
import subprocess
import sys

from flask import Flask, Response, jsonify, request
from flask_cors import CORS

from core.cache import get_launch_stats, record_launch
from core.settings import get_setting, set_setting
from mods import fflags, fonts
from roblox.discovery import find_roblox_exe, get_roblox_version
from roblox.mutex import is_mutex_alive, start_mutex, stop_mutex
from roblox.process import get_roblox_procs, is_roblox_running, kill_all_roblox
from integrations.RPC import close, clear_presence, JelloPresence

app = Flask(__name__)
CORS(app)

# Electron doesn't surface this process's stdout, so mirror every printed
# line (ours + Flask/werkzeug's request logs) into a buffer readable over
# HTTP via /logs.
LOG_BUFFER = collections.deque(maxlen=2000)


class _LogTee:
    def __init__(self, stream):
        self._stream = stream

    def write(self, data):
        self._stream.write(data)
        for line in data.splitlines():
            if line.strip():
                ts = datetime.datetime.now().strftime('%H:%M:%S')
                LOG_BUFFER.append(f'[{ts}] {line}')

    def flush(self):
        self._stream.flush()


sys.stdout = _LogTee(sys.stdout)
sys.stderr = _LogTee(sys.stderr)


def start_discord_rpc():
    try:
        JelloPresence()
    except Exception as e:
        print(f'[RPC] failed to start: {e}')


def stop_discord_rpc():
    try:
        clear_presence()
        close()
    except Exception as e:
        print(f'[RPC] failed to stop cleanly: {e}')


atexit.register(stop_discord_rpc)

if get_setting('multi_instance', False):
    start_mutex()

if get_setting('discord_rpc', False):
    start_discord_rpc()


def find_free_port() -> int:
    import socket
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(('', 0))
        return s.getsockname()[1]


@app.route('/')
def index():
    exe = find_roblox_exe()
    procs = get_roblox_procs()
    version = get_roblox_version(exe)
    stats = get_launch_stats()
    last_run = datetime.datetime.fromtimestamp(stats['last_run']).strftime('%Y-%m-%d %H:%M:%S') if stats['last_run'] else 'never'

    lines = [
        'JelloClient — Server Status',
        '=' * 32,
        '',
        '[Roblox]',
        f'  found      : {exe is not None}',
        f'  path       : {exe or "n/a"}',
        f'  version    : {version or "n/a"}',
        f'  running    : {len(procs) > 0}',
        f'  instances  : {len(procs)}',
        '',
        '[Launches]',
        f'  count      : {stats["count"]}',
        f'  last_run   : {last_run}',
        '',
        '[Settings]',
        f'  multi_instance : {get_setting("multi_instance", False)}',
        f'  font_id        : {get_setting("font_id", "default")}',
        f'  discord_rpc    : {get_setting("discord_rpc", False)}',
        '',
        '[Mutex]',
        f'  active : {is_mutex_alive()}',
    ]
    return Response('\n'.join(lines), mimetype='text/plain')


@app.route('/ping')
def ping():
    return jsonify({'status': 'ok'})


@app.route('/logs')
def logs():
    return Response('\n'.join(LOG_BUFFER), mimetype='text/plain')


@app.route('/launch-roblox')
def launch_roblox():
    if not get_setting('multi_instance', False) and is_roblox_running():
        return jsonify({'status': 'already_running', 'message': 'Roblox is already running.'})

    exe = find_roblox_exe()
    if not exe:
        return jsonify({'status': 'error', 'message': 'Roblox installation not found.'}), 404

    # Re-sync mods onto the live install before every launch, since Roblox
    # updates wipe and replace the version-* folder.
    ok, msg = fonts.resync_to_live()
    if not ok:
        return jsonify({'status': 'error', 'message': msg}), 500

    ok, msg = fflags.resync_to_live()
    if not ok:
        return jsonify({'status': 'error', 'message': msg}), 500

    try:
        subprocess.Popen([exe], cwd=os.path.dirname(exe))
        stats = record_launch()
        return jsonify({'status': 'ok', 'message': 'Roblox launched.', 'exe': exe, 'stats': stats})
    except Exception as e:
        return jsonify({'status': 'error', 'message': str(e)}), 500


@app.route('/launch-stats')
def launch_stats():
    return jsonify({'status': 'ok', **get_launch_stats()})


@app.route('/roblox-status')
def roblox_status():
    exe = find_roblox_exe()
    return jsonify({
        'status': 'ok',
        'found': exe is not None,
        'running': is_roblox_running(),
        'path': exe,
    })


@app.route('/documentation')
def documentation():
    return jsonify({'status': 'ok', 'url': 'https://github.com/aebkjayboo/JelloClient/blob/main/README.md'})


@app.route('/announcement')
def announcement():
    return jsonify({'status': 'ok', 'message': None})


@app.route('/settings/test')
def settings_test():
    exe = find_roblox_exe()
    return jsonify({
        'status': 'ok',
        'message': 'Server is alive!',
        'roblox_found': exe is not None,
        'roblox_path': exe,
    })


@app.route('/settings/get/<key>')
def settings_get(key: str):
    return jsonify({'status': 'ok', 'value': get_setting(key)})


@app.route('/settings/fonts')
def settings_fonts():
    return jsonify({
        'status': 'ok',
        'fonts': fonts.list_fonts(),
        'selected': fonts.get_selected_font_id(),
    })


@app.route('/settings/fonts/add', methods=['POST'])
def settings_fonts_add():
    data = request.get_json(silent=True) or {}
    ok, msg, font = fonts.add_font(data.get('path', ''))
    if not ok:
        return jsonify({'status': 'error', 'message': msg}), 400
    return jsonify({'status': 'ok', 'message': msg, 'font': font})


@app.route('/settings/fonts/apply', methods=['POST'])
def settings_fonts_apply():
    data = request.get_json(silent=True) or {}
    font_id = data.get('font_id', 'default')
    ok, msg = fonts.apply_selected_font(font_id)
    return jsonify({'status': 'ok' if ok else 'error', 'message': msg, 'selected': font_id}), (200 if ok else 400)


@app.route('/settings/multi-instance/enable', methods=['POST'])
def multi_instance_enable():
    kill_all_roblox()
    set_setting('multi_instance', True)
    start_mutex()
    return jsonify({'status': 'ok', 'enabled': True})


@app.route('/settings/multi-instance/disable', methods=['POST'])
def multi_instance_disable():
    set_setting('multi_instance', False)
    stop_mutex()
    return jsonify({'status': 'ok', 'enabled': False})


@app.route('/settings/discord-rpc/enable', methods=['POST'])
def discord_rpc_enable():
    set_setting('discord_rpc', True)
    start_discord_rpc()
    return jsonify({'status': 'ok', 'enabled': True})


@app.route('/settings/discord-rpc/disable', methods=['POST'])
def discord_rpc_disable():
    set_setting('discord_rpc', False)
    stop_discord_rpc()
    return jsonify({'status': 'ok', 'enabled': False})


@app.route('/settings/fflags')
def settings_fflags():
    return jsonify({'status': 'ok', 'fflags': fflags.list_fflags()})


@app.route('/settings/fflags/add', methods=['POST'])
def settings_fflags_add():
    data = request.get_json(silent=True) or {}
    ok, msg = fflags.set_fflag(data.get('name', ''), data.get('value', ''))
    return jsonify({'status': 'ok' if ok else 'error', 'message': msg}), (200 if ok else 400)


@app.route('/settings/fflags/remove', methods=['POST'])
def settings_fflags_remove():
    data = request.get_json(silent=True) or {}
    ok, msg = fflags.remove_fflag(data.get('name', ''))
    return jsonify({'status': 'ok' if ok else 'error', 'message': msg}), (200 if ok else 400)


@app.route('/settings/fflags/import', methods=['POST'])
def settings_fflags_import():
    data = request.get_json(silent=True) or {}
    ok, msg, count = fflags.import_json(data.get('json', ''))
    return jsonify({'status': 'ok' if ok else 'error', 'message': msg, 'count': count}), (200 if ok else 400)


if __name__ == '__main__':
    port = find_free_port()
    sys.stdout.write(f'PORT:{port}\n')
    sys.stdout.flush()
    app.run(host='127.0.0.1', port=port, debug=False)
