# Drives the PasteFix tray menu from a script: open it, read it, invoke an item.
# Test tooling only — the product sends no input.
#
# How it works: NotifyIcon responds to its private callback message
# (WM_USER + 0x800) with wParam = icon id and lParam = WM_RBUTTONUP. Posting that
# to the icon's window opens the ContextMenuStrip, which is itself a WinForms
# top-level window owned by the process. The managed type name ContextMenuStrip
# is NOT the Win32 class name (it is WindowsForms10.Window.20808.app.*), so the
# menu is identified by containing MenuItem elements, not by class name.
#
# The product is not involved: no SendInput, no keyboard hook, no mouse move.

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

Add-Type -Namespace Win32 -Name Tray -MemberDefinition @'
[DllImport("user32.dll", SetLastError=true)] public static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
'@

$script:WmTrayMouse = 0x0800
$script:WmRButtonUp = 0x0205
$script:WmKeyDown = 0x0100
$script:WmKeyUp = 0x0101
$script:VkEscape = 0x1B

function Get-PasteFixProcess {
    $proc = Get-Process PasteFix -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) { throw 'PasteFix 沒有在執行' }
    return $proc
}

function Get-PasteFixWindows {
    param([int]$ProcessId)

    $found = [System.Collections.Generic.List[object]]::new()
    $callback = [Win32.Tray+EnumProc] {
        param($hWnd, $lParam)
        $owner = 0
        [void][Win32.Tray]::GetWindowThreadProcessId($hWnd, [ref]$owner)
        if ($owner -eq $ProcessId) {
            $name = [System.Text.StringBuilder]::new(256)
            [void][Win32.Tray]::GetClassName($hWnd, $name, 256)
            $found.Add([pscustomobject]@{
                Handle    = $hWnd
                ClassName = $name.ToString()
                Visible   = [Win32.Tray]::IsWindowVisible($hWnd)
            })
        }
        return $true
    }
    [void][Win32.Tray]::EnumWindows($callback, [IntPtr]::Zero)
    return $found
}

# The open context menu: an onscreen top-level window of this process that
# contains MenuItem elements.
function Get-OpenMenu {
    param([int]$ProcessId)

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId)

    foreach ($window in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond)) {
        if ($window.Current.IsOffscreen) { continue }
        $itemCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::MenuItem)
        if ($window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)) {
            return $window
        }
    }
    return $null
}

function Get-MenuItems {
    param([Parameter(Mandatory)]$Menu)

    $all = $Menu.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
        $type = $element.Current.ControlType
        if ($type -ne [System.Windows.Automation.ControlType]::MenuItem) { continue }

        $patternObj = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$patternObj)) {
            $checked = $patternObj.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
        }
        else {
            # Items without a checkmark have no Toggle pattern at all.
            $checked = $false
        }

        [pscustomobject]@{
            Name    = $element.Current.Name
            Enabled = $element.Current.IsEnabled
            Checked = $checked
            Element = $element
        }
    }
}

function Close-TrayMenu {
    param([Parameter(Mandatory)]$Menu)

    $handle = [IntPtr]$Menu.Current.NativeWindowHandle
    [void][Win32.Tray]::PostMessage($handle, [uint32]$script:WmKeyDown, [IntPtr]$script:VkEscape, [IntPtr]::Zero)
    [void][Win32.Tray]::PostMessage($handle, [uint32]$script:WmKeyUp, [IntPtr]$script:VkEscape, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 300
}

function Open-TrayMenu {
    param([int]$TimeoutSeconds = 8)

    $proc = Get-PasteFixProcess
    $existing = Get-OpenMenu -ProcessId $proc.Id
    if ($existing) { return $existing }

    # The NotifyIcon's window is one of several WinForms windows the process
    # owns, and the callback message is ignored by the others. Every window gets
    # a short turn rather than the whole timeout, so a wrong guess does not spend
    # the budget for the right one.
    $iconWindows = @(Get-PasteFixWindows -ProcessId $proc.Id |
        Where-Object { $_.ClassName -like 'WindowsForms10.Window.*' })

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($window in $iconWindows) {
            [void][Win32.Tray]::PostMessage($window.Handle, [uint32]$script:WmTrayMouse, [IntPtr]1, [IntPtr]$script:WmRButtonUp)

            $turnEnds = (Get-Date).AddMilliseconds(1200)
            while ((Get-Date) -lt $turnEnds -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 150
                $menu = Get-OpenMenu -ProcessId $proc.Id
                if ($menu) { return $menu }
            }
        }
    }

    throw "tray 選單沒有出現（process $($proc.Id)，視窗：$(($iconWindows | ForEach-Object { $_.ClassName }) -join ', ')）"
}

function Get-TrayMenuItems {
    $menu = Open-TrayMenu
    try {
        return Get-MenuItems -Menu $menu
    }
    finally {
        Close-TrayMenu -Menu $menu
    }
}

function Invoke-TrayMenuItem {
    param(
        [Parameter(Mandatory)][string]$ItemName,
        [int]$TimeoutSeconds = 8
    )

    $menu = Open-TrayMenu -TimeoutSeconds $TimeoutSeconds
    try {
        $match = Get-MenuItems -Menu $menu | Where-Object { $_.Name -eq $ItemName } | Select-Object -First 1
        if (-not $match) {
            $available = (Get-MenuItems -Menu $menu | ForEach-Object { $_.Name }) -join ' / '
            throw "tray 選單裡找不到『$ItemName』，現有項目：$available"
        }
        if (-not $match.Enabled) { throw "『$ItemName』目前是停用的" }

        $invoke = $match.Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        return $true
    }
    finally {
        # Invoking usually closes the menu; closing again is harmless.
        Close-TrayMenu -Menu $menu
    }
}
