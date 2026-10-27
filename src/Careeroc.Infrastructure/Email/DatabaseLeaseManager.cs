using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Careeroc.Application.Abstractions;
using Careeroc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Infrastructure.Email;

public sealed class DatabaseLeaseManager : ILeaseManager
{
    private readonly CareerocDbContext _dbContext;

    public DatabaseLeaseManager(CareerocDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsPostgresProvider())
        {
            return await NoopLeaseManager.Instance.TryAcquireAsync(resource, cancellationToken);
        }

        var connection = _dbContext.Database.GetDbConnection();
        var closeConnectionOnRelease = connection.State != ConnectionState.Open;
        if (closeConnectionOnRelease)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = BuildLockKey(resource);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lock_key);";
        AddParameter(command, "lock_key", lockKey);

        var acquired = await command.ExecuteScalarAsync(cancellationToken);
        if (acquired is not true)
        {
            if (closeConnectionOnRelease)
            {
                await connection.CloseAsync();
            }

            return null;
        }

        return new PostgresLease(connection, lockKey, closeConnectionOnRelease);
    }

    private bool IsPostgresProvider()
    {
        return string.Equals(_dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
    }

    private static long BuildLockKey(string resource)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"careeroc:{resource}"));
        return BinaryPrimitives.ReadInt64LittleEndian(hash);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class PostgresLease : IAsyncDisposable
    {
        private readonly DbConnection _connection;
        private readonly long _lockKey;
        private readonly bool _closeConnectionOnRelease;
        private bool _disposed;

        public PostgresLease(DbConnection connection, long lockKey, bool closeConnectionOnRelease)
        {
            _connection = connection;
            _lockKey = lockKey;
            _closeConnectionOnRelease = closeConnectionOnRelease;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    using var command = _connection.CreateCommand();
                    command.CommandText = "SELECT pg_advisory_unlock(@lock_key);";
                    AddParameter(command, "lock_key", _lockKey);
                    await command.ExecuteScalarAsync();
                }
            }
            finally
            {
                if (_closeConnectionOnRelease && _connection.State == ConnectionState.Open)
                {
                    _connection.Close();
                }
            }
        }
    }
}
