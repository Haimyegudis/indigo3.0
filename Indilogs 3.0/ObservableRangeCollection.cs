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
        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            CheckReentrancy();

            if (Items is List<T> list)
            {
                list.AddRange(collection);
            }
            else
            {
                foreach (var i in collection) Items.Add(i);
            }

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        // --- Critical optimization for Live Monitoring (insertion at the head of the list) ---
        // Inside the ObservableRangeCollection<T> class
        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            var list = collection.ToList();
            if (list.Count == 0) return;

            CheckReentrancy();

            // 1. Fast addition to the internal list (without updating the UI yet)
            if (Items is List<T> items)
            {
                items.InsertRange(index, list);
            }
            else
            {
                // Fallback in case this is not a regular List
                int i = index;
                foreach (var item in list) Items.Insert(i++, item);
            }

            // 2. Critical fix for WPF: use Reset
            // WPF does not support Range Actions (adding a list). Using Reset notifies the UI
            // that the list has changed and requires only a single refresh. This prevents the crash and fixes the freeze.
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
        public void ReplaceAll(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            CheckReentrancy();

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
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0 || count < 0 || index + count > Items.Count)
                return;

            CheckReentrancy();

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
            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }
    }
}