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
        private const string TargetStyleKey = "FileListViewItemInsideGridViewStyle";
        private static bool _isInitialized;

        // VS 2022 風格暗色邊框顏色 (#3F3F46)
        private static readonly SolidColorBrush Vs2022BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));

        public static void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;

            // 1. 修補 Application 級別的 ResourceDictionary
            PatchResourceDictionaries();

            // 2. 註冊 TreeViewItem 與 ListViewItem 的 ClassHandler（解決樹狀與清單檢視的列高與內距）
            EventManager.RegisterClassHandler(
                typeof(TreeViewItem),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnItemLoaded),
                handledEventsToo: true);

            EventManager.RegisterClassHandler(
                typeof(ListViewItem),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnItemLoaded),
                handledEventsToo: true);

            // 3. 註冊 TreeView 與 ListView 的 ClassHandler（為「暫存變更」與「變更」清單加上 VS2022 邊框）
            EventManager.RegisterClassHandler(
                typeof(TreeView),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnItemsControlLoaded),
                handledEventsToo: true);

            EventManager.RegisterClassHandler(
                typeof(ListView),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnItemsControlLoaded),
                handledEventsToo: true);

            // 4. 註冊 TextBox 的 ClassHandler（縮窄「輸入訊息」輸入框高度）
            EventManager.RegisterClassHandler(
                typeof(TextBox),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnTextBoxLoaded),
                handledEventsToo: true);

            // 5. 對已開啟的視窗立即套用
            PatchExistingWindows();
        }

        public static void PatchResourceDictionaries()
        {
            try
            {
                if (Application.Current == null)
                {
                    return;
                }

                Style? patchedStyle = FindAndPatchStyle(Application.Current.Resources);

                if (patchedStyle != null)
                {
                    Application.Current.Resources[TargetStyleKey] = patchedStyle;
                }
                else
                {
                    var fallbackStyle = new Style(typeof(ListViewItem));
                    fallbackStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
                    fallbackStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 1, 0, 1)));
                    fallbackStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
                    Application.Current.Resources[TargetStyleKey] = fallbackStyle;
                }
            }
            catch
            {
                // 忽略例外
            }
        }

        private static Style? FindAndPatchStyle(ResourceDictionary dict)
        {
            if (dict == null)
            {
                return null;
            }

            if (dict.Contains(TargetStyleKey) && dict[TargetStyleKey] is Style originalStyle)
            {
                var newStyle = CreatePatchedStyle(originalStyle);
                dict[TargetStyleKey] = newStyle;
                return newStyle;
            }

            foreach (ResourceDictionary mergedDict in dict.MergedDictionaries)
            {
                var found = FindAndPatchStyle(mergedDict);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Style CreatePatchedStyle(Style originalStyle)
        {
            var newStyle = new Style(typeof(ListViewItem), originalStyle.BasedOn);

            foreach (SetterBase setterBase in originalStyle.Setters)
            {
                if (setterBase is Setter setter && setter.Property == FrameworkElement.MinHeightProperty)
                {
                    newStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
                }
                else
                {
                    newStyle.Setters.Add(setterBase);
                }
            }

            newStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0, 1, 0, 1)));
            newStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));

            foreach (TriggerBase trigger in originalStyle.Triggers)
            {
                newStyle.Triggers.Add(trigger);
            }

            return newStyle;
        }

        /// <summary>
        /// 處理清單或樹狀節點（TreeViewItem / ListViewItem）的列高與間距
        /// </summary>
        private static void OnItemLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement item && IsInsideGitToolWindow(item))
            {
                ApplyItemCompactStyle(item);
            }
        }

        private static void ApplyItemCompactStyle(FrameworkElement item)
        {
            item.MinHeight = 0.0;
            item.Margin = new Thickness(0);

            if (item is Control control)
            {
                control.Padding = new Thickness(0, 1, 0, 1);
            }

            var descriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.MinHeightProperty, item.GetType());
            descriptor?.RemoveValueChanged(item, OnItemMinHeightChanged);
            descriptor?.AddValueChanged(item, OnItemMinHeightChanged);
        }

        private static void OnItemMinHeightChanged(object? sender, EventArgs e)
        {
            if (sender is FrameworkElement element && element.MinHeight > 0)
            {
                element.MinHeight = 0.0;
            }
        }

        /// <summary>
        /// 處理清單容器（TreeView / ListView），為變更/暫存變更區塊加上邊框
        /// </summary>
        private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && IsInsideGitToolWindow(control))
            {
                ApplyListBorder(control);
            }
        }

        private static void ApplyListBorder(Control control)
        {
            control.BorderThickness = new Thickness(1);
            control.BorderBrush = Vs2022BorderBrush;
            control.Margin = new Thickness(0, 2, 0, 4);
        }

        /// <summary>
        /// 處理訊息輸入框（TextBox），縮窄 Commit 訊息輸入框的高度
        /// </summary>
        private static void OnTextBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && IsInsideGitToolWindow(textBox))
            {
                ApplyMessageTextBoxStyle(textBox);
            }
        }

        private static void ApplyMessageTextBoxStyle(TextBox textBox)
        {
            // 判斷是否為多行 Commit 訊息輸入框
            if (textBox.AcceptsReturn || textBox.MinLines > 1 || textBox.Height > 40 || textBox.ActualHeight > 40 || textBox.MinHeight > 40)
            {
                textBox.MinHeight = 45;
                textBox.MaxHeight = 60;
                textBox.Height = 52;
                textBox.Margin = new Thickness(0, 2, 0, 4);
                textBox.BorderThickness = new Thickness(1);
                textBox.BorderBrush = Vs2022BorderBrush;
            }
        }

        /// <summary>
        /// 判斷控制項是否位於 Git 相關視窗內（Git 變更、Team Explorer、Git 存放庫等）
        /// </summary>
        private static bool IsInsideGitToolWindow(DependencyObject element)
        {
            try
            {
                DependencyObject? current = element;

                while (current != null)
                {
                    string typeFullName = current.GetType().FullName ?? string.Empty;

                    if (typeFullName.IndexOf("FileListView", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("TeamExplorer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("GitChanges", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("GitRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("PendingChanges", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("GitWindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        typeFullName.IndexOf("CommitMessage", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    if (current is FrameworkElement fe)
                    {
                        current = VisualTreeHelper.GetParent(current) ?? fe.Parent;
                    }
                    else
                    {
                        current = VisualTreeHelper.GetParent(current);
                    }
                }
            }
            catch
            {
                // 忽略視覺樹例外
            }

            return false;
        }

        /// <summary>
        /// 尋找現存開啟中的視窗並立即套用樣式
        /// </summary>
        private static void PatchExistingWindows()
        {
            try
            {
                if (Application.Current == null)
                {
                    return;
                }

                foreach (Window window in Application.Current.Windows)
                {
                    if (window != null)
                    {
                        PatchVisualDescendants(window);
                    }
                }
            }
            catch
            {
                // 忽略例外
            }
        }

        private static void PatchVisualDescendants(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is TreeViewItem or ListViewItem && IsInsideGitToolWindow(child))
                {
                    ApplyItemCompactStyle((FrameworkElement)child);
                }
                else if (child is TreeView or ListView && IsInsideGitToolWindow(child))
                {
                    ApplyListBorder((Control)child);
                }
                else if (child is TextBox textBox && IsInsideGitToolWindow(textBox))
                {
                    ApplyMessageTextBoxStyle(textBox);
                }

                PatchVisualDescendants(child);
            }
        }
    }
}
