<#
.SYNOPSIS
    One-time Azure + GitHub setup for the Deploy workflow (OIDC, no secrets).

.DESCRIPTION
    Creates (or reuses) everything `.github/workflows/deploy.yml` needs:
      1. Resource group the app is deployed into
      2. Entra ID app registration + service principal
      3. Contributor and User Access Administrator role assignments, scoped to the resource group
         (User Access Administrator is needed because the deployment grants the app's managed identity
          AcrPull on the registry and Key Vault Secrets User on the vault)
      4. Federated credential trusting the GitHub environment
      5. GitHub environment with the AZURE_* variables
         (and the DB_CONNECTION_STRING secret when -DbConnectionString is given)

    Safe to re-run: existing resources are reused.

.EXAMPLE
    ./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase

.EXAMPLE
    ./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -DbConnectionString (Read-Host -AsSecureString 'Connection string')

.EXAMPLE
    ./scripts/setup-azure-oidc.ps1 -GitHubRepo radekwojpl2/aspire-showcase -Location northeurope -SkipGitHub
#>
[CmdletBinding()]
param(
    # GitHub repository as owner/name. Defaults to the repo of the current directory (via gh).
    [string]$GitHubRepo,

    # Azure subscription id. Defaults to the current `az account`.
    [string]$SubscriptionId,

    [string]$Location = 'westeurope',
    [string]$ResourceGroup = 'rg-aspire-showcase',
    [string]$AppName = 'aspire-showcase-github',
    [string]$GitHubEnvironment = 'production',

    # Production connection string, stored as the DB_CONNECTION_STRING environment secret.
    # `aspire deploy` writes it into Key Vault. Omit to leave the existing secret unchanged.
    [securestring]$DbConnectionString,

    # Only do the Azure part and print the variables instead of setting them in GitHub.
    [switch]$SkipGitHub
)

$ErrorActionPreference = 'Stop'

function Invoke-Cli {
    # Runs a native command, fails on non-zero exit code and returns trimmed stdout.
    # Uses $args (no param block) so flags like --name are passed through untouched.
    $exe, $rest = $args
    $output = & $exe @rest
    if ($LASTEXITCODE -ne 0) { throw "'$exe $($rest -join ' ')' failed with exit code $LASTEXITCODE" }
    if ($null -ne $output) { ($output | Out-String).Trim() }
}

function Test-Cli {
    # Runs a native command silently and returns whether it succeeded.
    $exe, $rest = $args
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue' # stderr redirection must not throw in Windows PowerShell 5.1
    try { & $exe @rest *> $null } finally { $ErrorActionPreference = $previous }
    $LASTEXITCODE -eq 0
}

function Write-Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

# --- Prerequisites -----------------------------------------------------------

foreach ($tool in @('az') + $(if ($SkipGitHub) { @() } else { @('gh') })) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "'$tool' CLI is not installed or not on PATH." }
}

if (-not (Test-Cli az account show)) { throw "Not logged in to Azure. Run 'az login' first." }
if (-not $SkipGitHub -and -not (Test-Cli gh auth status)) { throw "Not logged in to GitHub. Run 'gh auth login' first." }

if (-not $GitHubRepo) {
    $GitHubRepo = Invoke-Cli gh repo view --json nameWithOwner --jq .nameWithOwner
}
if ($GitHubRepo -notmatch '^[^/\s]+/[^/\s]+$') { throw "GitHubRepo must be in the form owner/name (got '$GitHubRepo')." }

if ($SubscriptionId) {
    Invoke-Cli az account set --subscription $SubscriptionId | Out-Null
}
$SubscriptionId = Invoke-Cli az account show --query id --output tsv
$TenantId = Invoke-Cli az account show --query tenantId --output tsv

Write-Host "Subscription : $SubscriptionId"
Write-Host "Tenant       : $TenantId"
Write-Host "Repository   : $GitHubRepo (environment '$GitHubEnvironment')"
Write-Host ""

# --- 1. Resource group -------------------------------------------------------

Write-Step "Resource group '$ResourceGroup' in '$Location'"
$rgId = Invoke-Cli az group create --name $ResourceGroup --location $Location --query id --output tsv

# --- 2. App registration + service principal ---------------------------------

Write-Step "App registration '$AppName'"
$appId = Invoke-Cli az ad app list --display-name $AppName --query '[0].appId' --output tsv
if (-not $appId) {
    $appId = Invoke-Cli az ad app create --display-name $AppName --query appId --output tsv
    Write-Host "    created $appId"
} else {
    Write-Host "    reusing $appId"
}

