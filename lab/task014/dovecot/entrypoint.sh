#!/bin/sh
set -eu
mkdir -p /mail
chown 5000:5000 /mail
exec /usr/sbin/dovecot -F
