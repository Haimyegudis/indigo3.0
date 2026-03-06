using Indigo.Infra.ICL.Core.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using IndiLogs_3._0.Services;

public class IndigoLogsReaderExplorer
{
    public static void ExploreAPI()
    {
        // Get the type
        Type readerType = typeof(IndigoLogsReader);

        // ================================================================
        // CONSTRUCTORS
        // ================================================================
        var constructors = readerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // ================================================================
        // PROPERTIES
        // ================================================================
        var properties = readerType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // ================================================================
        // METHODS
        // ================================================================
        var methods = readerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName) // Skip property getters/setters
            .OrderBy(m => m.Name);

        // ================================================================
        // CRITICAL FINDINGS
        // ================================================================

        // Check for position properties
        var positionProps = properties.Where(p =>
            p.Name.Contains("Position") ||
            p.Name.Contains("Offset") ||
            p.Name.Contains("Index")).ToList();

        // Check for seek/jump methods
        var seekMethods = methods.Where(m =>
            m.Name.Contains("Seek") ||
            m.Name.Contains("Jump") ||
            m.Name.Contains("Skip") ||
            m.Name.Contains("MoveTo")).ToList();

        // Check for stream access
        var streamProps = properties.Where(p =>
            p.PropertyType.Name.Contains("Stream") ||
            p.Name.Contains("Stream")).ToList();

        // Check for batch reading
        var batchMethods = methods.Where(m =>
            m.Name.Contains("ReadAll") ||
            m.Name.Contains("ReadMany") ||
            m.Name.Contains("Batch")).ToList();
    }
}


// ============================================================================
// PRACTICAL TESTS
// ============================================================================

public class IndigoLogsReaderTests
{
    public static void TestPositionTracking(string filePath)
    {
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new IndigoLogsReader(fs);

                // Read first 10 logs and track positions
                var positions = new List<long>();
                int count = 0;

                while (reader.MoveToNext() && count < 10)
                {
                    try
                    {
                        // Try to get CurrentLogStartPosition
                        var positionProp = reader.GetType().GetProperty("CurrentLogStartPosition");
                        if (positionProp != null && positionProp.CanRead)
                        {
                            long position = (long)positionProp.GetValue(reader)!;
                            positions.Add(position);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error("Reading log position failed", ex);
                    }

                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("TestPositionTracking failed", ex);
        }
    }

    public static void TestStreamPositionSeeking(string filePath)
    {
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new IndigoLogsReader(fs);

                int logsRead = 0;
                while (reader.MoveToNext() && logsRead < 1000)
                {
                    logsRead++;
                }

                // Try to get stream position
                var streamPosProp = reader.GetType().GetProperty("StreamPosition");
                if (streamPosProp != null && streamPosProp.CanRead)
                {
                    long savedPosition = (long)streamPosProp.GetValue(reader)!;

                    // Create new stream starting from saved position
                    using (var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs2.Seek(savedPosition, SeekOrigin.Begin);

                        try
                        {
                            var reader2 = new IndigoLogsReader(fs2);

                            int newLogsRead = 0;
                            while (reader2.MoveToNext() && newLogsRead < 5)
                            {
                                newLogsRead++;
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("Reading from seeked position failed", ex);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("TestStreamPositionSeeking failed", ex);
        }
    }
}
