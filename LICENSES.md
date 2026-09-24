# Third-party notices

Orbit DataBridge uses the following packages. Their license metadata was checked from the restored NuGet packages used by this solution. The authoritative license text remains with each upstream project/package.

| Package family | Use | License / notice |
| --- | --- | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Avalonia.Controls.DataGrid | Windows desktop UI | MIT; see https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md |
| Npgsql | PostgreSQL ADO.NET provider | PostgreSQL License; see https://github.com/npgsql/npgsql/blob/main/LICENSE |
| MySqlConnector | MySQL/MariaDB ADO.NET provider | MIT; see https://github.com/mysql-net/MySqlConnector/blob/master/LICENSE |
| Microsoft.Data.SqlClient | SQL Server ADO.NET provider | MIT; see https://github.com/dotnet/SqlClient/blob/main/LICENSE |
| Microsoft.Data.Sqlite | SQLite ADO.NET provider | MIT; see https://github.com/dotnet/efcore/blob/main/LICENSE |
| System.Security.Cryptography.ProtectedData | Windows DPAPI API | MIT; see https://github.com/dotnet/runtime/blob/main/LICENSE.TXT |
| xUnit and Microsoft.NET.Test.Sdk | Automated tests | Apache-2.0 / MIT; see their package metadata |

Provider libraries may include their own transitive notices. Before publishing an installer, export the exact package lock and include the corresponding upstream notices in the release artifact.
