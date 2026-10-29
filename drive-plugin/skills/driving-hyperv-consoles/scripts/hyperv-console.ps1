#requires -Version 7
<#
.SYNOPSIS
See and type into a Hyper-V VM's console from the host, without vmconnect, the mouse or keyboard focus.

.DESCRIPTION
Uses the Hyper-V WMI provider (root\virtualization\v2): GetVirtualSystemThumbnailImage for the screen and
Msvm_Keyboard for keys. Needs a Hyper-V administrator. Every key goes in as its own TypeKey with Shift held where needed:
Msvm_Keyboard.TypeText returns 0 yet drops or garbles text on a Linux console.

.EXAMPLE
pwsh hyperv-console.ps1 -VMName test-vm shot C:\temp\screen.png
pwsh hyperv-console.ps1 -VMName test-vm type 'sudo systemctl status ssh'
pwsh hyperv-console.ps1 -VMName test-vm key Enter
pwsh hyperv-console.ps1 -VMName test-vm key Down -Repeat 3
pwsh hyperv-console.ps1 -VMName test-vm combo Ctrl+Alt+F2
pwsh hyperv-console.ps1 -VMName test-vm type -SecretFile $env:USERPROFILE\vm-password.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $VMName,
    [Parameter(Mandatory, Position = 0)] [ValidateSet('shot', 'type', 'key', 'combo')] [string] $Action,
    # shot: the PNG path. type: the text. key: a key name or a virtual-key code. combo: keys joined by '+'.
    [Parameter(Position = 1)] [string] $Value,
    # type: read the text from this file instead (a password); it is never printed.
    [string] $SecretFile,
    [ValidateRange(1, 100)] [int] $Repeat = 1,
    [int] $Width = 1024,
    [int] $Height = 768
)
$ErrorActionPreference = 'Stop'
$ns = 'root\virtualization\v2'

$named = @{
    Enter = 13; Tab = 9; Esc = 27; Space = 32; Backspace = 8; Delete = 46; Home = 36; End = 35
    PageUp = 33; PageDown = 34; Left = 37; Up = 38; Right = 39; Down = 40
    Shift = 16; Ctrl = 17; Alt = 18
}
1..12 | ForEach-Object { $named["F$_"] = 111 + $_ }

# US layout. Characters on a key's shifted side map to that key with Shift held.
$plain = @{ ' ' = 32; '-' = 189; '=' = 187; '[' = 219; ']' = 221; '\' = 220; ';' = 186; "'" = 222; ',' = 188
    '.' = 190; '/' = 191; '`' = 192 }
$shifted = @{ '!' = 49; '@' = 50; '#' = 51; '$' = 52; '%' = 53; '^' = 54; '&' = 55; '*' = 56; '(' = 57; ')' = 48
    '_' = 189; '+' = 187; '{' = 219; '}' = 221; '|' = 220; ':' = 186; '"' = 222; '<' = 188; '>' = 190; '?' = 191
    '~' = 192 }

function Get-Vm {
    # Matched here, not in a WQL filter, so a name with a quote or backslash needs no escaping.
    $vm = @(Get-CimInstance -Namespace $ns -ClassName Msvm_ComputerSystem | Where-Object ElementName -eq $VMName)
    if ($vm.Count -eq 0) { throw "No VM named '$VMName' (run as a Hyper-V administrator)." }
    if ($vm.Count -gt 1) { throw "$($vm.Count) VMs are named '$VMName'; rename one first." }
    $vm[0]
}

function Get-Keyboard { Get-CimAssociatedInstance -InputObject (Get-Vm) -ResultClassName Msvm_Keyboard -Namespace $ns }

function Invoke-Key([object] $keyboard, [string] $method, [int] $code) {
    $result = Invoke-CimMethod -InputObject $keyboard -MethodName $method -Arguments @{ keyCode = [uint32]$code }
    if ($result.ReturnValue -ne 0) { throw "$method($code) returned $($result.ReturnValue)." }
}

function Resolve-Key([string] $name) {
    if ($name -match '^\d+$') { return [int]$name }
    if ($named.ContainsKey($name)) { return $named[$name] }
    if ($name.Length -eq 1 -and $name -match '[A-Za-z0-9]') { return [int][char]$name.ToUpperInvariant() }
    throw "Unknown key '$name'. Use a name ($($named.Keys -join ', ')), a letter or digit, or a virtual-key code."
}

