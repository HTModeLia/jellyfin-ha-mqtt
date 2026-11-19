# Compile en Release puis prépare dist\ : les DLL à copier dans plugins\, le zip et le manifest.json
# d'un dépôt d'extensions. -UrlZip = adresse où le zip sera servi (dépôt d'extensions uniquement).
param(
    [string]$UrlZip = "http://ADRESSE_A_REMPLACER/jellyfin-ha-mqtt.zip",
    [string]$Changelog = "Capteur « Redémarrage requis » : passe à ON quand Jellyfin attend un redémarrage (extension installée ou mise à jour)."
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet build Jellyfin.Plugin.HaMqtt\Jellyfin.Plugin.HaMqtt.csproj -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version  # seule source de la version
$dist = "dist"
$dossier = "$dist\HomeAssistantMqtt_$version"
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dossier | Out-Null
Copy-Item Jellyfin.Plugin.HaMqtt\bin\Release\net10.0\Jellyfin.Plugin.HaMqtt.dll, Jellyfin.Plugin.HaMqtt\bin\Release\net10.0\MQTTnet.dll $dossier

$zip = "$dist\jellyfin-ha-mqtt_$version.zip"
Compress-Archive "$dossier\*" $zip
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLower()

# Versions déjà publiées (manifest.json à la racine) : gardées pour l'historique des versions dans Jellyfin
$precedentes = @()
if (Test-Path manifest.json) {
    $precedentes = @((Get-Content manifest.json -Raw -Encoding UTF8 | ConvertFrom-Json)[0].versions | Where-Object { $_.version -ne $version })
}

$manifest = @(
    [ordered]@{
        guid        = "a757374b-70ab-405b-82ad-bfb8817d3271"
        name        = "Home Assistant MQTT"
        overview    = "Publie l'état de Jellyfin dans Home Assistant par MQTT"
        description = "Appareil « Jellyfin » dans Home Assistant (découverte MQTT) : compteurs par bibliothèque, lectures en cours, dernière lecture, dernier ajout, redémarrage requis, CPU et RAM."
        owner       = "aurelien"
        category    = "General"
        imageUrl    = "https://raw.githubusercontent.com/HTModeLia/jellyfin-ha-mqtt/main/image.png"
        versions    = @(
            [ordered]@{
                version   = $version
                changelog = $Changelog
                targetAbi = "12.4.0.0"
                sourceUrl = $UrlZip
                checksum  = $md5
                timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            }
        ) + $precedentes
    }
)
# -InputObject : sinon PowerShell 5.1 « déroule » le tableau à un seul élément
ConvertTo-Json -InputObject $manifest -Depth 5 | Out-File "$dist\manifest.json" -Encoding utf8
Write-Host "Prêt : $dossier (copie manuelle), $zip et $dist\manifest.json (MD5 $md5)"
