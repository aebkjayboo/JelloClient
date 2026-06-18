import psutil

ROBLOX_PROCESS = 'RobloxPlayerBeta.exe'


def get_roblox_procs() -> list:
    return [p for p in psutil.process_iter(['name']) if p.info['name'] == ROBLOX_PROCESS]


def is_roblox_running() -> bool:
    return len(get_roblox_procs()) > 0


def kill_all_roblox() -> int:
    killed = 0
    for proc in get_roblox_procs():
        try:
            proc.kill()
            killed += 1
        except psutil.NoSuchProcess:
            pass
    return killed
