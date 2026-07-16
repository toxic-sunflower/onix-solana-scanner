#!/usr/bin/env bash
set -euo pipefail

# ── Onix Server Bootstrap ──────────────────────────────────────
# Ubuntu 24.04, 4CPU/8GB, PostgreSQL 16 + TimescaleDB + Docker

export DEBIAN_FRONTEND=noninteractive

echo "=== System packages ==="
apt-get update
apt-get upgrade -y
apt-get install -y --no-install-recommends \
    ca-certificates curl gnupg lsb-release \
    ufw fail2ban htop net-tools jq

echo "=== Docker ==="
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | \
    gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" \
  > /etc/apt/sources.list.d/docker.list
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
systemctl enable docker
usermod -aG docker "$SUDO_USER"

echo "=== PostgreSQL 16 + TimescaleDB ==="
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | \
    gpg --dearmor -o /etc/apt/keyrings/postgresql.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/postgresql.gpg] \
    https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" \
    > /etc/apt/sources.list.d/pgdg.list

curl -fsSL https://packagecloud.io/timescale/timescaledb/gpgkey | \
    gpg --dearmor -o /etc/apt/keyrings/timescaledb.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/timescaledb.gpg] \
    https://packagecloud.io/timescale/timescaledb/ubuntu/$(lsb_release -cs) main" \
    > /etc/apt/sources.list.d/timescaledb.list

apt-get update
apt-get install -y postgresql-16 postgresql-16-timescaledb-timescaledb-tune

timescaledb-tune --quiet --yes

systemctl enable postgresql
systemctl start postgresql

echo "=== Firewall ==="
ufw default deny incoming
ufw default allow outgoing
ufw allow ssh
ufw allow 5432/tcp comment 'PostgreSQL from app'
ufw allow 5000/tcp comment 'API'
ufw --force enable

echo "=== fail2ban ==="
systemctl enable fail2ban
systemctl start fail2ban

echo ""
echo "=== DONE ==="
echo "Next steps (manual):"
echo "  1. sudo -u postgres psql"
echo "     CREATE USER onix WITH PASSWORD '...';"
echo "     CREATE DATABASE onix_scanner OWNER onix;"
echo "     \\c onix_scanner"
echo "     CREATE EXTENSION IF NOT EXISTS timescaledb;"
echo "     CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";"
echo "  2. Edit /etc/postgresql/16/main/pg_hba.conf:"
echo "     - Add: host onix_scanner onix <app_subnet>/24 scram-sha-256"
echo "  3. Clone repo and docker compose up"
echo "  4. Set secrets via .env file"
