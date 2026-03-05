using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IndiLogs_3._0.ViewModels
{
    public partial class MainViewModel
    {
        // Full-column DataView for EVENTS tab (all CSV columns as-is)
        private System.Data.DataView? _eventsDataView;
        public System.Data.DataView? EventsDataView
        {
            get => _eventsDataView;
            set { _eventsDataView = value; OnPropertyChanged(); }
        }

        public void LoadEventsDataView()
        {
            var session = SessionVM?.SelectedSession;
            if (session == null) return;

            string? csvText = null;

            // 1. Primary: pressEvents / event-history CSV stored during ZIP parsing
            if (!string.IsNullOrEmpty(session.EventsCsvRawContent))
            {
                csvText = session.EventsCsvRawContent;
            }
            else
            {
                // 2. Fallback: look for event CSVs in TerminalCsvBytes (only actual event files)
                var eventsCsvEntries = session.TerminalCsvBytes?
                    .Where(kv =>
                    {
                        string name = Path.GetFileName(kv.Key);
                        return name.StartsWith("event-history__From", StringComparison.OrdinalIgnoreCase) ||
                               name.StartsWith("pressEvents.", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (eventsCsvEntries != null && eventsCsvEntries.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    bool first = true;
                    foreach (var kv in eventsCsvEntries)
                    {
                        string text = System.Text.Encoding.UTF8.GetString(kv.Value);
                        if (first) { sb.Append(text); first = false; }
                        else
                        {
                            int nl = text.IndexOf('\n');
                            if (nl >= 0 && nl < text.Length - 1) sb.Append(text.AsSpan(nl + 1));
                        }
                    }
                    csvText = sb.ToString();
                }
            }

            if (string.IsNullOrEmpty(csvText))
            {
                EventsDataView = null;
                return;
            }

            var dt = new System.Data.DataTable("Events");
            using var reader = new StringReader(csvText);
            string? headerLine = reader.ReadLine();
            if (headerLine == null) { EventsDataView = null; return; }

            var headers = SplitCsvLineHelper(headerLine);
            foreach (var h in headers)
                dt.Columns.Add(h.Trim(), typeof(string));

            string? dataLine;
            while ((dataLine = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(dataLine)) continue;
                var values = SplitCsvLineHelper(dataLine);
                var row = dt.NewRow();
                for (int i = 0; i < Math.Min(values.Count, dt.Columns.Count); i++)
                    row[i] = values[i].Trim();
                dt.Rows.Add(row);
            }
            EventsDataView = dt.DefaultView;
        }

        private static List<string> SplitCsvLineHelper(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var sb = new System.Text.StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }

        // ── SQLite content loading ──

        private string LoadSqliteContent(byte[] dbBytes)
        {
            var sb = new System.Text.StringBuilder();
            string tempDbPath = null;

            try
            {
                tempDbPath = Path.Combine(Path.GetTempPath(), $"indilogs_temp_{Guid.NewGuid()}.db");
                File.WriteAllBytes(tempDbPath, dbBytes);

                using (var connection = new SqliteConnection($"Data Source={tempDbPath};Mode=ReadOnly"))
                {
                    connection.Open();
                    var tables = new List<string>();
                    using (var cmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) { tables.Add(reader.GetString(0)); }
                    }

                    sb.AppendLine($"=== SQLite Database: {tables.Count} tables ===");
                    sb.AppendLine();

                    foreach (var tableName in tables)
                    {
                        sb.AppendLine($"━━━ TABLE: {tableName} ━━━");
                        using (var countCmd = new SqliteCommand($"SELECT COUNT(*) FROM [{AppConstants.EscapeSqlBracketId(tableName)}]", connection))
                        {
                            var count = countCmd.ExecuteScalar();
                            sb.AppendLine($"Rows: {count}");
                        }
                        using (var cmd = new SqliteCommand($"SELECT * FROM [{AppConstants.EscapeSqlBracketId(tableName)}] LIMIT 100", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            var columns = new List<string>();
                            for (int i = 0; i < reader.FieldCount; i++) { columns.Add(reader.GetName(i)); }
                            sb.AppendLine($"Columns: {string.Join(", ", columns)}");
                            sb.AppendLine();

                            int rowNum = 0;
                            while (reader.Read() && rowNum < 100)
                            {
                                sb.AppendLine($"--- Row {++rowNum} ---");
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    var value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "NULL";
                                    if (value.Length > 500) value = value.Substring(0, 500) + "...";
                                    sb.AppendLine($"  {columns[i]}: {value}");
                                }
                            }
                        }
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error reading SQLite database: {ex.Message}");
            }
            finally
            {
                if (tempDbPath != null && File.Exists(tempDbPath))
                {
                    try { File.Delete(tempDbPath); } catch (Exception ex) { AppLogger.Error("Temp DB cleanup failed", ex); }
                }
            }
            return sb.ToString();
        }
    }
}
