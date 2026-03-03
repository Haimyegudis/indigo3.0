#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using IndiLogs_3._0.Models.Grep;
using IndiLogs_3._0.Services;
using IndiLogs_3._0.Services.Grep;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace IndiLogs_3._0.ViewModels
{
    public partial class GlobalGrepViewModel
    {
        #region Config / Profile Management

        private void SaveConfig()
        {
            var name = PromptInput("Save Profile", "Profile name:", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            var profile = new SearchProfile
            {
                Name = name,
                Locations = Locations.ToList(),
                Criteria = BuildCriteria()
            };
            _configService.SaveProfile(profile);
            RefreshSavedProfiles();
            StatusMessage = $"Profile '{name}' saved.";
        }

        private void LoadConfig()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Search Profile (*.json)|*.json",
                Title = "Load Search Profile"
            };
            if (dlg.ShowDialog() != true) return;
            var profile = _configService.LoadProfileFromFile(dlg.FileName);
            if (profile != null) ApplyProfile(profile);
        }

        private void LoadSelectedProfile()
        {
            if (SelectedProfile == null) return;
            var profile = _configService.LoadProfile(SelectedProfile);
            if (profile != null) ApplyProfile(profile);
        }

        private void DeleteSelectedProfile()
        {
            if (SelectedProfile == null) return;
            if (_dialogService.ShowConfirm($"Delete profile '{SelectedProfile}'?", "Confirm") != MessageBoxResult.Yes) return;
            _configService.DeleteProfile(SelectedProfile);
            RefreshSavedProfiles();
        }

        private void RenameSelectedProfile()
        {
            if (SelectedProfile == null) return;
            var newName = PromptInput("Rename Profile", "New name:", SelectedProfile);
            if (string.IsNullOrWhiteSpace(newName)) return;
            _configService.RenameProfile(SelectedProfile, newName);
            RefreshSavedProfiles();
        }

        private void ImportProfile()
        {
            var dlg = new OpenFileDialog { Filter = "Search Profile (*.json)|*.json", Title = "Import Profile" };
            if (dlg.ShowDialog() != true) return;
            _configService.ImportProfile(dlg.FileName);
            RefreshSavedProfiles();
            StatusMessage = "Profile imported.";
        }

        private void ApplyProfile(SearchProfile profile)
        {
            // Apply locations
            Locations.Clear();
            foreach (var loc in profile.Locations)
                Locations.Add(loc);
            _locationService.Save();

            // Apply criteria
            var c = profile.Criteria;
            if (c != null)
            {
                SearchPLC = c.SearchPLC;
                SearchAPP = c.SearchAPP;
                SelectedGroupOperator = c.GroupOperator;
                FileTimeFrom = c.FileTimeFilter?.From;
                FileTimeTo = c.FileTimeFilter?.To;
                ResultTimeFrom = c.ResultTimeFilter?.From;
                ResultTimeTo = c.ResultTimeFilter?.To;

                ConditionGroups.Clear();
                if (c.Groups != null && c.Groups.Count > 0)
                {
                    foreach (var g in c.Groups)
                    {
                        var gvm = new ConditionGroupVM { Operator = g.Operator };
                        foreach (var cond in g.Conditions)
                            gvm.Conditions.Add(new ConditionVM
                            {
                                Field = cond.Field,
                                Operator = cond.Operator,
                                Value = cond.Value,
                                Negate = cond.Negate
                            });
                        ConditionGroups.Add(gvm);
                    }
                }
                else
                {
                    ConditionGroups.Add(new ConditionGroupVM());
                }
            }

            StatusMessage = $"Profile '{profile.Name}' loaded.";
        }

        private void RefreshSavedProfiles()
        {
            SavedProfiles = new ObservableCollection<string>(_configService.ListProfiles());
        }

        private void UpdateProfilePreview()
        {
            if (string.IsNullOrEmpty(SelectedProfile))
            {
                ProfilePreview = "";
                return;
            }
            var profile = _configService.LoadProfile(SelectedProfile);
            if (profile == null) { ProfilePreview = "Profile not found."; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Name: {profile.Name}");
            sb.AppendLine($"Locations: {profile.Locations?.Count ?? 0}");
            if (profile.Locations != null)
                foreach (var loc in profile.Locations)
                    sb.AppendLine($"  - {loc.Name} ({loc.Address}) → {loc.BasePath}");
            if (profile.Criteria != null)
            {
                sb.AppendLine($"PLC: {profile.Criteria.SearchPLC}, APP: {profile.Criteria.SearchAPP}");
                sb.AppendLine($"Groups: {profile.Criteria.Groups?.Count ?? 0} (operator: {profile.Criteria.GroupOperator})");
                if (profile.Criteria.Groups != null)
                    foreach (var g in profile.Criteria.Groups)
                    {
                        sb.AppendLine($"  Group ({g.Operator}):");
                        foreach (var c in g.Conditions)
                            sb.AppendLine($"    {(c.Negate ? "NOT " : "")}{c.Field} {c.Operator} \"{c.Value}\"");
                    }
            }
            ProfilePreview = sb.ToString();
        }

        #endregion

        #region Export

        private void ExportCsv()
        {
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"grep_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
            if (dlg.ShowDialog() != true) return;
            using (var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("Timestamp,Location,FilePath,LineNumber,LogType,Thread,Logger,Method,Data,Message");
                foreach (var r in Results)
                {
                    var e = r.ReferencedLogEntry;
                    writer.WriteLine($"\"{r.TimestampDisplay}\",\"{Esc(r.LocationName)}\",\"{Esc(r.FilePath)}\",{r.LineNumber},\"{Esc(r.LogType)}\",\"{Esc(e?.ThreadName)}\",\"{Esc(e?.Logger)}\",\"{Esc(e?.Method)}\",\"{Esc(e?.Data)}\",\"{Esc(e?.Message)}\"");
                }
            }
            StatusMessage = $"Exported {Results.Count:N0} results to CSV.";
        }

        private void ExportJson()
        {
            var dlg = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = $"grep_results_{DateTime.Now:yyyyMMdd_HHmmss}.json" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(Results.ToList(), Formatting.Indented));
            StatusMessage = $"Exported {Results.Count:N0} results to JSON.";
        }

        private static string Esc(string s) => s?.Replace("\"", "\"\"") ?? "";

        private void ExportReport()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "HTML Report (*.html)|*.html",
                FileName = $"search_report_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };
            if (dlg.ShowDialog() != true) return;

            var reportParams = _lastSearchParams ?? new SearchReportParams
            {
                LocationNames = Locations.Where(l => l.IsActive).Select(l => l.Name).ToList(),
                QueryText = SearchQuery,
                CriteriaSummary = BuildCriteriaSummary(),
                SearchDuration = SearchDuration,
                LogTypes = (SearchPLC && SearchAPP) ? "PLC + APP" : SearchPLC ? "PLC" : SearchAPP ? "APP" : "None"
            };

            SearchReportService.GenerateHtmlReport(dlg.FileName, reportParams, Results.ToList());
            StatusMessage = $"Report saved to {Path.GetFileName(dlg.FileName)}.";

            try { System.Diagnostics.Process.Start(dlg.FileName); }
            catch (Exception ex) { AppLogger.Warn($"Could not open report file: {ex.Message}"); }
        }

        private string BuildCriteriaSummary()
        {
            var parts = new List<string>();
            foreach (var group in ConditionGroups)
            {
                var conditions = group.Conditions
                    .Where(c => !string.IsNullOrWhiteSpace(c.Value))
                    .Select(c => $"{(c.Negate ? "NOT " : "")}{c.Field} {c.Operator} \"{c.Value}\"");
                if (conditions.Any())
                    parts.Add($"({string.Join($" {group.Operator} ", conditions)})");
            }
            return parts.Count > 0 ? string.Join($" {SelectedGroupOperator} ", parts) : "";
        }

        private static string FormatTimeRange(DateTime? from, DateTime? to)
        {
            if (!from.HasValue && !to.HasValue) return null;
            string f = from?.ToString("yyyy-MM-dd") ?? "...";
            string t = to?.ToString("yyyy-MM-dd") ?? "...";
            return $"{f} to {t}";
        }

        #endregion
    }
}
