using System;

namespace Peek
{
    class SiteCheck
    {
        public Guid Id { get; set; }
        public string URL { get; set; }
        public int Interval { get; set; }
        public string SearchString { get; set; }
        public int LastState { get; set; }
        public string Message { get; set; }
        public DateTime NextCheck { get; set; }
        public DateTime ConfigUpdated { get; set; }
        public DateTime LastReported { get; set; }
    }
}
