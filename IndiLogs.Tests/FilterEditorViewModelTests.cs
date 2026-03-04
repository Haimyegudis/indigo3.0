using IndiLogs_3._0.Models;
using IndiLogs_3._0.ViewModels;
using Xunit;

namespace IndiLogs.Tests
{
    public class FilterEditorViewModelTests
    {
        private FilterEditorViewModel CreateVM() => new FilterEditorViewModel();

        // ── Constructor defaults ──

        [Fact]
        public void Constructor_CreatesRootGroupNode()
        {
            var vm = CreateVM();
            Assert.Single(vm.RootNodes);
            Assert.Equal(NodeType.Group, vm.RootNodes[0].Type);
            Assert.Equal("AND", vm.RootNodes[0].LogicalOperator);
        }

        [Fact]
        public void Constructor_DefaultFilterFields_ContainsStandardFields()
        {
            var vm = CreateVM();
            Assert.Contains("Message", vm.AvailableFilterFields);
            Assert.Contains("Level", vm.AvailableFilterFields);
            Assert.Contains("ThreadName", vm.AvailableFilterFields);
            Assert.Contains("Logger", vm.AvailableFilterFields);
        }

        // ── AddGroup ──

        [Fact]
        public void AddGroupCommand_AddsGroupChild()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            vm.AddGroupCommand.Execute(root);

            Assert.Single(root.Children);
            Assert.Equal(NodeType.Group, root.Children[0].Type);
            Assert.Equal("AND", root.Children[0].LogicalOperator);
        }

        [Fact]
        public void AddGroupCommand_NullParam_DoesNotThrow()
        {
            var vm = CreateVM();
            vm.AddGroupCommand.Execute(null);
            // Should not throw, just no-op
        }

        // ── AddCondition ──

        [Fact]
        public void AddConditionCommand_AddsConditionChild()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            vm.AddConditionCommand.Execute(root);

            Assert.Single(root.Children);
            Assert.Equal(NodeType.Condition, root.Children[0].Type);
            Assert.Equal("Message", root.Children[0].Field);
            Assert.Equal("Contains", root.Children[0].Operator);
        }

        [Fact]
        public void AddConditionCommand_MultipleConditions()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            vm.AddConditionCommand.Execute(root);
            vm.AddConditionCommand.Execute(root);

            Assert.Equal(2, root.Children.Count);
        }

        // ── RemoveNode ──

        [Fact]
        public void RemoveNodeCommand_RemovesChildNode()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            vm.AddConditionCommand.Execute(root);
            var child = root.Children[0];

            vm.RemoveNodeCommand.Execute(child);

            Assert.Empty(root.Children);
        }

        [Fact]
        public void RemoveNodeCommand_RootNode_ClearsChildrenOnly()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            vm.AddConditionCommand.Execute(root);

            // Trying to remove root should only clear children, not remove root
            vm.RemoveNodeCommand.Execute(root);

            Assert.Single(vm.RootNodes); // Root still exists
            Assert.Empty(root.Children); // But children cleared
        }

        [Fact]
        public void RemoveNodeCommand_NestedNode_RemovesRecursively()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            vm.AddGroupCommand.Execute(root);
            var subGroup = root.Children[0];
            vm.AddConditionCommand.Execute(subGroup);
            var condition = subGroup.Children[0];

            vm.RemoveNodeCommand.Execute(condition);

            Assert.Empty(subGroup.Children);
            Assert.Single(root.Children); // SubGroup still there
        }

        // ── Set operator commands ──

        [Fact]
        public void SetAndCommand_ChangesOperator()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];
            root.LogicalOperator = "OR";

            vm.SetAndCommand.Execute(root);

            Assert.Equal("AND", root.LogicalOperator);
        }

        [Fact]
        public void SetOrCommand_ChangesOperator()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];

            vm.SetOrCommand.Execute(root);

            Assert.Equal("OR", root.LogicalOperator);
        }

        [Fact]
        public void SetNotAndCommand_ChangesOperator()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];

            vm.SetNotAndCommand.Execute(root);

            Assert.Equal("NOT AND", root.LogicalOperator);
        }

        [Fact]
        public void SetNotOrCommand_ChangesOperator()
        {
            var vm = CreateVM();
            var root = vm.RootNodes[0];

            vm.SetNotOrCommand.Execute(root);

            Assert.Equal("NOT OR", root.LogicalOperator);
        }

        // ── Property change ──

        [Fact]
        public void AvailableThreads_SetterRaisesPropertyChanged()
        {
            var vm = CreateVM();
            bool raised = false;
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(vm.AvailableThreads)) raised = true; };

            vm.AvailableThreads = new System.Collections.Generic.List<string> { "Thread1" };

            Assert.True(raised);
        }
    }
}
