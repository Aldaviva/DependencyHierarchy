$scriptDir = Split-Path $script:MyInvocation.MyCommand.Path

$success = $true

ForEach ($packageIdSuffix in @("Deploy", "Install", "Publish", "Restore")) {
	$packageId = "PackageTools.$packageIdSuffix"

	Write-Output "Packing $packageId"
	dotnet pack (Join-Path $scriptDir $packageIdSuffix) -p:WorkspaceResolution=false -v:q
	if ($LASTEXITCODE -ne 0) { 
		$success=$false; 
		continue;
	}

	Write-Output "Uninstalling $packageId"
	dotnet tool uninstall --global $packageId > $null 2>$null

	Write-Output "Installing $packageId"
	dotnet tool install --global --source (Join-Path $scriptDir bin) $packageId --prerelease
	if ($LASTEXITCODE -ne 0) { 
		$success=$false;
	}
}

if ($success -eq $false) {
	exit 1
}