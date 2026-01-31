using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Provides text comparison functionality using LCS (Longest Common Subsequence) algorithm.
    /// Supports regex-based masking to ignore dynamic content like IDs and timestamps.
    /// </summary>
    public class DiffEngine
    {
        private string _ignoreMaskPattern;
        private Regex _maskRegex;

        /// <summary>
        /// Regex pattern for content to ignore during comparison.
        /// Example: "\d+" to ignore all numbers.
        /// </summary>
        public string IgnoreMaskPattern
        {
            get => _ignoreMaskPattern;
            set
            {
                _ignoreMaskPattern = value;
                _maskRegex = null;

                ComparisonDebugLogger.Log("PATTERN", $"Setting IgnoreMaskPattern to: \"{value ?? "(null)"}\"");

                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        _maskRegex = new Regex(value, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                        ComparisonDebugLogger.Log("PATTERN", $"Pattern compiled successfully. Regex created.");
                    }
                    catch (ArgumentException ex)
                    {
                        ComparisonDebugLogger.Log("PATTERN", $"ERROR: Invalid regex pattern! Exception: {ex.Message}");
                        // Invalid regex - leave _maskRegex as null
                    }
                }
                else
                {
                    ComparisonDebugLogger.Log("PATTERN", "Pattern is empty/null - masking disabled.");
                }
            }
        }

        /// <summary>
        /// Indicates whether the current mask pattern is valid.
        /// </summary>
        public bool IsMaskPatternValid => string.IsNullOrEmpty(_ignoreMaskPattern) || _maskRegex != null;

        /// <summary>
        /// Applies the mask pattern to text, replacing matched content with a placeholder.
        /// </summary>
        public string ApplyMask(string text)
        {
            if (string.IsNullOrEmpty(text) || _maskRegex == null)
            {
                ComparisonDebugLogger.Log("MASK", $"ApplyMask skipped - text empty: {string.IsNullOrEmpty(text)}, regex null: {_maskRegex == null}");
                return text;
            }

            try
            {
                var matches = _maskRegex.Matches(text);
                string result = _maskRegex.Replace(text, "#");

                if (matches.Count > 0)
                {
                    ComparisonDebugLogger.Log("MASK", $"Found {matches.Count} matches in text");
                    foreach (Match m in matches)
                    {
                        ComparisonDebugLogger.Log("MASK", $"  Matched: \"{m.Value}\" at position {m.Index}");
                    }
                    ComparisonDebugLogger.Log("MASK", $"  Before: \"{Truncate(text, 100)}\"");
                    ComparisonDebugLogger.Log("MASK", $"  After:  \"{Truncate(result, 100)}\"");
                }

                return result;
            }
            catch (Exception ex)
            {
                ComparisonDebugLogger.Log("MASK", $"ERROR applying mask: {ex.Message}");
                return text;
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        /// <summary>
        /// Compares two strings and returns diff results for both sides.
        /// </summary>
        public DiffResult Compare(string left, string right)
        {
            if (left == null) left = string.Empty;
            if (right == null) right = string.Empty;

            ComparisonDebugLogger.Log("DIFF", "=== Starting Compare ===");
            ComparisonDebugLogger.Log("DIFF", $"Left  ({left.Length} chars): \"{Truncate(left, 80)}\"");
            ComparisonDebugLogger.Log("DIFF", $"Right ({right.Length} chars): \"{Truncate(right, 80)}\"");

            // Apply masking before comparison
            string maskedLeft = ApplyMask(left);
            string maskedRight = ApplyMask(right);

            ComparisonDebugLogger.Log("DIFF", $"After masking - MaskedLeft: \"{Truncate(maskedLeft, 80)}\"");
            ComparisonDebugLogger.Log("DIFF", $"After masking - MaskedRight: \"{Truncate(maskedRight, 80)}\"");

            // Quick equality check
            if (maskedLeft == maskedRight)
            {
                ComparisonDebugLogger.Log("DIFF", "RESULT: Texts are EQUAL (after masking)");
                return new DiffResult
                {
                    AreEqual = true,
                    LeftSegments = new List<DiffSegment> { new DiffSegment { Text = left, Type = DiffType.Unchanged } },
                    RightSegments = new List<DiffSegment> { new DiffSegment { Text = right, Type = DiffType.Unchanged } }
                };
            }

            ComparisonDebugLogger.Log("DIFF", "Texts are DIFFERENT - computing LCS diff...");

            // Performance optimization: limit diff to first N characters for very long strings
            const int MaxDiffLength = 500;
            string diffLeft = maskedLeft.Length > MaxDiffLength ? maskedLeft.Substring(0, MaxDiffLength) : maskedLeft;
            string diffRight = maskedRight.Length > MaxDiffLength ? maskedRight.Substring(0, MaxDiffLength) : maskedRight;

            // Compute LCS matrix
            int[,] lcsMatrix = ComputeLCSMatrix(diffLeft, diffRight);

            // Backtrack to get diff segments
            var (leftSegs, rightSegs) = BacktrackDiff(diffLeft, diffRight, lcsMatrix);

            // If we truncated, add remainder as unchanged
            if (maskedLeft.Length > MaxDiffLength)
            {
                leftSegs.Add(new DiffSegment { Text = left.Substring(MaxDiffLength), Type = DiffType.Unchanged });
            }
            if (maskedRight.Length > MaxDiffLength)
            {
                rightSegs.Add(new DiffSegment { Text = right.Substring(MaxDiffLength), Type = DiffType.Unchanged });
            }

            // Map masked segments back to original text positions
            var finalLeftSegs = MapToOriginalText(left, maskedLeft, leftSegs);
            var finalRightSegs = MapToOriginalText(right, maskedRight, rightSegs);

            return new DiffResult
            {
                AreEqual = false,
                LeftSegments = finalLeftSegs,
                RightSegments = finalRightSegs
            };
        }

        /// <summary>
        /// Computes the LCS (Longest Common Subsequence) matrix using dynamic programming.
        /// </summary>
        private int[,] ComputeLCSMatrix(string a, string b)
        {
            int m = a.Length;
            int n = b.Length;
            int[,] dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (a[i - 1] == b[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                    }
                }
            }

            return dp;
        }

        /// <summary>
        /// Backtracks through the LCS matrix to identify diff segments.
        /// </summary>
        private (List<DiffSegment> left, List<DiffSegment> right) BacktrackDiff(string a, string b, int[,] dp)
        {
            var leftSegments = new List<DiffSegment>();
            var rightSegments = new List<DiffSegment>();

            int i = a.Length;
            int j = b.Length;

            // Temporary buffers for building segments
            var unchangedBuffer = new List<char>();
            var removedBuffer = new List<char>();
            var addedBuffer = new List<char>();

            while (i > 0 || j > 0)
            {
                if (i > 0 && j > 0 && a[i - 1] == b[j - 1])
                {
                    // Characters match - unchanged
                    FlushBuffers(leftSegments, rightSegments, removedBuffer, addedBuffer);
                    unchangedBuffer.Insert(0, a[i - 1]);
                    i--;
                    j--;
                }
                else if (j > 0 && (i == 0 || dp[i, j - 1] >= dp[i - 1, j]))
                {
                    // Character added in right
                    FlushUnchanged(leftSegments, rightSegments, unchangedBuffer);
                    addedBuffer.Insert(0, b[j - 1]);
                    j--;
                }
                else if (i > 0)
                {
                    // Character removed from left
                    FlushUnchanged(leftSegments, rightSegments, unchangedBuffer);
                    removedBuffer.Insert(0, a[i - 1]);
                    i--;
                }
            }

            // Flush remaining buffers
            FlushUnchanged(leftSegments, rightSegments, unchangedBuffer);
            FlushBuffers(leftSegments, rightSegments, removedBuffer, addedBuffer);

            return (leftSegments, rightSegments);
        }

        private void FlushUnchanged(List<DiffSegment> left, List<DiffSegment> right, List<char> buffer)
        {
            if (buffer.Count > 0)
            {
                string text = new string(buffer.ToArray());
                left.Insert(0, new DiffSegment { Text = text, Type = DiffType.Unchanged });
                right.Insert(0, new DiffSegment { Text = text, Type = DiffType.Unchanged });
                buffer.Clear();
            }
        }

        private void FlushBuffers(List<DiffSegment> left, List<DiffSegment> right,
            List<char> removedBuffer, List<char> addedBuffer)
        {
            if (removedBuffer.Count > 0)
            {
                left.Insert(0, new DiffSegment { Text = new string(removedBuffer.ToArray()), Type = DiffType.Removed });
                removedBuffer.Clear();
            }
            if (addedBuffer.Count > 0)
            {
                right.Insert(0, new DiffSegment { Text = new string(addedBuffer.ToArray()), Type = DiffType.Added });
                addedBuffer.Clear();
            }
        }

        /// <summary>
        /// Maps diff segments from masked text back to original text positions.
        /// </summary>
        private List<DiffSegment> MapToOriginalText(string original, string masked, List<DiffSegment> segments)
        {
            // If no masking was applied, return segments as-is
            if (original == masked)
                return segments;

            // For simplicity, when masking is applied, we rebuild segments from original
            // This is a simplified approach - a more sophisticated implementation would
            // track character mappings during masking
            var result = new List<DiffSegment>();
            int origPos = 0;

            foreach (var seg in segments)
            {
                if (seg.Type == DiffType.Unchanged)
                {
                    // Find the corresponding unchanged portion in original
                    int len = Math.Min(seg.Text.Length, original.Length - origPos);
                    if (len > 0)
                    {
                        result.Add(new DiffSegment
                        {
                            Text = original.Substring(origPos, len),
                            Type = DiffType.Unchanged
                        });
                        origPos += len;
                    }
                }
                else
                {
                    // For added/removed, use the segment as-is
                    result.Add(seg);
                }
            }

            // Add any remaining original text
            if (origPos < original.Length)
            {
                result.Add(new DiffSegment
                {
                    Text = original.Substring(origPos),
                    Type = DiffType.Unchanged
                });
            }

            return result;
        }
    }

    /// <summary>
    /// Result of a text comparison operation.
    /// </summary>
    public class DiffResult
    {
        /// <summary>
        /// True if the texts are equal (after masking).
        /// </summary>
        public bool AreEqual { get; set; }

        /// <summary>
        /// Diff segments for the left (source) text.
        /// </summary>
        public List<DiffSegment> LeftSegments { get; set; }

        /// <summary>
        /// Diff segments for the right (target) text.
        /// </summary>
        public List<DiffSegment> RightSegments { get; set; }
    }

    /// <summary>
    /// A segment of text with its diff type.
    /// </summary>
    public class DiffSegment
    {
        /// <summary>
        /// The text content of this segment.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The type of difference for this segment.
        /// </summary>
        public DiffType Type { get; set; }
    }

    /// <summary>
    /// Type of difference for a text segment.
    /// </summary>
    public enum DiffType
    {
        /// <summary>
        /// Text is unchanged between left and right.
        /// </summary>
        Unchanged,

        /// <summary>
        /// Text was added (exists only in right).
        /// </summary>
        Added,

        /// <summary>
        /// Text was removed (exists only in left).
        /// </summary>
        Removed
    }
}
