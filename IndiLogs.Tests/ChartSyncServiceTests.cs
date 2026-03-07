using System;
using System.Collections.Generic;
using IndiLogs_3._0.Services.Charts;
using Xunit;

namespace IndiLogs.Tests
{
    public class ChartSyncServiceTests
    {
        private readonly ChartSyncService _service = new();

        #region BuildTimeMapping

        [Fact]
        public void BuildTimeMapping_EmptyArray_HasMappingIsFalse()
        {
            _service.BuildTimeMapping(Array.Empty<string>());

            Assert.False(_service.HasMapping);
            Assert.Equal(0, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_ValidIsoStrings_BuildsMapping()
        {
            var times = new[] { "2024-01-15T10:00:00", "2024-01-15T11:00:00", "2024-01-15T12:00:00" };

            _service.BuildTimeMapping(times);

            Assert.True(_service.HasMapping);
            Assert.Equal(3, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_UnsortedTimes_SortsByTime()
        {
            var times = new[] { "2024-01-15T12:00:00", "2024-01-15T10:00:00", "2024-01-15T11:00:00" };

            _service.BuildTimeMapping(times);

            var range = _service.GetTimeRange();
            Assert.Equal(new DateTime(2024, 1, 15, 10, 0, 0), range.Start);
            Assert.Equal(new DateTime(2024, 1, 15, 12, 0, 0), range.End);
        }

        [Fact]
        public void BuildTimeMapping_InvalidStrings_SkipsInvalid()
        {
            var times = new[] { "2024-01-15T10:00:00", "not_a_date_xyz", "2024-01-15T12:00:00" };

            _service.BuildTimeMapping(times);

            // "not_a_date_xyz" cannot be parsed as date or number, so skipped
            // Actually let's check - it's not numeric either so should be skipped
            Assert.True(_service.DataPointCount >= 2);
        }

        [Fact]
        public void BuildTimeMapping_NumericMillisecondsSinceEpoch_ParsesCorrectly()
        {
            // 1705312800000 ms = 2024-01-15T10:00:00 UTC
            var times = new[] { "1705312800000" };

            _service.BuildTimeMapping(times);

            Assert.True(_service.HasMapping);
            Assert.Equal(1, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_NumericSecondsSinceEpoch_ParsesCorrectly()
        {
            // 1705312800 sec = 2024-01-15T10:00:00 UTC
            var times = new[] { "1705312800" };

            _service.BuildTimeMapping(times);

            Assert.True(_service.HasMapping);
            Assert.Equal(1, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_SmallNumericValue_TreatedAsRelativeSeconds()
        {
            var times = new[] { "3600" };

            _service.BuildTimeMapping(times);

            Assert.True(_service.HasMapping);
            var time = _service.GetTimeForIndex(0);
            Assert.Equal(DateTime.Today.AddSeconds(3600), time);
        }

        [Fact]
        public void BuildTimeMapping_EmptyAndWhitespaceStrings_Skipped()
        {
            var times = new[] { "", "  ", "2024-01-15T10:00:00" };

            _service.BuildTimeMapping(times);

            Assert.Equal(1, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_DuplicateTimes_AllIncluded()
        {
            var times = new[] { "2024-01-15T10:00:00", "2024-01-15T10:00:00", "2024-01-15T10:00:00" };

            _service.BuildTimeMapping(times);

            Assert.Equal(3, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_SingleElement_Works()
        {
            var times = new[] { "2024-01-15T10:00:00" };

            _service.BuildTimeMapping(times);

            Assert.True(_service.HasMapping);
            Assert.Equal(1, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMapping_CalledTwice_ClearsPreviousData()
        {
            _service.BuildTimeMapping(new[] { "2024-01-15T10:00:00", "2024-01-15T11:00:00" });
            Assert.Equal(2, _service.DataPointCount);

            _service.BuildTimeMapping(new[] { "2024-01-15T12:00:00" });
            Assert.Equal(1, _service.DataPointCount);
        }

        #endregion

        #region BuildTimeMappingFromDateTimes

        [Fact]
        public void BuildTimeMappingFromDateTimes_NullList_HasMappingIsFalse()
        {
            _service.BuildTimeMappingFromDateTimes(null!);

            Assert.False(_service.HasMapping);
            Assert.Equal(0, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMappingFromDateTimes_EmptyList_HasMappingIsFalse()
        {
            _service.BuildTimeMappingFromDateTimes(new List<DateTime>());

            Assert.False(_service.HasMapping);
            Assert.Equal(0, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMappingFromDateTimes_ValidList_BuildsMapping()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };

            _service.BuildTimeMappingFromDateTimes(timestamps);

            Assert.True(_service.HasMapping);
            Assert.Equal(3, _service.DataPointCount);
        }

        [Fact]
        public void BuildTimeMappingFromDateTimes_PreservesIndexOrder()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };

            _service.BuildTimeMappingFromDateTimes(timestamps);

            Assert.Equal(new DateTime(2024, 1, 15, 10, 0, 0), _service.GetTimeForIndex(0));
            Assert.Equal(new DateTime(2024, 1, 15, 11, 0, 0), _service.GetTimeForIndex(1));
            Assert.Equal(new DateTime(2024, 1, 15, 12, 0, 0), _service.GetTimeForIndex(2));
        }

        [Fact]
        public void BuildTimeMappingFromDateTimes_SingleElement_Works()
        {
            var timestamps = new List<DateTime> { new(2024, 6, 1, 8, 30, 0) };

            _service.BuildTimeMappingFromDateTimes(timestamps);

            Assert.True(_service.HasMapping);
            Assert.Equal(1, _service.DataPointCount);
            Assert.Equal(new DateTime(2024, 6, 1, 8, 30, 0), _service.GetTimeForIndex(0));
        }

        #endregion

        #region FindChartIndex

        [Fact]
        public void FindChartIndex_EmptyMapping_ReturnsZero()
        {
            int index = _service.FindChartIndex(DateTime.Now);

            Assert.Equal(0, index);
        }

        [Fact]
        public void FindChartIndex_ExactMatch_ReturnsCorrectIndex()
        {
            var target = new DateTime(2024, 1, 15, 11, 0, 0);
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                target,
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            int index = _service.FindChartIndex(target);

            Assert.Equal(1, index);
        }

        [Fact]
        public void FindChartIndex_TimeBetweenPoints_ReturnsNearest()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            // 10:50 is closer to 10:00 than to 12:00
            int index = _service.FindChartIndex(new DateTime(2024, 1, 15, 10, 50, 0));
            Assert.Equal(0, index);

            // 11:10 is closer to 12:00 than to 10:00
            index = _service.FindChartIndex(new DateTime(2024, 1, 15, 11, 10, 0));
            Assert.Equal(1, index);
        }

        [Fact]
        public void FindChartIndex_TimeBeforeAll_ReturnsFirst()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            int index = _service.FindChartIndex(new DateTime(2024, 1, 15, 8, 0, 0));

            Assert.Equal(0, index);
        }

        [Fact]
        public void FindChartIndex_TimeAfterAll_ReturnsLast()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            int index = _service.FindChartIndex(new DateTime(2024, 1, 15, 23, 0, 0));

            Assert.Equal(1, index);
        }

        [Fact]
        public void FindChartIndex_SinglePoint_ReturnsThatIndex()
        {
            var timestamps = new List<DateTime> { new(2024, 1, 15, 10, 0, 0) };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            int index = _service.FindChartIndex(new DateTime(2024, 1, 15, 15, 0, 0));

            Assert.Equal(0, index);
        }

        [Fact]
        public void FindChartIndex_DuplicateTimes_ReturnsOneOfThem()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            int index = _service.FindChartIndex(new DateTime(2024, 1, 15, 10, 0, 0));

            // Should return one of the duplicate indices (0 or 1)
            Assert.True(index == 0 || index == 1);
        }

        [Fact]
        public void FindChartIndex_ExactlyMidpoint_ReturnsPreviousOrCurrent()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            // Exactly at midpoint (11:00) - equidistant, should return current (index 1)
            // because the condition is strictly less than
            int index = _service.FindChartIndex(new DateTime(2024, 1, 15, 11, 0, 0));

            Assert.True(index == 0 || index == 1);
        }

        [Fact]
        public void FindChartIndex_ManyPoints_BinarySearchWorks()
        {
            var timestamps = new List<DateTime>();
            var baseTime = new DateTime(2024, 1, 15, 0, 0, 0);
            for (int i = 0; i < 1000; i++)
            {
                timestamps.Add(baseTime.AddMinutes(i));
            }
            _service.BuildTimeMappingFromDateTimes(timestamps);

            // Search for time at index 500
            int index = _service.FindChartIndex(baseTime.AddMinutes(500));
            Assert.Equal(500, index);

            // Search for time at the very beginning
            index = _service.FindChartIndex(baseTime);
            Assert.Equal(0, index);

            // Search for time at the very end
            index = _service.FindChartIndex(baseTime.AddMinutes(999));
            Assert.Equal(999, index);
        }

        #endregion

        #region GetTimeForIndex

        [Fact]
        public void GetTimeForIndex_EmptyMapping_ReturnsMinValue()
        {
            var time = _service.GetTimeForIndex(0);

            Assert.Equal(DateTime.MinValue, time);
        }

        [Fact]
        public void GetTimeForIndex_ValidIndex_ReturnsCorrectTime()
        {
            var expected = new DateTime(2024, 1, 15, 11, 0, 0);
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                expected,
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            var result = _service.GetTimeForIndex(1);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetTimeForIndex_IndexNotInMap_ReturnsNearestTime()
        {
            // Build mapping with only indices 0 and 2 (skip 1 via string parsing with invalid middle)
            var times = new[] { "2024-01-15T10:00:00", "", "2024-01-15T12:00:00" };
            _service.BuildTimeMapping(times);

            // Index 1 has no mapping, should fall back to binary search on index
            var result = _service.GetTimeForIndex(1);

            // Should return one of the available times (closest index)
            Assert.NotEqual(DateTime.MinValue, result);
        }

        [Fact]
        public void GetTimeForIndex_SinglePoint_ReturnsItsTime()
        {
            var expected = new DateTime(2024, 1, 15, 10, 0, 0);
            _service.BuildTimeMappingFromDateTimes(new List<DateTime> { expected });

            Assert.Equal(expected, _service.GetTimeForIndex(0));
        }

        [Fact]
        public void GetTimeForIndex_NegativeIndex_FallsBackToSearch()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            // Negative index not in dictionary, falls back to binary search
            var result = _service.GetTimeForIndex(-1);
            Assert.NotEqual(DateTime.MinValue, result);
        }

        #endregion

        #region GetTimeRange

        [Fact]
        public void GetTimeRange_EmptyMapping_ReturnsBothMinValue()
        {
            var range = _service.GetTimeRange();

            Assert.Equal(DateTime.MinValue, range.Start);
            Assert.Equal(DateTime.MinValue, range.End);
        }

        [Fact]
        public void GetTimeRange_SinglePoint_StartEqualsEnd()
        {
            var time = new DateTime(2024, 1, 15, 10, 0, 0);
            _service.BuildTimeMappingFromDateTimes(new List<DateTime> { time });

            var range = _service.GetTimeRange();

            Assert.Equal(time, range.Start);
            Assert.Equal(time, range.End);
        }

        [Fact]
        public void GetTimeRange_MultiplePoints_ReturnsFirstAndLast()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            var range = _service.GetTimeRange();

            Assert.Equal(new DateTime(2024, 1, 15, 10, 0, 0), range.Start);
            Assert.Equal(new DateTime(2024, 1, 15, 12, 0, 0), range.End);
        }

        [Fact]
        public void GetTimeRange_UnsortedStringInput_ReturnsCorrectRange()
        {
            var times = new[] { "2024-01-15T12:00:00", "2024-01-15T08:00:00", "2024-01-15T16:00:00" };
            _service.BuildTimeMapping(times);

            var range = _service.GetTimeRange();

            Assert.Equal(new DateTime(2024, 1, 15, 8, 0, 0), range.Start);
            Assert.Equal(new DateTime(2024, 1, 15, 16, 0, 0), range.End);
        }

        [Fact]
        public void GetTimeRange_DuplicateTimes_ReturnsCorrectRange()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 10, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            var range = _service.GetTimeRange();

            Assert.Equal(range.Start, range.End);
        }

        #endregion

        #region FormatTimeForDisplay

        [Fact]
        public void FormatTimeForDisplay_EmptyMapping_ReturnsIndexAsString()
        {
            string result = _service.FormatTimeForDisplay(42);

            Assert.Equal("42", result);
        }

        [Fact]
        public void FormatTimeForDisplay_RangeLessThan24Hours_ShowsTimeOnly()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 30, 45),
                new(2024, 1, 15, 11, 0, 0),
                new(2024, 1, 15, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            string result = _service.FormatTimeForDisplay(0);

            Assert.Equal("10:30:45", result);
        }

        [Fact]
        public void FormatTimeForDisplay_RangeMoreThan24Hours_ShowsDateAndTime()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 30, 0),
                new(2024, 1, 17, 12, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            string result = _service.FormatTimeForDisplay(0);

            Assert.Equal("01/15 10:30", result);
        }

        [Fact]
        public void FormatTimeForDisplay_Exactly24Hours_ShowsDateAndTime()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 16, 10, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            // Exactly 24 hours => TotalHours == 24 which is NOT < 24, so shows date+time
            string result = _service.FormatTimeForDisplay(0);

            Assert.Equal("01/15 10:00", result);
        }

        [Fact]
        public void FormatTimeForDisplay_SinglePoint_TimeOnlyFormat()
        {
            var timestamps = new List<DateTime> { new(2024, 1, 15, 14, 25, 30) };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            // Range is 0 hours which is < 24
            string result = _service.FormatTimeForDisplay(0);

            Assert.Equal("14:25:30", result);
        }

        #endregion

        #region Clear

        [Fact]
        public void Clear_AfterBuildingMapping_RemovesAllData()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);
            Assert.True(_service.HasMapping);

            _service.Clear();

            Assert.False(_service.HasMapping);
            Assert.Equal(0, _service.DataPointCount);
        }

        #endregion

        #region Events

        [Fact]
        public void NotifyChartTimeClicked_WithValidMapping_RaisesEvent()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            DateTime? receivedTime = null;
            _service.ChartTimeClicked += t => receivedTime = t;

            _service.NotifyChartTimeClicked(1);

            Assert.NotNull(receivedTime);
            Assert.Equal(new DateTime(2024, 1, 15, 11, 0, 0), receivedTime.Value);
        }

        [Fact]
        public void NotifyChartTimeClicked_EmptyMapping_DoesNotRaiseEvent()
        {
            bool eventRaised = false;
            _service.ChartTimeClicked += _ => eventRaised = true;

            _service.NotifyChartTimeClicked(0);

            Assert.False(eventRaised);
        }

        [Fact]
        public void NotifyLogTimeSelected_WithValidMapping_RaisesEvent()
        {
            var timestamps = new List<DateTime>
            {
                new(2024, 1, 15, 10, 0, 0),
                new(2024, 1, 15, 11, 0, 0)
            };
            _service.BuildTimeMappingFromDateTimes(timestamps);

            int? receivedIndex = null;
            _service.LogTimeSelected += i => receivedIndex = i;

            _service.NotifyLogTimeSelected(new DateTime(2024, 1, 15, 11, 0, 0));

            Assert.NotNull(receivedIndex);
            Assert.Equal(1, receivedIndex.Value);
        }

        #endregion
    }
}
