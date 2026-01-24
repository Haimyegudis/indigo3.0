using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace IndiLogs_3._0.Models
{
    public class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        // --- אופטימיזציה להוספה רגילה ---
        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            // אם זה List, משתמשים ביכולת המהירה שלו
            if (Items is List<T> list)
            {
                list.AddRange(collection);
            }
            else
            {
                foreach (var i in collection) Items.Add(i);
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // --- אופטימיזציה קריטית ל-Live Monitoring (הכנסה לראש הרשימה) ---
        // בתוך המחלקה ObservableRangeCollection<T>
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var list = collection.ToList();
            if (list.Count == 0) return;

            CheckReentrancy();

            // 1. הוספה מהירה לרשימה הפנימית (ללא עדכון UI עדיין)
            if (Items is List<T> items)
            {
                items.InsertRange(index, list);
            }
            else
            {
                // Fallback למקרה שזה לא List רגיל
                int i = index;
                foreach (var item in list) Items.Insert(i++, item);
            }

            // 2. תיקון קריטי ל-WPF: שימוש ב-Reset
            // WPF לא תומך ב-Range Actions (הוספת רשימה). השימוש ב-Reset מודיע ל-UI
            // שהרשימה השתנתה ומחייב רענון אחד בלבד. זה מונע את הקריסה ופותר את ה-Freeze.
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
        public void ReplaceAll(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            Items.Clear();

            if (Items is List<T> list)
            {
                list.AddRange(collection);
            }
            else
            {
                foreach (var i in collection) Items.Add(i);
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0 || count < 0 || index + count > Items.Count)
                return;

            if (Items is List<T> list)
            {
                list.RemoveRange(index, count);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Items.RemoveAt(index);
                }
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}