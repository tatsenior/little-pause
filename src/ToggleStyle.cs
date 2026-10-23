using System.Windows;
using System.Windows.Controls;

namespace LittlePause {
    public partial class Widget {
        void RegisterToggleStyle(){
            Resources.Add(typeof(CheckBox),System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
              <Setter Property='MinHeight' Value='28'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
                <Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='40'/></Grid.ColumnDefinitions>
                  <ContentPresenter Margin='0,0,14,0' VerticalAlignment='Center'><ContentPresenter.ContentTemplate><DataTemplate><TextBlock Text='{Binding}' TextWrapping='Wrap'/></DataTemplate></ContentPresenter.ContentTemplate></ContentPresenter>
                  <Border Name='SwitchTrack' Grid.Column='1' Width='40' Height='22' CornerRadius='11' BorderThickness='1' BorderBrush='#565656' Background='{DynamicResource SwitchOffSurfaceBrush}' VerticalAlignment='Center'>
                    <Ellipse Name='SwitchThumb' Width='14' Height='14' Fill='#BEBEBE' HorizontalAlignment='Left' VerticalAlignment='Center' Margin='3,0'/>
                  </Border>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsChecked' Value='True'><Setter TargetName='SwitchTrack' Property='Background' Value='{DynamicResource SwitchOnSurfaceBrush}'/><Setter TargetName='SwitchTrack' Property='BorderBrush' Value='#517A68'/><Setter TargetName='SwitchThumb' Property='HorizontalAlignment' Value='Right'/><Setter TargetName='SwitchThumb' Property='Fill' Value='#D5E7DE'/><Setter Property='ToolTip' Value='已开启，点击关闭'/></Trigger>
                  <Trigger Property='IsChecked' Value='False'><Setter Property='ToolTip' Value='已关闭，点击开启'/></Trigger>
                  <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='SwitchTrack' Property='BorderBrush' Value='#8A8A8A'/></Trigger>
                  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='SwitchTrack' Property='BorderBrush' Value='#BDBDBD'/></Trigger>
                  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>"));
        }
    }
}
