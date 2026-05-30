using System.Data;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// Thin Npgsql helpers that collapse the repeated open → command → bind → read boilerplate
/// shared by every repository. The caller owns the connection (created via the appropriate
/// <c>IOrgDbConnectionFactory</c> / <c>IDbConnectionFactory</c> method and disposed with
/// <c>await using</c>); these helpers open it on first use, so a single connection can drive
/// several calls (e.g. a fetch followed by an update) safely.
/// </summary>
internal static class NpgsqlQueryExtensions
{
    private static async Task EnsureOpenAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
    }

    /// <summary>Run a query and map every row to <typeparamref name="T"/>.</summary>
    public static async Task<List<T>> QueryListAsync<T>(
        this NpgsqlConnection conn,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(conn, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind?.Invoke(cmd.Parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct)) rows.Add(map(reader));
        return rows;
    }

    /// <summary>Run a query and map the first row, or return <c>default</c> when there are none.</summary>
    public static async Task<T?> QuerySingleOrDefaultAsync<T>(
        this NpgsqlConnection conn,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(conn, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind?.Invoke(cmd.Parameters);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? map(reader) : default;
    }

    /// <summary>Execute a non-query statement and return the affected row count.</summary>
    public static async Task<int> ExecuteAsync(
        this NpgsqlConnection conn,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(conn, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind?.Invoke(cmd.Parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Execute a scalar query and return the cast value, or <c>default(TScalar)</c> for NULL / no rows.
    /// For value types, use the result directly (e.g. <c>long</c> → 0 when the query returns NULL).
    /// </summary>
    public static async Task<TScalar> ExecuteScalarAsync<TScalar>(
        this NpgsqlConnection conn,
        string sql,
        Action<NpgsqlParameterCollection>? bind,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(conn, ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind?.Invoke(cmd.Parameters);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? default! : (TScalar)result;
    }

    /// <summary>
    /// Add a parameter, substituting <see cref="DBNull"/> for a null value. Replaces the
    /// per-repository <c>NullableParam(object?)</c> helpers.
    /// </summary>
    public static NpgsqlParameter AddNullable(this NpgsqlParameterCollection parameters, string name, object? value)
        => parameters.AddWithValue(name, value ?? DBNull.Value);
}

/// <summary>
/// Accumulates <c>column = @column</c> assignments plus their parameter values for a dynamic
/// partial UPDATE. Replaces the hand-rolled <c>sets</c>/<c>setClauses</c> lists copy-pasted
/// across repositories.
/// </summary>
internal sealed class UpdateBuilder
{
    private readonly List<string> _sets = [];
    private readonly List<(string Name, object Value)> _params = [];

    /// <summary>Always set <paramref name="column"/> to <paramref name="value"/> (use <see cref="DBNull"/> for an explicit NULL).</summary>
    public UpdateBuilder Set(string column, object value)
    {
        _sets.Add($"{column} = @{column}");
        _params.Add((column, value));
        return this;
    }

    /// <summary>Set <paramref name="column"/> only when <paramref name="value"/> is non-null (the common "patch" case).</summary>
    public UpdateBuilder SetIfNotNull(string column, object? value)
    {
        if (value is not null) Set(column, value);
        return this;
    }

    public bool IsEmpty => _sets.Count == 0;

    /// <summary>The comma-joined <c>SET</c> body, e.g. <c>name = @name, code = @code</c>.</summary>
    public string SetClause => string.Join(", ", _sets);

    /// <summary>Bind every accumulated value onto <paramref name="parameters"/>.</summary>
    public void Apply(NpgsqlParameterCollection parameters)
    {
        foreach (var (name, value) in _params)
            parameters.AddWithValue(name, value);
    }
}
