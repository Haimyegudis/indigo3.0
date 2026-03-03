using IndiLogs_3._0.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Xunit;

namespace IndiLogs.Tests
{
    public class ObservableRangeCollectionTests
    {
        [Fact]
        public void AddRange_AddsAllItems()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2, 3 });

            Assert.Equal(3, collection.Count);
            Assert.Equal(1, collection[0]);
            Assert.Equal(2, collection[1]);
            Assert.Equal(3, collection[2]);
        }

        [Fact]
        public void AddRange_FiresSingleResetNotification()
        {
            var collection = new ObservableRangeCollection<int>();
            int notificationCount = 0;
            NotifyCollectionChangedAction? lastAction = null;

            collection.CollectionChanged += (s, e) =>
            {
                notificationCount++;
                lastAction = e.Action;
            };

            collection.AddRange(new[] { 1, 2, 3 });

            // Should fire exactly 1 CollectionChanged (Reset) — not 3 individual Add events
            Assert.Equal(1, notificationCount);
            Assert.Equal(NotifyCollectionChangedAction.Reset, lastAction);
        }

        [Fact]
        public void AddRange_NullCollection_Throws()
        {
            var collection = new ObservableRangeCollection<int>();
            Assert.Throws<ArgumentNullException>(() => collection.AddRange(null));
        }

        [Fact]
        public void ReplaceAll_ClearsAndAddsItems()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2, 3 });

            collection.ReplaceAll(new[] { 10, 20 });

            Assert.Equal(2, collection.Count);
            Assert.Equal(10, collection[0]);
            Assert.Equal(20, collection[1]);
        }

        [Fact]
        public void ReplaceAll_NullCollection_Throws()
        {
            var collection = new ObservableRangeCollection<int>();
            Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null));
        }

        [Fact]
        public void InsertRange_InsertsAtCorrectPosition()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 4, 5 });

            collection.InsertRange(1, new[] { 2, 3 });

            Assert.Equal(5, collection.Count);
            Assert.Equal(1, collection[0]);
            Assert.Equal(2, collection[1]);
            Assert.Equal(3, collection[2]);
            Assert.Equal(4, collection[3]);
            Assert.Equal(5, collection[4]);
        }

        [Fact]
        public void InsertRange_AtBeginning_Prepends()
        {
            var collection = new ObservableRangeCollection<string>();
            collection.AddRange(new[] { "c", "d" });

            collection.InsertRange(0, new[] { "a", "b" });

            Assert.Equal(4, collection.Count);
            Assert.Equal("a", collection[0]);
            Assert.Equal("b", collection[1]);
        }

        [Fact]
        public void InsertRange_EmptyCollection_DoesNothing()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2 });
            int notificationCount = 0;
            collection.CollectionChanged += (s, e) => notificationCount++;

            collection.InsertRange(0, new List<int>());

            Assert.Equal(2, collection.Count);
            Assert.Equal(0, notificationCount);
        }

        [Fact]
        public void InsertRange_NullCollection_Throws()
        {
            var collection = new ObservableRangeCollection<int>();
            Assert.Throws<ArgumentNullException>(() => collection.InsertRange(0, null));
        }

        [Fact]
        public void RemoveRange_RemovesCorrectItems()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2, 3, 4, 5 });

            collection.RemoveRange(1, 2);

            Assert.Equal(3, collection.Count);
            Assert.Equal(1, collection[0]);
            Assert.Equal(4, collection[1]);
            Assert.Equal(5, collection[2]);
        }

        [Fact]
        public void RemoveRange_InvalidIndex_DoesNothing()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2, 3 });

            // Should not throw, just return
            collection.RemoveRange(-1, 1);
            Assert.Equal(3, collection.Count);

            collection.RemoveRange(0, 10);
            Assert.Equal(3, collection.Count);
        }

        [Fact]
        public void RemoveRange_FiresSingleResetNotification()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2, 3, 4, 5 });

            int notificationCount = 0;
            collection.CollectionChanged += (s, e) => notificationCount++;

            collection.RemoveRange(1, 3);

            Assert.Equal(1, notificationCount);
        }

        [Fact]
        public void ReplaceAll_FiresSingleResetNotification()
        {
            var collection = new ObservableRangeCollection<int>();
            collection.AddRange(new[] { 1, 2, 3 });

            int notificationCount = 0;
            collection.CollectionChanged += (s, e) => notificationCount++;

            collection.ReplaceAll(new[] { 10, 20, 30 });

            Assert.Equal(1, notificationCount);
        }
    }
}
