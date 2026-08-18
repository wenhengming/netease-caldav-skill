#!/usr/bin/env sh
set -eu

skill_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet publish "$skill_dir/src/CalDavCli/CalDavCli.csproj" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  --source https://api.nuget.org/v3/index.json \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  --output "$skill_dir/bin"
chmod 0755 "$skill_dir/bin/caldav-cli"
