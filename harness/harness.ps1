<#
.SYNOPSIS
  AHK2 AST test harness entry point.

.DESCRIPTION
  Builds a private copy of the engine from the working tree (harness\bin\AstEngine.dll, never touches build\)
  plus the harness itself, then forwards the command. Rebuilds only when sources change.

  .\harness\harness.ps1 selftest
  .\harness\harness.ps1 scan
  .\harness\harness.ps1 run --tier core
  .\harness\harness.ps1 cases
  .\harness\harness.ps1 check C:\path\script.ahk
  .\harness\harness.ps1 validate C:\path\script.ahk
  .\harness\harness.ps1 build            # force rebuild
  .\harness\harness.ps1 help
#>
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = 'Stop'
$H = $PSScriptRoot
$Root = Split-Path -Parent $H
$Bin = Join-Path $H 'bin'
$Src = Join-Path $Root 'src'

function Get-Csc {
    foreach ($fx in @('Framework64', 'Framework')) {
        $c = Join-Path $env:SystemRoot "Microsoft.NET\$fx\v4.0.30319\csc.exe"
        if (Test-Path $c) { return $c }
    }
    throw 'csc.exe (.NET Framework 4.x) not found'
}

function Get-SourceHash([string[]]$files) {
    $sha = [Security.Cryptography.SHA1]::Create()
    $ms = New-Object IO.MemoryStream
    foreach ($f in ($files | Sort-Object)) {
        $b = [IO.File]::ReadAllBytes($f); $ms.Write($b, 0, $b.Length)
        $n = [Text.Encoding]::UTF8.GetBytes($f); $ms.Write($n, 0, $n.Length)
    }
    ($sha.ComputeHash($ms.ToArray()) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Invoke-Csc([string]$csc, [string[]]$cscArgs, [string]$what) {
    $rsp = Join-Path $Bin ("$what.rsp")
    Set-Content -Path $rsp -Value ($cscArgs -join "`r`n") -Encoding UTF8
    $out = & $csc "@$rsp" 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Where-Object { $_ -match 'error' } | Select-Object -First 40 | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "$what compilation failed"
    }
}

function Build-All([switch]$Force, [switch]$Workbench, [switch]$HostExe) {
    New-Item -ItemType Directory -Force -Path $Bin | Out-Null
    $csc = Get-Csc
    $fx = Split-Path -Parent $csc
    $vendor = Join-Path $Root 'vendor'
    $refs = @('System.dll', 'System.Core.dll', 'Microsoft.CSharp.dll', 'System.Data.dll', 'System.Drawing.dll',
        'System.Windows.Forms.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Web.Extensions.dll', 'System.Design.dll', 'System.Management.dll')
    $common = @('/nologo', '/optimize+', '/unsafe', "/lib:`"$fx`"", "/lib:`"$fx\WPF`"") + ($refs | ForEach-Object { "/reference:$_" })

    # 1. Engine (all plugins) from the working tree
    $engineSrc = @(Get-ChildItem "$Src\ast\*.cs", "$Src\jit\*.cs", "$Src\plugins\*.cs" | ForEach-Object FullName)
    $engineHash = Get-SourceHash $engineSrc
    $engineDll = Join-Path $Bin 'AstEngine.dll'
    $stamp = Join-Path $Bin 'engine.hash'
    if ($Force -or -not (Test-Path $engineDll) -or -not (Test-Path $stamp) -or (Get-Content $stamp) -ne $engineHash) {
        Write-Host "[build] engine ($($engineSrc.Count) files)" -ForegroundColor DarkGray
        Invoke-Csc $csc ($common + @('/target:library', "/out:`"$engineDll`"") + ($engineSrc | ForEach-Object { "`"$_`"" })) 'engine'
        Set-Content $stamp $engineHash
        Remove-Item (Join-Path $Bin 'harness.hash') -ErrorAction SilentlyContinue
    }

    # 2. Harness
    $harnessSrc = @(Get-ChildItem "$H\src\*.cs" | ForEach-Object FullName)
    $harnessHash = Get-SourceHash $harnessSrc
    $exe = Join-Path $Bin 'AstHarness.exe'
    $hstamp = Join-Path $Bin 'harness.hash'
    if ($Force -or -not (Test-Path $exe) -or -not (Test-Path $hstamp) -or (Get-Content $hstamp) -ne $harnessHash) {
        Write-Host "[build] harness ($($harnessSrc.Count) files)" -ForegroundColor DarkGray
        Invoke-Csc $csc ($common + @('/target:exe', "/out:`"$exe`"", "/reference:`"$engineDll`"") + ($harnessSrc | ForEach-Object { "`"$_`"" })) 'harness'
        Set-Content $hstamp $harnessHash
    }

    # 3. Optional: a private AstWorkbench.exe for UI screenshots (state files stay in harness\bin\wb)
    if ($Workbench) {
        $wb = Join-Path $Bin 'wb'
        New-Item -ItemType Directory -Force -Path $wb | Out-Null
        $uiSrc = @((Join-Path $Src 'AstWorkbench.cs')) + @(Get-ChildItem "$Src\ui\*.cs" | ForEach-Object FullName)
        $uiHash = (Get-SourceHash $uiSrc) + $engineHash
        $wexe = Join-Path $wb 'AstWorkbench.exe'
        $wstamp = Join-Path $wb 'wb.hash'
        if ($Force -or -not (Test-Path $wexe) -or -not (Test-Path $wstamp) -or (Get-Content $wstamp) -ne $uiHash) {
            Write-Host "[build] workbench ($($uiSrc.Count) files)" -ForegroundColor DarkGray
            Copy-Item $engineDll (Join-Path $wb 'AstEngine.dll') -Force
            $vendorDlls = @('DiffPlex.dll', 'FastColoredTextBox.dll', 'WeifenLuo.WinFormsUI.Docking.dll', 'WeifenLuo.WinFormsUI.Docking.ThemeVS2015.dll')
            $vrefs = $vendorDlls | ForEach-Object { "/reference:`"$vendor\$_`"" }
            $vres = $vendorDlls | ForEach-Object { "/resource:`"$vendor\$_`",ui.resources.$_" }
            Invoke-Csc $csc ($common + @('/target:winexe', "/out:`"$wexe`"", "/win32manifest:`"$Root\app.manifest`"", "/reference:`"$(Join-Path $wb 'AstEngine.dll')`"") + $vrefs + $vres + ($uiSrc | ForEach-Object { "`"$_`"" })) 'workbench'
            if (Test-Path "$Root\build\Flows") { Copy-Item "$Root\build\Flows" $wb -Recurse -Force }
            Set-Content $wstamp $uiHash
        }
    }

    # 4. Optional: AstHost.exe, the JSON-over-stdio helper (engine compiled in: one file, nothing to keep in step).
    #    A GUI-subsystem exe, so it never opens a console window; its stdin/stdout are the caller's pipes.
    if ($HostExe) {
        $hdir = Join-Path $Bin 'host'
        New-Item -ItemType Directory -Force -Path $hdir | Out-Null
        $hostSrc = $engineSrc + @(Get-ChildItem "$Src\host\*.cs" | ForEach-Object FullName)
        $hostHash = Get-SourceHash $hostSrc
        $hexe = Join-Path $hdir 'AstHost.exe'
        $hstamp2 = Join-Path $hdir 'host.hash'
        if ($Force -or -not (Test-Path $hexe) -or -not (Test-Path $hstamp2) -or (Get-Content $hstamp2) -ne $hostHash) {
            Write-Host "[build] host ($($hostSrc.Count) files)" -ForegroundColor DarkGray
            Invoke-Csc $csc ($common + @('/target:winexe', "/out:`"$hexe`"") + ($hostSrc | ForEach-Object { "`"$_`"" })) 'host'
            Set-Content $hstamp2 $hostHash
        }
    }
}

function Install-Host {
    # build\AstHost.exe (only that file; the rest of build\ is left alone) and the AxStudio copy
    $hexe = Join-Path $Bin 'host\AstHost.exe'
    $targets = @((Join-Path $Root 'build'), 'C:\Users\o\Downloads\AHK2-ActiveX-Gui\studio\bin')
    foreach ($t in $targets) {
        if (-not (Test-Path (Split-Path -Parent $t))) { Write-Host "  (skipping $($t) - parent folder missing)"; continue }
        New-Item -ItemType Directory -Force -Path $t | Out-Null
        Copy-Item $hexe (Join-Path $t 'AstHost.exe') -Force
        Copy-Item (Join-Path $Src 'host\AxtReader.ahk') (Join-Path $t 'AxtReader.ahk') -Force
        Write-Host "  installed $(Join-Path $t 'AstHost.exe') (+ AxtReader.ahk)"
    }
}

function Invoke-Verify {
    # The legacy tests\VerifyTest.cs suite, compiled against the harness's engine (not build\).
    $csc = Get-Csc
    $vdir = Join-Path $Bin 'verify'
    New-Item -ItemType Directory -Force -Path $vdir | Out-Null
    Copy-Item (Join-Path $Bin 'AstEngine.dll') $vdir -Force
    Copy-Item (Join-Path $Root 'tests\test_*.ahk') $vdir -Force
    $exe = Join-Path $vdir 'VerifyTest.exe'
    Invoke-Csc $csc @('/nologo', "/out:`"$exe`"", "/reference:`"$vdir\AstEngine.dll`"", '/reference:System.dll', '/reference:System.Core.dll',
        '/reference:Microsoft.CSharp.dll', "`"$Root\tests\VerifyTest.cs`"") 'verify'
    $env:AHK2AST_HEADLESS = 'true'
    $out = & $exe 2>&1
    $code = $LASTEXITCODE
    $out | Select-String -Pattern '^\s*(FAIL|SUCCESS|SKIPPING|Unhandled)' | ForEach-Object { Write-Host $_.Line }
    Write-Host ("VerifyTest exit code {0}" -f $code)
    return $code
}

switch ($Command.ToLowerInvariant()) {
    'build' { Build-All -Force -Workbench:($Rest -contains '--workbench') -HostExe:($Rest -contains '--host'); Write-Host 'build ok'; exit 0 }
    'host' {
        # `host [--install]`: build AstHost.exe, run its protocol test; --install copies it to build\ and AxStudio
        Build-All -HostExe
        $env:AHK2AST_HEADLESS = 'true'
        & (Join-Path $Bin 'AstHarness.exe') host (Join-Path $Bin 'host\AstHost.exe') @Rest
        $code = $LASTEXITCODE
        if ($code -eq 0 -and ($Rest -contains '--install')) { Install-Host }
        exit $code
    }
    'verify' { Build-All; exit (Invoke-Verify) }
    'help' { Build-All; & (Join-Path $Bin 'AstHarness.exe'); exit 0 }
    default {
        Build-All -Workbench:($Command -eq 'shot')
        $env:AHK2AST_HEADLESS = 'true'
        & (Join-Path $Bin 'AstHarness.exe') $Command @Rest
        exit $LASTEXITCODE
    }
}
