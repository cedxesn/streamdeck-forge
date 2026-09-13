<#
.SYNOPSIS
    Produit une version de StreamDeckForge a remettre a un utilisateur final.

.DESCRIPTION
    Par defaut, la publication est autonome : le runtime .NET 8 est embarque dans
    l'executable compresse, il n'y a donc rien a installer sur le poste de destination.
    Le fichier obtenu pese environ 68 Mo.

    Avec -FrameworkDependent, l'executable ne pese que 0,3 Mo - assez pour passer par
    courriel - mais le poste de destination doit avoir le "Runtime .NET 8 Desktop"
    installe (winget install Microsoft.DotNet.DesktopRuntime.8).

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -FrameworkDependent
#>

[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = 'publish'
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot 'src\StreamDeckForge.App\StreamDeckForge.App.csproj'
$cliPath = Join-Path $PSScriptRoot 'src\StreamDeckForge.Cli\StreamDeckForge.Cli.csproj'
$output = Join-Path $PSScriptRoot $OutputDirectory

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

$common = @(
    '-c', 'Release',
    '-r', $Runtime,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-o', $output
)

if ($FrameworkDependent) {
    $common += '--self-contained'
    $common += 'false'
    Write-Host 'Publication legere : le Runtime .NET 8 Desktop doit etre present sur le poste cible.'
}
else {
    $common += '--self-contained'
    $common += 'true'

    # Compresse les assemblies embarquees : 154 Mo -> 68 Mo, au prix d'un premier
    # demarrage legerement plus lent (decompression dans le cache utilisateur).
    $common += '-p:EnableCompressionInSingleFile=true'
    Write-Host 'Publication autonome : rien a installer sur le poste cible.'
}

Write-Host "Publication de l'interface graphique..."
& dotnet publish $projectPath @common
if ($LASTEXITCODE -ne 0) { throw "Echec de la publication de l'interface." }

Write-Host 'Publication de la ligne de commande...'
& dotnet publish $cliPath @common
if ($LASTEXITCODE -ne 0) { throw 'Echec de la publication de la ligne de commande.' }

# Documentation destinee a l'utilisateur final, et un profil d'exemple pour verifier
# tout de suite l'import dans le logiciel Elgato.
Copy-Item (Join-Path $PSScriptRoot 'dist\LISEZ-MOI.txt') $output -Force

$sample = Join-Path $PSScriptRoot 'exemples\Editeur du Registre.streamDeckProfile'
if (Test-Path $sample) {
    Copy-Item $sample $output -Force
}

Write-Host ''
Write-Host "Termine. Contenu de $output :"
Get-ChildItem $output -File | Sort-Object Length -Descending |
    Select-Object -First 10 Name, @{ Name = 'Mo'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize

Write-Host 'A remettre : StreamDeckForge.exe (et sdforge.exe pour la ligne de commande).'
