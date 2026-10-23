using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace LittlePause {
    public partial class Widget {
        Viewbox fixedView;
        ScrollViewer freeScroll;
        Panel presetPanel;
        ComboBox sizeChoice;
        bool freeLayoutActive;
        bool IsFreeLayout {get{return freeLayoutActive;}}
        static ControlTemplate PageScrollTemplate(){return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollViewer'><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions><ScrollContentPresenter Name='PART_ScrollContentPresenter' Margin='0,0,8,0' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}'/><ScrollBar Name='PART_VerticalScrollBar' Grid.Column='1' MinWidth='0' Width='4' Style='{DynamicResource {x:Type ScrollBar}}' Orientation='Vertical' Minimum='0' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Value='{Binding VerticalOffset,RelativeSource={RelativeSource TemplatedParent},Mode=OneWay}' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/></Grid></ControlTemplate>");}
        void ConfigureFreeLayout(bool free){
            if(free==freeLayoutActive)return;
            freeLayoutActive=free;
            if(free){
                fixedView.Child=null;card.Child=null;card.Child=design;design.Width=double.NaN;design.Height=double.NaN;
                design.Children.Remove(body);freeScroll=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Template=PageScrollTemplate()};Panel.SetZIndex(freeScroll,1);design.Children.Add(freeScroll);body.VerticalAlignment=VerticalAlignment.Center;
            }else{
                freeScroll.Content=null;design.Children.Remove(freeScroll);freeScroll=null;card.Child=null;fixedView.Child=design;card.Child=fixedView;design.Width=344;body.VerticalAlignment=VerticalAlignment.Top;Panel.SetZIndex(body,1);design.Children.Add(body);
            }
            var buttons=presetPanel.Children.Cast<UIElement>().ToArray();presetPanel.Children.Clear();body.Children.Remove(presetPanel);
            presetPanel=free?(Panel)new WrapPanel {HorizontalAlignment=HorizontalAlignment.Center}:new UniformGrid {Columns=4};presetPanel.Margin=new Thickness(0,0,0,14);foreach(var button in buttons)presetPanel.Children.Add(button);body.Children.Insert(3,presetPanel);
            if(settingsPage!=null)Panel.SetZIndex(settingsPage,2);
        }
        Size ConstrainFreeSize(double width,double height){var area=dockSide!=DockSide.None?dockArea:DesktopWorkArea();return new Size(Math.Max(320,Math.Min(area.Width,double.IsNaN(width)||width<=0?400:width)),Math.Max(320,Math.Min(area.Height,double.IsNaN(height)||height<=0?720:height)));}
        void ApplyFreeSize(double width,double height){Size size=ConstrainFreeSize(width,height);Width=prefs.FreeWidth=size.Width;Height=prefs.FreeHeight=size.Height;UpdateFreeLayout();UpdateScaleReadout();}
        void UpdateFreeLayout(){if(!IsFreeLayout)return;design.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,Width-16),Math.Max(0,Height-16)),24,24);}
        void UpdateScaleReadout(){if(scaleCaption!=null)scaleCaption.Text=prefs.Size=="自由尺寸"?"尺寸":"缩放";if(scaleReadout!=null)scaleReadout.Text=prefs.Size=="自由尺寸"?(prefs.FreeWidth>0?prefs.FreeWidth:400).ToString("0")+" × "+(prefs.FreeHeight>0?prefs.FreeHeight:720).ToString("0"):(prefs.CardScale*100).ToString("0")+"%";}
    }
}
