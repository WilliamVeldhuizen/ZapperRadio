<#
.SYNOPSIS
    Submits new MSIX packages of ZapperRadio to the Microsoft Store through the Partner Center submission API.
.DESCRIPTION
    Creates a new submission for the app, which starts as a copy of the last published one (listing, pricing,
    screenshots and all), swaps its packages for the given .msixupload files, uploads them and commits the
    submission. Certification then runs as usual; with -PublishMode Immediate the update goes live as soon as it
    passes, with Manual it waits for a go in Partner Center.

    It signs in as a Microsoft Entra application that is linked to the Partner Center account with the Manager role
    (Account settings > User management > Microsoft Entra applications). The credentials come from the parameters or
    from the PARTNER_CENTER_* environment variables, which is how the Store MSIX workflow passes its secrets.

    Partner Center allows one pending submission per app. When there already is one (a draft, or an update still in
    certification), the script stops, unless -ReplacePending is given: that deletes a draft first. A submission that
    is being certified cannot be replaced; wait for it to finish.
.EXAMPLE
    .\scripts\submit-store.ps1 -Packages artifacts\store\*.msixupload
    .\scripts\submit-store.ps1 -Packages a.msixupload, b.msixupload -PublishMode Manual -ReplacePending
#>
param(
    [Parameter(Mandatory)] [string[]] $Packages,
    [ValidateSet('Immediate', 'Manual')] [string] $PublishMode = 'Immediate',
    [switch] $ReplacePending,
    [string] $AppId = '9NLNJGRFGPGH',
    [string] $TenantId = $env:PARTNER_CENTER_TENANT_ID,
    [string] $ClientId = $env:PARTNER_CENTER_CLIENT_ID,
    [string] $ClientSecret = $env:PARTNER_CENTER_CLIENT_SECRET
)

$ErrorActionPreference = 'Stop'
$api = 'https://manage.devcenter.microsoft.com/v1.0/my'

$credentials = [ordered]@{ TenantId = 'PARTNER_CENTER_TENANT_ID'; ClientId = 'PARTNER_CENTER_CLIENT_ID'; ClientSecret = 'PARTNER_CENTER_CLIENT_SECRET' }
foreach ($name in $credentials.Keys) {
    if (-not (Get-Variable $name -ValueOnly)) { throw "No $($name): pass -$name or set $($credentials[$name])." }
}

$files = @($Packages | ForEach-Object { Get-Item $_ } | Where-Object Extension -eq '.msixupload')
if (-not $files) { throw "No .msixupload files in '$($Packages -join "', '")'." }
if (@($files.Name | Select-Object -Unique).Count -ne $files.Count) { throw 'Two packages have the same file name.' }

$token = (Invoke-RestMethod -Method Post "https://login.microsoftonline.com/$TenantId/oauth2/token" -Body @{
    grant_type    = 'client_credentials'
    client_id     = $ClientId
    client_secret = $ClientSecret
    resource      = 'https://manage.devcenter.microsoft.com'
}).access_token
$headers = @{ Authorization = "Bearer $token" }

function Invoke-Api([string] $Method, [string] $Path, $Body) {
    # The API wants a JSON content type on every call, also a POST without a body.
    $call = @{ Method = $Method; Uri = "$api/$Path"; Headers = $headers; ContentType = 'application/json' }
    if ($null -ne $Body) {
        $call.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 50))
    }
    elseif ($Method -eq 'Post') {
        $call.Body = '{}'
    }
    Invoke-RestMethod @call
}

$app = Invoke-Api Get "applications/$AppId"
Write-Host "App: $($app.primaryName) ($AppId)"

$pending = $app.pendingApplicationSubmission
if ($pending) {
    if (-not $ReplacePending) {
        throw "There already is a pending submission ($($pending.id)). Finish or delete it in Partner Center, or pass -ReplacePending to delete a draft."
    }
    Write-Host "Deleting the pending submission $($pending.id)."
    Invoke-Api Delete "applications/$AppId/submissions/$($pending.id)" | Out-Null
}

$submission = Invoke-Api Post "applications/$AppId/submissions"
$id = $submission.id
Write-Host "Created submission $id."

try {
    # The copied submission lists the packages of the last one; they make way for the new ones.
    foreach ($package in @($submission.applicationPackages)) { $package.fileStatus = 'PendingDelete' }
    $submission.applicationPackages = @($submission.applicationPackages) + @($files | ForEach-Object {
        [pscustomobject]@{
            fileName              = $_.Name
            fileStatus            = 'PendingUpload'
            minimumDirectXVersion = 'None'
            minimumSystemRam      = 'None'
        }
    })
    $submission.targetPublishMode = $PublishMode
    Invoke-Api Put "applications/$AppId/submissions/$id" $submission | Out-Null

    # The packages go up as one zip, with the file names above at its root, to the blob the submission points at.
    $zip = Join-Path ([IO.Path]::GetTempPath()) "zapperradio-store-$id.zip"
    Compress-Archive -Path $files.FullName -DestinationPath $zip -Force
    Write-Host "Uploading $($files.Name -join ', ') ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)."
    Invoke-RestMethod -Method Put -Uri $submission.fileUploadUrl -InFile $zip -ContentType 'application/zip' `
        -Headers @{ 'x-ms-blob-type' = 'BlockBlob' } | Out-Null
    Remove-Item $zip

    Invoke-Api Post "applications/$AppId/submissions/$id/commit" | Out-Null
    Write-Host 'Committed; waiting for Partner Center to accept it.'

    do {
        Start-Sleep -Seconds 30
        $status = Invoke-Api Get "applications/$AppId/submissions/$id/status"
        Write-Host "  $($status.status)"
    } while ($status.status -eq 'CommitStarted')

    if ($status.status -eq 'CommitFailed') {
        $status.statusDetails.errors | ForEach-Object { Write-Host "  $($_.code): $($_.details)" }
        throw "Partner Center rejected submission $id."
    }
}
catch {
    Write-Host "Submission $id is left as a draft in Partner Center; fix it there or run again with -ReplacePending."
    throw
}

Write-Host "Submission $id is in certification ($PublishMode publish)."
