using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace IndiLogs_3._0.Models
{
    public class ExportPreset
    {
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; }

        public bool IncludeUnixTime { get; set; }
        public bool IncludeEvents { get; set; }
        public bool IncludeMachineState { get; set; }

        public List<string> SelectedIOComponents { get; set; }
        public List<string> SelectedAxisComponents { get; set; }
        public List<string> SelectedCHSteps { get; set; }
        public List<string> SelectedThreads { get; set; }

        public ExportPreset()
        {
            SelectedIOComponents = new List<string>();
            SelectedAxisComponents = new List<string>();
            SelectedCHSteps = new List<string>();
            SelectedThreads = new List<string>();
            IncludeUnixTime = true;
            IncludeEvents = true;
            IncludeMachineState = true;
            CreatedDate = DateTime.Now;
        }
    }

    public class SelectableItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _name;
        private string _category;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(nameof(Category)); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}