using IndiLogs_3._0.Models.Cpr;
using IndiLogs_3._0.Services.Cpr;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace IndiLogs.Tests
{
    public class CprDataServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public CprDataServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "IndiLogsTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private string CreateCsvFile(string content)
        {
            var path = Path.Combine(_tempDir, "test.csv");
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public void LoadCsv_BasicFile_LoadsRecords()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY\n" +
                      "1,1,1,100.0,200.0\n" +
                      "1,1,2,100.0,200.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.True(svc.IsLoaded);
            Assert.Equal(new[] { 1 }, svc.GetMachineNumbers());
        }

        [Fact]
        public void LoadCsv_EmptyFile_NoRecords()
        {
            var path = CreateCsvFile("sn,IterationNum\n");

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.False(svc.IsLoaded);
        }

        [Fact]
        public void LoadCsv_HeaderOnly_NoRecords()
        {
            var path = CreateCsvFile("sn,IterationNum,CycleNumber\n");

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.False(svc.IsLoaded);
        }

        [Fact]
        public void LoadCsv_SemicolonDelimited_Works()
        {
            var csv = "sn;IterationNum;CycleNumber;ElementLocationX;ElementLocationY\n" +
                      "1;1;1;100.0;200.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.True(svc.IsLoaded);
        }

        [Fact]
        public void LoadCsv_TabDelimited_Works()
        {
            var csv = "sn\tIterationNum\tCycleNumber\tElementLocationX\tElementLocationY\n" +
                      "1\t1\t1\t100.0\t200.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.True(svc.IsLoaded);
        }

        [Fact]
        public void LoadCsv_NormalizesColumnNames()
        {
            // iterationnum should be mapped to IterationNum
            var csv = "sn,iterationnum,cyclenumber,elementlocationx,elementlocationy\n" +
                      "1,5,10,100.0,200.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            var iterations = svc.GetIterations(1, "");
            Assert.Contains(5, iterations);
        }

        [Fact]
        public void LoadCsv_WithStationData_ParsesStations()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY," +
                      "RegistrationDataStationX1,RegistrationDataStationY1," +
                      "RegistrationDataStationX2,RegistrationDataStationY2\n" +
                      "1,1,1,100.0,200.0,10.5,20.5,30.5,40.5\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            var filter = new CprFilterState
            {
                MachineSN = 1, Iteration = 1, CycleFrom = 1, CycleTo = 1,
                ColumnFrom = 100, ColumnTo = 100
            };
            var records = svc.ApplyFilters(filter);
            Assert.Single(records);
            Assert.Equal(10.5, records[0].StationX[1]);
            Assert.Equal(20.5, records[0].StationY[1]);
            Assert.Equal(30.5, records[0].StationX[2]);
            Assert.Equal(0, records[0].StationX[0]); // Reference station is always 0
        }

        [Fact]
        public void LoadCsv_WithRevolution_MapsCorrectly()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY,RevolutionIndex\n" +
                      "1,1,1,100.0,200.0,RevolutionOneOnly\n" +
                      "1,1,2,100.0,200.0,RevolutionFirstOfMany\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            var revs = svc.GetRevolutions(1, "");
            Assert.Contains("One Only", revs);
            Assert.Contains("First of Many", revs);
        }

        [Fact]
        public void GetMachineNumbers_MultipleSN_ReturnsSorted()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY\n" +
                      "3,1,1,100.0,200.0\n" +
                      "1,1,1,100.0,200.0\n" +
                      "2,1,1,100.0,200.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.Equal(new[] { 1, 2, 3 }, svc.GetMachineNumbers());
        }

        [Fact]
        public void GetCalibrationTimes_FiltersBySN()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY,StartCalibrationTime\n" +
                      "1,1,1,100.0,200.0,2026-01-01T10:00:00\n" +
                      "2,1,1,100.0,200.0,2026-02-01T10:00:00\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            var times = svc.GetCalibrationTimes(1);
            Assert.Single(times);
        }

        [Fact]
        public void GetCycles_ReturnsSortedDistinct()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY\n" +
                      "1,1,3,100.0,200.0\n" +
                      "1,1,1,100.0,200.0\n" +
                      "1,1,3,100.0,200.0\n" +
                      "1,1,2,100.0,200.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.Equal(new[] { 1, 2, 3 }, svc.GetCycles(1, ""));
        }

        [Fact]
        public void ApplyFilters_CycleRange_FiltersCorrectly()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY\n" +
                      "1,1,1,0.0,0.0\n" +
                      "1,1,2,0.0,0.0\n" +
                      "1,1,3,0.0,0.0\n" +
                      "1,1,4,0.0,0.0\n" +
                      "1,1,5,0.0,0.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            var filter = new CprFilterState
            {
                MachineSN = 1, Iteration = 1,
                CycleFrom = 2, CycleTo = 4,
                ColumnFrom = 0, ColumnTo = 0
            };
            var results = svc.ApplyFilters(filter);

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.InRange(r.CycleNumber, 2, 4));
        }

        [Fact]
        public void ApplyFilters_SwappedRange_StillWorks()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY\n" +
                      "1,1,1,0.0,0.0\n" +
                      "1,1,2,0.0,0.0\n" +
                      "1,1,3,0.0,0.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            // CycleFrom > CycleTo — should auto-swap
            var filter = new CprFilterState
            {
                MachineSN = 1, Iteration = 1,
                CycleFrom = 3, CycleTo = 1,
                ColumnFrom = 0, ColumnTo = 0
            };
            var results = svc.ApplyFilters(filter);

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public void ApplyBaseFilters_NoCycleColumnRange()
        {
            var csv = "sn,IterationNum,CycleNumber,ElementLocationX,ElementLocationY\n" +
                      "1,1,1,0.0,0.0\n" +
                      "1,1,2,0.0,0.0\n" +
                      "1,2,1,0.0,0.0\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            var filter = new CprFilterState { MachineSN = 1, Iteration = 1 };
            var results = svc.ApplyBaseFilters(filter);

            Assert.Equal(2, results.Count); // Only iteration 1
        }

        [Fact]
        public void GetStationValue_X_ReturnsStationX()
        {
            var rec = new CprRecord();
            rec.StationX[3] = 42.5;
            rec.StationY[3] = 99.9;

            Assert.Equal(42.5, CprDataService.GetStationValue(rec, "X", 3));
        }

        [Fact]
        public void GetStationValue_Y_ReturnsStationY()
        {
            var rec = new CprRecord();
            rec.StationX[3] = 42.5;
            rec.StationY[3] = 99.9;

            Assert.Equal(99.9, CprDataService.GetStationValue(rec, "Y", 3));
        }

        [Fact]
        public void LoadCsv_QuotedValues_TrimsQuotes()
        {
            var csv = "\"sn\",\"IterationNum\",\"CycleNumber\",\"ElementLocationX\",\"ElementLocationY\"\n" +
                      "\"1\",\"1\",\"1\",\"100.0\",\"200.0\"\n";
            var path = CreateCsvFile(csv);

            var svc = new CprDataService();
            svc.LoadCsv(path);

            Assert.True(svc.IsLoaded);
        }
    }
}
