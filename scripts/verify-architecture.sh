#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fail() { echo "[architecture] $1" >&2; exit 1; }

if rg -n 'System\.Windows|Microsoft\.Win32|DllImport|LibraryImport|OperatingSystem\.IsWindows' \
  "$root/Pupu.Behavior" -g '*.cs' -g '*.csproj' >/dev/null; then
  fail "Pupu.Behavior contains a UI or Windows dependency"
fi

if rg -n 'System\.Windows|Microsoft\.Win32|DllImport|LibraryImport|OperatingSystem\.IsWindows' \
  "$root/Pupu.Application" \
  "$root/Pupu.Desktop/Models" \
  "$root/Pupu.Desktop/Services/AlbumExperienceService.cs" \
  "$root/Pupu.Desktop/Services/BehaviorDecisionLogger.cs" \
  "$root/Pupu.Desktop/Services/ConversationSessionStore.cs" \
  "$root/Pupu.Desktop/Services/DesktopRoutePlanner.cs" \
  "$root/Pupu.Desktop/Services/LocalPetStore.cs" \
  "$root/Pupu.Desktop/Services/MemoryEngine.cs" \
  "$root/Pupu.Desktop/Services/ModelProtocolAdapter.cs" \
  "$root/Pupu.Desktop/Services/NaturalLanguageRuleService.cs" \
  "$root/Pupu.Desktop/Services/PhotoAlbumService.cs" \
  "$root/Pupu.Desktop/Services/StoragePaths.cs" >/dev/null; then
  fail "Pupu.Application ownership set contains a UI or Windows dependency"
fi

if rg -n 'System\.Windows(?!\.Input)|ImageSource|BitmapSource|BitmapImage|CroppedBitmap|Int32Rect|DispatcherTimer|Application\.Current|EnvironmentContextService|WindowsCredentialVault|new ModelApiService|new AssetPackService|new CodexIterationService' \
  "$root/Pupu.Desktop/ViewModels/MainViewModel.cs" \
  "$root/Pupu.Desktop/ViewModels/MainViewModel.Albums.cs" --pcre2 >/dev/null; then
  fail "MainViewModel bypasses a presentation or platform port"
fi

if rg -n '<Compile Include="\.\.\\Pupu\.Desktop' "$root/Pupu.Tests/Pupu.Tests.csproj" >/dev/null; then
  fail "Tests compile Desktop implementation files directly"
fi

rg -q '<TargetFramework>net8\.0</TargetFramework>' "$root/Pupu.Application/Pupu.Application.csproj" ||
  fail "Pupu.Application must remain plain net8.0"
rg -q 'Pupu\.Application' "$root/Pupu.Tests/Pupu.Tests.csproj" ||
  fail "Tests must consume Pupu.Application through a project reference"
rg -q 'Pupu\.Platform\.Windows' "$root/Pupu.Desktop/Pupu.Desktop.csproj" ||
  fail "WPF composition root must reference the Windows platform adapter"

echo "[architecture] PASS: Core/Application are platform-neutral; Windows and WPF adapters are isolated."
