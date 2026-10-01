param(
    [string]$AppDirectory = "",
    [switch]$RequireRelocated
)
# Fails loudly when a build cannot serve a non-English language.
#
# That is what happens if Resources.Designer.cs stops using LanguageFolderResourceManager
# (Visual Studio regenerates it when a .resx is edited) or if the satellites are not where the
# resolver expects them. Without this check the app just quietly shows English, which is easy
# to ship by accident.
#
# Method: for every culture under Language\, pick a key THAT CULTURE ACTUALLY HAS, then
# compare what the app's own ResourceManager returns against what that satellite contains.
# Picking a fixed key does not work here - most translations lag behind the neutral .resx, so
# a key added recently exists in only a couple of satellites.
#
# Strictly ASCII (PowerShell reads a BOM-less .ps1 with the OEM code page).
# The assembly is x86, so this re-launches itself in 32-bit PowerShell.

if ([Environment]::Is64BitProcess) {
    $ps32 = Join-Path $env:WINDIR "SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
    if (-not (Test-Path $ps32)) { Write-Host "[X] need 32-bit PowerShell at $ps32"; exit 1 }
    $forward = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath,
                 "-AppDirectory", $AppDirectory)
    if ($RequireRelocated) { $forward += "-RequireRelocated" }
    & $ps32 @forward
    exit $LASTEXITCODE
}

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AppDirectory)) {
    $AppDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) "Build\Release"
}
$AppDirectory = (Resolve-Path $AppDirectory).Path
$languageDir = Join-Path $AppDirectory "Language"
$exe = Join-Path $AppDirectory "EarTrumpet.exe"
$baseName = "EarTrumpet.Properties.Resources"

$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $exe)) { Write-Host ("[X] not found: " + $exe); exit 1 }
if (-not (Test-Path $languageDir)) { Write-Host ("[X] no Language folder in " + $AppDirectory); exit 1 }

# Control: with the old per-culture folders still present the runtime's own probing could
# satisfy the lookup and the test would prove nothing about the relocation.
$strays = @(Get-ChildItem $AppDirectory -Directory | Where-Object {
    $_.Name -ne "Language" -and $_.Name -match '^[a-z]{2,3}(-[A-Za-z]{2,4})*$' -and
    (Test-Path (Join-Path $_.FullName "EarTrumpet.resources.dll")) })
if ($strays.Count -gt 0) {
    Write-Host ("[X] conventional culture folders still present, test would be meaningless: " + (($strays | ForEach-Object { $_.Name }) -join ", "))
    $failures.Add("layout")
}
elseif ($RequireRelocated) {
    Write-Host "  layout ok: no conventional culture folders at the top level"
}

# Read a satellite's strings into a hashtable. Enumerating through the enumerator (rather
# than foreach over the reader) keeps PowerShell from converting things behind our back.
function Read-Satellite([string]$path) {
    $map = @{}
    $asm = [System.Reflection.Assembly]::LoadFrom($path)
    # Take whatever the satellite actually holds: the resource name follows the .resx file
    # name ("bs-latn-ba"), which is not always the canonical culture name ("bs-Latn-BA").
    $names = @($asm.GetManifestResourceNames() | Where-Object { $_.EndsWith(".resources") })
    if ($names.Count -eq 0) { return $map }
    $stream = $asm.GetManifestResourceStream($names[0])
    if ($stream -eq $null) { return $map }
    $reader = New-Object System.Resources.ResourceReader($stream)
    $en = $reader.GetEnumerator()
    while ($en.MoveNext()) {
        $entry = $en.Entry
        $value = $entry.Value
        if ($value -is [string] -and -not [string]::IsNullOrEmpty($value)) {
            $map[[string]$entry.Key] = [string]$value
        }
    }
    $reader.Close()
    return $map
}

$resolve = [System.ResolveEventHandler] {
    param($s, $e)
    $simple = $e.Name.Split(',')[0]
    $p = [System.IO.Path]::Combine($script:appDir, $simple + ".dll")
    if ([System.IO.File]::Exists($p)) { return [System.Reflection.Assembly]::LoadFrom($p) }
    return $null
}
$script:appDir = $AppDirectory
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolve)

