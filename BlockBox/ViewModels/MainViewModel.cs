using BlockBox.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BlockBox.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        // 当前模式（工作、游戏、常规）
        public AppMode CurrentMode { get; set; }

        // 左侧菜单列表（根据 CurrentMode 动态变化）
        public ObservableCollection<Category> SidebarMenus { get; set; }

        // 标签栏列表
        public ObservableCollection<string> Tabs { get; set; }

        // 内容区的快捷方式列表（如果 CurrentMode 是工作模式，里面就包含文件；游戏模式过滤办公软件）
        public ObservableCollection<ShortcutItem> DisplayedShortcuts { get; set; }

        // 切换模式的命令
        public ICommand SwitchModeCommand { get; }
    }
}
