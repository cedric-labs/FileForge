[CmdletBinding()]
param(
    [string]$ProjectKey = "FileForge",
    [string]$SonarHost = $(if ($env:SONAR_HOST_URL) { $env:SONAR_HOST_URL } else { "http://localhost:9000" }),
    [string]$SonarToken,
    [switch]$SkipFormat,
    [switch]$SkipSonar
)

$ErrorActionPreference = "Stop"
$ScriptRoot = $PSScriptRoot
Set-Location $ScriptRoot

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Pipeline Qualité & SonarQube - FileForge" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$SolutionPath = Join-Path $ScriptRoot "FileForge.slnx"

# Détection automatique du token SonarQube
if (-not $SonarToken) {
    if ($env:SONAR_TOKEN) {
        $SonarToken = $env:SONAR_TOKEN
    } elseif (Test-Path "$ScriptRoot/.sonar-token") {
        $SonarToken = (Get-Content "$ScriptRoot/.sonar-token" -Raw).Trim()
    } elseif (Test-Path "$ScriptRoot/../.sonar-token") {
        $SonarToken = (Get-Content "$ScriptRoot/../.sonar-token" -Raw).Trim()
    } elseif (Test-Path "D:/Dev/Anima/.sonar-token") {
        $SonarToken = (Get-Content "D:/Dev/Anima/.sonar-token" -Raw).Trim()
    }
}

# 1. Restauration des outils dotnet locaux
Write-Host "`n[1/6] Restauration des outils locaux..." -ForegroundColor Yellow
dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Échec de 'dotnet tool restore'."
    exit $LASTEXITCODE
}

# 2. Vérification du formatage du code
if (-not $SkipFormat) {
    Write-Host "`n[2/6] Vérification du formatage du code (dotnet format)..." -ForegroundColor Yellow
    dotnet format $SolutionPath --verify-no-changes
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Échec du formatage. Exécutez 'dotnet format' pour corriger les écarts."
        exit $LASTEXITCODE
    }
    Write-Host "  Formatage conforme." -ForegroundColor Green
} else {
    Write-Host "`n[2/6] Vérification du formatage ignorée." -ForegroundColor DarkGray
}

# Nettoyage des résultats de tests et d'analyses précédents
Remove-Item -Path "$ScriptRoot/TestResults" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$ScriptRoot/FileForge.Tests/TestResults" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$ScriptRoot/.sonarqube" -Recurse -Force -ErrorAction SilentlyContinue

# 3. SonarScanner Begin
$useSonar = (-not $SkipSonar) -and (-not [string]::IsNullOrWhiteSpace($SonarToken))
if ($useSonar) {
    Write-Host "`n[3/6] Initialisation SonarScanner sur $SonarHost..." -ForegroundColor Yellow
    $sonarArgs = @(
        "begin",
        "/k:$ProjectKey",
        "/n:$ProjectKey",
        "/d:sonar.host.url=$SonarHost",
        "/d:sonar.token=$SonarToken",
        "/d:sonar.qualitygate.wait=true",
        "/d:sonar.cs.opencover.reportsPaths=**/TestResults/**/coverage.opencover.xml",
        "/d:sonar.cs.vstest.reportsPaths=**/TestResults/**/*.trx",
        "/d:sonar.exclusions=**/bin/**,**/obj/**,verify.ps1",
        "/d:sonar.coverage.exclusions=verify.ps1",
        "/d:sonar.cpd.exclusions=verify.ps1",
        "/d:sonar.cs.roslyn.ignoreIssues=true"
    )
    dotnet sonarscanner @sonarArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Échec de l'initialisation SonarScanner."
        exit $LASTEXITCODE
    }
} else {
    Write-Host "`n[3/6] SonarScanner ignoré (aucun token fourni ou -SkipSonar activé)." -ForegroundColor DarkGray
}

# 4. Compilation Release avec zéro avertissement
Write-Host "`n[4/6] Compilation Release sans incrémentation (-warnaserror)..." -ForegroundColor Yellow
dotnet build $SolutionPath -warnaserror --configuration Release --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Error "Échec de la compilation ou avertissements détectés."
    exit $LASTEXITCODE
}
Write-Host "  Compilation réussie sans aucun avertissement." -ForegroundColor Green

# 5. Exécution des tests et collecte OpenCover
Write-Host "`n[5/6] Exécution des tests unitaires et de couverture OpenCover..." -ForegroundColor Yellow
dotnet test $SolutionPath --no-build --configuration Release --logger "trx" --collect:"XPlat Code Coverage;Format=opencover" --results-directory "$ScriptRoot/TestResults"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Des tests unitaires ou d'architecture ont échoué."
    exit $LASTEXITCODE
}
Write-Host "  Tous les tests sont au vert." -ForegroundColor Green

# 6. SonarScanner End & Quality Gate
if ($useSonar) {
    Write-Host "`n[6/6] Finalisation SonarScanner et attente de la Quality Gate..." -ForegroundColor Yellow
    dotnet sonarscanner end /d:sonar.token=$SonarToken
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Échec de la finalisation SonarScanner ou Quality Gate non passée."
        exit $LASTEXITCODE
    }
    Write-Host "  Analyse SonarQube terminée avec succès." -ForegroundColor Green
} else {
    Write-Host "`n[6/6] Analyse SonarQube non finalisée car ignorée." -ForegroundColor DarkGray
}

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host "  Pipeline validé avec SUCCÈS !           " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
