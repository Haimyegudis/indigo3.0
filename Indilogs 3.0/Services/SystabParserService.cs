using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using IndiLogs_3._0.Models;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Parses Windows Registry format (.reg) files containing systab parameters.
    /// Converts systab_saved, systab_default, systab_minimum, systab_maximum files
    /// into a tree structure for display and comparison.
    /// </summary>
    public static class SystabParserService
    {
        /// <summary>
        /// Parse a single .reg file content (string) and return a dictionary
        /// keyed by (registry path, parameter name) with cleaned values.
        /// </summary>
        public static Dictionary<(string Path, string Key), string> ParseRegContent(string content)
        {
            var entries = new Dictionary<(string Path, string Key), string>();
            if (string.IsNullOrEmpty(content))
                return entries;

            string? currentPath = null;
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                // Section header: [HKEY_LOCAL_MACHINE\SOFTWARE\...]
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentPath = line.Substring(1, line.Length - 2);
                }
                // Parameter line: "Name"=value
                else if (line.Contains("=") && currentPath != null)
                {
                    int eqIndex = line.IndexOf('=');
                    string key = line.Substring(0, eqIndex).Trim().Trim('"');
                    string value = CleanValue(line.Substring(eqIndex + 1));
                    entries[(currentPath, key)] = value;
                }
            }

            return entries;
        }

        /// <summary>
        /// Clean a registry value string.
        /// Strips surrounding quotes from string values and converts DWORD hex to decimal.
        /// </summary>
        private static string CleanValue(string value)
        {
            if (value == null)
                return string.Empty;

            value = value.Trim();

            // String value: "something"
            if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
            {
                value = value.Substring(1, value.Length - 2);
            }
            // DWORD value: dword:0000001e -> convert hex to decimal
            else if (value.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
            {
                string hexVal = value.Substring("dword:".Length);
                if (int.TryParse(hexVal, System.Globalization.NumberStyles.HexNumber, null, out int intVal))
                {
                    value = intVal.ToString();
                }
            }

            return value;
        }

        /// <summary>
        /// Extract topic, station, and index from a registry path.
        /// Path pattern: ...\Indigo\Unicorn\{Mode}\{Station}\{Topic}\{Index}
        /// Returns (topic, station, index) or (null, null, null) if no match.
        /// </summary>
        public static (string? Topic, string? Station, string? Index) ExtractTopicInfo(string path)
        {
            if (string.IsNullOrEmpty(path))
                return (null, null, null);

            var match = Regex.Match(path, @"Indigo\\Unicorn\\[^\\]+\\([^\\]+)\\([^\\]+)\\([^\\]+)$", RegexOptions.IgnoreCase, AppConstants.RegexTimeout);
            if (match.Success)
            {
                string station = match.Groups[1].Value;
                string topic = match.Groups[2].Value;
                string index = match.Groups[3].Value;
                return (topic, station, index);
            }

            return (null, null, null);
        }

        /// <summary>
        /// Build the tree structure from 4 systab file contents.
        /// Keys in systabFileContents should be "saved", "default", "minimum", "maximum".
        /// Returns an ObservableCollection of SystabTopicNode as the tree root nodes.
        /// </summary>
        public static ObservableCollection<SystabTopicNode> BuildSystabTree(
            Dictionary<string, string> systabFileContents)
        {
            var tree = new ObservableCollection<SystabTopicNode>();

            if (systabFileContents == null || systabFileContents.Count == 0)
                return tree;

            // Parse all files
            var parsedFiles = new Dictionary<string, Dictionary<(string Path, string Key), string>>();
            foreach (var kvp in systabFileContents)
            {
                parsedFiles[kvp.Key] = ParseRegContent(kvp.Value);
            }

            parsedFiles.TryGetValue("saved", out var savedEntriesRaw);
            parsedFiles.TryGetValue("default", out var defaultEntriesRaw);
            parsedFiles.TryGetValue("minimum", out var minimumEntriesRaw);
            parsedFiles.TryGetValue("maximum", out var maximumEntriesRaw);

            var savedEntries = savedEntriesRaw ?? new Dictionary<(string Path, string Key), string>();
            var defaultEntries = defaultEntriesRaw ?? new Dictionary<(string Path, string Key), string>();
            var minimumEntries = minimumEntriesRaw ?? new Dictionary<(string Path, string Key), string>();
            var maximumEntries = maximumEntriesRaw ?? new Dictionary<(string Path, string Key), string>();

            // Collect all unique (path, key) pairs from all files
            var allKeys = new HashSet<(string Path, string Key)>();
            foreach (var dict in parsedFiles.Values)
            {
                foreach (var key in dict.Keys)
                    allKeys.Add(key);
            }

            // Group by Topic -> "Station|Index"
            var topicGroups = new Dictionary<string, Dictionary<string, List<SystabEntry>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (path, paramKey) in allKeys)
            {
                var (topic, station, index) = ExtractTopicInfo(path);
                if (topic == null || station == null || index == null)
                    continue;

                string stationIndex = $"{station}|{index}";

                if (!topicGroups.TryGetValue(topic, out var stationDict))
                {
                    stationDict = new Dictionary<string, List<SystabEntry>>(StringComparer.OrdinalIgnoreCase);
                    topicGroups[topic] = stationDict;
                }

                if (!stationDict.TryGetValue(stationIndex, out var entryList))
                {
                    entryList = new List<SystabEntry>();
                    stationDict[stationIndex] = entryList;
                }

                savedEntries.TryGetValue((path, paramKey), out string? savedVal);
                defaultEntries.TryGetValue((path, paramKey), out string? defaultVal);
                minimumEntries.TryGetValue((path, paramKey), out string? minVal);
                maximumEntries.TryGetValue((path, paramKey), out string? maxVal);

                var entry = new SystabEntry
                {
                    Parameter = paramKey,
                    Saved = savedVal ?? string.Empty,
                    Default = defaultVal ?? string.Empty,
                    Minimum = minVal ?? string.Empty,
                    Maximum = maxVal ?? string.Empty,
                    IsDifferent = !string.Equals(savedVal ?? string.Empty, defaultVal ?? string.Empty, StringComparison.Ordinal)
                };

                entryList.Add(entry);
            }

            // Build the tree: top level = Topics, children = Station|Index nodes
            foreach (var topicKvp in topicGroups.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
            {
                int childCount = topicKvp.Value.Count;
                int topicCount = 0;

                // Single child: flatten - put entries directly on the topic node (no expand arrow)
                if (childCount == 1)
                {
                    var singleChild = topicKvp.Value.First();
                    var sortedEntries = singleChild.Value.OrderBy(e => e.Parameter, StringComparer.OrdinalIgnoreCase).ToList();

                    var topicNode = new SystabTopicNode
                    {
                        Name = topicKvp.Key,
                        FullPath = topicKvp.Key,
                        IsTopLevel = true,
                        Entries = new List<SystabEntry>(sortedEntries),
                        Count = sortedEntries.Count,
                        HasDifferences = sortedEntries.Any(e => e.IsDifferent)
                    };

                    tree.Add(topicNode);
                    continue;
                }

                // Multiple children: build tree with parent name + index for numeric children
                var parentNode = new SystabTopicNode
                {
                    Name = topicKvp.Key,
                    FullPath = topicKvp.Key,
                    IsTopLevel = true
                };

                foreach (var stationKvp in topicKvp.Value.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var sortedEntries = stationKvp.Value.OrderBy(e => e.Parameter, StringComparer.OrdinalIgnoreCase).ToList();

                    // Extract the index part from "Station|Index" key
                    string childDisplayName = stationKvp.Key;
                    string[] keyParts = stationKvp.Key.Split('|');
                    if (keyParts.Length == 2)
                    {
                        string indexPart = keyParts[1].Trim();
                        // If the index is a simple number (0-9+), show "TopicName IndexNumber"
                        if (int.TryParse(indexPart, out _))
                        {
                            childDisplayName = $"{topicKvp.Key} {indexPart}";
                        }
                    }

                    var childNode = new SystabTopicNode
                    {
                        Name = childDisplayName,
                        FullPath = $"{topicKvp.Key}/{stationKvp.Key}",
                        IsTopLevel = false,
                        Entries = new List<SystabEntry>(sortedEntries),
                        Count = sortedEntries.Count
                    };

                    childNode.HasDifferences = sortedEntries.Any(e => e.IsDifferent);
                    topicCount += sortedEntries.Count;

                    parentNode.Children.Add(childNode);
                }

                parentNode.Count = topicCount;
                parentNode.HasDifferences = parentNode.Children.Any(c => c.HasDifferences);

                tree.Add(parentNode);
            }

            return tree;
        }
    }
}
