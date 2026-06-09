using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Models;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue.Handlers;

public class SettingsQueueHandler : IQueueRequestHandler
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly IQueueRepository _queueRepo;
    private readonly IAuditRepository _auditRepo;

    public SettingsQueueHandler(ISettingsRepository settingsRepo, IQueueRepository queueRepo, IAuditRepository auditRepo)
    {
        _settingsRepo = settingsRepo;
        _queueRepo = queueRepo;
        _auditRepo = auditRepo;
    }

    public bool CanHandle(DelayRequest request)
    {
        return request.BlockName == "System" && request.TargetUrl.StartsWith("GlobalDelayHours|");
    }

    public void Handle(DelayRequest request, QueueBatchContext context)
    {
        var parts = request.TargetUrl.Split('|');

        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            _auditRepo.LogAction("System", "GlobalDelayHours", "Rejected: malformed TargetUrl — missing value segment");
            _queueRepo.UpdateRequestStatus(request.Id, "Rejected");
            return;
        }

        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double parsedHours))
        {
            _auditRepo.LogAction("System", "GlobalDelayHours", $"Rejected: non-numeric value '{parts[1]}'");
            _queueRepo.UpdateRequestStatus(request.Id, "Rejected");
            return;
        }

        string newValue = parsedHours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _settingsRepo.SetSetting("GlobalDelayHours", newValue);
        _queueRepo.UpdateRequestStatus(request.Id, "Applied");
        _auditRepo.LogAction("System", "GlobalDelayHours", $"Decreased to {newValue}");
    }
}


