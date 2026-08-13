using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NodeCraft.Flow
{
    /// <summary>
    /// 按照步骤 1a 或 1b 操作，然后执行步骤 2 以在 XAML 文件中使用此自定义控件。
    ///
    /// 步骤 1a) 在当前项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:NodeCraft.Flow"
    ///
    ///
    /// 步骤 1b) 在其他项目中存在的 XAML 文件中使用该自定义控件。
    /// 将此 XmlNamespace 特性添加到要使用该特性的标记文件的根
    /// 元素中:
    ///
    ///     xmlns:MyNamespace="clr-namespace:NodeCraft.Flow;assembly=NodeCraft.Flow"
    ///
    /// 您还需要添加一个从 XAML 文件所在的项目到此项目的项目引用，
    /// 并重新生成以避免编译错误:
    ///
    ///     在解决方案资源管理器中右击目标项目，然后依次单击
    ///     “添加引用”->“项目”->[浏览查找并选择此项目]
    ///
    ///
    /// 步骤 2)
    /// 继续操作并在 XAML 文件中使用控件。
    ///
    ///     <MyNamespace:Connector/>
    ///
    /// </summary>
    public class Connector : Control
    {
        static Connector()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Connector), new FrameworkPropertyMetadata(typeof(Connector)));
        }

        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.Register("Direction", typeof(EPortDirection), typeof(Connector));

        public EPortDirection Direction
        {
            get => (EPortDirection)GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        public static readonly DependencyProperty IOTypeProperty =
            DependencyProperty.Register("IOType", typeof(EIOType), typeof(Connector));

        public EIOType IOType
        {
            get => (EIOType)GetValue(IOTypeProperty);
            set => SetValue(IOTypeProperty, value);
        }

        public static readonly DependencyProperty SlotProperty =
            DependencyProperty.Register("Slot", typeof(int), typeof(Connector), new PropertyMetadata(0));

        public int Slot
        {
            get => (int)GetValue(SlotProperty);
            set => SetValue(SlotProperty, value);
        }

        public static readonly DependencyProperty IsInputProperty =
            DependencyProperty.Register("IsInput", typeof(bool), typeof(Connector), new PropertyMetadata(false));

        public bool IsInput
        {
            get => (bool)GetValue(IsInputProperty);
            set => SetValue(IsInputProperty, value);
        }

        public Brush RestingBackground { get; set; }

        public Connector()
        {

        }

        public void Highlight()
        {
            this.Background = (Brush)FindResource("colorStrokeFocus2");
        }

        public void Unhighlight()
        {
            this.Background = RestingBackground ?? (Brush)FindResource("colorBrandStroke1");
        }
    }
}
