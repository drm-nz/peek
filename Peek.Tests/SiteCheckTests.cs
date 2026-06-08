namespace Peek.Tests;

public class SiteCheckTests
{
    [Fact]
    public void new_site_check_has_default_values()
    {
        var site = new SiteCheck();
        Assert.Equal(Guid.Empty, site.Id);
        Assert.Equal(string.Empty, site.URL);
        Assert.Equal(0, site.Interval);
        Assert.Equal(string.Empty, site.SearchString);
        Assert.Equal(0, site.LastState);
        Assert.Equal(string.Empty, site.Message);
        Assert.Equal(default, site.NextCheck);
    }

    [Fact]
    public void can_set_all_properties()
    {
        var now = DateTime.Now;
        var site = new SiteCheck
        {
            URL = "https://example.com",
            Interval = 60,
            SearchString = "foo",
            LastState = 200,
            Message = "OK",
            NextCheck = now,
            ConfigUpdated = now,
            NextNotification = now
        };

        Assert.Equal("https://example.com", site.URL);
        Assert.Equal(60, site.Interval);
        Assert.Equal("foo", site.SearchString);
        Assert.Equal(200, site.LastState);
        Assert.Equal("OK", site.Message);
        Assert.Equal(now, site.NextCheck);
        Assert.Equal(now, site.ConfigUpdated);
        Assert.Equal(now, site.NextNotification);
    }
}
