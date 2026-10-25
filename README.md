# FlClash Updater

Check for new FlClash releases and install updates on Windows or macOS.

## Windows

[Download](https://github.com/kowyo/flclash-updater/releases/latest)

![Screenshot](docs/screenshot.png)

## macOS

`fu` updates `/Applications/FlClash.app` to the latest official stable release on Apple Silicon and Intel Macs.

### Requirements

- `uv` on your `PATH`, with Python 3.11 or newer available to it.
- Write access to `/Applications` and the existing FlClash app.

### Install

Clone this repository and install the command in a directory on your `PATH`:

```sh
git clone https://github.com/kowyo/flclash-updater.git
cd flclash-updater
sudo install -d /usr/local/bin
sudo install -m 755 fu /usr/local/bin/fu
```

### Usage

Run as your normal user:

```sh
fu
```

For help, run `fu --help`.

The command selects the installer for your Mac, verifies its SHA-256 checksum against the official GitHub release, replaces the app, and launches it. Proxy connections briefly disconnect during the update. If FlClash is already up to date, the command exits without restarting it.

Successful updates remove the old app and temporary installation files. Subscriptions, settings, rules, and caches remain in the user data directory. If replacing or launching the app fails, the command attempts to restore the previous app.
