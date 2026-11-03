<#
.SYNOPSIS
  Proceso de PRUEBA del CI: pone un icono en la bandeja de Windows con la API nativa (WinForms NotifyIcon → Shell_NotifyIcon) y lo quita al terminar.
  Solo lo usa tools/ci/Test-TrayCapture.ps1. No forma parte de WinMac.
#>
param([string]$Text = 'WinMac prueba', [ValidateSet('Information', 'Warning', 'Error', 'Question', 'Shield', 'Application')][string]$Kind = 'Information', [int]$Seconds = 90)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$ni = New-Object System.Windows.Forms.NotifyIcon
$ni.Icon = [System.Drawing.SystemIcons]::$Kind
$ni.Text = $Text
$ni.Visible = $true
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = $Seconds * 1000
$timer.add_Tick({ $ni.Visible = $false; $ni.Dispose(); [System.Windows.Forms.Application]::Exit() })
$timer.Start()
[System.Windows.Forms.Application]::Run()
