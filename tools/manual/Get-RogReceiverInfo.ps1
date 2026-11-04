<#
.SYNOPSIS
  Diagnóstico de SOLO LECTURA de los receptores y dispositivos ASUS (VID 0x0B05) conectados a este Windows.

.DESCRIPTION
  Sirve para la investigación de compatibilidad de batería por 2,4 GHz (docs/45). NO modifica nada, NO envía comandos ni informes HID, NO abre los dispositivos con permiso de lectura
  o escritura, NO instala ni cambia controladores, NO toca Armoury Crate ni sus servicios, y NO necesita administrador.

  QUÉ RECOPILA (y solo esto):
    1. Dispositivos Plug and Play presentes con VID 0x0B05: nombre, clase, estado, servicio/controlador asociado, proveedor y versión del controlador, IDs de hardware (VID/PID/MI/COL).
       El tramo final del identificador de instancia (que puede contener un número de serie o de puerto) se SUSTITUYE por «<id>».
    2. Con -IncludeHidCaps (opcional, apagado por defecto): para cada interfaz HID de VID 0x0B05, sus capacidades públicas que Windows ya tiene en caché (Usage Page, Usage, longitudes
       de informe de entrada/salida/característica). Se abre la interfaz con permiso de acceso 0 (ni lectura ni escritura) y solo se llama a HidD_GetAttributes, HidD_GetPreparsedData y
       HidP_GetCaps. No se pide ni el número de serie ni la cadena de producto, y no se envía ningún informe.
    3. Servicios de Windows y nombres de proceso (sin ruta) cuyo nombre contenga ASUS, Armoury, ROG o Aura: nombre, estado y tipo de inicio.

  QUÉ NO RECOPILA: números de serie, nombre del equipo o del usuario, rutas del disco, claves, tokens, direcciones de red, contenido de informes HID.

  RESULTADO: dos archivos (texto y JSON) en -OutDir. Revísalos antes de compartirlos; lo que contienen está descrito arriba.

.PARAMETER OutDir
  Carpeta de salida (por defecto, la carpeta actual).
.PARAMETER IncludeHidCaps
  Añade las capacidades HID en caché (Usage Page, longitudes de informe). Seguro: no envía nada al dispositivo.
#>
[CmdletBinding()]
param(
  [string]$OutDir = (Get-Location).Path,
  [switch]$IncludeHidCaps
)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Continue'

function Mask-Instance([string]$id) {
  if ([string]::IsNullOrEmpty($id)) { return $id }
  $i = $id.LastIndexOf('\')
  if ($i -lt 0) { return $id }
  return ($id.Substring(0, $i + 1) + '<id>')
}

function Get-Prop($dev, [string]$key) {
  try { return (Get-PnpDeviceProperty -InstanceId $dev.InstanceId -KeyName $key -ErrorAction Stop).Data } catch { return $null }
}

$report = [ordered]@{
  tool        = 'Get-RogReceiverInfo.ps1 (solo lectura)'
  generatedAt = (Get-Date).ToUniversalTime().ToString('s') + 'Z'
  windows     = [System.Environment]::OSVersion.VersionString
  pnp         = @()
  hid         = @()
  services    = @()
  processes   = @()
}

Write-Host 'Leyendo dispositivos PnP con VID 0x0B05 (solo lectura)...'
$devs = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -match 'VID_0B05' -or $_.InstanceId -match 'VID&0\d?0B05' })
foreach ($d in $devs) {
  $hw = Get-Prop $d 'DEVPKEY_Device_HardwareIds'
  $report.pnp += [ordered]@{
    friendlyName   = $d.FriendlyName
    class          = $d.Class
    status         = [string]$d.Status
    instance       = Mask-Instance $d.InstanceId
    hardwareIds    = @($hw)
    service        = Get-Prop $d 'DEVPKEY_Device_Service'
    driverProvider = Get-Prop $d 'DEVPKEY_Device_DriverProvider'
    driverVersion  = Get-Prop $d 'DEVPKEY_Device_DriverVersion'
    parent         = Mask-Instance (Get-Prop $d 'DEVPKEY_Device_Parent')
  }
}

