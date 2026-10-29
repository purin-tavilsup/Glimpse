---
name: driving-hyperv-consoles
description: Use when an agent must see or type into a Hyper-V virtual machine's console from the Windows host without the mouse or keyboard focus — an OS installer, a login prompt, a VM whose network or SSH is down — or when Msvm_Keyboard.TypeText types nothing or garbage, or a thumbnail copy crashes with "Internal CLR error 0x80131506".
---

# Driving Hyper-V consoles

`scripts/hyperv-console.ps1` reads a VM's screen and types into it through Hyper-V's WMI provider. vmconnect, focus
and the mouse stay with the person. Run it from an elevated PowerShell 7 (a Hyper-V administrator;
`whoami /groups | findstr Hyper-V` shows it):

```powershell
pwsh <skill dir>/scripts/hyperv-console.ps1 -VMName <vm> shot <file.png>     # then Read the PNG
pwsh <skill dir>/scripts/hyperv-console.ps1 -VMName <vm> type 'ls -la /etc'  # printable ASCII, US layout
pwsh <skill dir>/scripts/hyperv-console.ps1 -VMName <vm> type -SecretFile <path>   # a password, never printed
pwsh <skill dir>/scripts/hyperv-console.ps1 -VMName <vm> key Enter           # names, letters, digits or VK codes
pwsh <skill dir>/scripts/hyperv-console.ps1 -VMName <vm> key Down -Repeat 3
pwsh <skill dir>/scripts/hyperv-console.ps1 -VMName <vm> combo Ctrl+Alt+F2
```

Each step is act, then `shot` and Read. Type a command, `shot` to check the echo, then `key Enter`; `type` never
presses Enter itself, not after `-SecretFile` either. The screen keeps drawing after Enter (a login, a slow command):
when a `shot` looks unfinished, take another rather than acting on it. A text UI (an installer) shows focus as a
highlight: `shot` after every Tab before pressing Enter.

## Traps

| Trap | Do this |
|---|---|
| `Msvm_Keyboard.TypeText` returns 0 but types nothing, or garbage, on a Linux console | Never use it; the script types key by key |
| The thumbnail buffer is a few bytes longer than width × height × 2; copying all of it kills the process | Use the script's `shot` (copies row by row) |
| An installer runs from its ISO: ejecting it early leaves `Bus error`, a black screen, SSH `Connection reset` | Eject only at "remove the installation medium, then press ENTER" |
| WMI calls block while the VM changes state (a hanging guest shutdown) | Run them in `Start-Job` with `Wait-Job -Timeout`; retry `Stop-VM -TurnOff` a minute later before touching `vmwp.exe` or `vmms` |

To build the same VM again, take a checkpoint after the install, or use autoinstall/cloud-init, instead of driving
the installer twice.
