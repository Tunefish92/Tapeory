#!/bin/sh
# Starts Tapeory as an unprivileged user that owns the storage folder.
#
# A host folder bind-mounted to /data is often owned by root (Docker creates missing folders as
# root) or by the host's own user (Unraid's appdata is 99:100). Started as root, this script hands
# the folder to PUID:PGID (default 1000:1000) and then drops to that user. Started as non-root
# (docker run --user, rootless setups), it runs the app as-is and the folder must already be
# writable by that user.
set -e

STORAGE_PATH="${TAPEORY_STORAGE_PATH:-/data}"

if [ "$(id -u)" = "0" ]; then
    PUID="${PUID:-1000}"
    PGID="${PGID:-1000}"

    mkdir -p "$STORAGE_PATH"

    # Only walk the whole tree when the top folder isn't ours yet (first start, or PUID changed);
    # everything the app creates afterwards already has the right owner.
    if [ "$(stat -c '%u:%g' "$STORAGE_PATH")" != "$PUID:$PGID" ]; then
        echo "Setting owner of $STORAGE_PATH to $PUID:$PGID"
        chown -R "$PUID:$PGID" "$STORAGE_PATH"
    fi

    # The target user may not exist in /etc/passwd, so give it a writable HOME (fontconfig
    # keeps its cache there).
    export HOME=/tmp
    exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups dotnet Tapeory.Api.dll "$@"
fi

exec dotnet Tapeory.Api.dll "$@"
