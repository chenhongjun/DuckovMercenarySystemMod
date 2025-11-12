namespace DuckovMercenarySystemMod
{
    /// <summary>
    /// 贿赂记录类 - 记录每个敌人的贿赂信息
    /// </summary>
    public class BribeRecord
    {
        public int Times = 0;         // 贿赂次数
        public int TotalAmount = 0;   // 累计金额
        public int FailedAttempts = 0; // 达到门槛后的失败次数
        public int RequiredAmount = 0; // 目标开价
    }
}

