import hashlib
import os
import stat


def assert_writable(path: str) -> None:
    if not os.path.exists(path):
        return
    try:
        os.chmod(path, stat.S_IWRITE | stat.S_IREAD)
    except OSError:
        pass


def md5_file(path: str) -> str | None:
    if not os.path.isfile(path):
        return None
    h = hashlib.md5()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(8192), b''):
            h.update(chunk)
    return h.hexdigest()


def hashes_match(path_a: str, path_b: str) -> bool:
    hash_a = md5_file(path_a)
    return hash_a is not None and hash_a == md5_file(path_b)


def safe_id(name: str) -> str:
    clean = ''.join(c if c.isalnum() or c in ('-', '_') else '_' for c in name)
    return clean.strip('_')[:80] or 'mod'
