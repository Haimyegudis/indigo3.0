

using Indigo.Infra.ICL.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

public class IndigoLogsReaderExplorer
{
    public static void ExploreAPI()
    {
        Debug.WriteLine("=== INDIGO LOGS READER API EXPLORATION ===");
        Debug.WriteLine("");

        // Get the type
        Type readerType = typeof(IndigoLogsReader);

        Debug.WriteLine($"Full name: {readerType.FullName}");
        Debug.WriteLine($"Assembly: {readerType.Assembly.GetName().Name}");
        Debug.WriteLine("");

        // ================================================================
        // CONSTRUCTORS
        // ================================================================
        Debug.WriteLine("=== CONSTRUCTORS ===");
        var constructors = readerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        foreach (var ctor in constructors)
        {
            var parameters = ctor.GetParameters();
            var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Debug.WriteLine($"  new IndigoLogsReader({paramStr})");
        }
        Debug.WriteLine("");

        // ================================================================
        // PROPERTIES
        // ================================================================
        Debug.WriteLine("=== PROPERTIES ===");
        var properties = readerType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            string access = "";
            if (prop.CanRead) access += "get; ";
            if (prop.CanWrite) access += "set; ";

            Debug.WriteLine($"  {prop.PropertyType.Name} {prop.Name} {{ {access}}}");
        }
        Debug.WriteLine("");

        // ================================================================
        // METHODS
        // ================================================================
        Debug.WriteLine("=== PUBLIC METHODS ===");
        var methods = readerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName) // Skip property getters/setters
            .OrderBy(m => m.Name);

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Debug.WriteLine($"  {method.ReturnType.Name} {method.Name}({paramStr})");
        }
        Debug.WriteLine("");

        // ================================================================
        // CRITICAL FINDINGS
        // ================================================================
        Debug.WriteLine("=== 🔥 CRITICAL FINDINGS 🔥 ===");
        Debug.WriteLine("");

        // Check for position properties
        var positionProps = properties.Where(p =>
            p.Name.Contains("Position") ||
            p.Name.Contains("Offset") ||
            p.Name.Contains("Index")).ToList();

        if (positionProps.Any())
        {
            Debug.WriteLine("📍 POSITION TRACKING PROPERTIES FOUND:");
            foreach (var prop in positionProps)
            {
                string rw = prop.CanRead && prop.CanWrite ? "READ/WRITE ✅" :
                           prop.CanRead ? "READ ONLY" : "WRITE ONLY";
                Debug.WriteLine($"   • {prop.Name} ({prop.PropertyType.Name}) - {rw}");
            }
            Debug.WriteLine("");
        }

        // Check for seek/jump methods
        var seekMethods = methods.Where(m =>
            m.Name.Contains("Seek") ||
            m.Name.Contains("Jump") ||
            m.Name.Contains("Skip") ||
            m.Name.Contains("MoveTo")).ToList();

        if (seekMethods.Any())
        {
            Debug.WriteLine("🎯 SEEK/JUMP METHODS FOUND:");
            foreach (var method in seekMethods)
            {
                var parameters = method.GetParameters();
                var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Debug.WriteLine($"   • {method.Name}({paramStr}) → {method.ReturnType.Name}");
            }
            Debug.WriteLine("");
        }

        // Check for stream access
        var streamProps = properties.Where(p =>
            p.PropertyType.Name.Contains("Stream") ||
            p.Name.Contains("Stream")).ToList();

        if (streamProps.Any())
        {
            Debug.WriteLine("📁 STREAM ACCESS PROPERTIES:");
            foreach (var prop in streamProps)
            {
                Debug.WriteLine($"   • {prop.Name} ({prop.PropertyType.Name})");
            }
            Debug.WriteLine("");
        }

        // Check for batch reading
        var batchMethods = methods.Where(m =>
            m.Name.Contains("ReadAll") ||
            m.Name.Contains("ReadMany") ||
            m.Name.Contains("Batch")).ToList();

        if (batchMethods.Any())
        {
            Debug.WriteLine("📦 BATCH READING METHODS:");
            foreach (var method in batchMethods)
            {
                Debug.WriteLine($"   • {method.Name}() → {method.ReturnType.Name}");
            }
            Debug.WriteLine("");
        }

        Debug.WriteLine("=== END OF EXPLORATION ===");
    }
}


