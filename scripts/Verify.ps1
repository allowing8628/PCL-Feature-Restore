param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$DnlibPath)
$ErrorActionPreference = 'Stop'
Add-Type -Path $DnlibPath
$module = [dnlib.DotNet.ModuleDefMD]::Load($Path)
try {
    if ($module.Assembly.Version.ToString() -ne '2.99.0.0') { throw 'Unexpected version' }
    $types = @{}
    foreach ($t in $module.GetTypes()) { $types[$t.FullName]=$t }
    $install = @($types['PCL.ModModpack'].Methods | Where-Object { $_.Name -eq 'ModpackInstall' -and $_.Parameters.Count -eq 3 })[0]
    $instructions = @($install.Body.Instructions)
    $finally = @($install.Body.ExceptionHandlers | Where-Object HandlerType -eq 'Finally')
    if ($finally.Count -ne 2) { throw 'Unexpected finally handler count' }
    foreach ($handler in $finally) {
        if ($null -eq $handler.HandlerEnd) { throw 'Invalid finally end boundary' }
        $start = [Array]::IndexOf($instructions,$handler.HandlerStart)
        $end = [Array]::IndexOf($instructions,$handler.HandlerEnd)
        if ($start -lt 0 -or $end -le $start) { throw 'Invalid finally range' }
        for ($i=$start; $i -lt $end; $i++) {
            if ($instructions[$i].OpCode.Name -eq 'ret') { throw 'ret inside finally handler' }
        }
    }
    $handlers = @($instructions | Where-Object { $_.OpCode.Name -eq 'call' -and $_.Operand.Name -like 'InstallPack*' } | ForEach-Object { $_.Operand.Name.String } | Sort-Object -Unique)
    if ($handlers.Count -ne 7) { throw 'Missing pack format dispatch handlers' }
    $flag = @($types['PCL.ModSecret'].Fields | Where-Object Name -eq 'RestrictMcDownload')[0]
    if ($flag.Constant.Value -ne $false) { throw 'Restriction metadata remains enabled' }
    $loader = @($types['PCL.PageDownloadInstall'].Methods | Where-Object Name -eq 'LoaderInit')[0]
    if (@($loader.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldsfld' -and $_.Operand.Name -eq 'DlClientListLoader' }).Count -ne 1) { throw 'MC loader not restored' }
    Write-Host 'PASS: loader, seven pack format handlers, feature constant and finally return boundary. Static checks only.'
} finally { $module.Dispose() }
