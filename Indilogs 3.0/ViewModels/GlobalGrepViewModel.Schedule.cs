using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Schedule Management

        /// <summary>
        /// Builds a combined SearchCriteria from ALL current UI settings:
        /// quick search text (Section 2), structured conditions (Section 3),
        /// PLC/APP toggles, and time filters.
        /// </summary>
        private SearchCriteria BuildFullCriteria()
        {
            // Start with structured conditions + PLC/APP + time filters
            var criteria = BuildCriteria();

            // If there's also quick search text, add it as an additional group
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var quickGroup = new SearchConditionGroup
                {
                    Operator = ConditionOperator.Or,
                    Conditions = new List<SearchCondition>
                    {
                        new SearchCondition
                        {
                            Field = SelectedQuickSearchField,
                            Operator = UseRegex ? SearchOperator.Regex : SearchOperator.Contains,
                            Value = SearchQuery
                        }
                    }
                };
                criteria.Groups.Insert(0, quickGroup);
            }

            return criteria;
        }

        private void AddSchedule()
        {
            var schedule = new ScheduledSearch
            {
                Criteria = new SearchCriteria { SearchPLC = true, SearchAPP = true },
                ScheduleType = ScheduleType.Daily,
                RunTime = new TimeSpan(8, 0, 0),
                OutputDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "IndiLogs_GrepResults")
            };

            if (!ShowScheduleDialog("New Scheduled Search", schedule)) return;

            _schedulerService.AddSchedule(schedule);
            _taskSchedulerService.RegisterSchedule(schedule);
            Schedules.Add(schedule);
            StatusMessage = $"Schedule '{schedule.Name}' added.";

            // Immediately check if this schedule should run now
            _ = _schedulerService.TriggerCheckAsync();
        }

        private void EditSchedule()
        {
            if (SelectedSchedule == null) return;
            var schedule = SelectedSchedule;

            if (!ShowScheduleDialog("Edit Scheduled Search", schedule)) return;

            _schedulerService.UpdateSchedule(schedule);
            _taskSchedulerService.RegisterSchedule(schedule);
            var idx = Schedules.IndexOf(schedule);
            if (idx >= 0)
            {
                Schedules.RemoveAt(idx);
                Schedules.Insert(idx, schedule);
                SelectedSchedule = schedule;
            }
            StatusMessage = $"Schedule '{schedule.Name}' updated.";

            // Immediately check if this schedule should run now
            _ = _schedulerService.TriggerCheckAsync();

        }

        /// <summary>
        /// Self-contained schedule editor dialog. Configures EVERYTHING:
        /// name, timing, search criteria, locations, time filters, output.
        /// </summary>
        private bool ShowScheduleDialog(string title, ScheduledSearch schedule)
        {
            // ═══ THEME RESOURCES ═══
            var bgDark = (System.Windows.Media.Brush)Application.Current.FindResource("BgDark");
            var bgCard = (System.Windows.Media.Brush)Application.Current.FindResource("BgCard");
            var bgPanel = (System.Windows.Media.Brush)Application.Current.FindResource("BgPanel");
            var textPrimary = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
            var textSecondary = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");
            var borderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("BorderColor");
            var primaryColor = (System.Windows.Media.Brush)Application.Current.FindResource("PrimaryColor");
            System.Windows.Media.Brush dangerColor = null;
            try { dangerColor = (System.Windows.Media.Brush)Application.Current.FindResource("DangerColor"); } catch (Exception) { /* resource not found, use fallback */ }
            dangerColor = dangerColor ?? System.Windows.Media.Brushes.Red;

            bool dialogResult = false;

            // ═══ PARSE EXISTING CRITERIA ═══
            bool isSimpleMode = true;
            string existingSearchText = "";
            SearchField existingField = SearchField.Any;
            bool existingRegex = false;
            var existingConditions = new List<SearchCondition>();
            ConditionOperator existingCondOp = ConditionOperator.And;
            bool existingPLC = schedule.Criteria?.SearchPLC ?? true;
            bool existingAPP = schedule.Criteria?.SearchAPP ?? true;
            var existingLocIds = new HashSet<Guid>(schedule.Criteria?.LocationIds ?? new List<Guid>());
            DateTime? existingFileFrom = schedule.Criteria?.FileTimeFilter?.From;
            DateTime? existingFileTo = schedule.Criteria?.FileTimeFilter?.To;
            DateTime? existingResFrom = schedule.Criteria?.ResultTimeFilter?.From;
            DateTime? existingResTo = schedule.Criteria?.ResultTimeFilter?.To;

            if (schedule.Criteria?.Groups != null && schedule.Criteria.Groups.Count > 0)
            {
                var allConds = schedule.Criteria.Groups.SelectMany(g => g.Conditions).ToList();
                if (allConds.Count == 1 && schedule.Criteria.Groups.Count == 1)
                {
                    existingField = allConds[0].Field;
                    existingSearchText = allConds[0].Value ?? "";
                    existingRegex = allConds[0].Operator == SearchOperator.Regex;
                }
                else if (allConds.Count > 0)
                {
                    isSimpleMode = false;
                    existingCondOp = schedule.Criteria.Groups[0].Operator;
                    existingConditions.AddRange(allConds);
                }
            }

            // ═══ DIALOG WINDOW ═══
            var dialog = new Window
            {
                Title = title,
                Width = 700,
                Height = 920,
                MaxHeight = SystemParameters.WorkArea.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = bgDark
            };

            var scroll = new System.Windows.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Padding = new Thickness(18)
            };
            var root = new System.Windows.Controls.StackPanel();

            // ═══ HELPERS ═══
            Func<string, System.Windows.Controls.TextBlock> makeLabel = text =>
                new System.Windows.Controls.TextBlock
                {
                    Text = text, Foreground = textPrimary,
                    FontWeight = FontWeights.SemiBold, FontSize = 12,
                    Margin = new Thickness(0, 6, 0, 3)
                };

            Func<string, System.Windows.Controls.TextBox> makeTextBox = val =>
                new System.Windows.Controls.TextBox
                {
                    Text = val ?? "", Padding = new Thickness(6, 4, 6, 4),
                    Background = bgCard, Foreground = textPrimary,
                    BorderBrush = borderBrush, FontSize = 12
                };

            Func<string, System.Windows.Controls.StackPanel, System.Windows.Controls.Border> makeSection = (header, content) =>
            {
                content.Children.Insert(0, new System.Windows.Controls.TextBlock
                {
                    Text = header, FontWeight = FontWeights.Bold,
                    Foreground = primaryColor, FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 6)
                });
                return new System.Windows.Controls.Border
                {
                    Background = bgPanel, BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10),
                    Child = content
                };
            };

            // ════════════════════════════════════════════════════════
            // SECTION 1: SCHEDULE DETAILS
            // ════════════════════════════════════════════════════════
            var sec1 = new System.Windows.Controls.StackPanel();
            sec1.Children.Add(makeLabel("Schedule Name"));
            var nameBox = makeTextBox(schedule.Name);
            sec1.Children.Add(nameBox);
            var enabledCheck = new System.Windows.Controls.CheckBox
            {
                Content = "Enabled", Foreground = textPrimary,
                IsChecked = schedule.IsEnabled, FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0)
            };
            sec1.Children.Add(enabledCheck);

            // Scan Mode
            sec1.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(0, 8, 0, 4), Background = borderBrush
            });
            sec1.Children.Add(makeLabel("Scan Mode"));
            var scanModePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var radioSearchOnly = new System.Windows.Controls.RadioButton
            {
                Content = "Search Only", Foreground = textPrimary,
                IsChecked = schedule.ScanMode == ScanMode.SearchOnly,
                FontSize = 12, Margin = new Thickness(0, 0, 16, 0)
            };
            var radioStatsOnly = new System.Windows.Controls.RadioButton
            {
                Content = "Statistics Only", Foreground = textPrimary,
                IsChecked = schedule.ScanMode == ScanMode.StatisticsOnly,
                FontSize = 12, Margin = new Thickness(0, 0, 16, 0)
            };
            var radioSearchAndStats = new System.Windows.Controls.RadioButton
            {
                Content = "Search + Statistics", Foreground = textPrimary,
                IsChecked = schedule.ScanMode == ScanMode.SearchAndStatistics,
                FontSize = 12
            };
            scanModePanel.Children.Add(radioSearchOnly);
            scanModePanel.Children.Add(radioStatsOnly);
            scanModePanel.Children.Add(radioSearchAndStats);
            sec1.Children.Add(scanModePanel);
            sec1.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Search Only = find matching log entries. Statistics Only = compute error histograms, load distribution, gaps, state analysis (no search query needed). Search + Statistics = both.",
                Foreground = textSecondary, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

            root.Children.Add(makeSection("Schedule Details", sec1));

            // ════════════════════════════════════════════════════════
            // SECTION 2: WHEN TO RUN
            // ════════════════════════════════════════════════════════
            var sec2 = new System.Windows.Controls.StackPanel();

            sec2.Children.Add(makeLabel("Schedule Type"));
            var typeCombo = new System.Windows.Controls.ComboBox
            {
                Background = bgCard, Foreground = textPrimary,
                BorderBrush = borderBrush, FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3)
            };
            typeCombo.Items.Add("Once (specific date & time)");
            typeCombo.Items.Add("Daily");
            typeCombo.Items.Add("Weekly");
            typeCombo.Items.Add("Interval (repeat every N hours/days)");
            switch (schedule.ScheduleType)
            {
                case ScheduleType.Once: typeCombo.SelectedIndex = 0; break;
                case ScheduleType.Daily: typeCombo.SelectedIndex = 1; break;
                case ScheduleType.Weekly: typeCombo.SelectedIndex = 2; break;
                case ScheduleType.Interval: typeCombo.SelectedIndex = 3; break;
            }
            sec2.Children.Add(typeCombo);

            // Run Date (Once only)
            var dateLabel = makeLabel("Run Date");
            sec2.Children.Add(dateLabel);
            var datePicker = new System.Windows.Controls.DatePicker
            {
                SelectedDate = schedule.RunDate, FontSize = 12
            };
            sec2.Children.Add(datePicker);

            // Run Time (HH:mm)
            var timeLabel = makeLabel("Run Time (HH:mm)");
            sec2.Children.Add(timeLabel);
            var timePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var hourBox = new System.Windows.Controls.TextBox
            {
                Text = schedule.RunTime.Hours.ToString("00"),
                Width = 50, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush, FontSize = 14
            };
            var colonLbl = new System.Windows.Controls.TextBlock
            {
                Text = ":", Foreground = textPrimary, FontSize = 16,
                FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            var minuteBox = new System.Windows.Controls.TextBox
            {
                Text = schedule.RunTime.Minutes.ToString("00"),
                Width = 50, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush, FontSize = 14
            };
            timePanel.Children.Add(hourBox);
            timePanel.Children.Add(colonLbl);
            timePanel.Children.Add(minuteBox);
            timePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "(24-hour format)", Foreground = textSecondary,
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            });
            sec2.Children.Add(timePanel);

            // Days of Week (Weekly only)
            var daysLabel = makeLabel("Run on Days");
            sec2.Children.Add(daysLabel);
            var daysPanel = new System.Windows.Controls.WrapPanel();
            var dayCheckBoxes = new Dictionary<DayOfWeek, System.Windows.Controls.CheckBox>();
            var dayNames = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
            var shortDayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int di = 0; di < dayNames.Length; di++)
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = shortDayNames[di], Foreground = textPrimary,
                    IsChecked = schedule.RunDays?.Contains(dayNames[di]) == true,
                    Margin = new Thickness(0, 0, 10, 0), FontSize = 12
                };
                dayCheckBoxes[dayNames[di]] = cb;
                daysPanel.Children.Add(cb);
            }
            sec2.Children.Add(daysPanel);

            // Repeat Interval (Interval only) — value + unit
            var intervalLabel = makeLabel("Repeat Every");
            sec2.Children.Add(intervalLabel);
            var intervalPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var intervalBox = makeTextBox(schedule.RepeatIntervalValue.ToString());
            intervalBox.Width = 60;
            intervalBox.HorizontalAlignment = HorizontalAlignment.Left;
            var intervalUnitCombo = new System.Windows.Controls.ComboBox
            {
                Background = bgCard, Foreground = textPrimary,
                BorderBrush = borderBrush, FontSize = 12,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(4, 3, 4, 3)
            };
            intervalUnitCombo.Items.Add("Minutes");
            intervalUnitCombo.Items.Add("Hours");
            intervalUnitCombo.Items.Add("Days");
            switch (schedule.IntervalUnit)
            {
                case IntervalUnit.Minutes: intervalUnitCombo.SelectedIndex = 0; break;
                case IntervalUnit.Hours: intervalUnitCombo.SelectedIndex = 1; break;
                case IntervalUnit.Days: intervalUnitCombo.SelectedIndex = 2; break;
            }
            intervalPanel.Children.Add(intervalBox);
            intervalPanel.Children.Add(intervalUnitCombo);
            sec2.Children.Add(intervalPanel);

            // Visibility toggling
            Action updateWhenVisibility = () =>
            {
                int idx = typeCombo.SelectedIndex;
                dateLabel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
                datePicker.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
                // Show time for all types — label changes for Interval
                timeLabel.Visibility = Visibility.Visible;
                timePanel.Visibility = Visibility.Visible;
                timeLabel.Text = idx == 3 ? "Start Time (HH:mm)" : "Run Time (HH:mm)";
                daysLabel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
                daysPanel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
                intervalLabel.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
                intervalPanel.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            };
            typeCombo.SelectionChanged += (s, e) => updateWhenVisibility();
            updateWhenVisibility();

            root.Children.Add(makeSection("When to Run", sec2));

            // ════════════════════════════════════════════════════════
            // SECTION 3: WHAT TO SEARCH
            // ════════════════════════════════════════════════════════
            var sec3 = new System.Windows.Controls.StackPanel();

            // Search mode toggle
            var modePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var simpleRadio = new System.Windows.Controls.RadioButton
            {
                Content = "Simple Search", Foreground = textPrimary,
                IsChecked = isSimpleMode, FontSize = 12, Margin = new Thickness(0, 0, 20, 0)
            };
            var advancedRadio = new System.Windows.Controls.RadioButton
            {
                Content = "Advanced Conditions", Foreground = textPrimary,
                IsChecked = !isSimpleMode, FontSize = 12
            };
            modePanel.Children.Add(simpleRadio);
            modePanel.Children.Add(advancedRadio);
            sec3.Children.Add(modePanel);

            // --- Simple Search Panel ---
            var simplePanel = new System.Windows.Controls.StackPanel
            {
                Visibility = isSimpleMode ? Visibility.Visible : Visibility.Collapsed
            };
            var simpleFldRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            simpleFldRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Search in:", Foreground = textSecondary, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            var simpleFieldCombo = new System.Windows.Controls.ComboBox
            {
                Width = 110, FontSize = 11, Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush
            };
            foreach (var f in Enum.GetValues(typeof(SearchField)).Cast<SearchField>())
                simpleFieldCombo.Items.Add(f);
            simpleFieldCombo.SelectedItem = existingField;
            simpleFldRow.Children.Add(simpleFieldCombo);
            simplePanel.Children.Add(simpleFldRow);

            simplePanel.Children.Add(makeLabel("Search text"));
            var simpleTextBox = makeTextBox(existingSearchText);
            simplePanel.Children.Add(simpleTextBox);

            var simpleRegexCheck = new System.Windows.Controls.CheckBox
            {
                Content = "Use Regular Expression", Foreground = textPrimary,
                IsChecked = existingRegex, FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
            };
            simplePanel.Children.Add(simpleRegexCheck);
            sec3.Children.Add(simplePanel);

            // --- Advanced Conditions Panel ---
            var advancedPanel = new System.Windows.Controls.StackPanel
            {
                Visibility = isSimpleMode ? Visibility.Collapsed : Visibility.Visible
            };
            var advOpRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            advOpRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Combine conditions with:", Foreground = textSecondary,
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            var advOperatorCombo = new System.Windows.Controls.ComboBox
            {
                Width = 70, FontSize = 11, Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush
            };
            foreach (var op in Enum.GetValues(typeof(ConditionOperator)).Cast<ConditionOperator>())
                advOperatorCombo.Items.Add(op);
            advOperatorCombo.SelectedItem = existingCondOp;
            advOpRow.Children.Add(advOperatorCombo);
            advancedPanel.Children.Add(advOpRow);

            // Dynamic condition rows
            var condPanel = new System.Windows.Controls.StackPanel();
            var condFields = new List<System.Windows.Controls.ComboBox>();
            var condOps = new List<System.Windows.Controls.ComboBox>();
            var condValues = new List<System.Windows.Controls.TextBox>();
            var condNegates = new List<System.Windows.Controls.CheckBox>();
            var condRows = new List<System.Windows.Controls.Grid>();

            Action addConditionRow = null;
            addConditionRow = () =>
            {
                var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

                var fieldCb = new System.Windows.Controls.ComboBox
                {
                    FontSize = 11, Margin = new Thickness(0, 0, 3, 0),
                    Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush
                };
                foreach (var f in Enum.GetValues(typeof(SearchField)).Cast<SearchField>())
                    fieldCb.Items.Add(f);
                fieldCb.SelectedItem = SearchField.Any;

                var opCb = new System.Windows.Controls.ComboBox
                {
                    FontSize = 11, Margin = new Thickness(0, 0, 3, 0),
                    Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush
                };
                foreach (var op in Enum.GetValues(typeof(SearchOperator)).Cast<SearchOperator>())
                    opCb.Items.Add(op);
                opCb.SelectedItem = SearchOperator.Contains;

                var valBox = new System.Windows.Controls.TextBox
                {
                    FontSize = 11, Padding = new Thickness(4, 2, 4, 2),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = bgCard, Foreground = textPrimary, BorderBrush = borderBrush
                };

                var negCheck = new System.Windows.Controls.CheckBox
                {
                    Content = "Excl", Foreground = textPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0), FontSize = 10
                };

                var removeBtn = new System.Windows.Controls.Button
                {
                    Content = "X", Width = 22, Padding = new Thickness(0, 2, 0, 2),
                    Background = bgCard, Foreground = dangerColor,
                    BorderBrush = borderBrush, FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                System.Windows.Controls.Grid.SetColumn(fieldCb, 0);
                System.Windows.Controls.Grid.SetColumn(opCb, 1);
                System.Windows.Controls.Grid.SetColumn(valBox, 2);
                System.Windows.Controls.Grid.SetColumn(negCheck, 3);
                System.Windows.Controls.Grid.SetColumn(removeBtn, 4);

                row.Children.Add(fieldCb);
                row.Children.Add(opCb);
                row.Children.Add(valBox);
                row.Children.Add(negCheck);
                row.Children.Add(removeBtn);

                condRows.Add(row);
                condFields.Add(fieldCb);
                condOps.Add(opCb);
                condValues.Add(valBox);
                condNegates.Add(negCheck);
                condPanel.Children.Add(row);

                removeBtn.Click += (s, e) =>
                {
                    int ri = condRows.IndexOf(row);
                    if (ri >= 0)
                    {
                        condRows.RemoveAt(ri);
                        condFields.RemoveAt(ri);
                        condOps.RemoveAt(ri);
                        condValues.RemoveAt(ri);
                        condNegates.RemoveAt(ri);
                        condPanel.Children.Remove(row);
                    }
                };
            };

            // Pre-populate existing conditions
            if (existingConditions.Count > 0)
            {
                foreach (var c in existingConditions)
                {
                    addConditionRow();
                    condFields[condFields.Count - 1].SelectedItem = c.Field;
                    condOps[condOps.Count - 1].SelectedItem = c.Operator;
                    condValues[condValues.Count - 1].Text = c.Value ?? "";
                    condNegates[condNegates.Count - 1].IsChecked = c.Negate;
                }
            }
            else if (!isSimpleMode)
            {
                addConditionRow();
            }

            advancedPanel.Children.Add(condPanel);

            var addCondBtn = new System.Windows.Controls.Button
            {
                Content = "+ Add Condition", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 11, Padding = new Thickness(8, 3, 8, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            addCondBtn.Click += (s, e) => addConditionRow();
            advancedPanel.Children.Add(addCondBtn);
            sec3.Children.Add(advancedPanel);

            // Toggle simple/advanced
            simpleRadio.Checked += (s, e) =>
            {
                simplePanel.Visibility = Visibility.Visible;
                advancedPanel.Visibility = Visibility.Collapsed;
            };
            advancedRadio.Checked += (s, e) =>
            {
                simplePanel.Visibility = Visibility.Collapsed;
                advancedPanel.Visibility = Visibility.Visible;
                if (condRows.Count == 0) addConditionRow();
            };

            // Separator + PLC/APP
            sec3.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(0, 8, 0, 4), Background = borderBrush
            });
            sec3.Children.Add(makeLabel("Log types to scan"));
            var logTypeRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var plcCheck = new System.Windows.Controls.CheckBox
            {
                Content = "PLC logs", Foreground = textPrimary,
                IsChecked = existingPLC, FontSize = 12, Margin = new Thickness(0, 0, 20, 0)
            };
            var appCheck = new System.Windows.Controls.CheckBox
            {
                Content = "APP logs", Foreground = textPrimary,
                IsChecked = existingAPP, FontSize = 12
            };
            logTypeRow.Children.Add(plcCheck);
            logTypeRow.Children.Add(appCheck);
            sec3.Children.Add(logTypeRow);

            var sec3Border = makeSection("What to Search", sec3);
            root.Children.Add(sec3Border);

            // Toggle Section 3 visibility based on scan mode
            Action updateScanModeVisibility = () =>
            {
                bool needsSearch = radioSearchOnly.IsChecked == true || radioSearchAndStats.IsChecked == true;
                sec3Border.Visibility = needsSearch ? Visibility.Visible : Visibility.Collapsed;
            };
            radioSearchOnly.Checked += (s, e) => updateScanModeVisibility();
            radioStatsOnly.Checked += (s, e) => updateScanModeVisibility();
            radioSearchAndStats.Checked += (s, e) => updateScanModeVisibility();
            updateScanModeVisibility();

            // ════════════════════════════════════════════════════════
            // SECTION 4: WHERE TO SEARCH
            // ════════════════════════════════════════════════════════
            var sec4 = new System.Windows.Controls.StackPanel();
            var locationChecks = new Dictionary<Guid, System.Windows.Controls.CheckBox>();
            var locListPanel = new System.Windows.Controls.StackPanel();
            var locScroll = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 140,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Content = locListPanel
            };
            var noLocLabel = new System.Windows.Controls.TextBlock
            {
                Text = "No locations configured. Click '+ Add Location' below.",
                Foreground = textSecondary, FontSize = 11, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap
            };

            // Rebuilds the location checkbox list from _locationService
            Action rebuildLocationList = () =>
            {
                locationChecks.Clear();
                locListPanel.Children.Clear();
                foreach (var loc in _locationService.Locations)
                {
                    var cb = new System.Windows.Controls.CheckBox
                    {
                        Content = $"{loc.Name}  ({loc.Address} \u2014 {loc.BasePath})",
                        Foreground = textPrimary, FontSize = 11,
                        IsChecked = existingLocIds.Count == 0 || existingLocIds.Contains(loc.Id),
                        Margin = new Thickness(0, 0, 0, 3),
                        Tag = loc.Id
                    };
                    locationChecks[loc.Id] = cb;
                    locListPanel.Children.Add(cb);
                }
                noLocLabel.Visibility = _locationService.Locations.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
                locScroll.Visibility = _locationService.Locations.Count > 0
                    ? Visibility.Visible : Visibility.Collapsed;
            };

            sec4.Children.Add(noLocLabel);
            sec4.Children.Add(locScroll);
            rebuildLocationList();

            // Location management buttons
            var locBtnRow = new System.Windows.Controls.WrapPanel
            {
                Margin = new Thickness(0, 6, 0, 0)
            };

            var addLocBtn = new System.Windows.Controls.Button
            {
                Content = "+ Add Location", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            addLocBtn.Click += (s, e) =>
            {
                var result = ShowLocationDialog("Add Search Location", "", "", "");
                if (result == null) return;
                var newLoc = new SearchLocation
                {
                    Name = result.Value.name,
                    Address = result.Value.address,
                    BasePath = result.Value.path
                };
                _locationService.Add(newLoc);
                Locations.Add(newLoc);
                // After adding, mark new location as selected in this schedule
                existingLocIds.Add(newLoc.Id);
                rebuildLocationList();
            };

            var editLocBtn = new System.Windows.Controls.Button
            {
                Content = "Edit", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            editLocBtn.Click += (s, e) =>
            {
                // Find the first checked location to edit
                var checkedId = locationChecks.FirstOrDefault(kv => kv.Value.IsChecked == true).Key;
                if (checkedId == Guid.Empty)
                {
                    _dialogService.ShowInfo("Check a location to edit it.", "Edit Location");
                    return;
                }
                var loc = _locationService.Locations.FirstOrDefault(l => l.Id == checkedId);
                if (loc == null) return;
                var result = ShowLocationDialog("Edit Search Location", loc.Name, loc.Address, loc.BasePath);
                if (result == null) return;
                loc.Name = result.Value.name;
                loc.Address = result.Value.address;
                loc.BasePath = result.Value.path;
                _locationService.Update(loc);
                // Also update the main window's Locations collection
                var mainLoc = Locations.FirstOrDefault(l => l.Id == loc.Id);
                if (mainLoc != null) { mainLoc.Name = loc.Name; mainLoc.Address = loc.Address; mainLoc.BasePath = loc.BasePath; }
                rebuildLocationList();
            };

            var removeLocBtn = new System.Windows.Controls.Button
            {
                Content = "Remove", Background = bgCard,
                Foreground = dangerColor, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            removeLocBtn.Click += (s, e) =>
            {
                var checkedIds = locationChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                if (checkedIds.Count == 0)
                {
                    _dialogService.ShowInfo("Check the location(s) you want to remove.", "Remove Location");
                    return;
                }
                if (_dialogService.ShowConfirm($"Remove {checkedIds.Count} selected location(s)?", "Confirm") != MessageBoxResult.Yes) return;
                foreach (var id in checkedIds)
                {
                    _locationService.Remove(id);
                    var mainLoc = Locations.FirstOrDefault(l => l.Id == id);
                    if (mainLoc != null) Locations.Remove(mainLoc);
                    existingLocIds.Remove(id);
                }
                rebuildLocationList();
            };

            var selAllBtn = new System.Windows.Controls.Button
            {
                Content = "Select All", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            selAllBtn.Click += (s, e) => { foreach (var kv in locationChecks) kv.Value.IsChecked = true; };

            var selNoneBtn = new System.Windows.Controls.Button
            {
                Content = "Select None", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            selNoneBtn.Click += (s, e) => { foreach (var kv in locationChecks) kv.Value.IsChecked = false; };

            locBtnRow.Children.Add(addLocBtn);
            locBtnRow.Children.Add(editLocBtn);
            locBtnRow.Children.Add(removeLocBtn);
            locBtnRow.Children.Add(new System.Windows.Controls.Separator
            {
                Width = 1, Margin = new Thickness(4, 2, 4, 2),
                Background = borderBrush
            });
            locBtnRow.Children.Add(selAllBtn);
            locBtnRow.Children.Add(selNoneBtn);
            sec4.Children.Add(locBtnRow);

            root.Children.Add(makeSection("Where to Search", sec4));

            // ════════════════════════════════════════════════════════
            // SECTION 5: TIME FILTERS (optional)
            // ════════════════════════════════════════════════════════
            var sec5 = new System.Windows.Controls.StackPanel();

            // --- File modified date filter ---
            sec5.Children.Add(makeLabel("File modified date (skip files outside this range)"));

            // Relative time range presets
            var existingFileRelative = schedule.Criteria?.FileTimeFilter?.RelativeRange ?? RelativeTimeRange.None;
            var ffPresetPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 4)
            };
            var ffPresetNone = new System.Windows.Controls.RadioButton
            {
                Content = "Custom range", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingFileRelative == RelativeTimeRange.None,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var ffPreset24h = new System.Windows.Controls.RadioButton
            {
                Content = "Last 24 hours", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingFileRelative == RelativeTimeRange.Last24Hours,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var ffPresetWeek = new System.Windows.Controls.RadioButton
            {
                Content = "Last week", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingFileRelative == RelativeTimeRange.LastWeek
            };
            ffPresetPanel.Children.Add(ffPresetNone);
            ffPresetPanel.Children.Add(ffPreset24h);
            ffPresetPanel.Children.Add(ffPresetWeek);
            sec5.Children.Add(ffPresetPanel);

            var ffGrid = new System.Windows.Controls.Grid();
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(50) });
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            ffGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ffFromLbl = new System.Windows.Controls.TextBlock { Text = "From:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var ffFromPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingFileFrom, FontSize = 11 };
            var ffToLbl = new System.Windows.Controls.TextBlock { Text = "To:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var ffToPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingFileTo, FontSize = 11 };
            System.Windows.Controls.Grid.SetColumn(ffFromLbl, 0); System.Windows.Controls.Grid.SetColumn(ffFromPicker, 1);
            System.Windows.Controls.Grid.SetColumn(ffToLbl, 2); System.Windows.Controls.Grid.SetColumn(ffToPicker, 3);
            ffGrid.Children.Add(ffFromLbl); ffGrid.Children.Add(ffFromPicker);
            ffGrid.Children.Add(ffToLbl); ffGrid.Children.Add(ffToPicker);
            sec5.Children.Add(ffGrid);

            // Toggle date pickers visibility based on preset selection
            Action updateFilePresetVisibility = () =>
            {
                bool isCustom = ffPresetNone.IsChecked == true;
                ffGrid.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            };
            ffPresetNone.Checked += (s, e) => updateFilePresetVisibility();
            ffPreset24h.Checked += (s, e) => updateFilePresetVisibility();
            ffPresetWeek.Checked += (s, e) => updateFilePresetVisibility();
            updateFilePresetVisibility();

            // --- Result timestamp filter ---
            sec5.Children.Add(makeLabel("Result timestamp filter"));

            var existingResRelative = schedule.Criteria?.ResultTimeFilter?.RelativeRange ?? RelativeTimeRange.None;
            var rfPresetPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 4)
            };
            var rfPresetNone = new System.Windows.Controls.RadioButton
            {
                Content = "Custom range", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingResRelative == RelativeTimeRange.None,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var rfPreset24h = new System.Windows.Controls.RadioButton
            {
                Content = "Last 24 hours", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingResRelative == RelativeTimeRange.Last24Hours,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var rfPresetWeek = new System.Windows.Controls.RadioButton
            {
                Content = "Last week", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingResRelative == RelativeTimeRange.LastWeek
            };
            rfPresetPanel.Children.Add(rfPresetNone);
            rfPresetPanel.Children.Add(rfPreset24h);
            rfPresetPanel.Children.Add(rfPresetWeek);
            sec5.Children.Add(rfPresetPanel);

            var rfGrid = new System.Windows.Controls.Grid();
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(50) });
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(30) });
            rfGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rfFromLbl = new System.Windows.Controls.TextBlock { Text = "From:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var rfFromPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingResFrom, FontSize = 11 };
            var rfToLbl = new System.Windows.Controls.TextBlock { Text = "To:", Foreground = textSecondary, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var rfToPicker = new System.Windows.Controls.DatePicker { SelectedDate = existingResTo, FontSize = 11 };
            System.Windows.Controls.Grid.SetColumn(rfFromLbl, 0); System.Windows.Controls.Grid.SetColumn(rfFromPicker, 1);
            System.Windows.Controls.Grid.SetColumn(rfToLbl, 2); System.Windows.Controls.Grid.SetColumn(rfToPicker, 3);
            rfGrid.Children.Add(rfFromLbl); rfGrid.Children.Add(rfFromPicker);
            rfGrid.Children.Add(rfToLbl); rfGrid.Children.Add(rfToPicker);
            sec5.Children.Add(rfGrid);

            // Toggle result date pickers visibility
            Action updateResPresetVisibility = () =>
            {
                bool isCustom = rfPresetNone.IsChecked == true;
                rfGrid.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            };
            rfPresetNone.Checked += (s, e) => updateResPresetVisibility();
            rfPreset24h.Checked += (s, e) => updateResPresetVisibility();
            rfPresetWeek.Checked += (s, e) => updateResPresetVisibility();
            updateResPresetVisibility();

            root.Children.Add(makeSection("Time Filters (optional)", sec5));

            // ════════════════════════════════════════════════════════
            // SECTION 6: OUTPUT
            // ════════════════════════════════════════════════════════
            var sec6 = new System.Windows.Controls.StackPanel();
            sec6.Children.Add(makeLabel("Output Directory"));
            var dirDock = new System.Windows.Controls.DockPanel();
            var browseBtn = new System.Windows.Controls.Button
            {
                Content = "Browse...", Width = 70,
                Padding = new Thickness(4, 3, 4, 3), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 11, Margin = new Thickness(6, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            System.Windows.Controls.DockPanel.SetDock(browseBtn, System.Windows.Controls.Dock.Right);
            var dirBox = makeTextBox(schedule.OutputDirectory);
            browseBtn.Click += (s, e) =>
            {
                var folderDlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    SelectedPath = dirBox.Text,
                    Description = "Select output directory for search results"
                };
                if (folderDlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    dirBox.Text = folderDlg.SelectedPath;
            };
            dirDock.Children.Add(browseBtn);
            dirDock.Children.Add(dirBox);
            sec6.Children.Add(dirDock);
            root.Children.Add(makeSection("Output", sec6));

            // ════════════════════════════════════════════════════════
            // SECTION 7: EMAIL NOTIFICATIONS
            // ════════════════════════════════════════════════════════
            var sec7 = new System.Windows.Controls.StackPanel();
            var existingEmail = schedule.EmailConfig ?? new EmailNotificationConfig();

            var emailEnabledCheck = new System.Windows.Controls.CheckBox
            {
                Content = "Enable email notifications",
                Foreground = textPrimary,
                IsChecked = existingEmail.IsEnabled,
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            sec7.Children.Add(emailEnabledCheck);

            var emailDetailsPanel = new System.Windows.Controls.StackPanel();

            // SMTP Settings
            emailDetailsPanel.Children.Add(makeLabel("SMTP Server"));
            var smtpHostRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };
            var smtpHostBox = makeTextBox(existingEmail.SmtpHost);
            smtpHostBox.Width = 300;
            smtpHostRow.Children.Add(smtpHostBox);
            smtpHostRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Port:", Foreground = textSecondary, FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 4, 0)
            });
            var smtpPortBox = makeTextBox(existingEmail.SmtpPort.ToString());
            smtpPortBox.Width = 60;
            smtpHostRow.Children.Add(smtpPortBox);
            emailDetailsPanel.Children.Add(smtpHostRow);

            var sslCheck = new System.Windows.Controls.CheckBox
            {
                Content = "Use SSL/TLS", Foreground = textPrimary,
                IsChecked = existingEmail.UseSsl, FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            };
            emailDetailsPanel.Children.Add(sslCheck);

            // Authentication mode
            emailDetailsPanel.Children.Add(makeLabel("Authentication"));
            var authPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var authNone = new System.Windows.Controls.RadioButton
            {
                Content = "None", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingEmail.AuthMode == SmtpAuthMode.None,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var authWindows = new System.Windows.Controls.RadioButton
            {
                Content = "Windows (current user)", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingEmail.AuthMode == SmtpAuthMode.WindowsIntegrated,
                Margin = new Thickness(0, 0, 14, 0)
            };
            var authUserPass = new System.Windows.Controls.RadioButton
            {
                Content = "Username / Password", Foreground = textPrimary, FontSize = 11,
                IsChecked = existingEmail.AuthMode == SmtpAuthMode.UsernamePassword
            };
            authPanel.Children.Add(authNone);
            authPanel.Children.Add(authWindows);
            authPanel.Children.Add(authUserPass);
            emailDetailsPanel.Children.Add(authPanel);

            // Username/Password fields (only visible when UsernamePassword selected)
            var credentialsPanel = new System.Windows.Controls.StackPanel();
            credentialsPanel.Children.Add(makeLabel("Username"));
            var smtpUserBox = makeTextBox(existingEmail.SmtpUsername);
            credentialsPanel.Children.Add(smtpUserBox);
            credentialsPanel.Children.Add(makeLabel("Password"));
            var smtpPassBox = makeTextBox(existingEmail.SmtpPassword);
            credentialsPanel.Children.Add(smtpPassBox);
            emailDetailsPanel.Children.Add(credentialsPanel);

            Action updateCredentialsVisibility = () =>
            {
                credentialsPanel.Visibility = authUserPass.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            authNone.Checked += (s2, e2) => updateCredentialsVisibility();
            authWindows.Checked += (s2, e2) => updateCredentialsVisibility();
            authUserPass.Checked += (s2, e2) => updateCredentialsVisibility();
            updateCredentialsVisibility();

            emailDetailsPanel.Children.Add(makeLabel("From Address"));
            var fromBox = makeTextBox(existingEmail.FromAddress);
            emailDetailsPanel.Children.Add(fromBox);

            // Test button
            var testBtn = new System.Windows.Controls.Button
            {
                Content = "Send Test Email", Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush,
                FontSize = 11, Padding = new Thickness(8, 3, 8, 3),
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var testStatusLabel = new System.Windows.Controls.TextBlock
            {
                Foreground = textSecondary, FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            // recipientList declared here so test button can reference it
            var recipientList = new List<string>(existingEmail.Recipients ?? new List<string>());

            testBtn.Click += async (s2, e2) =>
            {
                var testAuthMode = authWindows.IsChecked == true ? SmtpAuthMode.WindowsIntegrated
                    : authUserPass.IsChecked == true ? SmtpAuthMode.UsernamePassword
                    : SmtpAuthMode.None;
                var testConfig = new EmailNotificationConfig
                {
                    SmtpHost = smtpHostBox.Text.Trim(),
                    SmtpPort = int.TryParse(smtpPortBox.Text, out int tp) ? tp : 25,
                    UseSsl = sslCheck.IsChecked == true,
                    AuthMode = testAuthMode,
                    SmtpUsername = testAuthMode == SmtpAuthMode.UsernamePassword ? smtpUserBox.Text.Trim() : null,
                    SmtpPassword = testAuthMode == SmtpAuthMode.UsernamePassword ? smtpPassBox.Text : null,
                    FromAddress = fromBox.Text.Trim(),
                    FromDisplayName = "IndiLogs 3.0"
                };
                if (recipientList.Count == 0)
                {
                    testStatusLabel.Text = "Add at least one recipient first.";
                    return;
                }
                testStatusLabel.Text = "Sending test email...";
                testBtn.IsEnabled = false;
                using (var emailSvc = new EmailNotificationService())
                {
                    var (ok, msg) = await emailSvc.TestConnectionAsync(testConfig, recipientList[0]);
                    testStatusLabel.Text = msg;
                }
                testBtn.IsEnabled = true;
            };
            emailDetailsPanel.Children.Add(testBtn);
            emailDetailsPanel.Children.Add(testStatusLabel);

            // Recipients
            emailDetailsPanel.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(0, 4, 0, 4), Background = borderBrush
            });
            emailDetailsPanel.Children.Add(makeLabel("Recipients"));
            var recipientPanel = new System.Windows.Controls.StackPanel();

            Action rebuildRecipients = null;
            rebuildRecipients = () =>
            {
                recipientPanel.Children.Clear();
                for (int ri = 0; ri < recipientList.Count; ri++)
                {
                    int idx = ri;
                    var row = new System.Windows.Controls.StackPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    row.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = recipientList[idx], Foreground = textPrimary,
                        FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    });
                    var removeRecipBtn = new System.Windows.Controls.Button
                    {
                        Content = "X", Width = 20, Padding = new Thickness(0),
                        Background = bgCard, Foreground = dangerColor,
                        BorderBrush = borderBrush, FontSize = 10,
                        Cursor = System.Windows.Input.Cursors.Hand
                    };
                    removeRecipBtn.Click += (s2, e2) =>
                    {
                        recipientList.RemoveAt(idx);
                        rebuildRecipients();
                    };
                    row.Children.Add(removeRecipBtn);
                    recipientPanel.Children.Add(row);
                }
            };
            rebuildRecipients();
            emailDetailsPanel.Children.Add(recipientPanel);

            var addRecipRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var newRecipBox = makeTextBox("");
            newRecipBox.Width = 250;
            var addRecipBtn = new System.Windows.Controls.Button
            {
                Content = "+", Width = 28, Padding = new Thickness(0, 2, 0, 2),
                Background = bgCard, Foreground = textPrimary,
                BorderBrush = borderBrush, FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0)
            };
            addRecipBtn.Click += (s2, e2) =>
            {
                string email = newRecipBox.Text.Trim();
                if (!string.IsNullOrEmpty(email) && email.Contains("@"))
                {
                    recipientList.Add(email);
                    newRecipBox.Text = "";
                    rebuildRecipients();
                }
            };
            addRecipRow.Children.Add(newRecipBox);
            addRecipRow.Children.Add(addRecipBtn);
            emailDetailsPanel.Children.Add(addRecipRow);

            // Timing
            emailDetailsPanel.Children.Add(new System.Windows.Controls.Separator
            {
                Margin = new Thickness(0, 8, 0, 4), Background = borderBrush
            });
            emailDetailsPanel.Children.Add(makeLabel("Email Timing"));
            var timingImmediate = new System.Windows.Controls.RadioButton
            {
                Content = "Send immediately after scan completes",
                Foreground = textPrimary, FontSize = 11,
                IsChecked = existingEmail.Timing == EmailTiming.Immediately,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var timingDeferred = new System.Windows.Controls.RadioButton
            {
                Content = "Send at specific time:",
                Foreground = textPrimary, FontSize = 11,
                IsChecked = existingEmail.Timing == EmailTiming.AtSpecificTime
            };
            var emailTimePanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(20, 4, 0, 0)
            };
            var emailHourBox = new System.Windows.Controls.TextBox
            {
                Text = existingEmail.SendTime.Hours.ToString("00"),
                Width = 40, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush, FontSize = 12
            };
            emailTimePanel.Children.Add(emailHourBox);
            emailTimePanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = ":", Foreground = textPrimary, FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 0)
            });
            var emailMinuteBox = new System.Windows.Controls.TextBox
            {
                Text = existingEmail.SendTime.Minutes.ToString("00"),
                Width = 40, TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4), Background = bgCard,
                Foreground = textPrimary, BorderBrush = borderBrush, FontSize = 12
            };
            emailTimePanel.Children.Add(emailMinuteBox);

            emailDetailsPanel.Children.Add(timingImmediate);
            emailDetailsPanel.Children.Add(timingDeferred);
            emailDetailsPanel.Children.Add(emailTimePanel);

            Action updateTimingVisibility = () =>
            {
                emailTimePanel.Visibility = timingDeferred.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            timingImmediate.Checked += (s2, e2) => updateTimingVisibility();
            timingDeferred.Checked += (s2, e2) => updateTimingVisibility();
            updateTimingVisibility();

            // Custom Subject
            emailDetailsPanel.Children.Add(makeLabel("Custom Subject (optional)"));
            var subjectBox = makeTextBox(existingEmail.CustomSubject);
            emailDetailsPanel.Children.Add(subjectBox);
            emailDetailsPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "Leave empty for auto-generated: \"[IndiLogs] Name - N matches, N logs\"",
                Foreground = textSecondary, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

            sec7.Children.Add(emailDetailsPanel);

            // Toggle email details visibility
            Action updateEmailVisibility = () =>
            {
                emailDetailsPanel.Visibility = emailEnabledCheck.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            emailEnabledCheck.Checked += (s2, e2) => updateEmailVisibility();
            emailEnabledCheck.Unchecked += (s2, e2) => updateEmailVisibility();
            updateEmailVisibility();

            root.Children.Add(makeSection("Email Notifications", sec7));

            // ════════════════════════════════════════════════════════
            // SAVE / CANCEL BUTTONS
            // ════════════════════════════════════════════════════════
            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var okBtn = new System.Windows.Controls.Button
            {
                Content = "Save", Width = 90, IsDefault = true,
                Padding = new Thickness(6, 8, 6, 8),
                Background = primaryColor, Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold, FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand
            };
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel", Width = 90, IsCancel = true,
                Padding = new Thickness(6, 8, 6, 8),
                Background = bgCard, Foreground = textPrimary, FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            okBtn.Click += (s, e) =>
            {
                // ═══ VALIDATION ═══
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    _dialogService.ShowWarning("Schedule name is required.", "Validation");
                    return;
                }

                int typeIdx = typeCombo.SelectedIndex;
                int hours = 0, minutes = 0;
                bool needsTime = typeIdx <= 2;
                if (needsTime)
                {
                    if (!int.TryParse(hourBox.Text, out hours) || hours < 0 || hours > 23)
                    {
                        _dialogService.ShowWarning("Hours must be 0\u201323.", "Validation");
                        return;
                    }
                    if (!int.TryParse(minuteBox.Text, out minutes) || minutes < 0 || minutes > 59)
                    {
                        _dialogService.ShowWarning("Minutes must be 0\u201359.", "Validation");
                        return;
                    }
                }

                if (typeIdx == 0 && datePicker.SelectedDate == null)
                {
                    _dialogService.ShowWarning("Please select a date for 'Once' schedule.", "Validation");
                    return;
                }

                if (typeIdx == 2 && !dayCheckBoxes.Any(kv => kv.Value.IsChecked == true))
                {
                    _dialogService.ShowWarning("Select at least one day for weekly schedule.", "Validation");
                    return;
                }

                int intervalValue = 1;
                if (typeIdx == 3)
                {
                    if (!int.TryParse(intervalBox.Text, out intervalValue) || intervalValue < 1)
                    {
                        _dialogService.ShowWarning("Interval value must be at least 1.", "Validation");
                        return;
                    }
                }

                bool isStatsOnly = radioStatsOnly.IsChecked == true;
                bool needsSearch = !isStatsOnly;

                bool isSimple = simpleRadio.IsChecked == true;
                if (needsSearch && isSimple && string.IsNullOrWhiteSpace(simpleTextBox.Text))
                {
                    _dialogService.ShowWarning("Please enter search text.", "Validation");
                    return;
                }
                if (needsSearch && !isSimple && condValues.All(v => string.IsNullOrWhiteSpace(v.Text)))
                {
                    _dialogService.ShowWarning("Add at least one search condition with a value.", "Validation");
                    return;
                }
                if (plcCheck.IsChecked != true && appCheck.IsChecked != true)
                {
                    _dialogService.ShowWarning("Select at least PLC or APP log type.", "Validation");
                    return;
                }
                if (locationChecks.Count > 0 && !locationChecks.Any(kv => kv.Value.IsChecked == true))
                {
                    _dialogService.ShowWarning("Select at least one search location.", "Validation");
                    return;
                }

                // ═══ BUILD SEARCH CRITERIA ═══
                var criteria = new SearchCriteria
                {
                    SearchPLC = plcCheck.IsChecked == true,
                    SearchAPP = appCheck.IsChecked == true,
                    LocationIds = locationChecks
                        .Where(kv => kv.Value.IsChecked == true)
                        .Select(kv => kv.Key).ToList()
                };

                // File time filter — relative presets or custom range
                if (ffPreset24h.IsChecked == true)
                    criteria.FileTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
                else if (ffPresetWeek.IsChecked == true)
                    criteria.FileTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
                else if (ffFromPicker.SelectedDate.HasValue || ffToPicker.SelectedDate.HasValue)
                    criteria.FileTimeFilter = new TimeRangeFilter { From = ffFromPicker.SelectedDate, To = ffToPicker.SelectedDate };

                // Result time filter — relative presets or custom range
                if (rfPreset24h.IsChecked == true)
                    criteria.ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.Last24Hours };
                else if (rfPresetWeek.IsChecked == true)
                    criteria.ResultTimeFilter = new TimeRangeFilter { RelativeRange = RelativeTimeRange.LastWeek };
                else if (rfFromPicker.SelectedDate.HasValue || rfToPicker.SelectedDate.HasValue)
                    criteria.ResultTimeFilter = new TimeRangeFilter { From = rfFromPicker.SelectedDate, To = rfToPicker.SelectedDate };

                // Only build search criteria groups if search mode requires it
                if (needsSearch)
                {
                    if (isSimple)
                    {
                        criteria.Groups.Add(new SearchConditionGroup
                        {
                            Operator = ConditionOperator.Or,
                            Conditions = new List<SearchCondition>
                            {
                                new SearchCondition
                                {
                                    Field = (SearchField)simpleFieldCombo.SelectedItem,
                                    Operator = simpleRegexCheck.IsChecked == true ? SearchOperator.Regex : SearchOperator.Contains,
                                    Value = simpleTextBox.Text.Trim()
                                }
                            }
                        });
                    }
                    else
                    {
                        var group = new SearchConditionGroup
                        {
                            Operator = (ConditionOperator)advOperatorCombo.SelectedItem
                        };
                        for (int ci = 0; ci < condValues.Count; ci++)
                        {
                            if (string.IsNullOrWhiteSpace(condValues[ci].Text)) continue;
                            group.Conditions.Add(new SearchCondition
                            {
                                Field = (SearchField)condFields[ci].SelectedItem,
                                Operator = (SearchOperator)condOps[ci].SelectedItem,
                                Value = condValues[ci].Text.Trim(),
                                Negate = condNegates[ci].IsChecked == true
                            });
                        }
                        if (group.Conditions.Count > 0)
                            criteria.Groups.Add(group);
                    }
                }

                // ═══ APPLY TO SCHEDULE ═══
                schedule.Name = nameBox.Text.Trim();
                schedule.IsEnabled = enabledCheck.IsChecked == true;
                switch (typeIdx)
                {
                    case 0: schedule.ScheduleType = ScheduleType.Once; break;
                    case 1: schedule.ScheduleType = ScheduleType.Daily; break;
                    case 2: schedule.ScheduleType = ScheduleType.Weekly; break;
                    case 3: schedule.ScheduleType = ScheduleType.Interval; break;
                }
                schedule.RunDate = typeIdx == 0 ? datePicker.SelectedDate : null;
                schedule.RunTime = new TimeSpan(hours, minutes, 0);
                schedule.RunDays = dayCheckBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                schedule.RepeatIntervalValue = intervalValue;
                schedule.IntervalUnit = intervalUnitCombo.SelectedIndex == 0 ? IntervalUnit.Minutes
                    : intervalUnitCombo.SelectedIndex == 1 ? IntervalUnit.Hours
                    : IntervalUnit.Days;
                schedule.OutputDirectory = dirBox.Text.Trim();
                schedule.ScanMode = radioStatsOnly.IsChecked == true ? ScanMode.StatisticsOnly
                    : radioSearchAndStats.IsChecked == true ? ScanMode.SearchAndStatistics
                    : ScanMode.SearchOnly;
                schedule.Criteria = criteria;

                // ═══ EMAIL CONFIG ═══
                if (emailEnabledCheck.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(smtpHostBox.Text))
                    {
                        _dialogService.ShowWarning("SMTP server is required when email is enabled.", "Validation");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(fromBox.Text) || !fromBox.Text.Contains("@"))
                    {
                        _dialogService.ShowWarning("A valid From address is required.", "Validation");
                        return;
                    }
                    if (recipientList.Count == 0)
                    {
                        _dialogService.ShowWarning("Add at least one email recipient.", "Validation");
                        return;
                    }

                    int emailSendHour = 0, emailSendMin = 0;
                    if (timingDeferred.IsChecked == true)
                    {
                        if (!int.TryParse(emailHourBox.Text, out emailSendHour) || emailSendHour < 0 || emailSendHour > 23)
                        {
                            _dialogService.ShowWarning("Email send hour must be 0\u201323.", "Validation");
                            return;
                        }
                        if (!int.TryParse(emailMinuteBox.Text, out emailSendMin) || emailSendMin < 0 || emailSendMin > 59)
                        {
                            _dialogService.ShowWarning("Email send minutes must be 0\u201359.", "Validation");
                            return;
                        }
                    }

                    var authMode = authWindows.IsChecked == true ? SmtpAuthMode.WindowsIntegrated
                        : authUserPass.IsChecked == true ? SmtpAuthMode.UsernamePassword
                        : SmtpAuthMode.None;

                    schedule.EmailConfig = new EmailNotificationConfig
                    {
                        IsEnabled = true,
                        SmtpHost = smtpHostBox.Text.Trim(),
                        SmtpPort = int.TryParse(smtpPortBox.Text, out int port) ? port : 25,
                        UseSsl = sslCheck.IsChecked == true,
                        AuthMode = authMode,
                        SmtpUsername = authMode == SmtpAuthMode.UsernamePassword ? smtpUserBox.Text.Trim() : null,
                        SmtpPassword = authMode == SmtpAuthMode.UsernamePassword ? smtpPassBox.Text : null,
                        FromAddress = fromBox.Text.Trim(),
                        Recipients = new List<string>(recipientList),
                        Timing = timingDeferred.IsChecked == true
                            ? EmailTiming.AtSpecificTime : EmailTiming.Immediately,
                        SendTime = new TimeSpan(emailSendHour, emailSendMin, 0),
                        CustomSubject = string.IsNullOrWhiteSpace(subjectBox.Text)
                            ? null : subjectBox.Text.Trim()
                    };
                }
                else
                {
                    schedule.EmailConfig = null;
                }

                dialogResult = true;
                dialog.Close();
            };

            cancelBtn.Click += (s, e) => dialog.Close();
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            root.Children.Add(btnPanel);

            scroll.Content = root;
            dialog.Content = scroll;
            dialog.ShowDialog();
            return dialogResult;
        }

        private void RemoveSchedule()
        {
            if (SelectedSchedule == null) return;
            if (_dialogService.ShowConfirm($"Remove schedule '{SelectedSchedule.Name}'?", "Confirm") != MessageBoxResult.Yes) return;
            _taskSchedulerService.UnregisterSchedule(SelectedSchedule.Id);
            _schedulerService.RemoveSchedule(SelectedSchedule.Id);
            Schedules.Remove(SelectedSchedule);
        }

        private async Task RunScheduleNowAsync()
        {
            if (SelectedSchedule == null) return;
            var schedule = SelectedSchedule;
            var scheduleName = schedule.Name;

            // "Start" = run immediately, no waiting
            RequestCloseForScheduledRun?.Invoke(scheduleName);
            AppLogger.Info($"[Grep] Running scheduled search '{scheduleName}' now...");

            try
            {
                var reportPath = await _schedulerService.RunNowAsync(schedule);
                AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' completed: {schedule.LastRunStatus}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, reportPath);
                });
            }
            catch (OperationCanceledException)
            {
                AppLogger.Info($"[Grep] Scheduled search '{scheduleName}' cancelled");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, null);
                });
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[Grep] Scheduled search '{scheduleName}' failed", ex);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScheduledRunCompleted?.Invoke(scheduleName, null);
                });
            }
        }

        #endregion
    }
}
