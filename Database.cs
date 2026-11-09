using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace WeaponPaints
{
    public class Database(string dbConnectionString)
    {
        public async Task EnsureDatabaseExistsAsync()
        {
            var builder = new MySqlConnectionStringBuilder(
                dbConnectionString);

            string databaseName = builder.Database;

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new InvalidOperationException(
                    "DatabaseName must not be empty.");
            }

            // First try connecting to the configured database.
            await using (var connection =
                new MySqlConnection(dbConnectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    return;
                }
                catch (MySqlException ex) when (ex.Number == 1049)
                {
                    // The configured database does not exist.
                }
            }

            // Connect to MySQL without selecting a database.
            builder.Database = string.Empty;
            builder.Pooling = false;

            await using var serverConnection =
                new MySqlConnection(builder.ConnectionString);

            await serverConnection.OpenAsync();

            // Database identifiers cannot be supplied as SQL parameters.
            string escapedDatabaseName =
                databaseName.Replace("`", "``");

            await using var command =
                serverConnection.CreateCommand();

            command.CommandText =
                $"CREATE DATABASE IF NOT EXISTS `{escapedDatabaseName}` " +
                "CHARACTER SET utf8mb4 " +
                "COLLATE utf8mb4_unicode_ci;";

            await command.ExecuteNonQueryAsync();
        }

        public async Task<MySqlConnection> GetConnectionAsync()
        {
            var connection =
                new MySqlConnection(dbConnectionString);

            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (Exception ex)
            {
                await connection.DisposeAsync();

                WeaponPaints.Instance.Logger.LogError(
                    ex,
                    "Unable to connect to the WeaponPaints database.");

                throw;
            }
        }
    }
}