# Publishes a new FMT version: every running FMT picks it up automatically.
# Usage: .\release.ps1 1.0.1 "Was ist neu"
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Notes = ""
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\FMT\FMT.csproj"
$out = Join-Path $root "release"
$tag = "v$Version"

function Run($exe) {
    # Only the exit code counts: git writes harmless warnings to stderr.
    $ErrorActionPreference = "Continue"
    & $exe @args
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

if (git -C $root status --porcelain) { throw "Uncommitted changes: commit them first." }

Run dotnet test (Join-Path $root "tests\Fmt.Tests\Fmt.Tests.csproj") --nologo -v q

$csproj = [IO.File]::ReadAllText($project)
$updated = $csproj -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
if ($updated -ne $csproj) {
    [IO.File]::WriteAllText($project, $updated)
    Run git -C $root add $project
    Run git -C $root commit -q -m "Release $tag"
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
Run dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out --nologo -v q

Run git -C $root tag $tag
Run git -C $root push -q origin main $tag
Run gh release create $tag (Join-Path $out "FMT.exe") --repo ImAestosDev/Frostvein.ManagementTool --title "FMT $Version" --notes $Notes
"Released $tag"
