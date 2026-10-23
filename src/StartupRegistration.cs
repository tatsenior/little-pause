using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace LittlePause {
    public interface IStartupRegistration {bool IsEnabled {get;} void SetEnabled(bool enabled);}
    public sealed class StartupRegistration : IStartupRegistration {
        public const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        readonly string registryPath,valueName,executable;
        public StartupRegistration():this(RunKey,"LittlePause",Process.GetCurrentProcess().MainModule.FileName){}
        public StartupRegistration(string path,string name,string exe){registryPath=path;valueName=name;executable=Path.GetFullPath(exe);}
        public static string Command(string executable){return "\""+Path.GetFullPath(executable)+"\" --autostart";}
        public bool IsEnabled {get{using(var key=Registry.CurrentUser.OpenSubKey(registryPath))return key!=null&&string.Equals(key.GetValue(valueName) as string,Command(executable),StringComparison.OrdinalIgnoreCase);}}
        public void SetEnabled(bool enabled){
            if(enabled&&!File.Exists(executable))throw new FileNotFoundException("程序文件不存在，请先将程序放到固定文件夹。",executable);
            if(enabled){using(var key=Registry.CurrentUser.CreateSubKey(registryPath))key.SetValue(valueName,Command(executable),RegistryValueKind.String);}
            else {using(var key=Registry.CurrentUser.OpenSubKey(registryPath,true))if(key!=null)key.DeleteValue(valueName,false);}
            if(IsEnabled!=enabled)throw new IOException("自启动设置未能保存，请检查当前用户的写入权限。");
        }
    }
    public partial class Widget {
        IStartupRegistration startup;
        CheckBox startupSetting;
        bool syncingStartup;
        void AddStartupSetting(StackPanel panel){
            startupSetting=new CheckBox {Content="开机自启动",Foreground=white,Margin=new Thickness(0,4,0,8)};
            var note=Text("登录当前 Windows 用户后自动运行。开启前请将程序放在固定文件夹。",11,muted);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,0,0,12);
            try{startupSetting.IsChecked=startup.IsEnabled;}catch(Exception e){startupSetting.IsEnabled=false;note.Text="无法读取自启动设置："+e.Message;}
            RoutedEventHandler changed=delegate{
                if(syncingStartup)return;bool enabled=startupSetting.IsChecked==true;
                try{startup.SetEnabled(enabled);note.Text=enabled?"已开启：登录当前 Windows 用户后自动运行。移动程序后请重新开关此选项。":"已关闭开机自启动。";}
                catch(Exception e){syncingStartup=true;try{startupSetting.IsChecked=startup.IsEnabled;}catch{startupSetting.IsChecked=!enabled;}finally{syncingStartup=false;}note.Text="设置未成功："+e.Message;Storage.Log(e.Message);}
            };
            startupSetting.Checked+=changed;startupSetting.Unchecked+=changed;panel.Children.Add(startupSetting);panel.Children.Add(note);
        }
    }
}