function Resolve-Char([char] $c) {
    $s = [string]$c
    if ($s -cmatch '^[a-z0-9]$') { return @{ Code = [int][char]$s.ToUpperInvariant(); Shift = $false } }
    if ($s -cmatch '^[A-Z]$') { return @{ Code = [int]$c; Shift = $true } }
    if ($plain.ContainsKey($s)) { return @{ Code = $plain[$s]; Shift = $false } }
    if ($shifted.ContainsKey($s)) { return @{ Code = $shifted[$s]; Shift = $true } }
    throw "Cannot type character U+$(([int]$c).ToString('X4')): only printable ASCII on a US layout."
}

function Send-Text([string] $text) {
    $keys = foreach ($c in $text.ToCharArray()) { Resolve-Char $c }   # all checked before the first key goes in
    $keyboard = Get-Keyboard
    foreach ($k in $keys) {
        if (-not $k.Shift) { Invoke-Key $keyboard TypeKey $k.Code; continue }
        Invoke-Key $keyboard PressKey 16
        # A failed TypeKey must not leave Shift down in the guest: it would corrupt every later key.
        try { Invoke-Key $keyboard TypeKey $k.Code } finally { Invoke-Key $keyboard ReleaseKey 16 }
    }
}

function Save-Screen([string] $path) {
    Add-Type -AssemblyName System.Drawing
    $vm = Get-Vm
    $service = Get-CimInstance -Namespace $ns -ClassName Msvm_VirtualSystemManagementService
    $settings = Get-CimAssociatedInstance -InputObject $vm -ResultClassName Msvm_VirtualSystemSettingData -Namespace $ns |
        Where-Object VirtualSystemType -eq 'Microsoft:Hyper-V:System:Realized'
    $result = Invoke-CimMethod -InputObject $service -MethodName GetVirtualSystemThumbnailImage -Arguments @{
        TargetSystem = $settings; WidthPixels = [uint16]$Width; HeightPixels = [uint16]$Height }
    [byte[]] $pixels = $result.ImageData
    $rowBytes = $Width * 2
    # The buffer is RGB565 rows plus a few trailing bytes; copying all of it overruns the bitmap and kills the process.
    if ($null -eq $pixels -or $pixels.Length -lt $rowBytes * $Height) {
        throw "No screen image (ReturnValue $($result.ReturnValue)); is the VM running?"
    }
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format16bppRgb565)
    try {
        $data = $bitmap.LockBits([System.Drawing.Rectangle]::new(0, 0, $Width, $Height), 'WriteOnly', $bitmap.PixelFormat)
        try {
            for ($y = 0; $y -lt $Height; $y++) {
                [System.Runtime.InteropServices.Marshal]::Copy($pixels, $y * $rowBytes, [IntPtr]($data.Scan0.ToInt64() + $y * $data.Stride), $rowBytes)
            }
        } finally { $bitmap.UnlockBits($data) }
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    "saved $path"
}

switch ($Action) {
    'shot' {
        if (-not $Value) { throw 'shot needs a PNG path.' }
        Save-Screen ([System.IO.Path]::GetFullPath($Value))
    }
    'type' {
        $text = if ($SecretFile) { (Get-Content -LiteralPath $SecretFile -Raw).TrimEnd("`r", "`n") } else { $Value }
        if (-not $text) { throw 'type needs text, or -SecretFile.' }
        Send-Text $text
        if ($SecretFile) { "typed the $($text.Length) characters of $SecretFile" } else { "typed $($text.Length) characters" }
    }
    'key' {
        $code = Resolve-Key $Value
        $keyboard = Get-Keyboard
        for ($i = 0; $i -lt $Repeat; $i++) { Invoke-Key $keyboard TypeKey $code; Start-Sleep -Milliseconds 100 }
        "pressed $Value x$Repeat"
    }
    'combo' {
        $codes = @($Value -split '\+' | ForEach-Object { Resolve-Key $_.Trim() })
        if ($codes.Count -lt 2) { throw "combo needs two or more keys, like Ctrl+Alt+F2." }
        $keyboard = Get-Keyboard
        $held = $codes[0..($codes.Count - 2)]
        try {
            foreach ($code in $held) { Invoke-Key $keyboard PressKey $code }
            Invoke-Key $keyboard TypeKey $codes[-1]
        } finally {
            [array]::Reverse($held)
            foreach ($code in $held) { Invoke-Key $keyboard ReleaseKey $code }
        }
        "pressed $Value"
    }
}
