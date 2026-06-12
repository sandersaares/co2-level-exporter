<#
.SYNOPSIS
    Applies the CO2 exporter database schema and configures its credentials.

.DESCRIPTION
    Idempotent. Connects to the shared "datastore" database as the SQL administrator
    (password read from Key Vault), applies sql/schema.sql (co2 schema, readings table,
    co2_writer user), generates and stores the co2_writer password and connection string
    in Key Vault, and sets the CO2_SQL_CONNECTION_STRING user environment variable so the
    logger can read it.

    Connection details default to the home-env datastore outputs file if present
    (../home-env/infra/datastore/.outputs.json), or can be passed explicitly.

.EXAMPLE
    ./deploy.ps1 -Verbose

.EXAMPLE
    ./deploy.ps1 -KeyVaultName kv-datastore-xxxx -SqlServerFqdn sql-datastore-xxxx.database.windows.net -Verbose
#>
[CmdletBinding()]
param(
    [string]$SubscriptionId = 'a94944a5-949a-4cc0-82bf-bf1dc763b137',
    [string]$KeyVaultName,
    [string]$SqlServerFqdn,
    [string]$DatabaseName = 'datastore',
    [string]$SqlAdminLogin = 'datastoreadmin',
    [string]$DatastoreOutputsPath,

    # Skip setting the CO2_SQL_CONNECTION_STRING user environment variable.
    [switch]$SkipSetEnvironment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $here

# --- Minimal self-contained helpers (this repo does not depend on home-env scripts) ---

function Assert-AzLogin {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SubscriptionId)

    Write-Verbose "Assert-AzLogin: verifying Azure CLI session"
    $account = az account show -o json 2>$null | ConvertFrom-Json
    if (-not $account) {
        throw "Azure CLI is not logged in. Run 'az login' first."
    }
    if ($account.id -ne $SubscriptionId) {
        Write-Verbose "Assert-AzLogin: switching to subscription $SubscriptionId"
        az account set --subscription $SubscriptionId | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to select subscription $SubscriptionId." }
    }
    Write-Verbose "Assert-AzLogin: subscription $SubscriptionId active"
}

function New-StrongPassword {
    [CmdletBinding()]
    param([int]$Length = 24)

    Write-Verbose "New-StrongPassword: generating a $Length-character password"
    $lower = 'abcdefghijklmnopqrstuvwxyz'
    $upper = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
    $digits = '0123456789'
    $special = '-_.~#'
    $all = $lower + $upper + $digits + $special

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $pick = {
            param([string]$set)
            $bytes = [byte[]]::new(4)
            $rng.GetBytes($bytes)
            return $set[[int]([System.BitConverter]::ToUInt32($bytes, 0) % [uint32]$set.Length)]
        }
        $chars = New-Object System.Collections.Generic.List[char]
        $chars.Add((& $pick $lower))
        $chars.Add((& $pick $upper))
        $chars.Add((& $pick $digits))
        $chars.Add((& $pick $special))
        for ($i = $chars.Count; $i -lt $Length; $i++) { $chars.Add((& $pick $all)) }
        for ($i = $chars.Count - 1; $i -gt 0; $i--) {
            $bytes = [byte[]]::new(4)
            $rng.GetBytes($bytes)
            $j = [int]([System.BitConverter]::ToUInt32($bytes, 0) % [uint32]($i + 1))
            $tmp = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $tmp
        }
        return -join $chars
    }
    finally {
        $rng.Dispose()
    }
}

# --- Resolve connection parameters ---

