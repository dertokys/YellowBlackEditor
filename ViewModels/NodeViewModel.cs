using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.ViewModels
{
    public class NodeViewModel : INotifyPropertyChanged
    {
        private string _name = "";
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isVisible = true;
        private bool _isHit;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string FullPath { get; set; } = "";
        public NodeType Type { get; set; }
        public NodeViewModel? Parent { get; set; }
        public ObservableCollection<NodeViewModel> Children { get; } = new();

        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public bool IsVisible { get => _isVisible; set { _isVisible = value; OnPropertyChanged(); } }
        public bool IsHit { get => _isHit; set { _isHit = value; OnPropertyChanged(); } }

        public string Icon =>
            Type == NodeType.Section ? "📁" :
            Type == NodeType.Document ? "📝" :
            "🖼️";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}