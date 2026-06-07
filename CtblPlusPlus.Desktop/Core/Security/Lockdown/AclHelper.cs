using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// In-process NTFS ACL enforcement. Replaces icacls.exe child process calls
/// with direct System.Security.AccessControl API usage.
/// Phase 07: eliminates session-0 child-process overhead and LTSC compatibility issues.
/// </summary>
public static class AclHelper
{
    // Built-in Administrators: S-1-5-32-544
    private static readonly SecurityIdentifier AdminSid =
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

    // Local System: S-1-5-18
    private static readonly SecurityIdentifier SystemSid =
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

    /// <summary>
    /// Deny Administrators write/delete/ownership on a directory.
    /// Equivalent to: icacls "{path}" /deny *S-1-5-32-544:(DE,WD,AD,WA,WEA,DC,WRITE_DAC,WRITE_OWNER)
    /// </summary>
    public static void DenyAdminWrite(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        var acl = dirInfo.GetAccessControl();

        var denyRule = new FileSystemAccessRule(
            AdminSid,
            FileSystemRights.Delete |
            FileSystemRights.Write |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        acl.AddAccessRule(denyRule);
        dirInfo.SetAccessControl(acl);
    }

    /// <summary>
    /// Deny Administrators delete only (preserves write access for other services).
    /// Equivalent to: icacls "{path}" /deny *S-1-5-32-544:(DE,DC)
    /// </summary>
    public static void DenyAdminDelete(string directoryPath)
    {
        var dirInfo = new DirectoryInfo(directoryPath);
        var acl = dirInfo.GetAccessControl();

        var denyRule = new FileSystemAccessRule(
            AdminSid,
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);

        acl.AddAccessRule(denyRule);
        dirInfo.SetAccessControl(acl);
    }

    /// <summary>
    /// Harden vault directory: strip inheritance, grant SYSTEM full control,
    /// deny Administrators write/delete/ownership.
    /// Equivalent to: icacls "{path}" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F
    ///                /deny *S-1-5-32-544:(DE,WD,AD,WA,WEA,DC,WRITE_DAC,WRITE_OWNER)
    /// </summary>
    public static void HardenVault(string vaultPath)
    {
        var dirInfo = new DirectoryInfo(vaultPath);
        var acl = dirInfo.GetAccessControl();

        // Strip inherited ACEs
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        // Grant SYSTEM full control with inheritance
        var systemRule = new FileSystemAccessRule(
            SystemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
        acl.AddAccessRule(systemRule);

        // Deny Administrators write/delete/ownership
        var denyRule = new FileSystemAccessRule(
            AdminSid,
            FileSystemRights.Delete |
            FileSystemRights.Write |
            FileSystemRights.AppendData |
            FileSystemRights.WriteAttributes |
            FileSystemRights.WriteExtendedAttributes |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        acl.AddAccessRule(denyRule);

        dirInfo.SetAccessControl(acl);
    }
}
