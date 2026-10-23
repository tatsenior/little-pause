using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LittlePause {
    public sealed class Countdown {
        public double Duration { get; private set; }
        public double Remaining { get; private set; }
        public bool Running { get; private set; }
        private DateTime deadline;
        public void Start(double seconds, DateTime now) { Duration = Remaining = seconds; deadline = now.AddSeconds(seconds); Running = true; }
        public void Pause(DateTime now) { Tick(now); Running = false; }
        public void Resume(DateTime now) { if (Remaining > 0) { deadline = now.AddSeconds(Remaining); Running = true; } }
        public bool Tick(DateTime now) { if (!Running) return false; Remaining = Math.Max(0, (deadline - now).TotalSeconds); if (Remaining > 0) return false; Running = false; return true; }
        public void Reset() { Remaining = Duration; Running = false; }
        public void Restore(double duration, double remaining, bool running, DateTime due) { Duration = duration; Remaining = remaining; Running = running; deadline = due; }
        public DateTime Deadline { get { return deadline; } }
        public static string Format(double seconds) { var t = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds))); return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00"); }
    }
    [DataContract] public class Preferences {
        [DataMember] public string Theme = "墨夜";
        [DataMember] public string Image = "";
        [DataMember] public double ImageOpacity = .65;
        [DataMember] public double Shade = .60;
        [DataMember] public string Message = "休息一下，喝杯水吧。";
        [DataMember] public int Seconds = 1500;
        [DataMember] public bool Pinned = true;
        [DataMember] public bool Sound = true;
        [DataMember] public double Left = -1;
        [DataMember] public double Top = -1;
        [DataMember] public double Duration;
        [DataMember] public double Remaining;
        [DataMember] public bool Running;
        [DataMember] public DateTime Due = DateTime.SpecifyKind(DateTime.MinValue,DateTimeKind.Utc);
        [DataMember] public string ActiveMessage;
        [DataMember] public string Display = "环形数字";
        [DataMember] public string Size = "完整卡片";
        [DataMember] public string UiFont = "Microsoft YaHei UI";
        [DataMember] public string UiWeight = "常规";
        [DataMember] public string TimerFont = "Segoe UI";
        [DataMember] public string TimerWeight = "细体";
        [DataMember] public double TimerFontSize = 48;
        [DataMember] public bool TypographyDefaultsV2;
        [DataMember] public double FreeWidth = 400;
        [DataMember] public double FreeHeight = 720;
        [DataMember] public double CardScale = 1;
        [DataMember] public bool AutoHide;
        [DataMember] public bool DesktopOptionsSaved;
        [DataMember] public string DockEdge;
    }
    public static class Storage {
        public static readonly string Folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittlePause");
        public static Preferences Read() { try { using (var s = File.OpenRead(System.IO.Path.Combine(Folder, "settings.json"))) return (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(s); } catch { return new Preferences(); } }
        public static void Write(Preferences p) { Directory.CreateDirectory(Folder); string path = System.IO.Path.Combine(Folder, "settings.json"), temp = path + ".tmp"; using (var s = File.Create(temp)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(s, p); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        public static void Log(string s) { try { Directory.CreateDirectory(Folder); File.AppendAllText(System.IO.Path.Combine(Folder, "app.log"), DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch { } }
    }
    public partial class Widget : Window {
        readonly Preferences prefs;
        readonly Countdown clock = new Countdown();
        readonly DispatcherTimer ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        readonly DispatcherTimer saver = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        readonly bool smoke;
        Forms.NotifyIcon tray;
        Border card, imageLayer, shadeLayer;
        StackPanel body;
        TextBlock digits, status, endAt, feedback;
        TextBox message, hours, minutes, seconds;
        Button primary, pin;
        System.Windows.Shapes.Path arc;
        Grid design, dial;
        Grid settingsPage;
        ScrollViewer settingsScroll;
        bool settingsOpen;
        int navigationVersion;
        readonly System.Collections.Generic.List<Slider> backgroundSliders=new System.Collections.Generic.List<Slider>();
        string cachedImagePath;
        ImageBrush cachedImageBrush;
        Color cachedImageAccent, cachedShadeTint;
        readonly System.Collections.Generic.List<Button> typographyButtons=new System.Collections.Generic.List<Button>();
        TextBlock typographyPreviewText,typographyPreviewDigits;
        ComboBox uiFontChoice,uiWeightChoice,timerFontChoice,timerWeightChoice,timerSizeChoice;
        static string[] installedFontNames;
        StackPanel centerDigits;
        Ellipse track;
        Line hourHand, minuteHand, secondHand;
        TextBlock analogRemaining;
        SolidColorBrush accent = new SolidColorBrush(Color.FromRgb(151, 235, 203));
        readonly SolidColorBrush primarySurface=new SolidColorBrush(Color.FromRgb(42,57,51));
        readonly SolidColorBrush white = new SolidColorBrush(Color.FromRgb(236, 236, 236));
        readonly SolidColorBrush muted = new SolidColorBrush(Color.FromRgb(165, 165, 165));
        readonly SolidColorBrush field = new SolidColorBrush(Color.FromArgb(225, 47, 47, 47));
        bool quitting, editing, completed;
        string activeMessage;
        public Widget(bool smokeTest) {
            smoke = smokeTest; prefs = smoke ? new Preferences() : Storage.Read();
            if(!prefs.TypographyDefaultsV2){if(prefs.TimerFontSize==43||prefs.TimerFontSize<=0)prefs.TimerFontSize=48;prefs.TypographyDefaultsV2=true;}
            if(!prefs.DesktopOptionsSaved){prefs.AutoHide=false;prefs.DesktopOptionsSaved=true;}
            Title = "小憩 · 桌面倒计时"; Width = 360; Height = 675; ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = prefs.Pinned;ShowInTaskbar=false;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14; Foreground = white;
            Resources.Add(typeof(ComboBox), System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>
              <Setter Property='Foreground' Value='#F2F4F9'/><Setter Property='Background' Value='#303030'/><Setter Property='Padding' Value='10,4'/>
              <Setter Property='ItemContainerStyle'><Setter.Value><Style TargetType='ComboBoxItem'><Setter Property='Foreground' Value='#F2F4F9'/><Setter Property='Background' Value='#303030'/><Setter Property='Padding' Value='10,8'/><Style.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter Property='Background' Value='#454545'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter Property='Background' Value='#404040'/></Trigger></Style.Triggers></Style></Setter.Value></Setter>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid>
                <ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='#303030' BorderBrush='#3A3A3A' BorderThickness='1' CornerRadius='8'/></ControlTemplate></ToggleButton.Template></ToggleButton>
                <ContentPresenter Margin='10,0,27,0' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}'/>
                <TextBlock Text='⌄' Foreground='#C0CCDA' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0' IsHitTestVisible='False'/>
                <Popup Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'><Border MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}' Background='#303030' BorderBrush='#3A3A3A' BorderThickness='1' CornerRadius='8' Padding='2'><ScrollViewer MaxHeight='280'><ItemsPresenter/></ScrollViewer></Border></Popup>
              </Grid></ControlTemplate></Setter.Value></Setter></Style>"));
            Resources.Add(typeof(Slider),System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'><Setter Property='Height' Value='22'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Grid Background='Transparent'><Border Height='3' Background='#494949' CornerRadius='2' VerticalAlignment='Center'/><Track Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='#D5D5D5' Height='3' CornerRadius='2'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='14' Height='14'><Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='#ECECEC'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>"));
            Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'><Setter Property='MinWidth' Value='0'/><Setter Property='Width' Value='5'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='#646464' Margin='1,2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter></Style>"));
            Resources.Add(typeof(TextBox),System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'><Border Name='InputSurface' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='Transparent' BorderThickness='1'><ScrollViewer Name='PART_ContentHost' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='InputSurface' Property='BorderBrush' Value='#454545'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='InputSurface' Property='BorderBrush' Value='#777777'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='InputSurface' Property='Opacity' Value='.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
            Build(); ApplyTheme(); ApplyTypography(); ApplyLayout();
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if(e.Key==Key.Escape&&settingsOpen){CloseSettings();e.Handled=true;} };
            var area = DesktopWorkArea();
            bool unplaced=prefs.Left==-1&&prefs.Top==-1;
            Left = unplaced ? area.Right - Width - 36 : Math.Max(area.Left, Math.Min(prefs.Left, area.Right - Width));
            Top = unplaced ? area.Top + 50 : Math.Max(area.Top, Math.Min(prefs.Top, area.Bottom - Height));
            Point initialPosition=DesktopGeometry.Clamp(new Point(Left,Top),new Size(Width,Height),area);Left=initialPosition.X;Top=initialPosition.Y;
            InitializeDesktopBehavior();
            SetDuration(prefs.Seconds > 0 && prefs.Seconds <= 359999 ? prefs.Seconds : 1500);
            message.Text = prefs.Message ?? "";
            activeMessage = prefs.ActiveMessage ?? prefs.Message;
            if (prefs.Duration > 0 && prefs.Remaining > 0) clock.Restore(prefs.Duration, prefs.Remaining, prefs.Running, prefs.Due);
            if (!smoke) { CreateTray(); ticker.Tick += delegate { Tick(); }; ticker.Start(); saver.Tick += delegate { Save(); }; saver.Start(); }
            Loaded += delegate { Refresh();
#if DIAGNOSTICS
                if(smoke)SmokeTest();
#endif
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!quitting && !smoke) { e.Cancel = true; Hide(); Save(); } };
        }
        TextBlock Text(string s, double size, Brush color) { return new TextBlock { Text = s, FontSize = size+2, Foreground = color, VerticalAlignment = VerticalAlignment.Center }; }
        Button Button(string s, Action action, bool main) {
            var b = new Button { Content = s, Height = main ? 44 : 32, Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand, Background = main ? primarySurface : field, Foreground = white, BorderThickness = new Thickness(0), FontSize = main ? 16 : 14, FontWeight = main ? FontWeights.SemiBold : FontWeights.Normal };
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(cp); b.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            b.MouseEnter += delegate { b.Opacity = .82; }; b.MouseLeave += delegate { b.Opacity = 1; }; b.Click += delegate { action(); }; typographyButtons.Add(b);b.FontWeight=ReadWeight(prefs.UiWeight,false);return b;
        }
        TextBox Input(string value, double width) { return new TextBox { Text = value, Width = width, Height = 38, Background = field, Foreground = white, CaretBrush = white, BorderThickness = new Thickness(0), Padding = new Thickness(11,0,11,0), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 16, MaxLength = 3 }; }
        void Build() {
            card = new Border { Margin = new Thickness(8), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Effect = new DropShadowEffect { Color=Colors.Black, BlurRadius=12, ShadowDepth=2, Opacity=.28, RenderingBias=RenderingBias.Quality } };
            desktopSurface=new Grid();desktopSurface.Children.Add(card);Content=desktopSurface;
            var root = new Grid { Width=344, Height=659 }; design=root; fixedView=new Viewbox {Child=root,Stretch=Stretch.Uniform};card.Child=fixedView;
            imageLayer = new Border { CornerRadius = new CornerRadius(23), IsHitTestVisible = false }; root.Children.Add(imageLayer);
            shadeLayer = new Border { CornerRadius = new CornerRadius(23), IsHitTestVisible = false }; root.Children.Add(shadeLayer);
            body = new StackPanel { Margin = new Thickness(22, 18, 22, 16) }; root.Children.Add(body);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
            header.MouseLeftButtonDown += DragHeader;
            header.Children.Add(Text("◷  小憩", 16, white));
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            pin = Button("置顶", delegate { Topmost = !Topmost; prefs.Pinned = Topmost; pin.Content = Topmost ? "已置顶" : "置顶"; Save(); }, false); pin.ToolTip = "切换窗口置顶"; pin.Content = prefs.Pinned ? "已置顶" : "置顶";
            tools.Children.Add(pin); var gear=Button("⚙",Settings,false);gear.Margin=new Thickness(4,0,0,0);gear.ToolTip="外观、显示方式与尺寸";tools.Children.Add(gear); var hide = Button("—", delegate { Hide(); Save(); }, false); hide.Margin = new Thickness(5,0,0,0); hide.ToolTip = "收起到托盘，倒计时继续"; tools.Children.Add(hide); header.Children.Add(tools); body.Children.Add(header);
            var eyebrow = Text("留一点时间，给自己。", 12, muted); eyebrow.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(eyebrow);
            var ring = new Grid { Width = 186, Height = 186, Margin = new Thickness(0, 14, 0, 10) };
            track=new Ellipse { Margin = new Thickness(6), Stroke = new SolidColorBrush(Color.FromArgb(40, 190, 215, 215)), StrokeThickness = 5 };ring.Children.Add(track);
            arc = new System.Windows.Shapes.Path { Stroke = accent, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; ring.Children.Add(arc);
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };centerDigits=center;
            status = Text("准备开始", 11, accent); status.HorizontalAlignment = HorizontalAlignment.Center; center.Children.Add(status);
            digits = Text("25:00", 43, white); digits.FontFamily = new FontFamily("Segoe UI"); digits.FontWeight = FontWeights.Light; digits.HorizontalAlignment = HorizontalAlignment.Center; center.Children.Add(digits);
            endAt = Text("把此刻交给专注", 10, muted); endAt.HorizontalAlignment = HorizontalAlignment.Center; center.Children.Add(endAt); ring.Children.Add(center);
            dial=new Grid {Width=186,Height=186};var face=new Canvas {Width=186,Height=186};dial.Children.Add(face);
            for(int i=0;i<60;i++){double angle=i*Math.PI/30;double inner=i%5==0?70:75;face.Children.Add(new Line {X1=93+inner*Math.Sin(angle),Y1=93-inner*Math.Cos(angle),X2=93+80*Math.Sin(angle),Y2=93-80*Math.Cos(angle),Stroke=i%5==0?white:muted,StrokeThickness=i%5==0?2:1,Opacity=.65});}
            foreach(var item in new[]{new{Label="12",X=85.0,Y=26.0},new{Label="3",X=148.0,Y=85.0},new{Label="6",X=89.0,Y=144.0},new{Label="9",X=29.0,Y=85.0}}){var label=Text(item.Label,10,muted);Canvas.SetLeft(label,item.X);Canvas.SetTop(label,item.Y);face.Children.Add(label);}
            hourHand=new Line{X1=93,Y1=93,Stroke=white,StrokeThickness=4,StrokeEndLineCap=PenLineCap.Round};minuteHand=new Line{X1=93,Y1=93,Stroke=accent,StrokeThickness=3,StrokeEndLineCap=PenLineCap.Round};secondHand=new Line{X1=93,Y1=93,Stroke=accent,StrokeThickness=1};face.Children.Add(hourHand);face.Children.Add(minuteHand);face.Children.Add(secondHand);face.Children.Add(new Ellipse{Width=7,Height=7,Fill=accent,Margin=new Thickness(89.5,89.5,0,0)});
            analogRemaining=Text("",11,white);analogRemaining.HorizontalAlignment=HorizontalAlignment.Center;analogRemaining.VerticalAlignment=VerticalAlignment.Top;analogRemaining.Margin=new Thickness(0,118,0,0);dial.Children.Add(analogRemaining);ring.Children.Add(dial);body.Children.Add(ring);
            var presets = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0,0,0,14) };presetPanel=presets;
            foreach (int n in new[] {5, 15, 25, 60}) { int duration = n * 60; var b = Button(n + " 分钟", delegate { if (CanEdit()) { SetDuration(duration); Refresh(); Save(); } }, false); b.Padding=new Thickness(8,0,8,0);b.Margin = new Thickness(0,0,5,0); presets.Children.Add(b); } body.Children.Add(presets);
            var scenarios = new ComboBox { Height=32, Margin=new Thickness(0,0,0,12), FontSize=14, VerticalContentAlignment=VerticalAlignment.Center, ToolTip="选择场景，同时填入时长和通知内容" };
            scenarios.Items.Add("选择场景 · 时长与提醒一起设置");
            foreach(string label in new[]{"40 分钟 · 眼部放松","1 小时 · 会议提醒","25 分钟 · 专注工作","5 分钟 · 短暂休息","45 分钟 · 起身活动"})scenarios.Items.Add(label);
            scenarios.SelectedIndex=0;
            scenarios.SelectionChanged+=delegate {int index=scenarios.SelectedIndex-1;if(index<0)return;if(CanEdit()){SetDuration(new[]{2400,3600,1500,300,2700}[index]);message.Text=new[]{"眼部放松时间到了，看看远处，让眼睛休息一下。","会议时间到了，请准备参会。","本轮专注已完成，休息一下再继续吧。","休息结束，带着好状态开始下一段专注。","起身活动一下，伸展身体，喝杯水吧。"}[index];Refresh();Save();}scenarios.SelectedIndex=0;};
            body.Children.Add(scenarios);
            body.Children.Add(Text("自定时长", 11, muted));
            var time = new Grid {Margin = new Thickness(0,6,0,13)};
            hours = Input("0", double.NaN); minutes = Input("25", double.NaN); seconds = Input("0", double.NaN);int column=0;
            foreach(var pair in new[] {new {Box=hours,Unit="时"},new {Box=minutes,Unit="分"},new {Box=seconds,Unit="秒"}}) {time.ColumnDefinitions.Add(new ColumnDefinition());var cell=new Grid();cell.ColumnDefinitions.Add(new ColumnDefinition());cell.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});cell.Children.Add(pair.Box);var unit=Text(pair.Unit,11,muted);unit.Margin=new Thickness(5,0,9,0);Grid.SetColumn(unit,1);cell.Children.Add(unit);Grid.SetColumn(cell,column++);time.Children.Add(cell);pair.Box.TextChanged += delegate { if (!editing && clock.Duration == 0) Refresh(); }; }
            body.Children.Add(time); body.Children.Add(Text("到点提醒我", 11, muted));
            message = Input("", double.NaN); message.Height = 39; message.MaxLength = 240; message.ToolTip = "自定义 Windows 通知内容（最多 240 字）"; message.Margin = new Thickness(0,6,0,14); body.Children.Add(message);
            var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition {Width = new GridLength(74)});
            primary = Button("开始专注", Toggle, true); actions.Children.Add(primary);
            var reset = Button("重置", delegate { clock.Reset(); clock.Restore(0,0,false,DateTime.MinValue); completed = false; Refresh(); Save(); }, false); reset.Height = 44; reset.Margin = new Thickness(8,0,0,0); Grid.SetColumn(reset,1); actions.Children.Add(reset); body.Children.Add(actions);
            var footer = new Grid { Margin = new Thickness(0,13,0,0) };
            var settings = Button("外观与设置", Settings, false); settings.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(settings);
            var test = Button("测试通知 ↗", async delegate { await Notify(true); }, false); test.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(test); body.Children.Add(footer);
            feedback = Text("拖动顶部移动 · 拖动四角缩放", 10, muted); feedback.TextWrapping = TextWrapping.Wrap; feedback.Margin = new Thickness(0,10,0,0); feedback.HorizontalAlignment = HorizontalAlignment.Center; body.Children.Add(feedback);
            BuildResizeHandles();design.SizeChanged+=delegate{if(IsFreeLayout)UpdateFreeLayout();};
        }
        bool CanEdit() { if (clock.Duration > 0 && (clock.Running || clock.Remaining < clock.Duration) && !completed) { feedback.Text = "请先重置，再修改时长或提醒文字。"; return false; } return true; }
        int ReadDuration() { int h,m,s; if (!int.TryParse(hours.Text,out h) || !int.TryParse(minutes.Text,out m) || !int.TryParse(seconds.Text,out s) || h < 0 || h > 99 || m < 0 || m > 59 || s < 0 || s > 59) return 0; return h*3600+m*60+s; }
        void SetDuration(int value) { editing=true; hours.Text=(value/3600).ToString(); minutes.Text=(value/60%60).ToString(); seconds.Text=(value%60).ToString(); editing=false; prefs.Seconds=value; Refresh(); }
        async void Toggle() {
            if (clock.Running) {var now=DateTime.UtcNow;if(clock.Tick(now)){completed=true;Refresh();Save();await Notify(false);return;}clock.Pause(now);}
            else if (clock.Duration > 0 && clock.Remaining > 0 && !completed) clock.Resume(DateTime.UtcNow);
            else { int total=ReadDuration(); if(total<1) { feedback.Text="请输入有效时长：1 秒至 99 时 59 分 59 秒。"; return; } activeMessage=string.IsNullOrWhiteSpace(message.Text)?"时间到了，休息一下吧。":message.Text.Trim(); completed=false; clock.Start(total,DateTime.UtcNow); }
            feedback.Text="拖动顶部移动 · 拖动四角缩放"; Refresh(); Save();
        }
        async void Tick() { if(clock.Tick(DateTime.UtcNow)) { completed=true; Refresh(); Save(); await Notify(false); } else if(!dockAnimating)Refresh(); }
        void Refresh() {
            double value=clock.Duration > 0 ? clock.Remaining : ReadDuration();
            digits.Text=Countdown.Format(value); FitTimerText();
            analogRemaining.Text=Countdown.Format(value);SetHand(hourHand,value/43200*2*Math.PI,37);SetHand(minuteHand,value/3600*2*Math.PI,54);SetHand(secondHand,value/60*2*Math.PI,66);
            status.Text=completed?"时间到了":clock.Running?"正在专注":clock.Duration>0?"已暂停":"准备开始";
            endAt.Text=clock.Running?"预计 "+DateTime.Now.AddSeconds(value).ToString("HH:mm")+" 结束":completed?"做得好，给自己片刻休息":"把此刻交给专注";
            primary.Content=completed?"再来一次":clock.Running?"暂停一下":clock.Duration>0?"继续倒计时":"开始专注";
            bool locked=clock.Duration>0 && !completed;
            hours.IsEnabled=minutes.IsEnabled=seconds.IsEnabled=message.IsEnabled=!locked;
            double fraction=clock.Duration>0?value/clock.Duration:1; fraction=Math.Max(0,Math.Min(.99999,fraction));
            if(fraction<=0) { arc.Data=null; return; }
            double angle=fraction*Math.PI*2; var figure=new PathFigure {StartPoint=new Point(93,6), IsClosed=false}; figure.Segments.Add(new ArcSegment(new Point(93+87*Math.Sin(angle),93-87*Math.Cos(angle)),new Size(87,87),0,fraction>.5,SweepDirection.Clockwise,true)); arc.Data=new PathGeometry(new[]{figure});
        }
        void Save() { if(smoke)return; try { prefs.Message=message.Text; int duration=ReadDuration(); if(duration>0)prefs.Seconds=duration; var position=SavedDesktopLocation();prefs.Left=position.X; prefs.Top=position.Y;prefs.DockEdge=dockSide.ToString(); prefs.Duration=completed?0:clock.Duration; prefs.Remaining=completed?0:clock.Remaining; prefs.Running=clock.Running; prefs.Due=DateTime.SpecifyKind(clock.Deadline,DateTimeKind.Utc); prefs.ActiveMessage=activeMessage; Storage.Write(prefs); } catch(Exception e) { Storage.Log(e.ToString()); feedback.Text="设置保存失败，可检查目录写入权限。"; } }
        void CreateTray() {
            tray=new Forms.NotifyIcon {Icon=System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName), Text="小憩 · 桌面倒计时", Visible=true};
            var menu=new Forms.ContextMenuStrip(); menu.Items.Add("显示小挂件",null,delegate { Dispatcher.Invoke(new Action(Reveal)); }); menu.Items.Add("暂停 / 继续",null,delegate { Dispatcher.Invoke(new Action(Toggle)); }); menu.Items.Add("退出",null,delegate { Dispatcher.Invoke(new Action(Quit)); }); tray.ContextMenuStrip=menu;
            tray.DoubleClick+=delegate { Dispatcher.Invoke(new Action(Reveal)); }; tray.BalloonTipClicked+=delegate { Dispatcher.Invoke(new Action(Reveal)); };
        }
        void Reveal() { Show(); WindowState=WindowState.Normal;RevealDock(true); Activate(); }
        void Quit() { Save(); quitting=true; ticker.Stop(); saver.Stop(); if(tray!=null)tray.Dispose(); Close(); }
        async Task Notify(bool test) {
            feedback.Text="正在发送 Windows 通知…";
            string result=await Task.Run(()=>Notifications.Send(test?"通知测试":"倒计时结束",test?"看到这条通知，就可以安心开始计时了。":activeMessage,prefs.Sound));
            if(result==null) feedback.Text=test?"通知已提交，请查看 Windows 通知中心。":"时间到了 · 通知已提交";
            else { feedback.Text="通知未成功发送："+result; Storage.Log(result); if(tray!=null) { tray.BalloonTipTitle="小憩 · 倒计时提醒"; tray.BalloonTipText=test?"这是一条测试提醒":activeMessage; tray.ShowBalloonTip(10000); } if(!test)Reveal(); }
        }
        void ApplyTheme() {
            string bg="#212121", ac="#97EBCB";
            if(prefs.Theme=="暮紫") {bg="#211B32";ac="#C5AFFA";} else if(prefs.Theme=="海盐") {bg="#142B39";ac="#88D9F3";} else if(prefs.Theme=="暖杏") {bg="#35251F";ac="#F2C69A";}
            design.Background=(Brush)new BrushConverter().ConvertFromString(bg); accent.Color=(Color)ColorConverter.ConvertFromString(ac);
            imageLayer.Background=null; shadeLayer.Background=null;
            if(!string.IsNullOrEmpty(prefs.Image)) try {
                if(cachedImagePath!=prefs.Image||cachedImageBrush==null){
                var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth=1200; bitmap.UriSource=new Uri(prefs.Image,UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
                cachedImageBrush=new ImageBrush(bitmap) {Stretch=Stretch.UniformToFill, AlignmentX=AlignmentX.Center, AlignmentY=AlignmentY.Center};
                var tiny=new TransformedBitmap(bitmap,new ScaleTransform(12.0/bitmap.PixelWidth,12.0/bitmap.PixelHeight)); var rgba=new FormatConvertedBitmap(tiny,PixelFormats.Bgra32,null,0); byte[] pixels=new byte[rgba.PixelWidth*rgba.PixelHeight*4]; rgba.CopyPixels(pixels,rgba.PixelWidth*4,0);
                double r=0,g=0,b=0; for(int i=0;i<pixels.Length;i+=4){ b+=pixels[i];g+=pixels[i+1];r+=pixels[i+2]; } double count=pixels.Length/4; r/=count;g/=count;b/=count;
                cachedImageAccent=Color.FromRgb((byte)(r*.4+153),(byte)(g*.4+153),(byte)(b*.4+153));
                cachedShadeTint=Color.FromRgb((byte)(r*.12),(byte)(g*.12),(byte)(b*.12));cachedImagePath=prefs.Image;
                }
                cachedImageBrush.Opacity=Math.Max(0,Math.Min(1,prefs.ImageOpacity));imageLayer.Background=cachedImageBrush;accent.Color=cachedImageAccent;
                shadeLayer.Background=new SolidColorBrush(Color.FromArgb((byte)(255*Math.Max(.25,Math.Min(.9,prefs.Shade))),cachedShadeTint.R,cachedShadeTint.G,cachedShadeTint.B));
            } catch(Exception e) { cachedImageBrush=null;cachedImagePath=null;feedback.Text="背景图片无法读取，已使用预设风格。"; Storage.Log(e.Message); }
            else {cachedImageBrush=null;cachedImagePath=null;}
            foreach(var slider in backgroundSliders)slider.IsEnabled=imageLayer.Background!=null;
            primarySurface.Color=Color.FromRgb((byte)(accent.Color.R*.13+22),(byte)(accent.Color.G*.13+22),(byte)(accent.Color.B*.13+22));
        }
        void SetHand(Line hand,double angle,double length){hand.X2=93+length*Math.Sin(angle);hand.Y2=93-length*Math.Cos(angle);}
        static FontWeight ReadWeight(string name,bool timer){
            if(name=="细体")return FontWeights.Light;if(name=="中等")return FontWeights.Medium;if(name=="半粗")return FontWeights.SemiBold;if(name=="粗体")return FontWeights.Bold;
            return name==null&&timer?FontWeights.Light:FontWeights.Normal;
        }
        static FontFamily ReadFamily(string name,string fallback){try{return new FontFamily(string.IsNullOrWhiteSpace(name)?fallback:name);}catch{return new FontFamily(fallback);}}
        static string FontLabel(FontFamily family){string name;return family.FamilyNames.TryGetValue(System.Windows.Markup.XmlLanguage.GetLanguage("zh-cn"),out name)?name:family.Source;}
        static string[] InstalledFonts(){if(installedFontNames==null)installedFontNames=Fonts.SystemFontFamilies.Select(FontLabel).Concat(new[]{"Microsoft YaHei UI","Segoe UI"}).Distinct().OrderBy(name=>name,StringComparer.CurrentCultureIgnoreCase).ToArray();return installedFontNames;}
        void ApplyTypography(){
            FontFamily=ReadFamily(prefs.UiFont,"Microsoft YaHei UI");FontWeight=ReadWeight(prefs.UiWeight,false);
            foreach(var button in typographyButtons)button.FontWeight=FontWeight;
            digits.FontFamily=ReadFamily(prefs.TimerFont,"Segoe UI");digits.FontWeight=ReadWeight(prefs.TimerWeight,true);
            analogRemaining.FontFamily=digits.FontFamily;analogRemaining.FontWeight=digits.FontWeight;FitTimerText();
            if(typographyPreviewText!=null){typographyPreviewText.FontFamily=FontFamily;typographyPreviewText.FontWeight=FontWeight;typographyPreviewDigits.FontFamily=digits.FontFamily;typographyPreviewDigits.FontWeight=digits.FontWeight;typographyPreviewDigits.FontSize=Math.Min(56,Math.Max(28,prefs.TimerFontSize>0?prefs.TimerFontSize:48));}
        }
        void FitTimerText(){
            double size=prefs.TimerFontSize>0?Math.Max(28,Math.Min(56,prefs.TimerFontSize)):48;
            var sample=new FormattedText(digits.Text??"25:00",System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(digits.FontFamily,digits.FontStyle,digits.FontWeight,digits.FontStretch),size,white,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            digits.FontSize=sample.WidthIncludingTrailingWhitespace>150?size*150/sample.WidthIncludingTrailingWhitespace:size;
        }
        void ApplyLayout(){
            bool compact=prefs.Size=="中号圆角方框"||prefs.Size=="大号方框";
            for(int i=0;i<body.Children.Count;i++)body.Children[i].Visibility=compact&&(i==1||(i>=3&&i<=8)||i>=10)?Visibility.Collapsed:Visibility.Visible;
            bool analog=prefs.Display=="钟表指针",plain=prefs.Display=="纯数字";
            dial.Visibility=analog?Visibility.Visible:Visibility.Collapsed;centerDigits.Visibility=analog?Visibility.Collapsed:Visibility.Visible;arc.Visibility=track.Visibility=plain?Visibility.Collapsed:Visibility.Visible;
            if(settingsOpen)return; // Keep the current page stable; apply a chosen size on return.
            activeDesktopSize=prefs.Size;
            ConfigureFreeLayout(prefs.Size=="自由尺寸");
            if(!IsFreeLayout)design.Height=compact?344:659;
            ApplyDesktopScale(prefs.CardScale>0?prefs.CardScale:1);
            if(IsFreeLayout)UpdateFreeLayout();else{double radius=prefs.Size=="大号方框"?8:24;design.Clip=new RectangleGeometry(new Rect(0,0,344,design.Height),radius,radius);}
            if(IsLoaded)RepositionAfterLayout();
        }
        void Settings() {
            if(settingsOpen)return;
            RevealDock(true);
            if(settingsPage==null)BuildSettingsPage();
            SwitchPage(true);
        }
        void BuildSettingsPage() {
            settingsPage=new Grid {Margin=new Thickness(22,18,22,16),Visibility=Visibility.Collapsed};
            settingsPage.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            settingsPage.RowDefinitions.Add(new RowDefinition());
            settingsPage.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            design.Children.Add(settingsPage);Panel.SetZIndex(settingsPage,2);
            var header=new Grid {Margin=new Thickness(0,0,0,18),Background=Brushes.Transparent,Cursor=Cursors.SizeAll};header.MouseLeftButtonDown+=DragHeader;
            var back=Button("‹ 返回",CloseSettings,false);back.HorizontalAlignment=HorizontalAlignment.Left;header.Children.Add(back);
            var title=Text("外观与设置",14,white);title.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(title);settingsPage.Children.Add(header);
            var panel=new StackPanel {Margin=new Thickness(0,0,5,0)};
            settingsScroll=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.VerticalOnly};Grid.SetRow(settingsScroll,1);settingsPage.Children.Add(settingsScroll);
            settingsScroll.Template=PageScrollTemplate();
            panel.Children.Add(Text("个性化",20,white)); var sub=Text("调整挂件的显示与外观",12,muted); sub.Margin=new Thickness(0,8,0,22);panel.Children.Add(sub);
            AddChoice(panel,"计时显示",new[]{"环形数字","纯数字","钟表指针"},prefs.Display,delegate(string v){prefs.Display=v;ApplyLayout();Refresh();Save();});
            sizeChoice=AddChoice(panel,"挂件尺寸",new[]{"完整卡片","大号卡片","中号圆角方框","大号方框","自由尺寸"},prefs.Size,delegate(string v){prefs.Size=v;prefs.CardScale=1;ApplyLayout();UpdateScaleReadout();Save();});
            AddDesktopSettings(panel);
            var themeLabel=Text("主题",11,muted);themeLabel.Margin=new Thickness(0,8,0,8);panel.Children.Add(themeLabel);
            var row=new System.Windows.Controls.Primitives.UniformGrid {Columns=4}; foreach(string name in new[]{"墨夜","暮紫","海盐","暖杏"}) { string theme=name; var b=Button(name,delegate {prefs.Theme=theme;prefs.Image="";ApplyTheme();Save();},false);b.Margin=new Thickness(0,0,5,0);row.Children.Add(b); }panel.Children.Add(row);
            AddTypographySettings(panel);
            var choose=Button("选择背景图片…",delegate {var picker=new OpenFileDialog {Title="选择背景图（自动居中裁剪）",Filter="图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|所有文件|*.*"}; if(picker.ShowDialog(this)==true) {try {var check=new BitmapImage();check.BeginInit();check.CacheOption=BitmapCacheOption.OnLoad;check.UriSource=new Uri(picker.FileName);check.DecodePixelWidth=64;check.EndInit(); if(!smoke){Directory.CreateDirectory(Storage.Folder);string target=System.IO.Path.Combine(Storage.Folder,"background"+System.IO.Path.GetExtension(picker.FileName)); if(!string.Equals(target,picker.FileName,StringComparison.OrdinalIgnoreCase))File.Copy(picker.FileName,target,true);prefs.Image=target;}else prefs.Image=picker.FileName;cachedImagePath=null;ApplyTheme();Save();} catch {MessageBox.Show(this,"无法读取这张图片，请选择 PNG、JPG 或 BMP 图片。","图片读取失败");}}},false);choose.Margin=new Thickness(0,18,0,8);panel.Children.Add(choose);
            var tip=Text("选择图片后可调节下方效果。图片自动居中裁剪，配色随图片调整。",11,muted);tip.TextWrapping=TextWrapping.Wrap;panel.Children.Add(tip);
            AddSlider(panel,"背景图片透明度",0,1,prefs.ImageOpacity,delegate(double v){prefs.ImageOpacity=v;ApplyTheme();});
            AddSlider(panel,"暗色遮罩强度（增强文字对比）",.25,.9,prefs.Shade,delegate(double v){prefs.Shade=v;ApplyTheme();});
            var divider=new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)),Margin=new Thickness(0,22,0,18)};panel.Children.Add(divider);
            panel.Children.Add(Text("通知",14,white));
            var sound=new CheckBox {Content="通知提示音",IsChecked=prefs.Sound,Foreground=white,Margin=new Thickness(0,14,0,10)};sound.Checked+=delegate {prefs.Sound=true;};sound.Unchecked+=delegate {prefs.Sound=false;};panel.Children.Add(sound);
            var note=Text("收起到托盘后继续计时；休眠期间到期会在唤醒后提醒。退出后暂停投递，下次启动会恢复并补发到期提醒。",11,muted);note.TextWrapping=TextWrapping.Wrap;panel.Children.Add(note);
            var close=Button("完成",CloseSettings,true);close.Margin=new Thickness(0,18,0,0);Grid.SetRow(close,2);settingsPage.Children.Add(close);
        }
        void DragHeader(object sender,MouseButtonEventArgs e){
            var node=e.OriginalSource as DependencyObject;
            while(node!=null&&node!=sender){if(node is System.Windows.Controls.Primitives.ButtonBase)return;node=VisualTreeHelper.GetParent(node);}
            DetachDock();userMoving=true;
            try{DragMove();}catch(InvalidOperationException){}finally{userMoving=false;FinishUserMove();}
        }
        void CloseSettings(){if(!settingsOpen)return;Save();SwitchPage(false);}
        void SwitchPage(bool showSettings){
            settingsOpen=showSettings;if(freeScroll!=null)freeScroll.IsHitTestVisible=!showSettings;int version=++navigationVersion;
            FrameworkElement incoming=showSettings?(FrameworkElement)settingsPage:body,outgoing=showSettings?(FrameworkElement)body:settingsPage;
            double fromOpacity=incoming.Visibility==Visibility.Visible?incoming.Opacity:0;
            double offset=incoming.Visibility==Visibility.Visible?((TranslateTransform)incoming.RenderTransform).X:(showSettings?18:-18);
            incoming.Visibility=Visibility.Visible;incoming.IsHitTestVisible=true;outgoing.IsHitTestVisible=false;
            var duration=TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?190:0);
            var easing=new CubicEase {EasingMode=EasingMode.EaseOut};
            var inTransform=incoming.RenderTransform as TranslateTransform;if(inTransform==null){inTransform=new TranslateTransform();incoming.RenderTransform=inTransform;}
            var outTransform=outgoing.RenderTransform as TranslateTransform;if(outTransform==null){outTransform=new TranslateTransform();outgoing.RenderTransform=outTransform;}
            incoming.BeginAnimation(OpacityProperty,new DoubleAnimation(fromOpacity,1,duration){EasingFunction=easing});
            inTransform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(offset,0,duration){EasingFunction=easing});
            outTransform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(outTransform.X,showSettings?-12:12,duration){EasingFunction=easing});
            var fade=new DoubleAnimation(outgoing.Opacity,0,duration){EasingFunction=easing};
            fade.Completed+=delegate{if(version!=navigationVersion)return;outgoing.Visibility=Visibility.Collapsed;if(!showSettings)ApplyLayout();incoming.Focus();};
            outgoing.BeginAnimation(OpacityProperty,fade);
        }
        ComboBox AddChoice(StackPanel panel,string title,string[] values,string current,Action<string> changed){panel.Children.Add(Text(title,11,muted));var combo=new ComboBox{Height=30,Margin=new Thickness(0,5,0,12),ItemsSource=values,SelectedItem=current??values[0]};combo.SelectionChanged+=delegate{if(combo.SelectedItem!=null)changed((string)combo.SelectedItem);};panel.Children.Add(combo);return combo;}
        void AddTypographySettings(StackPanel panel){
            panel.Children.Add(new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(30,255,255,255)),Margin=new Thickness(0,22,0,18)});
            var title=Text("字体与文字",14,white);title.Margin=new Thickness(0,0,0,14);panel.Children.Add(title);
            string[] fonts=InstalledFonts(),weights=new[]{"细体","常规","中等","半粗","粗体"};
            uiFontChoice=AddChoice(panel,"界面字体",fonts,FontLabel(FontFamily),delegate(string v){prefs.UiFont=v;ApplyTypography();Save();});
            uiWeightChoice=AddChoice(panel,"界面字重",weights,prefs.UiWeight??"常规",delegate(string v){prefs.UiWeight=v;ApplyTypography();Save();});
            timerFontChoice=AddChoice(panel,"倒计时数字字体",fonts,FontLabel(digits.FontFamily),delegate(string v){prefs.TimerFont=v;ApplyTypography();Save();});
            timerWeightChoice=AddChoice(panel,"倒计时数字字重",weights,prefs.TimerWeight??"细体",delegate(string v){prefs.TimerWeight=v;ApplyTypography();Save();});
            timerSizeChoice=AddChoice(panel,"倒计时数字字号",new[]{"28","32","36","40","43","48","52","56"},(prefs.TimerFontSize>0?prefs.TimerFontSize:48).ToString("0"),delegate(string v){prefs.TimerFontSize=double.Parse(v,System.Globalization.CultureInfo.InvariantCulture);ApplyTypography();Save();});
            var preview=new StackPanel();typographyPreviewText=Text("文字预览 · 休息一下，喝杯水吧。",12,white);typographyPreviewText.TextWrapping=TextWrapping.Wrap;preview.Children.Add(typographyPreviewText);typographyPreviewDigits=Text("25:00",43,white);preview.Children.Add(typographyPreviewDigits);
            panel.Children.Add(new Border {Background=field,CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Child=preview,Margin=new Thickness(0,0,0,10)});
            var tip=Text("数字过长时会自动缩小以适应表盘。字体设置只作用于挂件内文字。",11,muted);tip.TextWrapping=TextWrapping.Wrap;panel.Children.Add(tip);ApplyTypography();
            var reset=Button("恢复默认字体",ResetTypography,false);reset.Margin=new Thickness(0,12,0,0);panel.Children.Add(reset);
        }
        void ResetTypography(){
            prefs.UiFont="Microsoft YaHei UI";prefs.UiWeight="常规";prefs.TimerFont="Segoe UI";prefs.TimerWeight="细体";prefs.TimerFontSize=48;
            uiFontChoice.SelectedItem=FontLabel(ReadFamily(prefs.UiFont,prefs.UiFont));uiWeightChoice.SelectedItem="常规";timerFontChoice.SelectedItem=FontLabel(ReadFamily(prefs.TimerFont,prefs.TimerFont));timerWeightChoice.SelectedItem="细体";timerSizeChoice.SelectedItem="48";ApplyTypography();Save();
        }
        void AddSlider(StackPanel panel,string label,double min,double max,double value,Action<double> changed) {
            var header=new Grid {Margin=new Thickness(0,16,0,8)};header.Children.Add(Text(label,11,muted));
            var percent=Text((Math.Max(min,Math.Min(max,value))*100).ToString("0")+"%",11,muted);percent.HorizontalAlignment=HorizontalAlignment.Right;header.Children.Add(percent);panel.Children.Add(header);
            var slider=new Slider {Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),IsMoveToPointEnabled=true,IsSnapToTickEnabled=false,TickFrequency=.01,SmallChange=.01,LargeChange=(max-min)/10,IsEnabled=imageLayer.Background!=null,ToolTip="先选择背景图片；可点击轨道定位，也可拖动滑块连续调节"};
            slider.ValueChanged+=delegate {percent.Text=(slider.Value*100).ToString("0")+"%";changed(slider.Value);};backgroundSliders.Add(slider);panel.Children.Add(slider);
        }
    }
    public static class Notifications {
        public const string AppId="LittlePause.Desktop.Timer";
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        public static string Payload(string title,string message,bool sound) {return "<toast duration=\"long\"><visual><binding template=\"ToastGeneric\"><text>"+SecurityElement.Escape(title)+"</text><text>"+SecurityElement.Escape(message??"时间到了")+"</text></binding></visual>"+(sound?"<audio src=\"ms-winsoundevent:Notification.Reminder\"/>":"<audio silent=\"true\"/>")+"</toast>";}
        public static string Send(string title,string message,bool sound) {
            try {
                Register(); string xml=Convert.ToBase64String(Encoding.UTF8.GetBytes(Payload(title,message,sound)));
                string script="$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { [Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] > $null; [Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime] > $null; $x=New-Object Windows.Data.Xml.Dom.XmlDocument; $x.LoadXml([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+xml+"'))); $n=[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('"+AppId+"'); if ([int]$n.Setting -ne 0) { throw ('Windows notifications disabled: '+$n.Setting) }; $t=[Windows.UI.Notifications.ToastNotification]::new($x); $n.Show($t); Write-Output 'SUBMITTED'; } catch { Write-Output $_.Exception.Message; exit 1 }";
                var start=new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),"-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
                using(var process=Process.Start(start)) {var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(20000)){process.Kill();return "通知服务响应超时";}Task.WaitAll(output,error); if(process.ExitCode!=0)return (output.Result+error.Result).Trim(); if(!output.Result.Contains("SUBMITTED"))return "通知服务未确认提交";}
                return null;
            }catch(Exception e){return e.Message;}
        }
        static void Register() {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            string exe=Process.GetCurrentProcess().MainModule.FileName;
            using(var key=Registry.CurrentUser.CreateSubKey("Software\\Classes\\AppUserModelId\\"+AppId)) {key.SetValue("DisplayName","小憩倒计时");key.SetValue("IconUri",exe);}
            object link=new ShellLink(); try {
                ((IShellLinkW)link).SetPath(exe);((IShellLinkW)link).SetWorkingDirectory(System.IO.Path.GetDirectoryName(exe));((IShellLinkW)link).SetDescription("小憩 · 桌面倒计时");
                var store=(IPropertyStore)link;var key=new PropertyKey {Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),Id=5};var val=new PropVariant {Type=31,Pointer=Marshal.StringToCoTaskMemUni(AppId)};
                try{store.SetValue(ref key,ref val);store.Commit();}finally{Marshal.FreeCoTaskMem(val.Pointer);}
                ((IPersistFile)link).Save(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"小憩倒计时.lnk"),true);
            }finally{Marshal.FinalReleaseComObject(link);}
        }
        [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLink{}
        [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IShellLinkW {
            void GetPath(IntPtr file,int max,IntPtr data,uint flags);void GetIDList(out IntPtr id);void SetIDList(IntPtr id);void GetDescription(IntPtr name,int max);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string name);void GetWorkingDirectory(IntPtr dir,int max);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string dir);void GetArguments(IntPtr args,int max);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string args);void GetHotkey(out short key);void SetHotkey(short key);void GetShowCmd(out int cmd);void SetShowCmd(int cmd);void GetIconLocation(IntPtr path,int max,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string path,int index);void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string path,uint reserved);void Resolve(IntPtr hwnd,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string path);
        }
        [StructLayout(LayoutKind.Sequential)]struct PropertyKey {public Guid Format;public uint Id;}
        [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant {[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;}
        [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyStore {void GetCount(out uint count);void GetAt(uint index,out PropertyKey key);void GetValue(ref PropertyKey key,out PropVariant value);void SetValue(ref PropertyKey key,ref PropVariant value);void Commit();}
    }
    static partial class Program {
        [STAThread] static void Main(string[] args) {
#if DIAGNOSTICS
            if(args.Contains("--self-test")){try{SelfTest();File.WriteAllText("self-test.txt","PASS: deadlines, pause/resume, completion once, restore, long duration, XML escaping");}catch(Exception e){File.WriteAllText("self-test.txt",e.ToString());Environment.ExitCode=1;}return;}
            if(args.Contains("--test-notification")){string result=Notifications.Send("小憩 · 通知测试","这是一条 Windows 原生通知，倒计时到点后会这样提醒你。",true);File.WriteAllText("notification-test.txt",result??"SUBMITTED");Environment.ExitCode=result==null?0:1;return;}
            bool smoke=args.Contains("--smoke-test");
#else
            bool smoke=false;
#endif
            bool created;using(var mutex=new Mutex(true,"Local\\LittlePause.Desktop.Timer",out created)) {if(!created&&!smoke){MessageBox.Show("小憩已在运行，请双击系统托盘中的图标打开。","小憩");return;}try{Notifications.SetCurrentProcessExplicitAppUserModelID(Notifications.AppId);var app=new Application();app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){Storage.Log(e.Exception.ToString());MessageBox.Show("操作未完成："+e.Exception.Message,"小憩");e.Handled=true;};app.Run(new Widget(smoke));}catch(Exception e){Storage.Log(e.ToString());File.WriteAllText("startup-error.txt",e.ToString());Environment.ExitCode=1;}}
        }
    }
}
