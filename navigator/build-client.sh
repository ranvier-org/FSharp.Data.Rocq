#!/usr/bin/env bash
# Compiles the client with Fickl, then bundles it to the file the plugin embeds.
set -euo pipefail

navigator="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
client="$navigator/Rocq.Navigator.Client"

dotnet tool restore
# CI type-checks package sources on every build, instead of reusing Fickl's package cache.
dotnet fickl "$client" --outDir "$client/generated" --noCache ${CI:+--noPackageCache}

cd "$client"
npm ci --no-audit --no-fund
npx esbuild main.js --bundle --minify --format=esm --outfile=../Nacara.Plugin.Rocq/assets/navigator.js
