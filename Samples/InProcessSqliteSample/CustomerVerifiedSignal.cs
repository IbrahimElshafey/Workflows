using System;

namespace InProcessSqliteSample
{
    public class CustomerVerifiedSignal
    {
        public string CustomerEmail { get; set; } = string.Empty;
        public bool Verified { get; set; }
    }
}
