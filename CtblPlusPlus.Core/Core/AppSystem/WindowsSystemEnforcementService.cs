using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.AppSystem;

public class WindowsSystemEnforcementService : ISystemEnforcementService
{
    private readonly string _lockdownFile;

    public WindowsSystemEnforcementService()
    {
        _lockdownFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CtblPlusPlus", "LockdownSignal.txt");
    }

    public void EnforceSettingsPolicy()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
            if (key != null)
            {
                string currentValue = (key.GetValue("SettingsPageVisibility", "") as string) ?? "";
                bool needsUpdate = false;
                string newValue = currentValue;

                if (!currentValue.Trim().StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(currentValue.Trim()))
                    {
                        newValue = "hide:dateandtime";
                        needsUpdate = true;
                    }
                    else if (!currentValue.Trim().StartsWith("show:", StringComparison.OrdinalIgnoreCase))
                    {
                        newValue = "hide:dateandtime;" + currentValue;
                        needsUpdate = true;
                    }
                }
                else
                {
                    if (!currentValue.ToLower().Contains("dateandtime"))
                    {
                        string sep = currentValue.Trim().EndsWith(";") ? "" : ";";
                        newValue = currentValue + sep + "dateandtime";
                        needsUpdate = true;
                    }
                }
                
                if (needsUpdate)
                {
                    key.SetValue("SettingsPageVisibility", newValue, RegistryValueKind.String);
                }
            }
        }
        catch { }
    }

    public void RemoveSettingsPolicy()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", true);
            if (key != null)
            {
                string? currentVal = key.GetValue("SettingsPageVisibility", "") as string;
                if (!string.IsNullOrEmpty(currentVal))
                {
                    string newVal = currentVal;
                    if (newVal.ToLower().Contains("dateandtime"))
                    {
                        string prefix = "";
                        string body = newVal;

                        if (newVal.Trim().StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
                        {
                            prefix = newVal.Substring(0, 5);
                            body = newVal.Substring(5);
                        }
                        else if (newVal.Trim().StartsWith("show:", StringComparison.OrdinalIgnoreCase))
                        {
                            prefix = newVal.Substring(0, 5);
                            body = newVal.Substring(5);
                        }

                        var parts = body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        var kept = new global::System.Collections.Generic.List<string>();
                        foreach(var p in parts)
                        {
                            if (!p.Trim().Equals("dateandtime", StringComparison.OrdinalIgnoreCase))
                            {
                                kept.Add(p);
                            }
                        }
                        
                        newVal = prefix + string.Join(";", kept);
                        key.SetValue("SettingsPageVisibility", newVal);
                    }
                }
            }
        }
        catch { }
    }

    public void CorrectSystemTime(DateTime utc)
    {
        SYSTEMTIME st = new SYSTEMTIME
        {
            wYear = (short)utc.Year,
            wMonth = (short)utc.Month,
            wDay = (short)utc.Day,
            wHour = (short)utc.Hour,
            wMinute = (short)utc.Minute,
            wSecond = (short)utc.Second,
            wMilliseconds = (short)utc.Millisecond
        };
        SetSystemTime(ref st);
    }

    public void InitiateLogout()
    {
        try 
        {
            Thread.Sleep(3000);
            ExitWindowsEx(0 | 4, 0);
        }
        catch { }
    }

    public void TriggerLockdown()
    {
        try
        {
            if (!File.Exists(_lockdownFile))
            {
                File.WriteAllText(_lockdownFile, "LOCKDOWN_ACTIVE " + DateTime.UtcNow.ToString("o"));
            }
        }
        catch {}
    }

    public bool IsLockdownActive() => File.Exists(_lockdownFile);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public short wYear;
        public short wMonth;
        public short wDayOfWeek;
        public short wDay;
        public short wHour;
        public short wMinute;
        public short wSecond;
        public short wMilliseconds;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetSystemTime(ref SYSTEMTIME st);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);
}