if ($IncludeHidCaps) {
  Write-Host 'Leyendo capacidades HID en caché (acceso 0: sin lectura ni escritura, sin enviar informes)...'
  Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class RogHidProbe {
  [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
  [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
  [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS { public ushort Usage; public ushort UsagePage; public ushort InputReportByteLength; public ushort OutputReportByteLength; public ushort FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved; public ushort NumberLinkCollectionNodes; public ushort NumberInputButtonCaps; public ushort NumberInputValueCaps; public ushort NumberInputDataIndices;
    public ushort NumberOutputButtonCaps; public ushort NumberOutputValueCaps; public ushort NumberOutputDataIndices; public ushort NumberFeatureButtonCaps; public ushort NumberFeatureValueCaps; public ushort NumberFeatureDataIndices; }
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
  [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
  [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, ref HIDP_CAPS caps);
  [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int flags);
  [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr i, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
  [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
  [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

  public static List<string[]> Run() {
    var res = new List<string[]>(); Guid g; HidD_GetHidGuid(out g);
    IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x2 | 0x10);
    if (set == new IntPtr(-1)) return res;
    try {
      for (int i = 0; i < 512; i++) {
        var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
        if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
        int need; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out need, IntPtr.Zero);
        IntPtr buf = Marshal.AllocHGlobal(need);
        try {
          Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
          if (!SetupDiGetDeviceInterfaceDetail(set, ref d, buf, need, out need, IntPtr.Zero)) continue;
          string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
          if (path.IndexOf("vid_0b05", StringComparison.OrdinalIgnoreCase) < 0) continue;   // solo ASUS
          // acceso 0 = ni lectura ni escritura; compartido con todos
          using (var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
            if (h.IsInvalid) { res.Add(new[] { Mask(path), "no se pudo abrir (acceso 0)", "", "", "", "", "" }); continue; }
            var a = new HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)); HidD_GetAttributes(h, ref a);
            IntPtr pp; var caps = new HIDP_CAPS(); caps.Reserved = new ushort[17];
            if (HidD_GetPreparsedData(h, out pp)) { try { HidP_GetCaps(pp, ref caps); } finally { HidD_FreePreparsedData(pp); } }
            res.Add(new[] { Mask(path), a.VendorID.ToString("X4"), a.ProductID.ToString("X4"), caps.UsagePage.ToString("X4"), caps.Usage.ToString("X4"),
              caps.InputReportByteLength + "/" + caps.OutputReportByteLength + "/" + caps.FeatureReportByteLength, a.VersionNumber.ToString("X4") });
          }
        } finally { Marshal.FreeHGlobal(buf); }
      }
    } finally { SetupDiDestroyDeviceInfoList(set); }
    return res;
  }

  // deja solo vid/pid/mi/col de la ruta; el resto (identificador de instancia y GUID de contenedor) se descarta
  static string Mask(string p) {
    string l = p.ToLowerInvariant(); int s = l.IndexOf("vid_"); if (s < 0) return "<ruta>";
    int e = l.IndexOf('#', s); string core = e > s ? l.Substring(s, e - s) : l.Substring(s);
    return core.ToUpperInvariant();
  }
}
'@
  foreach ($r in [RogHidProbe]::Run()) {
    $report.hid += [ordered]@{ interface = $r[0]; vid = $r[1]; pid = $r[2]; usagePage = $r[3]; usage = $r[4]; reportBytesInOutFeature = $r[5]; version = $r[6] }
  }
}

Write-Host 'Leyendo servicios y procesos del fabricante (solo nombres y estado)...'
foreach ($s in @(Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ASUS|Armoury|ROG|Aura' -or $_.DisplayName -match 'ASUS|Armoury|ROG|Aura' })) {
  $report.services += [ordered]@{ name = $s.Name; displayName = $s.DisplayName; status = [string]$s.Status; startType = [string]$s.StartType }
}
foreach ($p in @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'ASUS|Armoury|ROG|Aura' } | Select-Object -ExpandProperty ProcessName -Unique)) {
  $report.processes += $p
}

$null = New-Item -ItemType Directory -Force -Path $OutDir
$json = Join-Path $OutDir 'rog-diagnostico.json'; $txt = Join-Path $OutDir 'rog-diagnostico.txt'
($report | ConvertTo-Json -Depth 6) | Set-Content -Path $json -Encoding UTF8

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('Diagnóstico de solo lectura ASUS (VID 0B05) — ' + $report.generatedAt + ' — ' + $report.windows)
$lines.Add('')
$lines.Add('== Dispositivos PnP presentes ==')
foreach ($p in $report.pnp) {
  $lines.Add(('{0} | clase {1} | {2} | servicio {3} | controlador {4} {5}' -f $p.friendlyName, $p.class, $p.status, $p.service, $p.driverProvider, $p.driverVersion))
  $lines.Add('    ids: ' + (($p.hardwareIds | Where-Object { $_ }) -join ' ; '))
  $lines.Add('    instancia: ' + $p.instance + ' | padre: ' + $p.parent)
}
if ($IncludeHidCaps) {
  $lines.Add(''); $lines.Add('== Interfaces HID (capacidades en caché; acceso 0) ==')
  foreach ($h in $report.hid) { $lines.Add(('{0} | VID {1} PID {2} | UsagePage {3} Usage {4} | bytes in/out/feature {5} | ver {6}' -f $h.interface, $h.vid, $h.pid, $h.usagePage, $h.usage, $h.reportBytesInOutFeature, $h.version)) }
}
$lines.Add(''); $lines.Add('== Servicios del fabricante ==')
foreach ($s in $report.services) { $lines.Add(('{0} ({1}) | {2} | inicio {3}' -f $s.name, $s.displayName, $s.status, $s.startType)) }
$lines.Add(''); $lines.Add('== Procesos del fabricante (solo nombre) ==')
foreach ($p in $report.processes) { $lines.Add($p) }
$lines | Set-Content -Path $txt -Encoding UTF8

Write-Host ''
Write-Host 'Listo. Revisa estos dos archivos antes de compartirlos:'
Write-Host ('  ' + $txt); Write-Host ('  ' + $json)
Write-Host 'No se envió nada a ningún dispositivo ni se cambió ninguna configuración.'
