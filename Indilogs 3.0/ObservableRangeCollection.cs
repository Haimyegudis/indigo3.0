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
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            // בדיקה האם הרשימה הפנימית היא List<T> לביצועים מהירים
            if (Items is List<T> list)
            {
                // זה מבצע הזזה אחת בזיכרון לכל הבלוק, במקום אלפי הזזות!
                list.InsertRange(index, collection);
            }
            else
            {
                // Fallback למקרה שזה לא List (איטי יותר)
                foreach (var i in collection)
                {
                    Items.Insert(index++, i);
                }
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
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