# Is a TEST Aviary making any sound right now? (Alpha Build 5 M6 stage 2 - "Mute tabs in the background".)
# Reads Windows' own audio meter (Core Audio: IAudioMeterInformation) for every audio session on the
# default output device that belongs to the test instance or one of its engine's processes - the same
# meter the Volume Mixer's bars show. Samples for -Ms and prints the loudest peak seen (0 = silence).
# No input is sent and nothing is played; it only listens to the meter.
#
#   audio-peak.ps1 -ProcessId <test Aviary pid> [-Ms 800]
param([int]$ProcessId, [int]$Ms = 800)

if (-not ('AudioPeek.Meter' -as [type])) {
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace AudioPeek {
  [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }
  [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDeviceEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
  }
  [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDevice {
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
  }
  [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioSessionManager2 {
    [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
  }
  [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioSessionEnumerator {
    [PreserveSig] int GetCount(out int n);
    [PreserveSig] int GetSession(int i, [MarshalAs(UnmanagedType.IUnknown)] out object s);
  }
  [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioSessionControl2 {
    [PreserveSig] int GetState(out int s);
    [PreserveSig] int GetDisplayName(out IntPtr n);
    [PreserveSig] int SetDisplayName(IntPtr n, IntPtr g);
    [PreserveSig] int GetIconPath(out IntPtr p);
    [PreserveSig] int SetIconPath(IntPtr p, IntPtr g);
    [PreserveSig] int GetGroupingParam(out Guid g);
    [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr c);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint pid);
  }
  [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioMeterInformation { [PreserveSig] int GetPeakValue(out float peak); }

  public static class Meter {
    // The loudest peak over the sessions of these processes, sampled every 20 ms for `ms`; -1 = no such session.
    public static float Loudest(int[] pids, int ms) {
      var wanted = new HashSet<uint>(); foreach (int p in pids) wanted.Add((uint)p);
      var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
      IMMDevice dev;
      if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return -2;
      Guid iid = typeof(IAudioSessionManager2).GUID;
      object o;
      if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return -2;
      var mgr = (IAudioSessionManager2)o;
      float loudest = -1;
      var until = DateTime.UtcNow.AddMilliseconds(ms);
      do {
        IAudioSessionEnumerator e;
        mgr.GetSessionEnumerator(out e);
        int n; e.GetCount(out n);
        for (int i = 0; i < n; i++) {
          object s; e.GetSession(i, out s);
          uint pid; ((IAudioSessionControl2)s).GetProcessId(out pid);
          if (!wanted.Contains(pid)) continue;
          float peak = 0;
          var meter = s as IAudioMeterInformation;
          if (meter != null) meter.GetPeakValue(out peak);
          if (peak > loudest) loudest = peak;
        }
        System.Threading.Thread.Sleep(20);
      } while (DateTime.UtcNow < until);
      return loudest;
    }
  }
}
'@
}

# The test Aviary and every process under it (its engine, and the engine's audio service).
$all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId
$family = New-Object System.Collections.Generic.List[int]; $family.Add($ProcessId)
for ($grew = $true; $grew; ) {
    $grew = $false
    foreach ($p in $all) { if ($family.Contains([int]$p.ParentProcessId) -and -not $family.Contains([int]$p.ProcessId)) { $family.Add([int]$p.ProcessId); $grew = $true } }
}
$peak = [AudioPeek.Meter]::Loudest($family.ToArray(), $Ms)
[Math]::Round($peak, 4)
