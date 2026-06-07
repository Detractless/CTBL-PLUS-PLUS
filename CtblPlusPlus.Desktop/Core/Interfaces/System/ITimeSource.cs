using System;

namespace CtblPlusPlus.Core.Interfaces.System;

public interface ITimeSource
{
    DateTime? GetUtcTime(out string sourceName);
}


