#!/usr/bin/env bash
# Pull origin/master and install the built DLL into the local STS2 mods folder.
#
#   ./pull-and-install.sh
#   ./pull-and-install.sh "/custom/path/Slay the Spire 2"
#
# Close Slay the Spire 2 first if the copy fails — the game can lock
# local-run-stats.dll while running.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$script_dir"

sts2_path="${1:-${STS2_PATH:-}}"
if [[ -z "$sts2_path" ]]; then
  sts2_path="${HOME}/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
fi

game_app="${sts2_path}/SlayTheSpire2.app"
macos_dir="${game_app}/Contents/MacOS"
resources_dir="${game_app}/Contents/Resources"
mod_install_dir="${macos_dir}/mods/LocalRunStats"

if [[ "$(uname -m)" == "x86_64" ]]; then
  sts2_data_dir="${resources_dir}/data_sts2_macos_x86_64"
else
  sts2_data_dir="${resources_dir}/data_sts2_macos_arm64"
fi

sts2_dll="${sts2_data_dir}/sts2.dll"
harmony_dll="${sts2_data_dir}/0Harmony.dll"

if [[ ! -d "$game_app" ]]; then
  echo "Slay the Spire 2 app not found at: $game_app" >&2
  echo "Pass the Steam install folder: $0 \"/path/to/Slay the Spire 2\"" >&2
  exit 1
fi

if [[ ! -f "$sts2_dll" || ! -f "$harmony_dll" ]]; then
  echo "Need sts2.dll and 0Harmony.dll in: $sts2_data_dir" >&2
  exit 1
fi

if pgrep -x "Slay the Spire 2" >/dev/null; then
  echo "Slay the Spire 2 is running. Quit it if the DLL copy fails, then run this again."
fi

if [[ -x /opt/homebrew/opt/dotnet@9/libexec/dotnet ]]; then
  export DOTNET_ROOT="/opt/homebrew/opt/dotnet@9/libexec"
  export PATH="${DOTNET_ROOT}:${PATH}"
elif ! command -v dotnet >/dev/null; then
  echo "dotnet not found. Install with: brew install dotnet@9" >&2
  exit 1
fi

git pull --ff-only origin master

dotnet build "${script_dir}/LocalRunStats/LocalRunStats.csproj" -c Release \
  "/p:Sts2Path=${sts2_path}" \
  "/p:Sts2DataDir=${sts2_data_dir}" \
  "/p:Sts2Dll=${sts2_dll}" \
  "/p:HarmonyDll=${harmony_dll}" \
  "/p:ModInstallDir=${mod_install_dir}"

echo "Installed the pulled Local Run Stats into ${mod_install_dir}"
