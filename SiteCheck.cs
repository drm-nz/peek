using System;

namespace Peek;

class SiteCheck
{
    public Guid Id { get; set; }
    public string URL { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string SearchString { get; set; } = string.Empty;
    public int LastState { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime NextCheck { get; set; }
    public DateTime ConfigUpdated { get; set; }
    public DateTime NextNotification { get; set; }
}
