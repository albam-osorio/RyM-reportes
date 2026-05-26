using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using RymReportes.Web.Models;
using RymReportes.Web.Options;

namespace RymReportes.Web.Services;

public sealed class SqlEventReportRepository(IOptions<DatabaseOptions> options) : IEventReportRepository
{
    private readonly DatabaseOptions _options = options.Value;

    public Task<IReadOnlyList<EventReportRow>> GetMonthlyReportAsync(
        MonthPeriod period,
        CancellationToken cancellationToken)
    {
        const string sql = """
            ;WITH
            cte_00 AS
            (
                SELECT
                    client_id,
                    shipment_number,
                    order_number,
                    event_type,
                    entity_state,
                    event_date,
                    client_event_response_summary,
                    created_at
                FROM [rymdb].[dbo].[events]
                WHERE order_number IN (
                    SELECT order_number
                    FROM [rymdb].[dbo].[events]
                    WHERE ISNUMERIC(order_number) = 1
                    AND created_at >= @StartDate
                    AND created_at < @EndDate
                )
                AND entity_state <> 'DESCARTADO'
            )
            SELECT
                client_id AS nit,
                shipment_number AS remesa,
                order_number AS numero_pedido,
                event_type AS tipo_evento,
                event_date AS fecha_evento,
                entity_state AS resultado_integracion,
                client_event_response_summary AS respuesta_natura,
                created_at AS reportador_por_sisifo
            FROM cte_00 a
            ORDER BY
                client_id,
                shipment_number,
                order_number,
                created_at,
                CAST([event_date] AS DATE),
                event_type,
                event_date;
            """;

        return QueryAsync(
            sql,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@StartDate", SqlDbType.DateTime2) { Value = period.StartDate.ToDateTime(TimeOnly.MinValue) });
                command.Parameters.Add(new SqlParameter("@EndDate", SqlDbType.DateTime2) { Value = period.EndDateExclusive.ToDateTime(TimeOnly.MinValue) });
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<EventReportRow>> GetOrdersReportAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @OrderNumbers TABLE
            (
                order_number nvarchar(100) NOT NULL PRIMARY KEY
            );

            INSERT INTO @OrderNumbers (order_number)
            SELECT DISTINCT LTRIM(RTRIM([value]))
            FROM OPENJSON(@OrderNumbersJson)
            WHERE LTRIM(RTRIM([value])) <> '';

            ;WITH
            cte_00 AS
            (
                SELECT
                    e.client_id,
                    e.shipment_number,
                    e.order_number,
                    e.event_type,
                    e.entity_state,
                    e.event_date,
                    e.client_event_response_summary,
                    e.created_at
                FROM [rymdb].[dbo].[events] e
                INNER JOIN @OrderNumbers o
                    ON o.order_number = e.order_number
                WHERE e.entity_state <> 'DESCARTADO'
            )
            SELECT
                client_id AS nit,
                shipment_number AS remesa,
                order_number AS numero_pedido,
                event_type AS tipo_evento,
                event_date AS fecha_evento,
                entity_state AS resultado_integracion,
                client_event_response_summary AS respuesta_natura,
                created_at AS reportador_por_sisifo
            FROM cte_00 a
            ORDER BY
                client_id,
                shipment_number,
                order_number,
                created_at,
                CAST([event_date] AS DATE),
                event_type,
                event_date;
            """;

        var json = JsonSerializer.Serialize(orderNumbers);
        return QueryAsync(
            sql,
            command => command.Parameters.Add(new SqlParameter("@OrderNumbersJson", SqlDbType.NVarChar) { Value = json }),
            cancellationToken);
    }

    private async Task<IReadOnlyList<EventReportRow>> QueryAsync(
        string sql,
        Action<SqlCommand> configureCommand,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 180
        };

        configureCommand(command);

        var rows = new List<EventReportRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new EventReportRow(
                ReadString(reader, "nit"),
                ReadString(reader, "remesa"),
                ReadString(reader, "numero_pedido"),
                ReadString(reader, "tipo_evento"),
                ReadDateTime(reader, "fecha_evento"),
                ReadString(reader, "resultado_integracion"),
                ReadString(reader, "respuesta_natura"),
                ReadDateTime(reader, "reportador_por_sisifo")));
        }

        return rows;
    }

    private static string? ReadString(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToString(value);
    }

    private static DateTime? ReadDateTime(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToDateTime(value);
    }
}