$appAsm = [System.Reflection.Assembly]::LoadFrom($exe)
$appResType = $appAsm.GetType("EarTrumpet.Properties.Resources")
$appManager = $appResType.GetProperty("ResourceManager").GetValue($null)
Write-Host ("  resource manager : " + $appManager.GetType().FullName)
Write-Host ("  LanguageRoot     : " + $appManager.LanguageRoot)

# The neutral (English) table, to report how many cultures really differ.
$neutral = @{}
$neutralStream = $appAsm.GetManifestResourceStream($baseName + ".resources")
if ($neutralStream -ne $null) {
    $nr = New-Object System.Resources.ResourceReader($neutralStream)
    $ne = $nr.GetEnumerator()
    while ($ne.MoveNext()) {
        $entry = $ne.Entry
        if ($entry.Value -is [string]) { $neutral[[string]$entry.Key] = [string]$entry.Value }
    }
    $nr.Close()
}

Write-Host ""
Write-Host "  culture      key used                     app lookup                satellite                 result"

$cultures = @(Get-ChildItem $languageDir -Directory | Sort-Object Name)
$ok = 0
$translated = 0

foreach ($c in $cultures) {
    $satPath = Join-Path $c.FullName "EarTrumpet.resources.dll"
    if (-not (Test-Path $satPath)) { $failures.Add($c.Name + "(no satellite)"); continue }

    $map = Read-Satellite $satPath
    if ($map.Count -eq 0) { $failures.Add($c.Name + "(empty satellite)"); continue }

    $key = @($map.Keys | Sort-Object)[0]
    $expected = $map[$key]
    $ci = [Globalization.CultureInfo]::GetCultureInfo($c.Name)
    $actual = [string]$appManager.GetString($key, $ci)

    $pass = ($actual -eq $expected)
    if ($pass) { $ok++ } else { $failures.Add($c.Name) }
    if ($neutral.ContainsKey($key) -and $neutral[$key] -ne $expected) { $translated++ }

    $verdict = if ($pass) { "ok" } else { "FAIL" }
    Write-Host ("  {0,-12} {1,-28} {2,-25} {3,-25} {4}" -f $c.Name, $key, $actual, $expected, $verdict)
}

# Also exercise the generated property (the path the UI actually takes) on a key that this
# build's zh-CN satellite really has.
$zhKey = "DeviceSettingsPageText"
$zhPath = Join-Path (Join-Path $languageDir "zh-CN") "EarTrumpet.resources.dll"
if (Test-Path $zhPath) {
    $zhMap = Read-Satellite $zhPath
    if ($zhMap.ContainsKey($zhKey)) {
        [Threading.Thread]::CurrentThread.CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo("zh-CN")
        $viaProperty = [string]$appResType.GetProperty($zhKey).GetValue($null)
        $same = ($viaProperty -eq $zhMap[$zhKey])
        Write-Host ""
        $verdict = if ($same) { "ok" } else { "FAIL" }
        $msg = "  generated property check: Properties.Resources." + $zhKey + " -> '" + $viaProperty + "' (satellite says '" + $zhMap[$zhKey] + "') " + $verdict
        Write-Host $msg
        if (-not $same) { $failures.Add("generated property") }
    }
}

Write-Host ""
Write-Host ("  {0}/{1} cultures resolved from the Language folder; {2} of them differ from English" -f $ok, $cultures.Count, $translated)

if ($ok -lt $cultures.Count) { $failures.Add("coverage") }

if ($failures.Count -gt 0) {
    Write-Host ("[X] localization check FAILED (" + $failures.Count + "): " + (($failures | Select-Object -First 8) -join ", "))
    Write-Host "    Most likely Resources.Designer.cs no longer builds a LanguageFolderResourceManager."
    exit 1
}

Write-Host "[OK] localization check passed"
exit 0
