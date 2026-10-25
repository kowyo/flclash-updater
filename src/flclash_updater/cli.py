import hashlib
import json
import os
import platform
import plistlib
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from pathlib import Path


APP = Path('/Applications/FlClash.app')
RELEASE_API = 'https://api.github.com/repos/chen08209/FlClash/releases/latest'


def run(*args, capture=False):
    return subprocess.run(args, check=True, text=True, capture_output=capture)


def version(app):
    with (app / 'Contents/Info.plist').open('rb') as source:
        return plistlib.load(source)['CFBundleShortVersionString']


def version_numbers(value):
    return tuple(int(part) for part in value.removeprefix('v').split('.'))


def processes():
    result = []
    for line in run('/bin/ps', '-axo', 'pid=,comm=', capture=True).stdout.splitlines():
        pid, command = line.strip().split(None, 1)
        if command in (str(APP / 'Contents/MacOS/FlClash'), str(APP / 'Contents/MacOS/FlClashCore')):
            result.append((int(pid), Path(command).name))
    return result


def stop_app():
    for pid, name in processes():
        if name == 'FlClash':
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
    for attempt in range(40):
        if not processes():
            return
        time.sleep(0.25)
    raise RuntimeError('FlClash is still running; the app has not been replaced. Quit FlClash and try again.')


def update():
    if sys.argv[1:]:
        if sys.argv[1:] in (['--help'], ['-h']):
            print('Usage: fu')
            return
        raise RuntimeError('Unsupported argument. Run fu --help for usage.')
    if platform.system() != 'Darwin':
        raise RuntimeError('This command supports macOS only.')
    architecture = {'arm64': 'arm64', 'x86_64': 'amd64'}.get(platform.machine())
    if architecture is None:
        raise RuntimeError(f'Unsupported architecture: {platform.machine()}')
    print('Checking for the latest official FlClash stable release…', flush=True)
    release = json.loads(run('/usr/bin/curl', '-fsSL', '--retry', '2', '--max-time', '60', RELEASE_API, capture=True).stdout)
    latest = release['tag_name'].removeprefix('v')
    installed = version(APP) if APP.exists() else None
    if installed and version_numbers(installed) >= version_numbers(latest):
        print(f'FlClash {installed} is up to date. No update needed.')
        return
    asset_name = f'FlClash-{latest}-macos-{architecture}.dmg'
    asset = next((item for item in release['assets'] if item['name'] == asset_name), None)
    if not asset or not asset.get('digest', '').startswith('sha256:'):
        raise RuntimeError('The official release is missing the matching installer or SHA-256 checksum. Update stopped.')
    print(f'{installed or "Not installed"} → {latest}. Downloading {asset_name}…', flush=True)
    with tempfile.TemporaryDirectory(prefix='flclash-download-') as download_directory:
        download = Path(download_directory)
        dmg = download / asset_name
        run('/usr/bin/curl', '-fL', '--retry', '2', '--connect-timeout', '30', '--max-time', '600', '-o', str(dmg), asset['browser_download_url'])
        with dmg.open('rb') as source:
            digest = hashlib.file_digest(source, 'sha256').hexdigest()
        if digest != asset['digest'].split(':', 1)[1]:
            raise RuntimeError('Installer SHA-256 verification failed. Update stopped.')
        mount = download / 'mount'
        run('/usr/bin/hdiutil', 'attach', '-readonly', '-nobrowse', '-mountpoint', str(mount), str(dmg), capture=True)
        try:
            source = mount / 'FlClash.app'
            if version(source) != latest:
                raise RuntimeError('The installer version does not match the release version. Update stopped.')
            with tempfile.TemporaryDirectory(prefix='.flclash-update-', dir=APP.parent) as staging_directory:
                staging = Path(staging_directory)
                replacement = staging / 'FlClash.app'
                backup = staging / 'previous.app'
                run('/usr/bin/ditto', str(source), str(replacement))
                print('Verification passed. Replacing and restarting FlClash; proxy connections will briefly disconnect…', flush=True)
                stop_app()
                if APP.exists():
                    APP.rename(backup)
                try:
                    replacement.rename(APP)
                    run('/usr/bin/open', '-a', str(APP))
                except Exception:
                    if APP.exists():
                        shutil.rmtree(APP)
                    if backup.exists():
                        backup.rename(APP)
                        run('/usr/bin/open', '-a', str(APP))
                    raise
        finally:
            subprocess.run(['/usr/bin/hdiutil', 'detach', str(mount)], stdout=subprocess.DEVNULL)
    print(f'Updated to FlClash {version(APP)} and launched the app. Your configuration has been preserved.')


def main():
    try:
        update()
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f'Update failed: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
