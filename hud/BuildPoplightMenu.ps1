param(
    [string]$Cs2 = "",
    [string]$AddonName = "poplight_menu",
    [switch]$InstallForLocalTest
)

$ErrorActionPreference = "Stop"
$resultCode = 0

try {
    if ([string]::IsNullOrWhiteSpace($Cs2)) {
        $Cs2 = Read-Host "Enter the path to Counter-Strike: Global Offensive"
    }

    $Cs2 = $Cs2.Trim().Trim('"')
    $addon = $AddonName

    $compiler = Join-Path $Cs2 "game\bin\win64\resourcecompiler.exe"

    if (-not (Test-Path -LiteralPath $compiler)) {
        throw "ResourceCompiler not found: $compiler"
    }

    $contentAddon = Join-Path $Cs2 "content\csgo_addons\$addon"
    $gameAddon = Join-Path $Cs2 "game\csgo_addons\$addon"

    $resources = @(
        @{
            Folder = "styles"
            Name = "poplight_menu"
            SourceExt = "css"
            CompiledExt = "vcss_c"
        },
        @{
            Folder = "styles"
            Name = "poplight_submenu_wear"
            SourceExt = "css"
            CompiledExt = "vcss_c"
        },
        @{
            Folder = "layout"
            Name = "poplight_menu"
            SourceExt = "xml"
            CompiledExt = "vxml_c"
        },
        @{
            Folder = "layout"
            Name = "poplight_submenu_wear"
            SourceExt = "xml"
            CompiledExt = "vxml_c"
        }
    )

    # Najprv skontrolujeme vsetky zdrojove subory.
    foreach ($resource in $resources) {
        $relativeSource =
            "$($resource.Folder)\$($resource.Name).$($resource.SourceExt)"

        $source = Join-Path $PSScriptRoot $relativeSource

        if (-not (Test-Path -LiteralPath $source)) {
            throw "Chyba zdrojovy subor: $source"
        }
    }

    # Skopirujeme zdroje oboch menu do jedneho addonu.
    foreach ($resource in $resources) {
        $relativeSource =
            "$($resource.Folder)\$($resource.Name).$($resource.SourceExt)"

        $source = Join-Path $PSScriptRoot $relativeSource

        $destinationDirectory = Join-Path $contentAddon `
            "panorama\$($resource.Folder)\custom_game"

        New-Item -ItemType Directory `
            -Path $destinationDirectory -Force | Out-Null

        Copy-Item -LiteralPath $source `
            -Destination $destinationDirectory -Force
    }

    # Najprv styly, potom layouty, ktore ich pouzivaju.
    foreach ($resource in $resources) {
        $inputPath = Join-Path $contentAddon `
            "panorama\$($resource.Folder)\custom_game\$($resource.Name).$($resource.SourceExt)"

        $outputPath = Join-Path $gameAddon `
            "panorama\$($resource.Folder)\custom_game\$($resource.Name).$($resource.CompiledExt)"

        Write-Host ""
        Write-Host "Kompilujem $($resource.Name).$($resource.SourceExt)"

        & $compiler -i $inputPath -r

        if ($LASTEXITCODE -ne 0) {
            throw "Kompilacia zlyhala: $inputPath (kod $LASTEXITCODE)"
        }

        if (-not (Test-Path -LiteralPath $outputPath)) {
            throw "Kompilator nevytvoril: $outputPath"
        }

        $output = Get-Item -LiteralPath $outputPath

        if ($output.Length -eq 0) {
            throw "Vystupny subor je prazdny: $outputPath"
        }

        Write-Host "OK: $outputPath ($($output.Length) bytes)" `
            -ForegroundColor Green
    }

    Write-Host ""
    if ($InstallForLocalTest) {
        foreach ($resource in $resources) {
            if ($resource.Name -ne "poplight_submenu_wear") { continue }
            $relative = "panorama\$($resource.Folder)\custom_game\$($resource.Name).$($resource.CompiledExt)"
            $compiled = Join-Path $gameAddon $relative
            $localTarget = Join-Path (Join-Path $Cs2 "game\csgo") $relative
            New-Item -ItemType Directory -Path (Split-Path $localTarget) -Force | Out-Null
            Copy-Item -LiteralPath $compiled -Destination $localTarget -Force
            Write-Host "Local test: $localTarget" -ForegroundColor Green
        }
        Write-Host "Restart CS2 before testing. Public Workshop was not updated." -ForegroundColor Yellow
    }

    Write-Host "Obe menu su skompilovane." -ForegroundColor Green
    Write-Host "Addon pre Workshop:"
    Write-Host $gameAddon
}
catch {
    $resultCode = 1
    Write-Host ""
    Write-Host $_.Exception.Message -ForegroundColor Red
}

Write-Host ""
Read-Host "Stlac Enter pre zatvorenie"
exit $resultCode