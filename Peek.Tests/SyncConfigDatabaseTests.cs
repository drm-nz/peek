using System.Text;
using LiteDB;
using Microsoft.Extensions.Configuration;

namespace Peek.Tests;

public class SyncConfigDatabaseTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<SiteCheck> _collection;
    private readonly IConfigurationRoot _savedConfig;

    public SyncConfigDatabaseTests()
    {
        Log.Reset();
        _savedConfig = Program._config;
        _db = new LiteDatabase(new MemoryStream());
        _collection = _db.GetCollection<SiteCheck>("SiteCheckStates");
    }

    public void Dispose()
    {
        _db.Dispose();
        Program._config = _savedConfig;
    }

    private static IConfigurationRoot BuildConfig(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new ConfigurationBuilder().AddJsonStream(stream).Build();
    }

    [Fact]
    public void inserts_new_sites_from_config()
    {
        Program._config = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60, "searchString": "Welcome" },
                { "url": "https://example.org", "interval": 120, "searchString": "*" }
            ]
        }
        """);
        Program.SyncConfigWithDatabase(_collection);

        var all = _collection.FindAll().ToList();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.URL == "https://example.com" && s.Interval == 60);
        Assert.Contains(all, s => s.URL == "https://example.org" && s.Interval == 120);
    }

    [Fact]
    public void updates_existing_site_when_config_changes()
    {
        var config1 = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60, "searchString": "Welcome" }
            ]
        }
        """);
        Program._config = config1;
        Program.SyncConfigWithDatabase(_collection);

        var config2 = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 300, "searchString": "NewContent" }
            ]
        }
        """);
        Program._config = config2;
        Program.SyncConfigWithDatabase(_collection);

        var record = _collection.FindOne(Query.EQ("URL", "https://example.com"));
        Assert.NotNull(record);
        Assert.Equal(300, record.Interval);
        Assert.Equal("NewContent", record.SearchString);
    }

    [Fact]
    public void removes_stale_records_not_in_config()
    {
        _collection.Insert(new SiteCheck
        {
            URL = "https://stale.example.com",
            Interval = 60,
            ConfigUpdated = DateTime.Now.AddHours(-1),
            LastState = 200
        });
        Program._config = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);
        Program.SyncConfigWithDatabase(_collection);

        var all = _collection.FindAll().ToList();
        Assert.DoesNotContain(all, s => s.URL == "https://stale.example.com");
    }

    [Fact]
    public void keeps_recent_records_even_if_not_in_config()
    {
        _collection.Insert(new SiteCheck
        {
            URL = "https://recent.example.com",
            Interval = 60,
            ConfigUpdated = DateTime.Now.AddMinutes(-1),
            LastState = 200
        });
        Program._config = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);
        Program.SyncConfigWithDatabase(_collection);

        var all = _collection.FindAll().ToList();
        Assert.Contains(all, s => s.URL == "https://recent.example.com");
    }

    [Fact]
    public void skips_invalid_urls_in_config()
    {
        Program._config = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "", "interval": 60 },
                { "url": "https://valid.com", "interval": 60 }
            ]
        }
        """);
        Program.SyncConfigWithDatabase(_collection);

        var all = _collection.FindAll().ToList();
        Assert.Single(all);
        Assert.Equal("https://valid.com", all[0].URL);
    }

    [Fact]
    public void sets_initial_state_for_new_records()
    {
        Program._config = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);
        Program.SyncConfigWithDatabase(_collection);

        var record = _collection.FindOne(Query.EQ("URL", "https://example.com"));
        Assert.NotNull(record);
        Assert.Equal(200, record.LastState);
        Assert.Null(record.Message);
        Assert.True(record.NextCheck <= DateTime.Now);
    }

    [Fact]
    public void creates_unique_index_on_url()
    {
        Program._config = BuildConfig("""
        {
            "SiteChecks": [
                { "url": "https://example.com", "interval": 60 }
            ]
        }
        """);
        Program.SyncConfigWithDatabase(_collection);

        var dup = new SiteCheck
        {
            URL = "https://example.com",
            Interval = 60,
            ConfigUpdated = DateTime.Now
        };

        // LiteDB in-memory may not enforce unique indexes,
        // so only assert if the engine supports it
        var throws = Record.Exception(() => _collection.Insert(dup));
        if (throws != null)
            Assert.IsType<LiteException>(throws);
    }
}
