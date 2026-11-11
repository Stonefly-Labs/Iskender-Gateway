using Iskender.Domain.Ports;

namespace Iskender.Infrastructure.PostgreSql.Tests.Modules.Alpha;

/// <summary>The fixture repository for <c>isk_alpha.settings</c>, an operator table with no owner columns.</summary>
internal static class SettingRepository
{
    public static async Task PutAsync(IUnitOfWork unit, string key, string value, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Settings, "INSERT INTO isk_alpha.settings (key, value) VALUES ($1, $2) ON CONFLICT (key) DO UPDATE SET value = excluded.value")
            .With(key).With(value).ExecuteAsync(cancellationToken);

    public static async Task<string?> GetAsync(IUnitOfWork unit, string key, CancellationToken cancellationToken) =>
        await PostgreSqlUnitOfWork.From(unit).Command(AlphaTables.Settings, "SELECT value FROM isk_alpha.settings WHERE key = $1").With(key).ScalarAsync<string>(cancellationToken);
}