$spId = Invoke-Cli az ad sp list --filter "appId eq '$appId'" --query '[0].id' --output tsv
if (-not $spId) {
    $spId = Invoke-Cli az ad sp create --id $appId --query id --output tsv
    Write-Host "    created service principal $spId"
}

# --- 3. Role assignments -----------------------------------------------------

foreach ($role in 'Contributor', 'User Access Administrator') {
    Write-Step "Role '$role' on $ResourceGroup"
    $existing = Invoke-Cli az role assignment list --assignee $spId --role $role --scope $rgId --query '[0].id' --output tsv
    if ($existing) {
        Write-Host "    already assigned"
        continue
    }
    # A freshly created service principal can take a moment to replicate; retry a few times.
    for ($attempt = 1; ; $attempt++) {
        & az role assignment create --assignee-object-id $spId --assignee-principal-type ServicePrincipal `
            --role $role --scope $rgId --output none
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -ge 5) { throw "Failed to assign role '$role'." }
        Write-Host "    principal not ready yet, retrying in 10s ($attempt/5)"
        Start-Sleep -Seconds 10
    }
}

# --- 4. Federated credential -------------------------------------------------

$subject = "repo:${GitHubRepo}:environment:$GitHubEnvironment"
Write-Step "Federated credential for '$subject'"
$existingSubjects = Invoke-Cli az ad app federated-credential list --id $appId --query '[].subject' --output tsv
if ($existingSubjects -and ($existingSubjects -split "`r?`n") -contains $subject) {
    Write-Host "    already exists"
} else {
    # Pass JSON via a file: inline JSON quoting is unreliable with az on Windows.
    $credentialFile = New-TemporaryFile
    try {
        @{
            name      = "github-$($GitHubRepo -replace '[^A-Za-z0-9-]', '-')-$GitHubEnvironment"
            issuer    = 'https://token.actions.githubusercontent.com'
            subject   = $subject
            audiences = @('api://AzureADTokenExchange')
        } | ConvertTo-Json | Set-Content -Path $credentialFile -Encoding ascii
        Invoke-Cli az ad app federated-credential create --id $appId --parameters "@$credentialFile" --output none | Out-Null
        Write-Host "    created"
    } finally {
        Remove-Item $credentialFile -ErrorAction SilentlyContinue
    }
}

# --- 5. GitHub environment + variables ---------------------------------------

$variables = [ordered]@{
    AZURE_CLIENT_ID       = $appId
    AZURE_TENANT_ID       = $TenantId
    AZURE_SUBSCRIPTION_ID = $SubscriptionId
    AZURE_LOCATION        = $Location
    AZURE_RESOURCE_GROUP  = $ResourceGroup
}

if ($SkipGitHub) {
    Write-Step "Set these variables on the '$GitHubEnvironment' environment in GitHub:"
    $variables.GetEnumerator() | ForEach-Object { Write-Host ("    {0,-22} {1}" -f $_.Key, $_.Value) }
    Write-Host "  and the DB_CONNECTION_STRING secret (the production connection string)."
} else {
    Write-Step "GitHub environment '$GitHubEnvironment'"
    Invoke-Cli gh api --method PUT "repos/$GitHubRepo/environments/$GitHubEnvironment" --silent | Out-Null

    foreach ($variable in $variables.GetEnumerator()) {
        Invoke-Cli gh variable set $variable.Key --env $GitHubEnvironment --repo $GitHubRepo --body $variable.Value | Out-Null
        Write-Host "    $($variable.Key) set"
    }

    if ($DbConnectionString) {
        # Piped via stdin so the value never shows up in a process command line.
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($DbConnectionString)
        try {
            [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) |
                gh secret set DB_CONNECTION_STRING --env $GitHubEnvironment --repo $GitHubRepo
            if ($LASTEXITCODE -ne 0) { throw "Failed to set the DB_CONNECTION_STRING secret." }
        } finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
        Write-Host "    DB_CONNECTION_STRING secret set"
    } else {
        $secretNames = Invoke-Cli gh secret list --env $GitHubEnvironment --repo $GitHubRepo --json name --jq '.[].name'
        if (-not $secretNames -or ($secretNames -split "`r?`n") -notcontains 'DB_CONNECTION_STRING') {
            Write-Warning "DB_CONNECTION_STRING secret is not set; deploys will fail until you add it (re-run with -DbConnectionString)."
        }
    }
}

Write-Host ""
Write-Host "Done. Push to main (or run the Deploy workflow manually) to deploy." -ForegroundColor Green
