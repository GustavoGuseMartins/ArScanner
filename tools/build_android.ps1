# Compila a árvore de trabalho atual com a versão Unity registrada no projeto.
# As validações exercitam o mesmo fluxo de cenas e controles incluído no APK.
[CmdletBinding()]
param(
    [string]$OutputPath = 'Builds/ScannerAR.apk',
    [string]$LogDirectory = 'diagnostics/android-build',
    [string]$UnityPath,
    [switch]$CheckScenes
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot

function Get-ProjectPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $projectDirectory $Path))
}

$versionFile = Join-Path $projectDirectory 'ProjectSettings/ProjectVersion.txt'
$versionText = Get-Content -LiteralPath $versionFile -Raw
if ($versionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)') {
    throw "Versão Unity ausente em $versionFile"
}
$editorVersion = $Matches[1]
if (!$UnityPath) {
    $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Unity.exe"
}
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $editorVersion não encontrado. Informe -UnityPath com o caminho do editor."
}
$apkPath = Get-ProjectPath $OutputPath
$logsPath = Get-ProjectPath $LogDirectory
New-Item -ItemType Directory -Force -Path $logsPath | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $apkPath) | Out-Null

function Invoke-UnityCheck([string]$Name, [string]$Method, [string[]]$ExtraArguments) {
    $logPath = Join-Path $logsPath "$Name.log"
    # Start-Process precisa das aspas internas para preservar caminhos com espaços.
    $arguments = @('-batchmode', '-buildTarget', 'Android', '-projectPath',
        ('"' + $projectDirectory + '"'), '-executeMethod', $Method,
        '-logFile', ('"' + $logPath + '"')) + $ExtraArguments
    Write-Output "Unity ${editorVersion}: $Name"
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    $process.Refresh()
    $process.ExitCode | Set-Content -LiteralPath (Join-Path $logsPath "$Name.exit")
    if ($process.ExitCode -ne 0) {
        if (Test-Path -LiteralPath $logPath) {
            Get-Content -LiteralPath $logPath -Tail 30 | Write-Output
        }
        throw "$Name falhou (código $($process.ExitCode)). Consulte $logPath"
    }
    Select-String -LiteralPath $logPath -Pattern '\] (PASS|PASSED|PLAY MODE PASSED|INTEGRATION PASSED)|\[Build OK\]' |
        ForEach-Object { Write-Output $_.Line }
}

# Cada método encerra o editor após verificar o resultado; não usar -quit antes dele.
if ($CheckScenes) {
    Invoke-UnityCheck 'menu-playmode' 'ArScanner.EditorTools.MenuSceneValidation.CheckMenuInPlayMode' @()
    Invoke-UnityCheck 'viewer-playmode' 'ArScanner.EditorTools.ViewerSceneValidation.CheckViewerInPlayMode' @()
}
Invoke-UnityCheck 'android-build' 'ArScanner.EditorTools.ScannerPoseValidation.BuildValidated' @(
    '-nographics', '-arscannerOutput', ('"' + $apkPath + '"'))
if (!(Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "O editor encerrou sem gerar o APK esperado: $apkPath"
}
$apk = Get-Item -LiteralPath $apkPath
[PSCustomObject]@{
    editor = $editorVersion
    apk = $apk.FullName
    bytes = $apk.Length
    sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
    scenesChecked = [bool]$CheckScenes
    logs = $logsPath
} | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $logsPath 'build-result.json')
Write-Output "APK validado: $apkPath"
