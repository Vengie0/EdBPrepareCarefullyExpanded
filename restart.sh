#!/usr/bin/env bash

echo "Closing RimWorld..."

# Gracefully terminate the actual game process
pkill -TERM -x RimWorld 2>/dev/null || true

# Wait up to 30 seconds for it to close
for i in {1..30}; do
    if ! pgrep -x RimWorld >/dev/null; then
        break
    fi
    sleep 1
done

# Don't launch another copy if the old one is still running
if pgrep -x RimWorld >/dev/null; then
    echo "RimWorld did not close. Aborting restart."
    exit 1
fi

echo "Launching RimWorld..."

steam -applaunch 294100
