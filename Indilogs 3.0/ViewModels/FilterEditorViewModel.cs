using IndiLogs_3._0.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IndiLogs_3._0.ViewModels
{
    public class FilterEditorViewModel : ViewModelBase
    {
        public ObservableCollection<FilterNode> RootNodes { get; set; }

        // Available values for dropdowns
        private List<string> _availableThreads = new List<string>();
        public List<string> AvailableThreads
        {
            get => _availableThreads;
            set { _availableThreads = value; OnPropertyChanged(); }
        }

        private List<string> _availableLoggers = new List<string>();
        public List<string> AvailableLoggers
        {
            get => _availableLoggers;
            set { _availableLoggers = value; OnPropertyChanged(); }
        }

        // Dynamic filter field list (standard fields + plugin columns)
        private List<string> _availableFilterFields = new List<string>
        {
            "Message", "Level", "ThreadName", "Logger", "ProcessName",
            "Method", "Pattern", "Data", "Exception"
        };
        public List<string> AvailableFilterFields
        {
            get => _availableFilterFields;
            set { _availableFilterFields = value; OnPropertyChanged(); }
        }

        // Commands for editing the tree
        public ICommand AddGroupCommand { get; }
        public ICommand AddConditionCommand { get; }
        public ICommand RemoveNodeCommand { get; }

        // Commands for changing the logical operator
        public ICommand SetAndCommand { get; }
        public ICommand SetOrCommand { get; }
        public ICommand SetNotAndCommand { get; }
        public ICommand SetNotOrCommand { get; }

        public FilterEditorViewModel()
        {
            RootNodes = new ObservableCollection<FilterNode>();
            // Create a root group (Root) as default
            RootNodes.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });

            // --- Initialize commands ---

            // Add a new group under the current node
            AddGroupCommand = new RelayCommand(node => 
            {
                if (node is FilterNode fn) 
                    fn.Children.Add(new FilterNode { Type = NodeType.Group, LogicalOperator = "AND" });
            });

            // Add a new condition under the current node
            AddConditionCommand = new RelayCommand(node => 
            {
                if (node is FilterNode fn)
                    fn.Children.Add(new FilterNode { Type = NodeType.Condition, Field = "Message", Operator = "Contains" });
            });

            // Delete a node (recursive)
            RemoveNodeCommand = new RelayCommand(RemoveNode);

            // --- Logic for changing group type ---
            SetAndCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "AND"; 
            });

            SetOrCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "OR"; 
            });

            SetNotAndCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "NOT AND"; 
            });

            SetNotOrCommand = new RelayCommand(node => 
            { 
                if (node is FilterNode fn) fn.LogicalOperator = "NOT OR"; 
            });
        }

        private void RemoveNode(object param)
        {
            if (param is FilterNode nodeToRemove)
            {
                // If trying to delete the root, only clear its children (so a root always remains)
                if (RootNodes.Contains(nodeToRemove))
                {
                    nodeToRemove.Children.Clear();
                }
                else
                {
                    RemoveNodeRecursive(RootNodes, nodeToRemove);
                }
            }
        }

        private bool RemoveNodeRecursive(ObservableCollection<FilterNode> nodes, FilterNode target)
        {
            if (nodes.Contains(target))
            {
                nodes.Remove(target);
                return true;
            }

            foreach (var node in nodes)
            {
                if (node.Type == NodeType.Group)
                {
                    if (RemoveNodeRecursive(node.Children, target)) return true;
                }
            }
            return false;
        }

        // INotifyPropertyChanged inherited from ViewModelBase
    }
}