#!/bin/bash
# Installed on the server at /usr/local/bin/onix-switch-nginx.sh, owned by
# root, chmod 750 (owner rwx, group rx, no write for the deploy user). The
# "app" deploy user is granted NOPASSWD sudo for exactly this one command
# with NO arguments — see deploy/onix-deploy.sudoers — so the port it
# switches to is read from a file the deploy script writes beforehand,
# rather than passed as a command-line argument (modern sudo disallows
# wildcards in sudoers argument matching, so a fixed no-args command is the
# cleanest way to keep this narrowly scoped).
set -e

PORT_FILE=/home/app/onix/.new_port
if [ ! -f "$PORT_FILE" ]; then
  echo "Missing $PORT_FILE" >&2
  exit 1
fi
PORT=$(cat "$PORT_FILE")

CONF=$(grep -rl "onix_backend" /etc/nginx/sites-enabled/ /etc/nginx/conf.d/ 2>/dev/null | head -1)
if [ -z "$CONF" ]; then
  echo "Could not find an nginx config referencing 'onix_backend'" >&2
  exit 1
fi

sed -i -E "s/server 127\.0\.0\.1:[0-9]+;/server 127.0.0.1:${PORT};/" "$CONF"
nginx -t
nginx -s reload
