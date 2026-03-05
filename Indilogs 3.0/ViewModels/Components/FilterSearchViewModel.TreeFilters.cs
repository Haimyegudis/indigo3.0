using IndiLogs_3._0.Models;
using System.Linq;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class FilterSearchViewModel
    {
        private void ExecuteTreeShowThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    _plcTreeShowOnlyLogger = null;
                    _plcTreeShowOnlyPrefix = null;
                    _plcTreeHiddenLoggers.Remove(node.FullPath);
                    _plcTreeHiddenPrefixes.Remove(node.FullPath);
                    var prefixesToRemove = _plcTreeHiddenPrefixes
                        .Where(p => node.FullPath == p || node.FullPath.StartsWith(p + ".")).ToList();
                    foreach (var p in prefixesToRemove) _plcTreeHiddenPrefixes.Remove(p);

                    node.IsHidden = false;
                    node.IsActive = false;
                    SetChildrenVisualState(node, false, false);

                    bool hasAny = _plcTreeHiddenLoggers.Count > 0 || _plcTreeHiddenPrefixes.Count > 0;
                    IsMainFilterActive = hasAny || IsMainFilterActive;
                    if (!hasAny) ResetPlcVisualStates();
                    ToggleFilterView(hasAny);
                }
                else
                {
                    _treeShowOnlyLogger = null;
                    _treeShowOnlyPrefix = null;
                    _treeHiddenLoggers.Remove(node.FullPath);
                    _treeHiddenPrefixes.Remove(node.FullPath);
                    var prefixesToRemove = _treeHiddenPrefixes
                        .Where(p => node.FullPath == p || node.FullPath.StartsWith(p + ".")).ToList();
                    foreach (var p in prefixesToRemove) _treeHiddenPrefixes.Remove(p);

                    node.IsHidden = false;
                    node.IsActive = false;
                    SetChildrenVisualState(node, false, false);

                    bool hasAnyTreeFilter = _treeHiddenLoggers.Count > 0 || _treeHiddenPrefixes.Count > 0;
                    IsAppFilterActive = hasAnyTreeFilter || HasAnyColumnFilter();
                    if (!IsAppFilterActive) ResetAllVisualStates();
                    ToggleFilterView(IsAppFilterActive);
                }
            }
        }

        private void ExecuteTreeHideThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    if (_plcTreeShowOnlyPrefix != null || _plcTreeShowOnlyLogger != null) ResetPlcVisualStates();
                    _plcTreeShowOnlyLogger = null;
                    _plcTreeShowOnlyPrefix = null;
                    if (node.Children != null && node.Children.Count > 0)
                        _plcTreeHiddenPrefixes.Add(node.FullPath);
                    else
                        _plcTreeHiddenLoggers.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    ToggleFilterView(true);
                }
                else
                {
                    if (_treeShowOnlyPrefix != null || _treeShowOnlyLogger != null) ResetAllVisualStates();
                    _treeShowOnlyLogger = null;
                    _treeShowOnlyPrefix = null;
                    if (node.Children != null && node.Children.Count > 0)
                        _treeHiddenPrefixes.Add(node.FullPath);
                    else
                        _treeHiddenLoggers.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeShowOnlyThis(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    ResetPlcTreeFilters();
                    _plcTreeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath, PlcLoggerTreeRoot);
                    ToggleFilterView(true);
                }
                else
                {
                    ResetTreeFilters();
                    _treeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeShowWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    ResetPlcTreeFilters();
                    _plcTreeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath, PlcLoggerTreeRoot);
                    ToggleFilterView(true);
                }
                else
                {
                    ResetTreeFilters();
                    _treeShowOnlyPrefix = node.FullPath;
                    MarkAllNodesShowOnly(node.FullPath);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeHideWithChildren(object obj)
        {
            if (obj is LoggerNode node)
            {
                if (IsPlcTabActive)
                {
                    if (_plcTreeShowOnlyPrefix != null || _plcTreeShowOnlyLogger != null) ResetPlcVisualStates();
                    _plcTreeShowOnlyLogger = null;
                    _plcTreeShowOnlyPrefix = null;
                    _plcTreeHiddenPrefixes.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    ToggleFilterView(true);
                }
                else
                {
                    if (_treeShowOnlyPrefix != null || _treeShowOnlyLogger != null) ResetAllVisualStates();
                    _treeShowOnlyLogger = null;
                    _treeShowOnlyPrefix = null;
                    _treeHiddenPrefixes.Add(node.FullPath);
                    node.IsHidden = true;
                    node.IsActive = false;
                    SetChildrenVisualState(node, true, false);
                    IsAppFilterActive = true;
                    ToggleFilterView(true);
                }
            }
        }

        private void ExecuteTreeShowAll(object obj)
        {
            if (IsPlcTabActive)
            {
                ResetPlcTreeFilters();
                ResetPlcVisualStates();
                ToggleFilterView(false);
            }
            else
            {
                ResetTreeFilters();
                ResetAllVisualStates();
                IsAppFilterActive = HasAnyColumnFilter();
                ToggleFilterView(IsAppFilterActive);
            }
        }
    }
}
