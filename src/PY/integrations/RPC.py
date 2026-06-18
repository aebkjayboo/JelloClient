import time

from pypresence import Presence

BOT_ID = '1516857351477526642'
SERVER_INVITE = 'https://discord.gg/tpnP9NNBap'

_rpc = None


def connect():
    global _rpc
    if _rpc is None:
        _rpc = Presence(BOT_ID)
        _rpc.connect()
    return _rpc


def update_presence(
    state: str = None,
    details: str = None,
    start: int = None,
    end: int = None,
    large_image: str = None,
    large_text: str = None,
    small_image: str = None,
    small_text: str = None,
    buttons: list = None,
):
    rpc = connect()
    fields = {
        'state': state,
        'details': details,
        'start': start,
        'end': end,
        'large_image': large_image,
        'large_text': large_text,
        'small_image': small_image,
        'small_text': small_text,
        'buttons': buttons,
    }
    return rpc.update(**{k: v for k, v in fields.items() if v not in (None, '')})


def clear_presence():
    if _rpc is not None:
        _rpc.clear()


def close():
    global _rpc
    if _rpc is not None:
        _rpc.close()
        _rpc = None


def JelloPresence():
    return update_presence(
        state='JelloClient',
        details='Download',
        start=int(time.time()),
        buttons=[{'label': 'Join Server', 'url': SERVER_INVITE}],
    )
