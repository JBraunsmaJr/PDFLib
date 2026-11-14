#!/usr/bin/env bash
#
# Builds the sample API and the load test, starts the API, runs the load test against it and stops the
# API again. Everything after "--" goes to the load test, for example:
#
#   samples/run-loadtest.sh -- --levels 1,4,8,16 --duration 30 --scenario invoice
#
# Settings for the API come from the environment, for example:
#
#   Chromium__MaxConcurrentRenders=8 Chromium__PagePoolSize=8 samples/run-loadtest.sh
#   Chromium__BinaryPath=/opt/chrome-headless-shell/chrome-headless-shell samples/run-loadtest.sh
#
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
port="${PORT:-5080}"
url="http://127.0.0.1:${port}"

if [[ "${1:-}" == "--" ]]; then shift; fi

echo "Building (Release)..."
dotnet build -c Release "$here/PDFLib.Samples.WebApi" --nologo -v quiet
dotnet build -c Release "$here/PDFLib.Samples.LoadTest" --nologo -v quiet

log="$(mktemp)"
ASPNETCORE_URLS="$url" ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}" \
    dotnet run -c Release --no-build --project "$here/PDFLib.Samples.WebApi" >"$log" 2>&1 &
api=$!

cleanup() {
    kill "$api" 2>/dev/null || true
    wait "$api" 2>/dev/null || true
    rm -f "$log"
}
trap cleanup EXIT

echo "Waiting for the API on $url..."
for _ in $(seq 1 120); do
    if curl -fsS -o /dev/null "$url/stats" 2>/dev/null; then ready=1; break; fi
    if ! kill -0 "$api" 2>/dev/null; then break; fi
    sleep 0.5
done

if [[ -z "${ready:-}" ]]; then
    echo "The API did not start. Its output:" >&2
    cat "$log" >&2
    exit 1
fi

dotnet run -c Release --no-build --project "$here/PDFLib.Samples.LoadTest" -- --url "$url" "$@"
