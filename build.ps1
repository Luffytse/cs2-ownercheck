param(
    # Cities: Skylines II install folder (the one containing Cities2_Data)
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II",
    # csc.exe from the Microsoft.Net.Compilers.Toolset NuGet package (tasks\net472\csc.exe)
    [Parameter(Mandatory = $true)][string]$Csc,
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$managed = Join-Path $GameDir "Cities2_Data\Managed"
$root = $PSScriptRoot
$out = Join-Path $root "bin"
New-Item -ItemType Directory -Force $out | Out-Null

$names = 'mscorlib','netstandard','System','System.Core','Game','Colossal.Core','Colossal.Logging',
         'Colossal.IO.AssetDatabase','Colossal.Mathematics','Unity.Entities','Unity.Collections',
         'Unity.Mathematics','Unity.Burst','UnityEngine.CoreModule','UnityEngine'
$refs = $names | Where-Object { Test-Path (Join-Path $managed "$_.dll") } | ForEach-Object { "/r:" + (Join-Path $managed "$_.dll") }

$dll = Join-Path $out "OwnerCheck.dll"
$src = Join-Path $root "src\OwnerCheck.cs"
& $Csc /nologo /noconfig /nostdlib+ /langversion:9 /target:library /optimize+ /unsafe /nowarn:1701,1702 "/out:$dll" $refs $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $(Join-Path $out 'OwnerCheck.dll')"

if ($Install) {
    $dst = Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\OwnerCheck"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item (Join-Path $out "OwnerCheck.dll") $dst -Force
    Write-Host "Installed to $dst"
}
