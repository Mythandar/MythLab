$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot
$packages = @{}
Get-ChildItem -Path (Join-Path $repoRoot 'src'), (Join-Path $repoRoot 'tests') -Filter project.assets.json -Recurse | ForEach-Object {
    $assets = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    $packageRoot = @($assets.packageFolders.PSObject.Properties.Name)[0]
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package' -or $packages.ContainsKey($library.Name)) { continue }
        $folder = Join-Path $packageRoot $library.Value.path
        $nuspec = Get-ChildItem -LiteralPath $folder -Filter *.nuspec | Select-Object -First 1
        [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
        $license = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
        $licenseUrl = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']")
        $packages[$library.Name] = [pscustomobject][ordered]@{
            package = $library.Name
            licenseType = if ($license) { $license.type } else { 'legacy-url' }
            license = if ($license) { $license.InnerText } else { $licenseUrl.InnerText }
            projectUrl = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='projectUrl']").InnerText
        }
        $noticeDir = Join-Path $repoRoot ('docs/licenses/' + $library.Name.Replace('/', '-'))
        New-Item -ItemType Directory -Path $noticeDir -Force | Out-Null
        if ($license -and $license.type -eq 'file') {
            Copy-Item -LiteralPath (Join-Path $folder $license.InnerText) -Destination $noticeDir
        }
        Get-ChildItem -LiteralPath $folder -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING)' } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $noticeDir -Force }
    }
}
$result = @($packages.Values | Sort-Object { $_.package })
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $repoRoot 'docs/dependency-licenses.json') -Encoding UTF8
$result | Format-Table package, licenseType, license -AutoSize
