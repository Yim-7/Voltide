# sign.ps1 - 给本机自用的 Voltide.exe 做自签名代码签名，消除 SmartScreen 弹窗。
# 用法：在本目录右键“使用 PowerShell 运行”，或 PowerShell 执行 .\sign.ps1
# 首次运行导入根证书时会弹一次安全确认，点“是”即可；之后不再需要。
$ErrorActionPreference = 'Stop'

$subject = 'CN=VoltideLocalDev'
$exe = Join-Path $PSScriptRoot 'Voltide.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe" }

# 1. 查找已有的本地代码签名证书；没有则创建并加入本机信任。
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $subject } | Select-Object -First 1

if (-not $cert) {
    Write-Host '首次运行：创建并信任本地代码签名证书…'
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
            -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10)

    $cer = Join-Path $env:TEMP 'voltide-code.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher | Out-Null
    Remove-Item $cer -Force
}

# 2. 给 exe 签名。
$sig = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert
if ($sig.Status -ne 'Valid') { throw "签名失败：$($sig.Status)" }

Write-Host "已签名：$exe"
Write-Host '现在双击运行不会再弹 SmartScreen。'
