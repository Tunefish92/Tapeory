namespace Tapeory.Api.Tests.Integration;

/// <summary>A test that needs the MySQL server (e.g. entering its connection in the setup):
/// skipped when the suite runs against SQLite.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (TapeoryWebApplicationFactory.UseSqlite)
        {
            Skip = "Needs MySQL; this run uses SQLite (TAPEORY_TEST_DATABASE=sqlite).";
        }
    }
}
