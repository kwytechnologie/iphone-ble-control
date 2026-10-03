# Requires an elevated PowerShell. Stages a signed driver; does NOT create a permanent monitor.
# No certificate import, test-signing, security-policy change, or physical-adapter changes.
$ErrorActionPreference = 'Stop'
$logPath = Join-Path $PSScriptRoot 'install.log'
Start-Transcript -Path $logPath -Force | Out-Null
try {
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Execute como administrador para cadastrar o driver.'
    }
    $packageDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\.tools\virtual-display-24.12.24\package'))
    $expected = @{
        'MttVDD.dll' = 'C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05'
        'mttvdd.cat' = '08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6'
        'MttVDD.inf' = '550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D'
    }
    foreach ($name in $expected.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $packageDir $name) -Algorithm SHA256).Hash -ne $expected[$name]) {
            throw "Pacote diferente do verificado: $name"
        }
    }
    foreach ($name in @('MttVDD.dll', 'mttvdd.cat')) {
        $signature = Get-AuthenticodeSignature -FilePath (Join-Path $packageDir $name)
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike 'CN=SignPath Foundation,*') {
            throw "Assinatura não aceita: $name. Nenhum certificado será importado."
        }
    }
    $configDir = 'C:\VirtualDisplayDriver'
    if (Test-Path -LiteralPath $configDir) {
        throw 'Já existe C:\VirtualDisplayDriver. Pare para preservar a configuração existente.'
    }
    # The INF, catalog and DLL are unmodified. Windows performs its own driver-package validation.
    & "$env:windir\System32\pnputil.exe" /add-driver (Join-Path $packageDir 'MttVDD.inf')
    if ($LASTEXITCODE -notin @(0, 3010)) { throw "Windows recusou o driver (código $LASTEXITCODE)." }
    New-Item -ItemType Directory -Path $configDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'vdd_settings.xml') -Destination (Join-Path $configDir 'vdd_settings.xml')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'blehid-owner.txt') -Destination (Join-Path $configDir 'blehid-owner.txt')
    'DRIVER CADASTRADO. Nenhum monitor permanente criado; as proteções do Windows foram mantidas.'
} catch {
    Write-Output "FALHA: $($_.Exception.Message)"
    exit 1
} finally { Stop-Transcript | Out-Null }
