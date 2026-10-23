using System;
using System.Windows;
using System.Windows.Controls;

namespace LittlePause {
    public partial class Widget {
        public static readonly DependencyProperty ControlSurfaceOpacityProperty=DependencyProperty.Register("ControlSurfaceOpacity",typeof(double),typeof(Widget),new PropertyMetadata(1.0));
        public double ControlSurfaceOpacity {get{return (double)GetValue(ControlSurfaceOpacityProperty);}set{SetValue(ControlSurfaceOpacityProperty,value);}}
        void InitializeControlTransparency(){
            foreach(var brush in new[]{field,primarySurface,controlOutline,switchOffSurface,switchOnSurface})System.Windows.Data.BindingOperations.SetBinding(brush,System.Windows.Media.Brush.OpacityProperty,new System.Windows.Data.Binding("ControlSurfaceOpacity"){Source=this,Mode=System.Windows.Data.BindingMode.OneWay});
        }
        void ApplyControlTransparency(){
            prefs.ControlTransparency=double.IsNaN(prefs.ControlTransparency)?0:Math.Max(0,Math.Min(1,prefs.ControlTransparency));
            double opacity=1-prefs.ControlTransparency;
            ControlSurfaceOpacity=opacity;
        }
        void AddControlTransparencySetting(StackPanel panel){
            var header=new Grid {Margin=new Thickness(0,20,0,8)};header.Children.Add(Text("控件背景透明度",11,muted));
            var percent=Text((prefs.ControlTransparency*100).ToString("0")+"%",11,muted);percent.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(percent);panel.Children.Add(header);
            controlTransparencySlider=new Slider {Minimum=0,Maximum=1,Value=prefs.ControlTransparency,IsMoveToPointEnabled=true,IsSnapToTickEnabled=false,SmallChange=.01,LargeChange=.1,ToolTip="数值越高，按钮、输入框和选择框的底色越透明"};
            controlTransparencySlider.ValueChanged+=delegate{prefs.ControlTransparency=controlTransparencySlider.Value;ApplyControlTransparency();percent.Text=(prefs.ControlTransparency*100).ToString("0")+"%";};panel.Children.Add(controlTransparencySlider);
            var hint=Text("数值越高，控件底色越透明，文字不受影响。",11,muted);hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new Thickness(0,6,0,8);panel.Children.Add(hint);
            var reset=Button("恢复默认控件底色",delegate{controlTransparencySlider.Value=0;},false);reset.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(reset);
        }
    }
}
