using IndiLogs_3._0.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        /// <summary>
        /// Builds the hierarchical APP logger tree from the provided log entries.
        /// </summary>
        public void BuildLoggerTree(IEnumerable<LogEntry> logs)
        {
            if (logs == null || !logs.Any())
            {
                LoggerTreeRoot = new ObservableCollection<LoggerNode>();
                return;
            }

            int totalCount = logs.Count();
            var rootNode = new LoggerNode { Name = "All Loggers", FullPath = "", IsExpanded = true, Count = totalCount };

            var loggerGroups = logs.GroupBy(l => l.Logger)
                                   .Select(g => new { Name = g.Key, Count = g.Count() })
                                   .ToList();

            foreach (var group in loggerGroups)
            {
                if (string.IsNullOrEmpty(group.Name)) continue;
                var parts = group.Name.Split('.');
                AddNodeRecursive(rootNode, parts, 0, "", group.Count);
            }

            LoggerTreeRoot = new ObservableCollection<LoggerNode>(rootNode.Children);
        }

        /// <summary>
        /// Builds the hierarchical PLC logger tree from the provided log entries.
        /// </summary>
        public void BuildPlcLoggerTree(IEnumerable<LogEntry> logs)
        {
            if (logs == null || !logs.Any())
            {
                PlcLoggerTreeRoot = new ObservableCollection<LoggerNode>();
                return;
            }

            int totalCount = logs.Count();
            var rootNode = new LoggerNode { Name = "All Loggers", FullPath = "", IsExpanded = true, Count = totalCount };

            var loggerGroups = logs.GroupBy(l => l.Logger)
                                   .Select(g => new { Name = g.Key, Count = g.Count() })
                                   .ToList();

            foreach (var group in loggerGroups)
            {
                if (string.IsNullOrEmpty(group.Name)) continue;
                var parts = group.Name.Split('.');
                AddNodeRecursive(rootNode, parts, 0, "", group.Count);
            }

            PlcLoggerTreeRoot = new ObservableCollection<LoggerNode>(rootNode.Children);
        }

        private void AddNodeRecursive(LoggerNode parent, string[] parts, int index, string currentPath, int count)
        {
            if (index >= parts.Length) return;
            string part = parts[index];
            string newPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";

            var child = parent.Children.FirstOrDefault(c => c.Name == part);
            if (child == null)
            {
                child = new LoggerNode { Name = part, FullPath = newPath };
                int insertIdx = 0;
                while (insertIdx < parent.Children.Count && string.Compare(parent.Children[insertIdx].Name, part) < 0)
                    insertIdx++;
                parent.Children.Insert(insertIdx, child);
            }
            child.Count += count;
            AddNodeRecursive(child, parts, index + 1, newPath, count);
        }

        /// <summary>
        /// Clears all APP logger tree filter state (hidden loggers, show-only selections).
        /// </summary>
        public void ResetTreeFilters()
        {
            _treeHiddenLoggers.Clear();
            _treeHiddenPrefixes.Clear();
            _treeShowOnlyLogger = null;
            _treeShowOnlyPrefix = null;
        }

        /// <summary>
        /// Clears all PLC logger tree filter state (hidden loggers, show-only selections).
        /// </summary>
        public void ResetPlcTreeFilters()
        {
            _plcTreeHiddenLoggers.Clear();
            _plcTreeHiddenPrefixes.Clear();
            _plcTreeShowOnlyLogger = null;
            _plcTreeShowOnlyPrefix = null;
        }

        private void ResetPlcVisualStates()
        {
            foreach (var rootNode in PlcLoggerTreeRoot)
            {
                ResetNodeVisualState(rootNode);
            }
        }

        private void ResetNodeVisualState(LoggerNode node)
        {
            node.IsHidden = false;
            node.IsActive = false;
            foreach (var child in node.Children)
                ResetNodeVisualState(child);
        }

        private void MarkAllNodesShowOnly(string activePrefix, ObservableCollection<LoggerNode> treeRoot)
        {
            foreach (var rootNode in treeRoot)
            {
                MarkNodeShowOnly(rootNode, activePrefix);
            }
        }

        /// <summary>
        /// Recursively set IsHidden and IsActive on all children of a node
        /// </summary>
        private void SetChildrenVisualState(LoggerNode node, bool isHidden, bool isActive)
        {
            if (node.Children == null) return;
            foreach (var child in node.Children)
            {
                child.IsHidden = isHidden;
                child.IsActive = isActive;
                SetChildrenVisualState(child, isHidden, isActive);
            }
        }

        /// <summary>
        /// Mark all nodes as hidden, then mark the matching node (by prefix) and its children as active.
        /// This gives clear visual feedback for "Show Only This" / "Show With Children".
        /// </summary>
        private void MarkAllNodesShowOnly(string activePrefix)
        {
            foreach (var rootNode in LoggerTreeRoot)
            {
                MarkNodeShowOnly(rootNode, activePrefix);
            }
        }

        private void MarkNodeShowOnly(LoggerNode node, string activePrefix)
        {
            bool isMatch = node.FullPath != null &&
                (node.FullPath.Equals(activePrefix, System.StringComparison.OrdinalIgnoreCase) ||
                 node.FullPath.StartsWith(activePrefix + ".", System.StringComparison.OrdinalIgnoreCase));

            // Also check if this node is a parent/ancestor of the active prefix
            bool isAncestor = activePrefix.StartsWith(node.FullPath + ".", System.StringComparison.OrdinalIgnoreCase);

            if (isMatch)
            {
                // This node matches - mark it and all children as active (green)
                node.IsHidden = false;
                node.IsActive = true;
                SetChildrenVisualState(node, false, true);
            }
            else if (isAncestor)
            {
                // This is a parent of the target - keep normal (not hidden, not active)
                node.IsHidden = false;
                node.IsActive = false;
                // Recurse into children to find the matching one
                if (node.Children != null)
                {
                    foreach (var child in node.Children)
                        MarkNodeShowOnly(child, activePrefix);
                }
            }
            else
            {
                // Not related - mark as hidden (greyed out with X)
                node.IsHidden = true;
                node.IsActive = false;
                SetChildrenVisualState(node, true, false);
            }
        }

        /// <summary>
        /// Reset all visual states (IsHidden + IsActive) on all tree nodes
        /// </summary>
        private void ResetAllVisualStates()
        {
            foreach (var rootNode in LoggerTreeRoot)
            {
                rootNode.IsHidden = false;
                rootNode.IsActive = false;
                SetChildrenVisualState(rootNode, false, false);
            }
        }

        /// <summary>
        /// Reset visual IsHidden state on all tree nodes (backward compat)
        /// </summary>
        private void ResetTreeVisualState()
        {
            ResetAllVisualStates();
        }

        /// <summary>
        /// Check if any column-based (non-tree) filters are active
        /// </summary>
        private bool HasAnyColumnFilter()
        {
            return _activeLoggerFilters.Any() || _activeThreadFilters.Any() || _activeMethodFilters.Any() ||
                   (_appFilterRoot != null && _appFilterRoot.Children.Count > 0) ||
                   _isAppTimeFocusActive;
        }
    }
}
