using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        private void OpenThreadFilter(object? obj)
        {
            // Check which tab is active and use appropriate cache
            bool isAppTab = _parent.SelectedTabIndex == 1;
            var cache = isAppTab ? _sessionVM.AllAppLogsCache : _sessionVM.AllLogsCache;

            if (cache == null || !cache.Any()) return;
            var threads = cache.Select(l => l.ThreadName).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();

            // Save the currently selected log and its scroll position BEFORE opening the dialog
            var savedSelectedLog = _parent.SelectedLog;
            if (savedSelectedLog != null)
            {
                _parent.SaveScrollPosition(savedSelectedLog);
            }

            var win = _viewFactory.Create<Views.ThreadFilterWindow>(threads);
            win.Title = "Filter by Thread";

            // Position window near the button that was clicked
            if (obj is FrameworkElement buttonElement)
            {
                win.Owner = _windowOwner.GetOwner();
                win.WindowStartupLocation = WindowStartupLocation.Manual;
                win.PositionNearElement(buttonElement);
            }

            if (win.ShowDialog() == true)
            {
                // Use the correct thread filter list per tab
                var threadList = isAppTab ? _appActiveThreadFilters : _activeThreadFilters;

                if (win.ShouldClear)
                {
                    threadList.Clear();
                    // Also remove thread conditions from the filter tree
                    RemoveThreadConditionsFromFilterTree(isAppTab);
                    CheckIfFiltersEmpty(isAppTab);
                }
                else if (win.SelectedThreads != null && win.SelectedThreads.Any())
                {
                    threadList.Clear();
                    threadList.AddRange(win.SelectedThreads);
                    // Sync thread filters to filter tree so they appear in Filter Window
                    SyncThreadFiltersToFilterTree(isAppTab, win.SelectedThreads);
                    SetFilterActive(isAppTab);
                }
                ToggleFilterView(true); // Must re-trigger filter

                // Restore the selected log and scroll to it after CLEAR
                if (win.ShouldClear && savedSelectedLog != null)
                {
                    _parent.SelectedLog = savedSelectedLog;
                    _parent.ScrollToLog(savedSelectedLog);
                }
            }
        }

        /// <summary>
        /// Syncs thread filters to the filter tree so they appear in the Filter Window.
        /// Creates an OR group with all selected threads as conditions.
        /// </summary>
        private void SyncThreadFiltersToFilterTree(bool isAppTab, List<string> selectedThreads)
        {
            // Get or create the root filter node
            var currentRoot = isAppTab ? AppFilterRoot : MainFilterRoot;

            if (currentRoot == null)
            {
                currentRoot = new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" };
                if (isAppTab) AppFilterRoot = currentRoot;
                else MainFilterRoot = currentRoot;
            }

            // First, remove any existing thread filter group
            RemoveThreadConditionsFromFilterTree(isAppTab);

            // If only one thread, add it directly as a condition
            if (selectedThreads.Count == 1)
            {
                var condition = new FilterNode
                {
                    Type = NodeType.Condition,
                    Field = "ThreadName",
                    Operator = "Equals",
                    Value = selectedThreads[0]
                };
                currentRoot.Children.Add(condition);
            }
            else if (selectedThreads.Count > 1)
            {
                // Create an OR group for multiple threads
                var threadGroup = new FilterNode
                {
                    Type = NodeType.Group,
                    LogicalOperator = "OR"
                };

                foreach (var thread in selectedThreads)
                {
                    var condition = new FilterNode
                    {
                        Type = NodeType.Condition,
                        Field = "ThreadName",
                        Operator = "Equals",
                        Value = thread
                    };
                    threadGroup.Children.Add(condition);
                }

                currentRoot.Children.Add(threadGroup);
            }

            // Notify property changed
            if (isAppTab) OnPropertyChanged(nameof(AppFilterRoot));
            else OnPropertyChanged(nameof(MainFilterRoot));
        }

        /// <summary>
        /// Removes all ThreadName conditions from the filter tree.
        /// </summary>
        private void RemoveThreadConditionsFromFilterTree(bool isAppTab)
        {
            var currentRoot = isAppTab ? AppFilterRoot : MainFilterRoot;
            if (currentRoot == null || currentRoot.Children == null) return;

            // Remove thread conditions recursively
            RemoveThreadConditionsRecursive(currentRoot);

            // Notify property changed
            if (isAppTab) OnPropertyChanged(nameof(AppFilterRoot));
            else OnPropertyChanged(nameof(MainFilterRoot));
        }

        private void RemoveThreadConditionsRecursive(FilterNode node)
        {
            if (node.Children == null) return;

            // Find items to remove (ThreadName conditions and groups containing only ThreadName conditions)
            var toRemove = new List<FilterNode>();

            foreach (var child in node.Children)
            {
                if (child.Type == NodeType.Condition && child.Field == "ThreadName")
                {
                    toRemove.Add(child);
                }
                else if (child.Type == NodeType.Group)
                {
                    // Check if this group contains only ThreadName conditions
                    if (child.Children != null && child.Children.All(c => c.Type == NodeType.Condition && c.Field == "ThreadName"))
                    {
                        toRemove.Add(child);
                    }
                    else
                    {
                        // Recursively clean nested groups
                        RemoveThreadConditionsRecursive(child);
                    }
                }
            }

            foreach (var item in toRemove)
            {
                node.Children.Remove(item);
            }
        }
    }
}
