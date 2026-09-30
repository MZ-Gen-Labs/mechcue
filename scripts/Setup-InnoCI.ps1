param()
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (!(Test-Path -LiteralPath $taskCompiler) -or (Get-Item -LiteralPath $taskCompiler).VersionInfo.ProductVersion.Trim() -ne '6.7.3') {
    $taskDirectory = Join-Path $env:RUNNER_TEMP 'mechcue-inno'
    New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
    $taskDownload = Join-Path $taskDirectory 'innosetup-6.7.3.exe'
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $taskDownload
    $taskExpected = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
    if ((Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskExpected) { throw 'Inno Setup download checksum mismatch' }
    if ((Get-AuthenticodeSignature -LiteralPath $taskDownload).Status -ne 'Valid') { throw 'Inno Setup publisher signature is invalid' }
    $taskInstall = Join-Path $taskDirectory 'compiler'
    $taskProcess = Start-Process -FilePath $taskDownload -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER',("/DIR=`"{0}`"" -f $taskInstall)) -PassThru -Wait -WindowStyle Hidden
    if ($taskProcess.ExitCode -ne 0) { throw 'Inno Setup compiler installation failed' }
    $taskCompiler = Join-Path $taskInstall 'ISCC.exe'
}
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'ISCC compiler not found' }
"INNO_SETUP_COMPILER=$taskCompiler" | Out-File -LiteralPath $env:GITHUB_ENV -Encoding utf8 -Append
