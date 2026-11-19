#pragma warning disable VSTHRD001, VSTHRD110
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GitChangesCompactUI
{
    public static class GitFileListViewPatcher
    {
        private const double TargetItemHeight = 22.0;
        private static bool _isInitialized;
        private static readonly DependencyPropertyDescriptor ItemContainerStyleDescriptor =
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemContainerStyleProperty, typeof(ListView));

        // VS 2022 風格暗色邊框顏色 (#3F3F46)
        private static readonly SolidColorBrush Vs2022BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));

        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            EventManager.RegisterClassHandler(
                typeof(ListView),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnItemsControlLoaded),
                handledEventsToo: true);

            EventManager.RegisterClassHandler(
                typeof(TreeView),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnItemsControlLoaded),
                handledEventsToo: true);

            // 註冊 TextBox 的 ClassHandler（縮窄 Commit 訊息輸入框）
            EventManager.RegisterClassHandler(
                typeof(TextBox),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnTextBoxLoaded),
                handledEventsToo: true);

            // 註冊 ButtonBase 的 ClassHandler（按鈕間距）
            EventManager.RegisterClassHandler(
                typeof(ButtonBase),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnButtonLoaded),
                handledEventsToo: true);

            // 正式 VS 可能在套件背景載入完成前就已建立 Git 視窗
            foreach (PresentationSource source in PresentationSource.CurrentSources)
            {
                if (source.RootVisual != null) ApplyExistingListStyles(source.RootVisual);
            }
        }

        #region Git 清單樣式

        private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && IsInsideGitChangesWindow(control))
                ApplyListStyle(control);
        }

        private static void ApplyListStyle(Control control)
        {
            ApplyListBorder(control);
            if (control is not ListView list || !IsGitChangeSection(list)) return;

            // VS 切換清單檢視時會指派新的官方樣式。
            ItemContainerStyleDescriptor.RemoveValueChanged(list, ApplyRowStyle);
            ApplyRowStyle(list, EventArgs.Empty);
            ItemContainerStyleDescriptor.AddValueChanged(list, ApplyRowStyle);
            list.Unloaded -= OnListUnloaded;
            list.Unloaded += OnListUnloaded;
        }

        private static void ApplyRowStyle(object sender, EventArgs e)
        {
            var list = (ListView)sender;
            if (list.ItemContainerStyle is Style style && style is not GitRowStyle)
                list.SetCurrentValue(ItemsControl.ItemContainerStyleProperty, new GitRowStyle(style));
        }

        private sealed class GitRowStyle : Style
        {
            public GitRowStyle(Style original) : base(original.TargetType, original)
            {
                Setters.Add(new Setter(FrameworkElement.MinHeightProperty, TargetItemHeight));
                Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, TargetItemHeight));
                Setters.Add(new Setter(FrameworkElement.HeightProperty, TargetItemHeight));
            }
        }

        private static void OnListUnloaded(object sender, RoutedEventArgs e)
        {
            var list = (ListView)sender;
            ItemContainerStyleDescriptor.RemoveValueChanged(list, ApplyRowStyle);
            list.Unloaded -= OnListUnloaded;
        }

        private static bool IsGitChangeSection(DependencyObject element)
        {
            for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current.GetType().FullName == "Microsoft.TeamFoundation.Git.Provider.PendingChanges.ChangesSectionView")
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 處理清單容器（TreeView / ListView），加上邊框與上下 2px 內距
        /// </summary>
        private static void ApplyListBorder(Control control)
        {
            control.BorderThickness = new Thickness(1);
            control.BorderBrush = Vs2022BorderBrush;
            control.Margin = new Thickness(0, 2, 0, 4);
            control.Padding = new Thickness(0, 2, 0, 2);
        }

        private static void ApplyExistingListStyles(DependencyObject element)
        {
            if (element is ListView or TreeView)
                OnItemsControlLoaded(element, new RoutedEventArgs());

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                ApplyExistingListStyles(VisualTreeHelper.GetChild(element, i));
        }

        #endregion

        #region TextBox（Commit 訊息輸入框）

        private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && IsInsideGitChangesWindow(textBox))
            {
                ApplyMessageTextBoxStyle(textBox);
            }
        }

        private static void ApplyMessageTextBoxStyle(TextBox textBox)
        {
            if (textBox.AcceptsReturn || textBox.MinLines > 1 ||
                textBox.Height > 35 || textBox.ActualHeight > 35 || textBox.MinHeight > 35)
            {
                textBox.MinHeight = 36;
                textBox.MaxHeight = 52;
                textBox.Height = 46;
                textBox.Margin = new Thickness(0, 1, 0, 4);
                textBox.VerticalAlignment = VerticalAlignment.Top;

                AdjustCommitSectionContainers(textBox);
            }
        }

        private static void AdjustCommitSectionContainers(FrameworkElement element)
        {
            try
            {
                DependencyObject? current = element;
                int depth = 0;

                while (current != null && depth < 8)
                {
                    depth++;

                    if (current is FrameworkElement fe && fe != element)
                    {
                        string typeName = fe.GetType().Name;

                        if (typeName.IndexOf("ToolWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            typeName.IndexOf("GitChangesWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fe is TreeView || fe is ListView)
                        {
                            break;
                        }

                        if (fe.MinHeight > 60) fe.MinHeight = 0;
                        if (!double.IsNaN(fe.Height) && fe.Height > 60) fe.Height = double.NaN;
                        fe.MaxHeight = double.PositiveInfinity;

                        Grid? parentGrid = (fe.Parent as Grid) ?? (VisualTreeHelper.GetParent(fe) as Grid);
                        if (parentGrid != null)
                        {
                            int row = Grid.GetRow(fe);
                            if (row >= 0 && row < parentGrid.RowDefinitions.Count)
                            {
                                RowDefinition rowDef = parentGrid.RowDefinitions[row];
                                if (rowDef.MinHeight > 60) rowDef.MinHeight = 0;
                                if (rowDef.Height.IsAbsolute && rowDef.Height.Value > 60)
                                    rowDef.Height = GridLength.Auto;
                                else if (rowDef.Height.IsStar)
                                    rowDef.Height = GridLength.Auto;
                            }
                        }
                    }

                    current = VisualTreeHelper.GetParent(current)
                        ?? (current is FrameworkElement feParent ? feParent.Parent : null);
                }
            }
            catch { }
        }

        #endregion

        #region Button（認可/提交按鈕間距）

        private static void OnButtonLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ButtonBase button && IsInsideGitChangesWindow(button))
            {
                button.MinHeight = 26;
                button.Margin = new Thickness(0, 3, 0, 4);
                button.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 判斷控制項是否位於 Git Changes 相關視窗內。
        /// </summary>
        private static bool IsInsideGitChangesWindow(DependencyObject element)
        {
            try
            {
                DependencyObject? current = element;

                while (current != null)
                {
                    string typeFullName = current.GetType().FullName ?? string.Empty;

                    if (typeFullName.IndexOf("GitChanges", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("FileListView", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("IndentedItemListView", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("DraggableListView", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("PendingChanges", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("CommitMessage", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    current = current is FrameworkElement fe
                        ? (VisualTreeHelper.GetParent(current) ?? fe.Parent)
                        : VisualTreeHelper.GetParent(current);
                }
            }
            catch { }

            return false;
        }

        #endregion
    }
}
