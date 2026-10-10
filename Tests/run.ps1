# Compiles Guide.cs + SimpleJson.cs with stubs into a .NET console app and runs the self-check.
# Needs the .NET runtime (dotnet) and a Roslyn csc.dll: the .NET SDK has one, or pass -Csc
# (e.g. from the Microsoft.Net.Compilers.Toolset NuGet package: tasks/netcore/bincore/csc.dll).
param([string]$Csc)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dotnetDir = Split-Path (Get-Command dotnet).Source
if (-not $Csc) { $Csc = Get-ChildItem "$dotnetDir\sdk\*\Roslyn\bincore\csc.dll" | Select-Object -Last 1 -ExpandProperty FullName }
$rt = Get-ChildItem "$dotnetDir\shared\Microsoft.NETCore.App\*" -Directory | Select-Object -Last 1
$out = Join-Path ([IO.Path]::GetTempPath()) 'nguguide-check'
New-Item -ItemType Directory -Force $out | Out-Null

$guideDir = Join-Path $root 'NGUInjector\Guide'
$resources = Get-ChildItem $guideDir -Recurse -Filter *.json | ForEach-Object {
    $name = 'NGUInjector.Guide.' + $_.FullName.Substring($guideDir.Length + 1).Replace('\', '.')
    "-resource:$($_.FullName),$name"
}
$refs = Get-ChildItem "$($rt.FullName)\*.dll" | Where-Object { ($_.Name -like 'System*' -and $_.Name -notlike '*.Native.dll') -or $_.Name -eq 'netstandard.dll' -or $_.Name -eq 'mscorlib.dll' } | ForEach-Object { "-r:$($_.FullName)" }

dotnet $Csc -nologo -nowarn:CS8632,CS0649 -out:"$out\GuideCheck.dll" @refs @resources `
    "$root\NGUInjector\Guide.cs" "$root\NGUInjector\SimpleJson.cs" "$PSScriptRoot\GuideSelfCheck.cs"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
@{ runtimeOptions = @{ tfm = 'net8.0'; rollForward = 'Major'; framework = @{ name = 'Microsoft.NETCore.App'; version = $rt.Name } } } |
    ConvertTo-Json -Depth 5 | Set-Content "$out\GuideCheck.runtimeconfig.json"
dotnet "$out\GuideCheck.dll"
exit $LASTEXITCODE
