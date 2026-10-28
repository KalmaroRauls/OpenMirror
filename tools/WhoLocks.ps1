<#
  OpenMirror - build helper.
  Reports what is stopping build.ps1 from replacing a file ("used by another process" /
  "access denied"), using the same Restart Manager API that installers use.

      .\tools\WhoLocks.ps1 -Path ..\bin\OpenMirror.exe
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $Path)

$ErrorActionPreference = 'Stop'
$full = (Resolve-Path -LiteralPath $Path).Path
Write-Host ('checking: ' + $full) -ForegroundColor Cyan

# 1. Which access modes does the file currently allow? A browser-style read handle is
#    harmless; a write or delete refusal is what breaks the build.
foreach ($access in 'Read', 'Write', 'ReadWrite') {
    try {
        $stream = [System.IO.File]::Open($full, [System.IO.FileMode]::Open, [System.IO.FileAccess]$access, [System.IO.FileShare]::None)
        $stream.Close()
        Write-Host ("  open for $access  : allowed") -ForegroundColor Green
    }
    catch {
        Write-Host ("  open for $access  : BLOCKED - " + $_.Exception.Message) -ForegroundColor Yellow
    }
}

# 2. Ask the Restart Manager who holds it.
if (-not ('OpenMirrorDiagnostics.WhoLocks' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OpenMirrorDiagnostics
{
    public static class WhoLocks
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint session, int flags, string key);
        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint session);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint session, uint fileCount, string[] files,
            uint appCount, RM_UNIQUE_PROCESS[] apps, uint serviceCount, string[] services);
        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint session, out uint needed, ref uint count,
            [In, Out] RM_PROCESS_INFO[] infos, ref uint reasons);

        public static string[] Who(string path)
        {
            List<string> found = new List<string>();
            uint session;
            int result = RmStartSession(out session, 0, Guid.NewGuid().ToString("N"));
            if (result != 0) { return new string[] { "Restart Manager unavailable (code " + result + ")" }; }
            try
            {
                string[] files = new string[] { path };
                result = RmRegisterResources(session, 1, files, 0, null, 0, null);
                if (result != 0) { return new string[] { "RmRegisterResources failed (code " + result + ")" }; }

                uint needed = 0, count = 0, reasons = 0;
                result = RmGetList(session, out needed, ref count, null, ref reasons);
                if (needed > 0)
                {
                    RM_PROCESS_INFO[] infos = new RM_PROCESS_INFO[needed];
                    count = needed;
                    result = RmGetList(session, out needed, ref count, infos, ref reasons);
                    if (result == 0)
                    {
                        for (uint i = 0; i < count; i++)
                        {
                            string line = "pid " + infos[i].Process.dwProcessId + "  " + infos[i].strAppName;
                            if (!string.IsNullOrEmpty(infos[i].strServiceShortName))
                            {
                                line += "  (service " + infos[i].strServiceShortName + ")";
                            }
                            found.Add(line);
                        }
                    }
                    else { found.Add("second RmGetList failed (code " + result + ")"); }
                }
            }
            finally { RmEndSession(session); }

            if (found.Count == 0)
            {
                found.Add("no process reported - usually a scanner holding a handle mid-scan, " +
                          "or a process we are not allowed to query; try again in a few seconds");
            }
            return found.ToArray();
        }
    }
}
'@
}

Write-Host ''
Write-Host 'holders reported by the Restart Manager:'
foreach ($holder in [OpenMirrorDiagnostics.WhoLocks]::Who($full)) {
    Write-Host ('  ' + $holder) -ForegroundColor Yellow
}
