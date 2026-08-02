$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Assert-NoMatch([string[]]$Paths, [string]$Pattern, [string]$Message) {
    $matches = Select-String -Path $Paths -Pattern $Pattern -ErrorAction SilentlyContinue
    if ($matches) {
        $matches | ForEach-Object { Write-Error "$($_.Path):$($_.LineNumber): $($_.Line)" }
        throw $Message
    }
}

$behaviorFiles = Get-ChildItem (Join-Path $root "Pupu.Behavior") -Recurse -File |
    Where-Object { $_.Extension -in ".cs", ".csproj" } |
    ForEach-Object FullName
Assert-NoMatch $behaviorFiles 'System\.Windows|Microsoft\.Win32|DllImport|LibraryImport|OperatingSystem\.IsWindows' `
    "Pupu.Behavior contains a UI or Windows dependency."

$applicationFiles = @()
$applicationFiles += Get-ChildItem (Join-Path $root "Pupu.Application") -Recurse -File |
    Where-Object { $_.Extension -in ".cs", ".csproj" } |
    ForEach-Object FullName
$applicationFiles += Get-ChildItem (Join-Path $root "Pupu.Desktop\Models") -File |
    ForEach-Object FullName
$applicationFiles += @(
    "AlbumExperienceService.cs", "BehaviorDecisionLogger.cs", "ConversationSessionStore.cs",
    "DesktopRoutePlanner.cs", "LocalPetStore.cs", "MemoryEngine.cs", "ModelProtocolAdapter.cs",
    "NaturalLanguageRuleService.cs", "PhotoAlbumService.cs", "StoragePaths.cs"
) | ForEach-Object { Join-Path $root "Pupu.Desktop\Services\$_" }
Assert-NoMatch $applicationFiles 'System\.Windows|Microsoft\.Win32|DllImport|LibraryImport|OperatingSystem\.IsWindows|global::Pupu\.Desktop\.App|\bApp\.' `
    "Pupu.Application ownership set contains a UI or Windows dependency."

$viewModels = Get-ChildItem (Join-Path $root "Pupu.Desktop\ViewModels") -File -Filter "*.cs" |
    ForEach-Object FullName
Assert-NoMatch $viewModels 'System\.Windows\.(?!Input)|MessageBox|ImageSource|BitmapSource|BitmapImage|CroppedBitmap|Int32Rect|DispatcherTimer|Application\.Current|global::Pupu\.Desktop\.App|\bApp\.|EnvironmentContextService|WindowsCredentialVault|new ModelApiService|new AssetPackService|new CodexIterationService' `
    "MainViewModel bypasses a presentation or platform port."

$testsProject = Get-Content (Join-Path $root "Pupu.Tests\Pupu.Tests.csproj") -Raw
if ($testsProject -match '<Compile Include="\.\.\\Pupu\.Desktop') {
    throw "Tests compile Desktop implementation files directly."
}
if ($testsProject -notmatch 'Pupu\.Application') {
    throw "Tests must consume Pupu.Application through a project reference."
}

Write-Host "[architecture] PASS: Core/Application are platform-neutral; Windows and WPF adapters are isolated." -ForegroundColor Green
