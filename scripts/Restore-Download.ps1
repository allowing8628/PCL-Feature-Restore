param([Parameter(Mandatory)][string]$WorkRoot, [Parameter(Mandatory)][string]$DnlibPath)
$ErrorActionPreference = 'Stop'
$root = $WorkRoot
$inputPath = Join-Path $root 'official\Plain Craft Launcher 2.exe'
$outputPath = Join-Path $root 'patched\Plain Craft Launcher 2.exe'
Add-Type -Path $DnlibPath
$module = [dnlib.DotNet.ModuleDefMD]::Load($inputPath)
try {
    if ($module.Assembly.Version.ToString() -ne '2.99.0.0') { throw 'Unexpected input version' }
    if ($module.IsStrongNameSigned) { throw 'Unexpected strong-name signature' }
    $type = @($module.GetTypes() | Where-Object FullName -eq 'PCL.PageDownloadInstall')
    $method = @($type.Methods | Where-Object Name -eq 'LoaderInit')
    if ($method.Count -ne 1 -or $method[0].Body.ExceptionHandlers.Count -ne 0) { throw 'Unexpected target method' }
    $method = $method[0]
    $instructions = @($method.Body.Instructions)
    $start = @($instructions | Where-Object {$_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq '关闭下载'})
    $end = @($instructions | Where-Object {$_.OpCode.Name -eq 'newobj' -and $_.Operand.FullName -like '*LoaderTask*::.ctor(System.String,System.Action*'})
    $field = @(($module.GetTypes() | Where-Object FullName -eq 'PCL.ModDownload').Fields | Where-Object Name -eq 'DlClientListLoader')
    if ($start.Count -ne 1 -or $end.Count -ne 1 -or $field.Count -ne 1) { throw 'Unexpected loader pattern' }
    $startOffset = [int]$start[0].Offset
    $endOffset = [int]($end[0].Offset + $end[0].GetSize())
    if ($endOffset -le $startOffset + 5) { throw 'Invalid patch range' }
    foreach ($ins in $instructions) {
        if ($ins.Offset -ge $startOffset -and $ins.Offset -lt $endOffset) { continue }
        if ($ins.Operand -is [dnlib.DotNet.Emit.Instruction] -and $ins.Operand.Offset -ge $startOffset -and $ins.Operand.Offset -lt $endOffset) { throw 'External branch enters patch range' }
        if ($ins.Operand -is [System.Collections.Generic.IList[dnlib.DotNet.Emit.Instruction]]) { throw 'Unexpected switch' }
    }
    $bytes = [IO.File]::ReadAllBytes($inputPath)
    $bodyOffset = [int]$module.Metadata.PEImage.ToFileOffset($method.RVA)
    if (($bytes[$bodyOffset] -band 3) -eq 2) { $headerSize=1 }
    elseif (($bytes[$bodyOffset] -band 3) -eq 3) { $headerSize=([BitConverter]::ToUInt16($bytes,$bodyOffset) -shr 12)*4 }
    else { throw 'Unknown method header' }
    $patchOffset = $bodyOffset + $headerSize + $startOffset
    $before = [Convert]::ToHexString($bytes[$patchOffset..($bodyOffset+$headerSize+$endOffset-1)])
    for ($i=$startOffset; $i -lt $endOffset; $i++) { $bytes[$bodyOffset+$headerSize+$i]=0 }
    $bytes[$patchOffset]=0x7E # ldsfld: reuse the actual version-list loader
    [BitConverter]::GetBytes([uint32]$field[0].MDToken.Raw).CopyTo($bytes,$patchOffset+1)
    New-Item -ItemType Directory -Path (Split-Path $outputPath) -Force | Out-Null
    [IO.File]::WriteAllBytes($outputPath,$bytes)
    $method.Body.Instructions | ForEach-Object ToString | Set-Content -LiteralPath "$root\LoaderInit-before.txt" -Encoding UTF8
    $record = [ordered]@{version='2.99.0.0';method=$method.FullName;fileOffset=$patchOffset;length=$endOffset-$startOffset;beforeHex=$before;afterHex=[Convert]::ToHexString($bytes[$patchOffset..($bodyOffset+$headerSize+$endOffset-1)]);originalSha256=(Get-FileHash -LiteralPath $inputPath).Hash;patchedSha256=(Get-FileHash -LiteralPath $outputPath).Hash;targetExecuted=$false}
} finally { $module.Dispose() }
$check = [dnlib.DotNet.ModuleDefMD]::Load($outputPath)
try {
    $after = ($check.GetTypes() | Where-Object FullName -eq 'PCL.PageDownloadInstall').Methods | Where-Object Name -eq 'LoaderInit'
    $after.Body.Instructions | ForEach-Object ToString | Set-Content -LiteralPath "$root\LoaderInit-after.txt" -Encoding UTF8
    if (@($after.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldsfld' -and $_.Operand.FullName -like '*PCL.ModDownload::DlClientListLoader'}).Count -ne 1) { throw 'Loader not restored' }
    if (@($after.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq '关闭下载'}).Count) { throw 'Disabled loader remains active' }
    $original=[IO.File]::ReadAllBytes($inputPath)
    $modified=[IO.File]::ReadAllBytes($outputPath)
    if ($original.Length -ne $modified.Length) { throw 'File size changed' }
    $changed=0
    for($i=0;$i -lt $original.Length;$i++) {
        if ($original[$i] -ne $modified[$i]) {
            if ($i -lt $record.fileOffset -or $i -ge $record.fileOffset+$record.length) { throw 'Unexpected byte change' }
            $changed++
        }
    }
    $record.changedBytes=$changed
    $record.staticValidation='PASS: valid IL parsed; real loader referenced; changes confined to one method byte range'
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$root\patch-record.json" -Encoding UTF8
    $record | ConvertTo-Json -Depth 5
} finally { $check.Dispose() }

