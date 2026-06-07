namespace CtblPlusPlus.Core.Interfaces.Security;

public interface IHmacProvider
{
    string ComputeHmac(string payload);
}


