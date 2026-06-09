using System.Collections.Generic;
using CtblPlusPlus.Core.Models;

namespace CtblPlusPlus.Core.Interfaces.Data;

/// <summary>
/// Unified repository for the AppRegistry table — the single source of truth
/// for all discovered applications. Replaces the old dual-table design
/// (AppRegistry + AppControlRules).
/// </summary>
public interface IAppControlRepository
{
    // Core CRUD
    void UpsertApp(string exePath, string displayName, string publisher);
    void BulkUpsertApps(IEnumerable<(string ExePath, string DisplayName, string Publisher)> apps);
    List<AppRegistryEntry> GetAllApps();
    void DeleteAppsByIds(IEnumerable<string> ids);

    // Status management
    void SetAppStatus(string id, string newStatus);
    void BulkSetAppStatus(IEnumerable<string> ids, string newStatus);
    void SetColdTurkeyInjected(string id, bool injected);

    // Queries
    bool IsPathAllowed(string exePath);
    bool AppExistsByPath(string exePath);
    HashSet<string> GetAllAppPaths();
    List<AppRegistryEntry> GetAppsByStatus(string status);
    List<AppRegistryEntry> GetUnjectedBlockedApps();
}


