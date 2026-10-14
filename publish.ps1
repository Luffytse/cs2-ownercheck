# Publishes OwnerCheck to Paradox Mods via the Cities: Skylines II modding toolchain.
#   .\publish.ps1 -Action New       first upload (writes the new ModId into Properties\PublishConfiguration.xml)
#   .\publish.ps1 -Action Version   upload a new version (bump ModVersion and ChangeLog first)
#   .\publish.ps1 -Action Update    update only the listing text / thumbnail
param(
    [Parameter(Mandatory = $true)][ValidateSet('New', 'Version', 'Update')][string]$Action
)

$ErrorActionPreference = 'Stop'

# The toolchain's ModPostProcessor/ModPublisher target the .NET 6 runtime; let them run on a newer one.
$env:DOTNET_ROLL_FORWARD = 'Major'

$profile = @{ New = 'PublishNewMod'; Version = 'PublishNewVersion'; Update = 'UpdatePublishedConfiguration' }[$Action]

Push-Location $PSScriptRoot
try {
    dotnet publish -c Release "/p:PublishProfile=$profile"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }
}
finally {
    Pop-Location
}
