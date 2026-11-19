using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.Api.Common.Persistence;

namespace TinaClass.Api.Tests.Infrastructure;

    /// <summary>The API in a given environment on a temporary SQLite file (an in-memory database would vanish between requests).</summary>
    public sealed class TempHost(string environment, params (string Key, string Value)[] settings) : WebApplicationFactory<Program>
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), $"startup-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            // The tests run on SQLite whichever database the template was generated for (appsettings.json says that one).
            builder.UseSetting("Database:Provider", "Sqlite");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
            // After every other configuration, so that appsettings.Development.json cannot point the test at its own database file.
            builder.ConfigureTestServices(services => services.PostConfigure<DatabaseOptions>(o => o.SqliteConnectionString = $"Data Source={_file}"));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            File.Delete(_file);
        }
    }
