using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace LittlePause {
    public enum DockSide { None, Left, Right, Top, Bottom }
    public static class DesktopGeometry {
        public static DockSide Detect(Rect card,Rect area,double distance){
            if(!card.IntersectsWith(area))return DockSide.None;
            double best=distance;DockSide side=DockSide.None;
            double[] gaps={Math.Max(0,card.Left-area.Left),Math.Max(0,area.Right-card.Right),Math.Max(0,card.Top-area.Top),Math.Max(0,area.Bottom-card.Bottom)};
            for(int i=0;i<4;i++)if(gaps[i]<=best){best=gaps[i];side=(DockSide)(i+1);}return side;
        }
        public static Point Clamp(Point point,Size size,Rect area){return new Point(Math.Max(area.Left,Math.Min(point.X,area.Right-size.Width)),Math.Max(area.Top,Math.Min(point.Y,area.Bottom-size.Height)));}
        public static Point Anchor(Point point,Size size,Rect area,DockSide side){
            point=Clamp(point,size,area);if(side==DockSide.Left)point.X=area.Left;if(side==DockSide.Right)point.X=area.Right-size.Width;if(side==DockSide.Top)point.Y=area.Top;if(side==DockSide.Bottom)point.Y=area.Bottom-size.Height;return point;
        }
        public static Point Concealed(Point anchor,Size size,Rect area,DockSide side){
            const double gap=2;switch(side){case DockSide.Left:return new Point(area.Left-size.Width-gap,anchor.Y);case DockSide.Right:return new Point(area.Right+gap,anchor.Y);case DockSide.Top:return new Point(anchor.X,area.Top-size.Height-gap);case DockSide.Bottom:return new Point(anchor.X,area.Bottom+gap);default:return anchor;}
        }
        public static Rect Proximity(Point anchor,Size size,Rect area,DockSide side){
            switch(side){case DockSide.Left:return new Rect(area.Left,anchor.Y,18,size.Height);case DockSide.Right:return new Rect(area.Right-18,anchor.Y,18,size.Height);case DockSide.Top:return new Rect(anchor.X,area.Top,size.Width,18);case DockSide.Bottom:return new Rect(anchor.X,area.Bottom-18,size.Width,18);default:return Rect.Empty;}
        }
    }
    public partial class Widget {
        Grid desktopSurface;
        readonly DispatcherTimer dockTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(80)};
        TimeSpan lastSlideFrame=TimeSpan.MinValue;
        readonly RectangleGeometry dockClip=new RectangleGeometry();
        int slideFrameCount;
        readonly Stopwatch slideClock=new Stopwatch();
        DockSide dockSide=DockSide.None;
        Rect dockArea;
        Point dockAnchor,slideFrom,slideTo;
        bool dockHidden,dockAnimating,userMoving,userResizing;
        DateTime outsideSince=DateTime.MinValue;
        TextBlock scaleReadout;
        TextBlock scaleCaption;
        System.Windows.Controls.Primitives.Thumb cornerHandle;
        int resizeX,resizeY;
        string activeDesktopSize;
        CheckBox dockSetting;
        Point lastObservedPosition=new Point(double.NaN,double.NaN);
        DateTime lastPositionChange=DateTime.MinValue;
        void InitializeDesktopBehavior(){
            PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&!settingsOpen){ChangeScale((IsFreeLayout?1:prefs.CardScale>0?prefs.CardScale:1)*(e.Delta>0?1.08:1/1.08));e.Handled=true;}};
            dockTimer.Tick+=delegate{if(!prefs.AutoHide)return;var cursor=Forms.Cursor.Position;PollDock(DeviceToDesktop(new Point(cursor.X,cursor.Y)),DateTime.UtcNow);};
            SourceInitialized+=delegate{var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);if(source!=null)source.AddHook(DesktopWindowHook);};
            Loaded+=delegate{
                if(!smoke){DockSide restored;if(prefs.AutoHide&&Enum.TryParse(prefs.DockEdge,out restored)&&restored!=DockSide.None)AttachDock(restored,DesktopWorkArea());dockTimer.Start();}
            };
            Closed+=delegate{dockTimer.Stop();StopSlide();};
            // The display can be removed or its work area changed while the widget is docked.
            SystemParameters.StaticPropertyChanged+=DesktopMetricsChanged;
            Closed+=delegate{SystemParameters.StaticPropertyChanged-=DesktopMetricsChanged;};
        }
        IntPtr DesktopWindowHook(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled){
            if(message==0x0232)Dispatcher.BeginInvoke(new Action(delegate{if(!userMoving&&!userResizing&&IsVisible)FinishUserMove();}),DispatcherPriority.Background);
            return IntPtr.Zero;
        }
        void DesktopMetricsChanged(object sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName=="WorkArea")Dispatcher.BeginInvoke(new Action(delegate{DetachDock();ApplyLayout();FinishUserMove();}));}
        Point DeviceToDesktop(Point point){var source=PresentationSource.FromVisual(this);if(source!=null&&source.CompositionTarget!=null)return source.CompositionTarget.TransformFromDevice.Transform(point);var dpi=VisualTreeHelper.GetDpi(this);return new Point(point.X/dpi.DpiScaleX,point.Y/dpi.DpiScaleY);}
        Rect DesktopWorkArea(){
            Forms.Screen screen;var handle=new WindowInteropHelper(this).Handle;
            if(handle!=IntPtr.Zero)screen=Forms.Screen.FromHandle(handle);
            else if(prefs.Left!=-1||prefs.Top!=-1){var dpi=VisualTreeHelper.GetDpi(this);screen=Forms.Screen.FromPoint(new System.Drawing.Point((int)((prefs.Left+100)*dpi.DpiScaleX),(int)((prefs.Top+100)*dpi.DpiScaleY)));}
            else screen=Forms.Screen.PrimaryScreen;
            var area=screen.WorkingArea;return new Rect(DeviceToDesktop(new Point(area.Left,area.Top)),DeviceToDesktop(new Point(area.Right,area.Bottom)));
        }
        static Size PresetDesktopSize(string preset){if(preset=="大号方框")return new Size(440,440);if(preset=="中号圆角方框")return new Size(320,320);if(preset=="大号卡片")return new Size(420,790);return new Size(360,675);}
        Size BaseDesktopSize(){return PresetDesktopSize(settingsOpen?activeDesktopSize:prefs.Size);}
        double LimitDesktopScale(double scale,Size basis,Rect area){
            double minimum=((basis.Width==basis.Height?240:280)-16)/(basis.Width-16);
            double maximum=Math.Min(2,Math.Min((area.Width-16)/(basis.Width-16),(area.Height-16)/(basis.Height-16)));
            return Math.Max(Math.Min(minimum,maximum),Math.Min(maximum,double.IsNaN(scale)?1:scale));
        }
        void ApplyDesktopScale(double scale){
            if(IsFreeLayout){ApplyFreeSize(prefs.FreeWidth,prefs.FreeHeight);return;}
            Size basis=BaseDesktopSize();Rect area=dockSide!=DockSide.None?dockArea:DesktopWorkArea();
            prefs.CardScale=LimitDesktopScale(scale,basis,area);
            Width=16+(basis.Width-16)*prefs.CardScale;Height=16+(basis.Height-16)*prefs.CardScale;
            UpdateScaleReadout();
        }
        void ChangeScale(double scale){if(settingsOpen)return;if(IsFreeLayout)ApplyFreeSize(16+(Width-16)*scale,16+(Height-16)*scale);else ApplyDesktopScale(scale);RepositionAfterLayout();Save();}
        void RepositionAfterLayout(){
            if(dockSide!=DockSide.None){StopSlide();dockAnchor=DesktopGeometry.Anchor(dockAnchor,new Size(Width,Height),dockArea,dockSide);dockHidden=false;Left=dockAnchor.X;Top=dockAnchor.Y;desktopSurface.Clip=null;outsideSince=DateTime.UtcNow;}
            else {Point clamped=DesktopGeometry.Clamp(new Point(Left,Top),new Size(Width,Height),DesktopWorkArea());Left=clamped.X;Top=clamped.Y;}
        }
        void BuildResizeHandles(){
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++){
                if(x==0&&y==0)continue;int dx=x,dy=y;
                var thumb=new Thumb {HorizontalAlignment=x<0?HorizontalAlignment.Left:x>0?HorizontalAlignment.Right:HorizontalAlignment.Stretch,VerticalAlignment=y<0?VerticalAlignment.Top:y>0?VerticalAlignment.Bottom:VerticalAlignment.Stretch,Cursor=x==0?Cursors.SizeNS:y==0?Cursors.SizeWE:x==y?Cursors.SizeNWSE:Cursors.SizeNESW,ToolTip="拖动调整大小；选择自由尺寸可分别调整宽高"};
                if(x!=0)thumb.Width=y==0?8:16;if(y!=0)thumb.Height=x==0?8:16;if(x==0)thumb.Margin=new Thickness(16,0,16,0);if(y==0)thumb.Margin=new Thickness(0,16,0,16);
                var visual=new FrameworkElementFactory(typeof(Border));visual.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(1,0,0,0)));thumb.Template=new ControlTemplate(typeof(Thumb)){VisualTree=visual};
                thumb.DragStarted+=delegate{DetachDock();userResizing=true;resizeX=dx;resizeY=dy;};
                thumb.DragDelta+=delegate(object sender,DragDeltaEventArgs e){ResizeBy(e.HorizontalChange,e.VerticalChange);};
                thumb.DragCompleted+=delegate{userResizing=false;RepositionAfterLayout();FinishUserMove();Save();};desktopSurface.Children.Add(thumb);if(x==1&&y==1)cornerHandle=thumb;
            }
        }
        void ResizeBy(double horizontal,double vertical){
            if(IsFreeLayout){double width=Width,height=Height;ApplyFreeSize(width+horizontal*resizeX,height+vertical*resizeY);if(resizeX<0)Left+=width-Width;if(resizeY<0)Top+=height-Height;return;}
            Size basis=BaseDesktopSize();double deltaX=resizeX==0?0:horizontal*resizeX/(basis.Width-16),deltaY=resizeY==0?0:vertical*resizeY/(basis.Height-16);
            double delta=resizeX==0?deltaY:resizeY==0?deltaX:Math.Abs(deltaX)>Math.Abs(deltaY)?deltaX:deltaY;
            double oldWidth=Width,oldHeight=Height;ApplyDesktopScale(prefs.CardScale+delta);
            if(resizeX<0)Left+=oldWidth-Width;else if(resizeX==0)Left+=(oldWidth-Width)/2;
            if(resizeY<0)Top+=oldHeight-Height;else if(resizeY==0)Top+=(oldHeight-Height)/2;
        }
        void AddDesktopSettings(StackPanel panel){
            var row=new Grid {Margin=new Thickness(0,0,0,12)};var note=Text("缩放",11,muted);scaleCaption=note;note.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(note);
            var controls=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
            controls.Children.Add(Button("−",delegate{AdjustSettingScale(1/1.1);},false));scaleReadout=Text((prefs.CardScale*100).ToString("0")+"%",11,white);scaleReadout.Margin=new Thickness(10,0,10,0);controls.Children.Add(scaleReadout);controls.Children.Add(Button("+",delegate{AdjustSettingScale(1.1);},false));var reset=Button("重置",delegate{SetSettingScale(1);},false);reset.Margin=new Thickness(8,0,0,0);controls.Children.Add(reset);row.Children.Add(controls);panel.Children.Add(row);
            var hide=new CheckBox {Content="贴边自动收纳，鼠标靠近滑出",Foreground=white,IsChecked=prefs.AutoHide,Margin=new Thickness(0,2,0,10)};hide.Checked+=delegate{prefs.AutoHide=true;FinishUserMove();Save();};hide.Unchecked+=delegate{prefs.AutoHide=false;DetachDock();Save();};dockSetting=hide;panel.Children.Add(hide);
            AddStartupSetting(panel);
            var hint=Text("预设按比例缩放；自由尺寸可分别拖动宽高，字号不变，小窗口可滚动。Ctrl + 滚轮也可调整大小。",11,muted);hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new Thickness(0,0,0,14);panel.Children.Add(hint);
            UpdateScaleReadout();
        }
        void SetSettingScale(double value){if(prefs.Size=="自由尺寸"){var size=ConstrainFreeSize(400,720);prefs.FreeWidth=size.Width;prefs.FreeHeight=size.Height;}else prefs.CardScale=LimitDesktopScale(value,PresetDesktopSize(prefs.Size),DesktopWorkArea());UpdateScaleReadout();Save();}
        void AdjustSettingScale(double factor){if(prefs.Size=="自由尺寸"){var size=ConstrainFreeSize(prefs.FreeWidth*factor,prefs.FreeHeight*factor);prefs.FreeWidth=size.Width;prefs.FreeHeight=size.Height;UpdateScaleReadout();Save();}else SetSettingScale((prefs.CardScale>0?prefs.CardScale:1)*factor);}
        Point SavedDesktopLocation(){return dockSide==DockSide.None?new Point(Left,Top):dockAnchor;}
        void FinishUserMove(){
            Rect area=DesktopWorkArea();DockSide edge=prefs.AutoHide?DesktopGeometry.Detect(new Rect(Left,Top,Width,Height),area,20):DockSide.None;
            if(edge==DockSide.None){DetachDock();RepositionAfterLayout();}else AttachDock(edge,area);Save();
            if(feedback!=null){if(edge!=DockSide.None)feedback.Text="已贴边，鼠标移开后自动收纳";else if(!prefs.AutoHide&&DesktopGeometry.Detect(new Rect(Left,Top,Width,Height),area,20)!=DockSide.None)feedback.Text="贴边收纳已关闭，可在设置中开启";}
        }
        void AttachDock(DockSide side,Rect area){
            StopSlide();dockHidden=false;dockSide=side;dockArea=area;dockAnchor=DesktopGeometry.Anchor(new Point(Left,Top),new Size(Width,Height),area,side);Left=dockAnchor.X;Top=dockAnchor.Y;desktopSurface.Clip=null;outsideSince=DateTime.UtcNow;
        }
        void DetachDock(){
            if(dockSide!=DockSide.None){StopSlide();Left=dockAnchor.X;Top=dockAnchor.Y;}
            dockHidden=false;dockSide=DockSide.None;desktopSurface.Clip=null;outsideSince=DateTime.MinValue;
        }
        void RevealDock(bool immediate){
            if(dockSide==DockSide.None)return;
            if(immediate){StopSlide();dockHidden=false;Left=dockAnchor.X;Top=dockAnchor.Y;desktopSurface.Clip=null;}
            else if(dockHidden)SlideDock(false);
            outsideSince=DateTime.UtcNow;
        }
        void SlideDock(bool hide){
            dockHidden=hide;slideFrom=new Point(Left,Top);slideTo=hide?DesktopGeometry.Concealed(dockAnchor,new Size(Width,Height),dockArea,dockSide):dockAnchor;
            if(!SystemParameters.ClientAreaAnimation){Left=slideTo.X;Top=slideTo.Y;StopSlide();if(hide)ClipAtMonitorEdge();else desktopSurface.Clip=null;return;}
            dockAnimating=true;lastSlideFrame=TimeSpan.MinValue;slideFrameCount=0;card.CacheMode=new BitmapCache();slideClock.Restart();CompositionTarget.Rendering-=RenderSlideFrame;CompositionTarget.Rendering+=RenderSlideFrame;
        }
        void StopSlide(){CompositionTarget.Rendering-=RenderSlideFrame;slideClock.Stop();dockAnimating=false;if(card!=null)card.CacheMode=null;}
        void RenderSlideFrame(object sender,EventArgs args){
            var frame=args as RenderingEventArgs;if(frame!=null){if(frame.RenderingTime==lastSlideFrame)return;lastSlideFrame=frame.RenderingTime;}
            double progress=Math.Min(1,slideClock.Elapsed.TotalMilliseconds/190),eased=1-Math.Pow(1-progress,3);
            double x=slideFrom.X+(slideTo.X-slideFrom.X)*eased,y=slideFrom.Y+(slideTo.Y-slideFrom.Y)*eased;
            if(Math.Abs(Left-x)>.001)Left=x;if(Math.Abs(Top-y)>.001)Top=y;ClipAtMonitorEdge();slideFrameCount++;
            if(progress>=1){StopSlide();if(!dockHidden)desktopSurface.Clip=null;Refresh();}
        }
        void ClipAtMonitorEdge(){
            Rect intersection=Rect.Intersect(new Rect(Left,Top,Width,Height),dockArea);dockClip.Rect=intersection.IsEmpty?Rect.Empty:new Rect(intersection.Left-Left,intersection.Top-Top,intersection.Width,intersection.Height);if(desktopSurface.Clip!=dockClip)desktopSurface.Clip=dockClip;
        }
        void PollDock(Point cursor,DateTime now){
            if(!prefs.AutoHide||!IsVisible||WindowState!=WindowState.Normal||userMoving||userResizing)return;
            if(dockSide==DockSide.None){
                Point position=new Point(Left,Top);
                if(position!=lastObservedPosition){lastObservedPosition=position;lastPositionChange=now;return;}
                if((Forms.Control.MouseButtons&Forms.MouseButtons.Left)!=0||(now-lastPositionChange).TotalMilliseconds<180)return;
                Rect area=DesktopWorkArea();DockSide edge=DesktopGeometry.Detect(new Rect(Left,Top,Width,Height),area,20);
                if(edge==DockSide.None)return;AttachDock(edge,area);Save();
            }
            bool editingCapture=Mouse.Captured is Thumb||Mouse.Captured is ComboBox||Mouse.Captured is TextBox;
            if(settingsOpen||editingCapture||!IsEnabled||(tray!=null&&tray.ContextMenuStrip.Visible)){RevealDock(false);outsideSince=now;return;}
            Rect panel=new Rect(dockAnchor,new Size(Width,Height));panel.Inflate(8,8);
            if(dockHidden){if(DesktopGeometry.Proximity(dockAnchor,new Size(Width,Height),dockArea,dockSide).Contains(cursor)||(dockAnimating&&new Rect(Left,Top,Width,Height).Contains(cursor)&&dockArea.Contains(cursor)))RevealDock(false);return;}
            if(panel.Contains(cursor)){outsideSince=now;return;}
            if(outsideSince==DateTime.MinValue)outsideSince=now;
            if(!dockAnimating&&(now-outsideSince).TotalMilliseconds>=650)SlideDock(true);
        }
    }
}
