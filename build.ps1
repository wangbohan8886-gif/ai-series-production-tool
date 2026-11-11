$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = $root
$out = Join-Path $root "out"
$name = "AI系列生产工具"
$exeName = "AI Series Production Tool 3.2.0.65.exe"
$payloadDir = Join-Path $out "payload"
$appDir = Join-Path $payloadDir $name
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "Cannot find .NET Framework C# compiler: $csc"
}

$refs = @(
    "/r:System.Windows.Forms.dll",
    "/r:System.Drawing.dll",
    "/r:System.IO.Compression.dll",
    "/r:System.IO.Compression.FileSystem.dll"
)

$mainSources = @(
    (Join-Path $src "AppCore.cs"),
    (Join-Path $src "AppDialogs.cs"),
    (Join-Path $src "AppMain.cs"),
    (Join-Path $src "AppPages.cs"),
    (Join-Path $src "VersionInfo.cs")
)
$mainOut = Join-Path $appDir $exeName
$mainIcon = Join-Path $src "main.ico"

$mainArgs = @("/nologo", "/target:winexe", "/out:$mainOut", "/win32icon:$mainIcon") + $refs + $mainSources
& $csc $mainArgs
if ($LASTEXITCODE -ne 0) { throw "Main app build failed with exit code $LASTEXITCODE" }

# Build the installer payload from the freshly compiled app, then inline it into the wizard source.
$payloadZip = Join-Path $out "payload.zip"
if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($payloadDir, $payloadZip, [IO.Compression.CompressionLevel]::Optimal, $true)
$base64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($payloadZip))
$sourceText = [IO.File]::ReadAllText((Join-Path $src "SetupFinal.cs"), [Text.Encoding]::UTF8)
$sourceText = [regex]::Replace($sourceText, 'ZipBase64\s*=\s*"[^"]*"', 'ZipBase64 = "' + $base64 + '"', 1)
$generated = Join-Path $out "SetupFinal.generated.cs"
[IO.File]::WriteAllText($generated, $sourceText, (New-Object Text.UTF8Encoding($false)))

$setupOut = Join-Path $out "AI-Series-Production-Tool_3.2.0.65-Setup.exe"
$setupIcon = Join-Path $src "setup.ico"
$setupArgs = @("/nologo", "/target:winexe", "/out:$setupOut", "/win32icon:$setupIcon") + $refs + @($generated, (Join-Path $src "VersionInfo.cs"))
& $csc $setupArgs
if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE" }

Get-Item -LiteralPath $mainOut, $setupOut | Select-Object Name, Length
