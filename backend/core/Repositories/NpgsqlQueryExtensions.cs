using System.Data;
using Api.Models;
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
    /// <summary>
    /// Tables that may be passed to <see cref="ExistsAsync"/>. Prevents SQL injection via the
    /// interpolated table name.
    /// </summary>
    private static readonly HashSet<string> AllowedExistsTables = new(StringComparer.Ordinal)
    {
        "sites", "spaces", "resource_groups", "resources", "criteria", "requests",
        "users", "tenants", "announcements", "presets", "templates"
    };

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
    /// Returns true when a row with <paramref name="id"/> exists in <paramref name="table"/>.
    /// <paramref name="table"/> must be in the allow-list (it is interpolated into the SQL).
    /// </summary>
    public static async Task<bool> ExistsAsync(this NpgsqlConnection conn, string table, Guid id, CancellationToken ct = default)
    {
        if (!AllowedExistsTables.Contains(table))
            throw new ArgumentException($"Table '{table}' is not in the allowed table list.", nameof(table));

        return await conn.ExecuteScalarAsync<object>(
            $"SELECT 1 FROM {table} WHERE id = @id LIMIT 1",
            p => p.AddWithValue("id", id), ct) is not null;
    }

    /// <summary>
    /// Run a COUNT query then the paged SELECT (which must contain <c>@limit</c> and
    /// <c>@offset</c>), returning a <see cref="PagedResult{T}"/>. <paramref name="bind"/> adds
    /// any filter params to the page query and, unless <paramref name="bindCount"/> is given,
    /// to the count query too.
    /// </summary>
    public static async Task<PagedResult<T>> QueryPagedAsync<T>(
        this NpgsqlConnection conn,
        PageRequest page,
        string countSql,
        string querySql,
        Action<NpgsqlParameterCollection>? bind,
        Func<NpgsqlDataReader, T> map,
        CancellationToken ct = default,
        Action<NpgsqlParameterCollection>? bindCount = null)
    {
        await EnsureOpenAsync(conn, ct);
        var p = page.Sanitize();

        await using var countCmd = new NpgsqlCommand(countSql, conn);
        (bindCount ?? bind)?.Invoke(countCmd.Parameters);
        var totalItems = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        await using var cmd = new NpgsqlCommand(querySql, conn);
        cmd.Parameters.AddWithValue("limit", p.PageSize);
        cmd.Parameters.AddWithValue("offset", p.Offset);
        bind?.Invoke(cmd.Parameters);

        var items = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) items.Add(map(reader));
        return PagedResult<T>.Create(items, totalItems, p);
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

    /// <summary>
    /// Add a raw SQL expression to the SET clause without a parameter (e.g. <c>"updated_at = NOW()"</c>).
    /// Use this for server-side expressions that cannot be passed as a typed parameter.
    /// </summary>
    public UpdateBuilder SetExpression(string sqlExpression)
    {
        _sets.Add(sqlExpression);
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
