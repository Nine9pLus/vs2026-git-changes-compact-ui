#pragma warning disable VSTHRD001, VSTHRD110
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace GitChangesCompactUI
{
    public static class GitFileListViewPatcher
    {
        private const double TargetItemHeight = 20.0;
        private const double TargetContentMinHeight = 14.0;
        private static bool _isInitialized;
        private static bool _fluentSizesPatched;

        // VS 2022 風格暗色邊框顏色 (#3F3F46)
        private static readonly SolidColorBrush Vs2022BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));

        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // 【核心】透過反射修改 FluentSizes 靜態欄位，從源頭解決行高 24px → 20px
            PatchFluentSizesViaReflection();

            // 註冊 ListView/TreeView 的 ClassHandler（邊框 + 容器重建）
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
        }

        #region FluentSizes 反射修補

        /// <summary>
        /// 【核心】透過反射修改 FluentSizes 和 FluentSpacings 的靜態欄位值。
        /// VS2026 Fluent 主題的 ControlTemplate 透過 {x:Static FluentSizes.ControlMinHeight} 綁定行高，
        /// 原始值 ControlMinHeight=24 + XSNudge Margin=3,3,3,3 → 實際佔高 ≈ 26px。
        /// 直接從源頭將 ControlMinHeight 改為 20，XSNudge margin 清零。
        /// </summary>
        private static void PatchFluentSizesViaReflection()
        {
            try
            {
                Assembly? targetAssembly = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.FullName != null && asm.FullName.Contains("Microsoft.TeamFoundation.Controls"))
                    {
                        targetAssembly = asm;
                        break;
                    }
                }

                if (targetAssembly == null) return;

                PatchStaticField(targetAssembly, "Microsoft.TeamFoundation.Controls.WPF.FluentSizes",
                    "ControlMinHeight", TargetItemHeight);
                PatchStaticField(targetAssembly, "Microsoft.TeamFoundation.Controls.WPF.FluentSizes",
                    "ContentMinHeight", TargetContentMinHeight);
                // 修改 FluentSpacings: 只清除垂直方向 margin (top=0, bottom=0)，保留水平方向 3px，還原左側紫色選取指示條
                PatchStaticField(targetAssembly, "Microsoft.TeamFoundation.Controls.WPF.FluentSpacings",
                    "XSNudge", new Thickness(3, 0, 3, 0));
                PatchStaticField(targetAssembly, "Microsoft.TeamFoundation.Controls.WPF.FluentSpacings",
                    "XSNudgeInvert", new Thickness(-3, 0, -3, 0));

                _fluentSizesPatched = true;
            }
            catch { }
        }

        private static void PatchStaticField(Assembly asm, string typeName, string fieldName, object newValue)
        {
            try
            {
                Type? type = asm.GetType(typeName);
                if (type == null) return;

                FieldInfo? field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                field?.SetValue(null, newValue);
            }
            catch { }
        }

        /// <summary>
        /// 防禦性：如果 Assembly 在 Initialize 時還沒載入，在第一個控制項 Loaded 時補修。
        /// </summary>
        private static void EnsureFluentSizesPatched()
        {
            if (_fluentSizesPatched) return;
            PatchFluentSizesViaReflection();
        }

        #endregion

        #region ListView / TreeView 邊框 + 容器重建

        private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && IsInsideGitChangesWindow(control))
            {
                EnsureFluentSizesPatched();
                ApplyListBorder(control);

                // 在所有修補完成後，透過微捲動強制 VirtualizingStackPanel 重建容器
                // 新容器會使用已修補的 FluentSizes 值（ControlMinHeight=20），從源頭正確渲染
                control.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    ForceContainerRegeneration(control);
                }));
            }
        }

        private static void ApplyListBorder(Control control)
        {
            control.BorderThickness = new Thickness(1);
            control.BorderBrush = Vs2022BorderBrush;
            control.Margin = new Thickness(0, 2, 0, 4);
        }

        /// <summary>
        /// 透過微捲動強制 VirtualizingStackPanel 丟棄並重建所有項目容器。
        /// 重建後的容器使用已修補的 FluentSizes.ControlMinHeight=20，從源頭正確渲染。
        /// </summary>
        private static void ForceContainerRegeneration(Control control)
        {
            try
            {
                var scrollViewer = FindChildOfType<ScrollViewer>(control);
                if (scrollViewer != null && scrollViewer.ScrollableHeight > 0)
                {
                    double originalOffset = scrollViewer.VerticalOffset;
                    scrollViewer.ScrollToVerticalOffset(originalOffset + 1);
                    scrollViewer.UpdateLayout();
                    scrollViewer.ScrollToVerticalOffset(originalOffset);
                }
                else if (control is ItemsControl ic)
                {
                    ic.Items.Refresh();
                }
            }
            catch { }
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

        private static T? FindChildOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                T? result = FindChildOfType<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion
    }
}
