using IndiLogs_3._0.Models;
using IndiLogs_3._0.Services.Interfaces;
using IndiLogs_3._0.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace IndiLogs_3._0.ViewModels.Components
{
    public partial class CaseManagementViewModel
    {
        // ── Coloring: open coloring windows for Main/App/DifferentLogs tabs ──

        private async Task OpenColoringWindow(object? obj)
        {
            // ── Different Logs tab (index 12): route to its own coloring logic ──
            if (_parent.SelectedTabIndex == 12)
            {
                await OpenDifferentLogsColoringWindow(obj);
                return;
            }

            try
            {
                var win = _viewFactory.Create<ColoringWindow>();
                bool isAppTab = _parent.SelectedTabIndex == 1;
                var currentRulesSource = isAppTab ? AppColoringRules : MainColoringRules;
                var rulesCopy = currentRulesSource.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    var newRules = win.ResultConditions;
                    _sessionVM.IsBusy = true;
                    _sessionVM.StatusMessage = isAppTab ? "Applying APP Colors..." : "Applying Main Colors...";

                    if (isAppTab)
                    {
                        AppColoringRules = newRules;
                        if (_sessionVM.AllAppLogsCache != null)
                        {
                            await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllAppLogsCache, true);
                            await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllAppLogsCache, AppColoringRules);
                        }
                    }
                    else
                    {
                        MainColoringRules = newRules;
                        if (_sessionVM.AllLogsCache != null)
                        {
                            await _coloringService.ApplyDefaultColorsAsync(_sessionVM.AllLogsCache, false);
                            await _coloringService.ApplyCustomColoringAsync(_sessionVM.AllLogsCache, MainColoringRules);
                        }
                    }

                    _dispatcher.Post(() =>
                    {
                        // Notify Active Filters panel so coloring labels appear
                        _parent.NotifyPropertyChanged(nameof(_parent.ActiveFilters));
                        _parent.NotifyPropertyChanged(nameof(_parent.HasActiveFilters));
                    });

                    _sessionVM.IsBusy = false;
                    _sessionVM.StatusMessage = "Colors Updated.";
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error: {ex.Message}");
                _sessionVM.IsBusy = false;
            }
        }

        /// <summary>
        /// Opens the coloring window targeting the Different Logs tab (tab 12).
        /// Uses dynamic fields from the loaded plugin columns.
        /// </summary>
        private async Task OpenDifferentLogsColoringWindow(object? obj)
        {
            try
            {
                var diffVM = _parent.DifferentLogsVM;
                if (diffVM == null || !diffVM.HasFile) return;

                // Safety: rebuild available fields if empty
                if (diffVM.AvailableFields == null || diffVM.AvailableFields.Count == 0)
                    diffVM.BuildAvailableFields();

                // Create window with dynamic fields from plugin columns
                var win = _viewFactory.Create<ColoringWindow>(diffVM.AvailableFields ?? new List<string>());

                // Load existing rules if any
                var rulesCopy = diffVM.ColoringRules.Select(r => r.Clone()).ToList();
                win.LoadSavedRules(rulesCopy);

                if (win.ShowDialog() == true)
                {
                    var newRules = win.ResultConditions;
                    diffVM.ColoringRules = newRules;

                    _sessionVM.IsBusy = true;
                    _sessionVM.StatusMessage = "Applying Colors to Different Logs...";

                    // Reset colors first, then apply custom rules
                    foreach (var entry in diffVM.AllLogEntries)
                        entry.CustomColor = null;

                    await _coloringService.ApplyCustomColoringAsync(diffVM.AllLogEntries, newRules);

                    int coloredCount = diffVM.AllLogEntries.Count(e => e.CustomColor.HasValue);

                    // Re-create the collection on the UI thread so WPF sees the
                    // new RowBackground values via fresh DataGridRow containers.
                    diffVM.FilteredEntries = new ObservableCollection<LogEntry>(
                        diffVM.IsFilterActive && diffVM.FilterRoot != null
                            ? diffVM.AllLogEntries.Where(l => _filterVM.EvaluateFilterNode(l, diffVM.FilterRoot))
                            : diffVM.AllLogEntries);

                    _sessionVM.IsBusy = false;
                    _sessionVM.StatusMessage = $"Colors Applied — {coloredCount}/{diffVM.AllLogEntries.Count} entries colored";
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error: {ex.Message}");
                _sessionVM.IsBusy = false;
            }
        }
    }
}
