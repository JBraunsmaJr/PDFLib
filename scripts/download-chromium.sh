#!/bin/bash
set -e
SCRIPT_DIR="$(realpath $(dirname "$0"))"

# Target directory
TARGET_DIR="${SCRIPT_DIR}/../PDFLib.Chromium/runtimes/linux-x64/native"
mkdir -p "$TARGET_DIR"

if [ -f "$TARGET_DIR/chrome-headless-shell" ] && [ "$FORCE_DOWNLOAD" != "true" ]; then
    echo "Chromium headless shell already exists in $TARGET_DIR. Skipping download."
    exit 0
fi

echo "--------------------------------------------------------------------------------"
echo "[PDFLib] Fetching latest Chrome for Testing (Stable) metadata..."
JSON_URL="https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json"
DOWNLOAD_URL=$(curl -s "$JSON_URL" | jq -r '.channels.Stable.downloads["chrome-headless-shell"][] | select(.platform=="linux64") | .url')

if [ -z "$DOWNLOAD_URL" ] || [ "$DOWNLOAD_URL" == "null" ]; then
    echo "[PDFLib] Error: Could not find download URL for linux64 chrome-headless-shell"
    exit 1
fi

echo "[PDFLib] Downloading latest Chromium headless shell from: $DOWNLOAD_URL"
curl -SL "$DOWNLOAD_URL" -o /tmp/chromium-shell.zip

echo "[PDFLib] Extracting..."
unzip -o /tmp/chromium-shell.zip -d /tmp/chromium-shell-extracted

# The zip usually contains a directory like chrome-headless-shell-linux64/
# We want the contents of that directory to go into our target dir.
EXTRACTED_DIR=$(find /tmp/chromium-shell-extracted -maxdepth 1 -type d -name "chrome-headless-shell-linux64")

if [ -d "$EXTRACTED_DIR" ]; then
    cp -r "$EXTRACTED_DIR"/* "$TARGET_DIR/"
    echo "[PDFLib] Successfully updated $TARGET_DIR"
else
    echo "[PDFLib] Error: Could not find extracted directory chrome-headless-shell-linux64"
    exit 1
fi

# Cleanup
rm /tmp/chromium-shell.zip
rm -rf /tmp/chromium-shell-extracted

echo "[PDFLib] Done."
echo "--------------------------------------------------------------------------------"
