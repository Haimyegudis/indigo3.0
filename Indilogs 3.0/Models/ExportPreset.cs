using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace IndiLogs_3._0.Models
{
    /// <summary>
    /// מודל לשמירת הגדרות ייצוא CSV
    /// </summary>
    public class ExportPreset
    {
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; }

        // הגדרות עמודות כלליות
        public bool IncludeUnixTime { get; set; }
        public bool IncludeEvents { get; set; }

        // רשימת קומפוננטות IOS שנבחרו
        public List<string> SelectedIOComponents { get; set; }

        // רשימת קומפוננטות AXIS שנבחרו
        public List<string> SelectedAxisComponents { get; set; }

        // רשימת Threads שנבחרו להצגה כעמודות
        public List<string> SelectedThreads { get; set; }

        public ExportPreset()
        {
            SelectedIOComponents = new List<string>();
            SelectedAxisComponents = new List<string>();
            SelectedThreads = new List<string>();
            IncludeUnixTime = true;
            IncludeEvents = true;
            CreatedDate = DateTime.Now;
        }
    }

    /// <summary>
    /// פריט בר-בחירה עבור רשימת קומפוננטות/threads
    /// </summary>
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
