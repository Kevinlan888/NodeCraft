using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace NodeCraft.Flow
{
    public class FlowNodePalette : Control
    {
        public static readonly DependencyProperty CategoriesProperty =
            DependencyProperty.Register(nameof(Categories), typeof(IEnumerable), typeof(FlowNodePalette), new PropertyMetadata(null));

        public IEnumerable Categories
        {
            get => (IEnumerable)GetValue(CategoriesProperty);
            set => SetValue(CategoriesProperty, value);
        }

        static FlowNodePalette()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FlowNodePalette), new FrameworkPropertyMetadata(typeof(FlowNodePalette)));
        }
    }

    public class FlowNodePaletteCategory : INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        public string Title { get; set; }

        public string IconKind { get; set; }

        public IList<FlowNodePaletteItem> Items { get; set; } = new List<FlowNodePaletteItem>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class FlowNodePaletteItem
    {
        public string DisplayName { get; set; }

        public string Description { get; set; }

        public string IconKind { get; set; }

        public string TypeKey { get; set; }

        public string NodeTypeName { get; set; }
    }

}
