#!/usr/bin/env bash
# Nightly backup of the daily puzzle database, run by cron on the server:
#   30 5 * * * $HOME/ricochet/backup-db.sh >> $HOME/ricochet/backups/backup.log 2>&1
# Dumps the database, checks the dump can be read back, uploads it to S3 and keeps the last 7 on the server.
# Needs ~/ricochet/backup.env (created on the server, not in the repo) with BACKUP_BUCKET=<bucket name>.
# The upload uses the server's IAM role (no keys stored anywhere) through the official AWS CLI image; the
# bucket's lifecycle rule deletes copies older than 30 days.
set -euo pipefail
cd "$(dirname "$0")"
source ./backup.env
mkdir -p backups

name="ricochet-$(date -u +%Y-%m-%dT%H%MZ).dump"
docker compose exec -T db pg_dump -U ricochet -d ricochet -Fc > "backups/$name"
# A truncated or corrupt dump fails here, before it's uploaded.
docker compose exec -T db pg_restore --list < "backups/$name" > /dev/null

docker run --rm --network host -v "$PWD/backups:/backups:ro" amazon/aws-cli:2.17.0 \
  s3 cp "/backups/$name" "s3://$BACKUP_BUCKET/daily/$name" --only-show-errors

ls -1t backups/*.dump | tail -n +8 | xargs -r rm --
echo "$(date -u +%FT%TZ) backed up $name ($(du -h "backups/$name" | cut -f1))"
