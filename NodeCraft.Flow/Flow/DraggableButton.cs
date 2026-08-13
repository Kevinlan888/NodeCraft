using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommonControls.WPF;

namespace NodeCraft.Flow
{
    public class DraggableButton : RoundButton
    {
        private bool _isDragging = false;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                Debug.WriteLine("Do drag drop");
                DragDrop.DoDragDrop(this, this.Tag, DragDropEffects.Copy);
                _isDragging = false;
            }
        }
    }
}
