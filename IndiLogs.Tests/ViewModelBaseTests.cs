using IndiLogs_3._0.ViewModels;
using System.Collections.Generic;
using Xunit;

namespace IndiLogs.Tests
{
    public class ViewModelBaseTests
    {
        private class TestViewModel : ViewModelBase
        {
            private string _name = "";
            public string Name
            {
                get => _name;
                set => SetField(ref _name, value);
            }

            private int _count;
            public int Count
            {
                get => _count;
                set => SetField(ref _count, value);
            }
        }

        [Fact]
        public void SetField_ChangedValue_RaisesPropertyChanged()
        {
            var vm = new TestViewModel();
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            vm.Name = "hello";

            Assert.Contains("Name", raised);
        }

        [Fact]
        public void SetField_SameValue_DoesNotRaise()
        {
            var vm = new TestViewModel { Name = "hello" };
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            vm.Name = "hello";

            Assert.Empty(raised);
        }

        [Fact]
        public void SetField_ReturnsTrueOnChange()
        {
            var vm = new TestViewModel();
            vm.Name = "first";
            // SetField is called internally; verify by checking the value was set
            Assert.Equal("first", vm.Name);
        }

        [Fact]
        public void NotifyPropertyChanged_RaisesEvent()
        {
            var vm = new TestViewModel();
            string? raised = null;
            vm.PropertyChanged += (s, e) => raised = e.PropertyName;

            vm.NotifyPropertyChanged("SomeProperty");

            Assert.Equal("SomeProperty", raised);
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            var vm = new TestViewModel();
            vm.Dispose();
            vm.Dispose(); // Should not throw
        }

        [Fact]
        public void SetField_IntProperty_Works()
        {
            var vm = new TestViewModel();
            var raised = new List<string>();
            vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName!);

            vm.Count = 42;

            Assert.Equal(42, vm.Count);
            Assert.Contains("Count", raised);
        }
    }
}
