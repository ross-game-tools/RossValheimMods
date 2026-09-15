using System;

namespace RossQoL.Core.Production
{
    /// <summary>Fermenter.GetStatus == Ready, from the ZDO's values.</summary>
    public static class FermenterReadiness
    {
        public static bool IsReady(int content, long startTicks, long nowTicks, float durationSeconds)
        {
            if (content == 0 || startTicks == 0) return false;
            double seconds = (double)(nowTicks - startTicks) / TimeSpan.TicksPerSecond;
            return seconds > durationSeconds;
        }
    }
}
