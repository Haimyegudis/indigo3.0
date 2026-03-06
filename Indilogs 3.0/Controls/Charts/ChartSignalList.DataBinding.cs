using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Services.Charts;

namespace IndiLogs_3._0.Controls.Charts
{
    public partial class ChartSignalList
    {
        /// <summary>
        /// Sets signal names (for CSV mode - legacy compatibility)
        /// </summary>
        public void SetSignals(List<string> signals)
        {
            _allItems.Clear();

            foreach (var signal in signals ?? new List<string>())
            {
                var item = CreateSignalItem(signal);
                _allItems.Add(item);
            }

            // CSV mode always shows full category tabs
            SetCategoryBarVisibility(true);
            ApplyFilters();
        }

        /// <summary>
        /// Sets full data package (for In-Memory mode with CHSTEP and Thread support)
        /// </summary>
        public void SetDataPackage(ChartDataPackage? package)
        {
            _allItems.Clear();

            if (package == null)
            {
                SetCategoryBarVisibility(false);
                ApplyFilters();
                return;
            }

            // Add regular signals (AXIS, IO) - use Category from SignalData
            foreach (var signal in package.Signals)
            {
                var item = CreateSignalItemFromData(signal);
                _allItems.Add(item);
            }

            // Add CHSTEP items (excluding MachineState which goes to timeline)
            foreach (var state in package.States)
            {
                if (state.Name.Equals("MachineState", StringComparison.OrdinalIgnoreCase))
                    continue;

                string displayName = !string.IsNullOrEmpty(state.Category)
                    ? $"{state.Category} > {state.Name}" : state.Name;
                string fullName = !string.IsNullOrEmpty(state.Category)
                    ? $"{state.Category}|{state.Name}" : state.Name;

                _allItems.Add(new SignalListItem
                {
                    FullName = fullName,
                    DisplayName = displayName,
                    TypeIcon = "G",
                    TypeColor = CHStepColor,
                    Category = SignalItemCategory.CHStep,
                    StateData = state
                });
            }

            // Add Thread items — pre-group with dictionary (avoids LINQ overhead on 100K+ messages)
            if (package.ThreadMessages != null && package.ThreadMessages.Count > 0)
            {
                var threadDict = new Dictionary<string, List<ThreadMessageData>>(StringComparer.OrdinalIgnoreCase);
                foreach (var msg in package.ThreadMessages)
                {
                    if (string.IsNullOrEmpty(msg.ThreadName)) continue;
                    if (!threadDict.TryGetValue(msg.ThreadName, out var list))
                    {
                        list = new List<ThreadMessageData>();
                        threadDict[msg.ThreadName] = list;
                    }
                    list.Add(msg);
                }

                foreach (var kvp in threadDict.OrderBy(k => k.Key))
                {
                    _allItems.Add(new SignalListItem
                    {
                        FullName = kvp.Key,
                        DisplayName = $"{kvp.Key} ({kvp.Value.Count} msgs)",
                        TypeIcon = "T",
                        TypeColor = ThreadColor,
                        Category = SignalItemCategory.Thread,
                        ThreadName = kvp.Key,
                        ThreadMessages = kvp.Value
                    });
                }
            }

            // Add Events item if events exist
            if (package.Events != null && package.Events.Count > 0)
            {
                _allItems.Add(new SignalListItem
                {
                    FullName = "[Events]",
                    DisplayName = $"Events ({package.Events.Count})",
                    TypeIcon = "E",
                    TypeColor = EventsColor,
                    Category = SignalItemCategory.Events
                });
            }

            // S4-5 (IoTerminal): show simplified category tabs (IO only, EM Stats added separately)
            if (package.SuppressGapDetection)
                SetSimplifiedCategoryBar();
            else
                SetCategoryBarVisibility(true);

            ApplyFilters();
        }

        /// <summary>
        /// Adds an EM Statistics entry to the signal list (displayed as a single item the user can double-click).
        /// </summary>
        public void AddEmStatisticsItems(int stateCount)
        {
            _allItems.Add(new SignalListItem
            {
                FullName = "[EM Statistics]",
                DisplayName = $"EM Statistics ({stateCount} components)",
                TypeIcon = "M",
                TypeColor = EmStatsColor,
                Category = SignalItemCategory.EmStats
            });

            // Make EM Stats category button visible
            if (EmStatsBtn != null) EmStatsBtn.Visibility = Visibility.Visible;

            ApplyFilters();
        }

        /// <summary>
        /// Shows or hides the category filter tab bar.
        /// When hidden, AllBtn is auto-checked so all items are visible.
        /// </summary>
        private void SetCategoryBarVisibility(bool visible)
        {
            var categoryBar = AllBtn?.Parent as FrameworkElement; // UniformGrid
            var border = categoryBar?.Parent as FrameworkElement;  // Border wrapper
            if (border != null)
                border.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            // Reset all buttons to visible (EM Stats stays collapsed — only shown when EM data exists)
            if (visible)
            {
                if (AxisBtn != null) AxisBtn.Visibility = Visibility.Visible;
                if (IOBtn != null) IOBtn.Visibility = Visibility.Visible;
                if (CHStepBtn != null) CHStepBtn.Visibility = Visibility.Visible;
                if (ThreadBtn != null) ThreadBtn.Visibility = Visibility.Visible;
                if (EventsBtn != null) EventsBtn.Visibility = Visibility.Visible;
                if (EmStatsBtn != null) EmStatsBtn.Visibility = Visibility.Collapsed;
            }

            // Ensure AllBtn is checked so no filter is active
            if (!visible && AllBtn != null)
                AllBtn.IsChecked = true;
        }

