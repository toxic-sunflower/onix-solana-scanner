# Nginx config — version-controlled

`onix-scanner.it.conf` here is the real, live nginx site config for the
production server — copy-pasted from `/etc/nginx/sites-enabled/` on
2026-07-27 (with the `/admin` location added for the Admin panel). This is
the single source of truth going forward: edit this file, push to `main`,
and `deploy.yml` applies it automatically. No more SSHing in by hand for
routine nginx changes (adding a new location, a new subdomain server block,
etc.) — just add the file/edit here.

## One-time server setup (do this once, manually)

`deploy.yml` calls `sudo -n /usr/local/bin/onix-sync-nginx.sh` on every
deploy. That script doesn't exist yet — create it once:

```bash
sudo tee /usr/local/bin/onix-sync-nginx.sh > /dev/null << 'EOF'
#!/bin/bash
set -e
cp /home/app/onix/deploy/nginx/*.conf /etc/nginx/sites-enabled/
nginx -t
systemctl reload nginx
EOF
sudo chmod +x /usr/local/bin/onix-sync-nginx.sh
```

Then let the `app` deploy user run it without a password prompt (same
pattern as the existing `onix-switch-nginx.sh` for blue/green):

```bash
sudo tee /etc/sudoers.d/onix-nginx-sync > /dev/null << 'EOF'
app ALL=(root) NOPASSWD: /usr/local/bin/onix-sync-nginx.sh
EOF
sudo visudo -c
```

`visudo -c` checks the new sudoers file for syntax errors before it takes
effect — if it reports a problem, fix `/etc/sudoers.d/onix-nginx-sync`
before moving on (a broken sudoers file can lock out `sudo` entirely).

## After that

Every future nginx change is: edit a file in `deploy/nginx/`, commit, push.
The next deploy copies it into `sites-enabled/` and reloads nginx. If
`nginx -t` fails, the sync step fails loudly in the deploy log but does
**not** fail the whole deploy (the app itself still ships) — check the
Actions log, fix the config here, push again.

Adding a new site (e.g. a future `admin.onix-scanner.it` subdomain instead
of the `/admin` path) is just a new file in this directory — the sync
script copies everything matching `*.conf`.
