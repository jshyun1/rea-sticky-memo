param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $out | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path $csc)) { throw '.NET Framework C# compiler was not found.' }
$app = Join-Path $out 'StickyMemo.exe'
$tests = Join-Path $out 'StickyMemo.Tests.exe'
$common = @('/nologo', '/utf8output', '/optimize+', '/r:System.dll', '/r:System.Core.dll',
    '/r:System.Runtime.Serialization.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
Write-Output ('Compiler: ' + $csc)
& $csc @common /target:winexe "/out:$app" (Join-Path $root 'src\NoteStore.cs') (Join-Path $root 'src\Windows.cs') (Join-Path $root 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
& $csc @common /target:exe "/out:$tests" "/r:$app" (Join-Path $root 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
Write-Output ('Built: ' + $app)
if ($Test) {
    & $tests $out
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($LASTEXITCODE)." }
}
