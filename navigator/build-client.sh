#!/usr/bin/env bash
# Compiles the client with Fable, then bundles it to the file the plugin embeds.
set -euo pipefail

navigator="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
client="$navigator/Rocq.Navigator.Client"

dotnet tool restore
dotnet fable "$client" --outDir "$client/generated" --noCache

cd "$client"
npm install --no-audit --no-fund
npx esbuild main.js --bundle --minify --format=esm --outfile=../Nacara.Plugin.Rocq/assets/navigator.js
