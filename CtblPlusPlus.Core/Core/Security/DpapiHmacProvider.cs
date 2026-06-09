using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CtblPlusPlus.Core.Interfaces.Security;

namespace CtblPlusPlus.Core.Security;

public class DpapiHmacProvider : IHmacProvider
{
    private readonly string _keyPath;
    private byte[] _hmacKey = Array.Empty<byte>();

    public DpapiHmacProvider()
    {
        string dirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus");
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
        _keyPath = Path.Combine(dirPath, "system.key");
        InitializeCryptoKey();
    }

    private void InitializeCryptoKey()
    {
        // Phase 2 fix: inter-process mutex prevents the concurrent-write race condition
        // where all three service processes start simultaneously on boot and each tries
        // to create system.key at the same moment. Only one process can enter the
        // create-if-absent path at a time; all others wait, then find the file already
        // exists and simply read it.
        using var keyInitMutex = new Mutex(false, @"Global\CtblPlusPlus_KeyInit_Mutex");
        bool mutexAcquired = false;
        try
        {
            mutexAcquired = keyInitMutex.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            // Another process crashed while holding the mutex. We now own it.
            mutexAcquired = true;
        }

        try
        {
            // Re-check inside the mutex: the file may have been created while we waited.
            if (File.Exists(_keyPath))
            {
                try
                {
                    byte[] encryptedKey = File.ReadAllBytes(_keyPath);
                    _hmacKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.LocalMachine);
                    return;
                }
                catch (Exception ex)
                {
                    // Phase 1 fix retained: replaced Environment.Exit(1) with a proper exception.
                    global::System.Diagnostics.EventLog.WriteEntry("Application",
                        $"Fatal Error: DPAPI failed to unprotect system.key. " +
                        $"Delete %ProgramData%\\CtblPlusPlus\\system.key and restart the service. " +
                        $"Error: {ex.Message}",
                        global::System.Diagnostics.EventLogEntryType.Error);
                    throw new InvalidOperationException(
                        "DPAPI could not unprotect system.key. See Application event log for details.", ex);
                }
            }

            // File does not exist — generate a new 256-bit key and persist it.
            // This path is serialized by the mutex so only one process ever writes.
            // In normal Phase 02+ operation the bat writes system.key before any
            // service starts, so this branch should never execute on a clean install.
            _hmacKey = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(_hmacKey);
            }

            byte[] encryptedNewKey = ProtectedData.Protect(_hmacKey, null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(_keyPath, encryptedNewKey);
        }
        finally
        {
            if (mutexAcquired) keyInitMutex.ReleaseMutex();
        }
    }

    public string ComputeHmac(string payload)
    {
        if (_hmacKey == null || _hmacKey.Length == 0) return "";

        using (var hmac = new HMACSHA256(_hmacKey))
        {
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }
    }
}