// ============================================================================
// 🧪 PRACTICAL TESTS
// ============================================================================
// הרץ את זה כדי לבדוק אם אפשר לשמור ולשחזר positions!
// ============================================================================

public class IndigoLogsReaderTests
{
    public static void TestPositionTracking(string filePath)
    {
        Debug.WriteLine("=== TESTING POSITION TRACKING ===");

        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new IndigoLogsReader(fs);

                // Read first 10 logs and track positions
                Debug.WriteLine("\n📖 Reading first 10 logs and tracking positions:");

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
                            long position = (long)positionProp.GetValue(reader);
                            positions.Add(position);

                            Debug.WriteLine($"  Log #{count + 1}:");
                            Debug.WriteLine($"    Time: {reader.Current.Time:HH:mm:ss.fff}");
                            Debug.WriteLine($"    Position: {position:N0}");
                            Debug.WriteLine($"    Message: {reader.Current.Message?.Substring(0, Math.Min(50, reader.Current.Message?.Length ?? 0))}...");
                        }
                        else
                        {
                            Debug.WriteLine($"  Log #{count + 1}: (position tracking not available)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"  ❌ Error getting position: {ex.Message}");
                    }

                    count++;
                }

                Debug.WriteLine("");
                Debug.WriteLine($"✅ Read {count} logs");
                Debug.WriteLine($"✅ Tracked {positions.Count} positions");

                if (positions.Count > 0)
                {
                    Debug.WriteLine("");
                    Debug.WriteLine("🎯 POSITION TRACKING IS AVAILABLE!");
                    Debug.WriteLine($"   First log position: {positions[0]:N0}");
                    Debug.WriteLine($"   Last log position: {positions[positions.Count - 1]:N0}");
                    Debug.WriteLine($"   Average bytes per log: {(positions[positions.Count - 1] - positions[0]) / (positions.Count - 1):N0}");
                    Debug.WriteLine("");
                    Debug.WriteLine("💡 THIS MEANS WE CAN:");
                    Debug.WriteLine("   ✅ Save last position");
                    Debug.WriteLine("   ✅ Skip to saved position");
                    Debug.WriteLine("   ✅ Read only NEW logs!");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Test failed: {ex.Message}");
            Debug.WriteLine($"   Stack: {ex.StackTrace}");
        }
    }

    public static void TestStreamPositionSeeking(string filePath)
    {
        Debug.WriteLine("\n=== TESTING STREAM POSITION SEEKING ===");

        try
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var reader = new IndigoLogsReader(fs);

                Debug.WriteLine("📍 Step 1: Read to middle of file...");
                int logsRead = 0;
                while (reader.MoveToNext() && logsRead < 1000)
                {
                    logsRead++;
                }

                Debug.WriteLine($"   Read {logsRead} logs");

                // Try to get stream position
                var streamPosProp = reader.GetType().GetProperty("StreamPosition");
                if (streamPosProp != null && streamPosProp.CanRead)
                {
                    long savedPosition = (long)streamPosProp.GetValue(reader);
                    Debug.WriteLine($"   Stream position: {savedPosition:N0}");

                    Debug.WriteLine("");
                    Debug.WriteLine("📍 Step 2: Create new reader from saved position...");

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
                                Debug.WriteLine($"   Log #{newLogsRead + 1}: {reader2.Current.Time:HH:mm:ss.fff}");
                                newLogsRead++;
                            }

                            if (newLogsRead > 0)
                            {
                                Debug.WriteLine("");
                                Debug.WriteLine("🎉 SUCCESS! We can seek to arbitrary positions!");
                                Debug.WriteLine("💡 THIS ENABLES ULTRA-FAST LIVE MONITORING!");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"   ❌ Failed to read from saved position: {ex.Message}");
                            Debug.WriteLine("   ⚠️ Reader requires reading from start (Position 0)");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("   ⚠️ StreamPosition property not available");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ Test failed: {ex.Message}");
        }
    }
}