if (-not $DatastoreOutputsPath) {
    $DatastoreOutputsPath = Join-Path $repoRoot '..\home-env\infra\datastore\.outputs.json'
}
if ((-not $KeyVaultName -or -not $SqlServerFqdn) -and (Test-Path $DatastoreOutputsPath)) {
    Write-Verbose "deploy.ps1: reading datastore outputs from $DatastoreOutputsPath"
    $outputs = Get-Content $DatastoreOutputsPath -Raw | ConvertFrom-Json
    if (-not $KeyVaultName) { $KeyVaultName = $outputs.keyVaultName }
    if (-not $SqlServerFqdn) { $SqlServerFqdn = $outputs.sqlServerFqdn }
    if ($outputs.databaseName) { $DatabaseName = $outputs.databaseName }
}
if (-not $KeyVaultName -or -not $SqlServerFqdn) {
    throw "Provide -KeyVaultName and -SqlServerFqdn (or a valid -DatastoreOutputsPath)."
}
Write-Verbose "deploy.ps1: vault '$KeyVaultName', server '$SqlServerFqdn', database '$DatabaseName'"

Assert-AzLogin -SubscriptionId $SubscriptionId

Write-Verbose "deploy.ps1: reading SQL admin password from Key Vault"
$adminPassword = az keyvault secret show --vault-name $KeyVaultName --name 'sql-admin-password' --query value -o tsv
if ($LASTEXITCODE -ne 0 -or -not $adminPassword) { throw "Could not read 'sql-admin-password' from Key Vault '$KeyVaultName'." }

Write-Verbose "deploy.ps1: resolving co2_writer password"
$writerPassword = az keyvault secret show --vault-name $KeyVaultName --name 'co2-writer-password' --query value -o tsv 2>$null
$generated = $false
if ($LASTEXITCODE -ne 0 -or -not $writerPassword) {
    Write-Verbose "deploy.ps1: no existing co2_writer password; generating a new one"
    $writerPassword = New-StrongPassword -Length 24
    $generated = $true
}
else {
    Write-Verbose "deploy.ps1: reusing existing co2_writer password from Key Vault"
}

Write-Verbose "deploy.ps1: applying sql/schema.sql"
& sqlcmd -S $SqlServerFqdn -d $DatabaseName -U $SqlAdminLogin -P $adminPassword -N -b `
    -i (Join-Path $repoRoot 'sql\schema.sql') `
    -v "Co2WriterPassword=$writerPassword"
if ($LASTEXITCODE -ne 0) { throw "Failed to apply sql/schema.sql." }

if ($generated) {
    Write-Verbose "deploy.ps1: storing co2_writer password in Key Vault"
    az keyvault secret set --vault-name $KeyVaultName --name 'co2-writer-password' --value $writerPassword --output none
    if ($LASTEXITCODE -ne 0) { throw "Failed to store 'co2-writer-password' in Key Vault." }
}

$connectionString = "Server=tcp:$SqlServerFqdn,1433;Database=$DatabaseName;User ID=co2_writer;Password=$writerPassword;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
Write-Verbose "deploy.ps1: storing co2_writer connection string in Key Vault"
az keyvault secret set --vault-name $KeyVaultName --name 'co2-writer-connection-string' --value $connectionString --output none | Out-Null

if (-not $SkipSetEnvironment) {
    Write-Verbose "deploy.ps1: setting user environment variable CO2_SQL_CONNECTION_STRING"
    [Environment]::SetEnvironmentVariable('CO2_SQL_CONNECTION_STRING', $connectionString, 'User')
}

Write-Host ""
Write-Host "CO2 exporter database is ready." -ForegroundColor Green
Write-Host "  Server   : $SqlServerFqdn"
Write-Host "  Database : $DatabaseName"
Write-Host "  Schema   : co2 (table co2.readings)"
Write-Host "  Writer   : co2_writer (INSERT only; password in Key Vault 'co2-writer-password')"
if (-not $SkipSetEnvironment) {
    Write-Host "  Set user env var CO2_SQL_CONNECTION_STRING (restart shells to pick it up)." -ForegroundColor Cyan
}
Write-Host ""
Write-Host "Run the logger with: dotnet run --project `"$repoRoot`"" -ForegroundColor Cyan