        /// <summary>
        /// Shows a simplified category bar for S4-5: only ALL and IO (hides AXIS, CHSTEP, THREAD, EVENTS).
        /// </summary>
        private void SetSimplifiedCategoryBar()
        {
            var categoryBar = AllBtn?.Parent as FrameworkElement;
            var border = categoryBar?.Parent as FrameworkElement;
            if (border != null)
                border.Visibility = Visibility.Visible;

            // Show only ALL, IO, and EM STATS
            if (AxisBtn != null) AxisBtn.Visibility = Visibility.Collapsed;
            if (CHStepBtn != null) CHStepBtn.Visibility = Visibility.Collapsed;
            if (ThreadBtn != null) ThreadBtn.Visibility = Visibility.Collapsed;
            if (EventsBtn != null) EventsBtn.Visibility = Visibility.Collapsed;
            if (IOBtn != null) IOBtn.Visibility = Visibility.Visible;
            if (EmStatsBtn != null) EmStatsBtn.Visibility = Visibility.Visible;

            if (AllBtn != null) AllBtn.IsChecked = true;
        }

        /// <summary>
        /// Creates signal item from SignalData (uses Category from data)
        /// </summary>
        private SignalListItem CreateSignalItemFromData(SignalData signalData)
        {
            var item = new SignalListItem
            {
                FullName = signalData.Name,
                DisplayName = signalData.Name
            };

            // Use the Category from the parsed SignalData
            string category = signalData.Category?.Trim() ?? "";

            // Match exact category names from ChartDataTransferService
            if (category.Equals("Axis", StringComparison.OrdinalIgnoreCase) ||
                category.Equals("AxisMon", StringComparison.OrdinalIgnoreCase))
            {
                item.Category = SignalItemCategory.Axis;
                item.TypeIcon = "A";
                item.TypeColor = AxisColor;
            }
            else if (category.Equals("IO", StringComparison.OrdinalIgnoreCase))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else
            {
                // S4-5 IoTerminal: device names (e.g. "BIM[0]", "ECM") classify as IO
                // since all IoTerminal CSV signals are IO data
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }

            return item;
        }

        /// <summary>
        /// Creates signal item from name only (legacy for CSV mode).
        /// Recognizes exported CSV header patterns to assign proper categories.
        /// </summary>
        private SignalListItem CreateSignalItem(string signalName)
        {
            var item = new SignalListItem
            {
                FullName = signalName,
                DisplayName = signalName
            };

            string lower = signalName.ToLower();

            // Check for exported CSV patterns first (from CsvExportService):
            //   Axis:  "Subsys-Motor-Param [Thread]" with Param = SetP/ActP/SetV/ActV/Trq/LagErr
            //   IO:    "Subsys-Comp-Value [IOs-I]" or [IOs-Q]
            //   CHStep: column name contains § separator
            //   Thread: ends with _Message (but not Events_Message)

            if (lower.Contains("§") || lower.StartsWith("chstep"))
            {
                item.Category = SignalItemCategory.CHStep;
                item.TypeIcon = "G";
                item.TypeColor = CHStepColor;
            }
            else if (lower.Contains("[ios-") || lower.Contains("[io_mon"))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else if (lower.Contains("-setp") || lower.Contains("-actp") || lower.Contains("-setv") ||
                     lower.Contains("-actv") || lower.Contains("-trq") || lower.Contains("-lagerr"))
            {
                item.Category = SignalItemCategory.Axis;
                item.TypeIcon = "A";
                item.TypeColor = AxisColor;
            }
            else if (lower.Contains("-value") || lower.Contains("-mottemp") || lower.Contains("-drvtemp"))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else if (lower.Contains("axis") || lower.Contains("motor") || lower.Contains("position") ||
                     lower.Contains("velocity") || lower.Contains("encoder") || lower.Contains("servo"))
            {
                item.Category = SignalItemCategory.Axis;
                item.TypeIcon = "A";
                item.TypeColor = AxisColor;
            }
            else if (lower.Contains("io_") || lower.Contains("sensor") || lower.Contains("switch") ||
                     lower.Contains("valve") || lower.Contains("relay") ||
                     lower.StartsWith("di_") || lower.StartsWith("do_") ||
                     lower.StartsWith("ai_") || lower.StartsWith("ao_"))
            {
                item.Category = SignalItemCategory.IO;
                item.TypeIcon = "I";
                item.TypeColor = IOColor;
            }
            else
            {
                item.Category = SignalItemCategory.All;
                item.TypeIcon = "S";
                item.TypeColor = DefaultColor;
            }

            return item;
        }
    }
}
