using System;
using System.Drawing;
using System.Windows.Forms;

namespace AHK2AST.UI
{
    public class PluginDragData
    {
        public string Title { get; set; }
        public string Icon { get; set; }
        public Type ConfigType { get; set; }
    }

    public class ItemDroppedEventArgs : EventArgs
    {
        public PluginDragData Data { get; set; }
        public int Index { get; set; }
    }

    public class ReorderableFlowLayoutPanel : FlowLayoutPanel
    {
        private Control _draggingControl;
        private Control _targetControl;
        private bool _insertAfter;
        private bool _isExternalDrag;

        public event EventHandler<ItemDroppedEventArgs> ExternalItemDropped;
        public event EventHandler FlowOrderChanged;

        protected virtual void OnFlowOrderChanged()
        {
            var handler = FlowOrderChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public ReorderableFlowLayoutPanel()
        {
            this.AllowDrop = true;
            this.DoubleBuffered = true;
            this.FlowDirection = FlowDirection.TopDown;
            this.WrapContents = false;
        }

        private Point _mouseDownLocation;
        private bool _isMouseDown;

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.MouseDown += Child_MouseDown;
            e.Control.MouseMove += Child_MouseMove;
            e.Control.MouseUp += Child_MouseUp;
            OnFlowOrderChanged();
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            base.OnControlRemoved(e);
            e.Control.MouseDown -= Child_MouseDown;
            e.Control.MouseMove -= Child_MouseMove;
            e.Control.MouseUp -= Child_MouseUp;
            OnFlowOrderChanged();
        }

        private void Child_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isMouseDown = true;
                _mouseDownLocation = e.Location;
            }
        }

        private void Child_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMouseDown && e.Button == MouseButtons.Left)
            {
                if (Math.Abs(e.X - _mouseDownLocation.X) > SystemInformation.DragSize.Width ||
                    Math.Abs(e.Y - _mouseDownLocation.Y) > SystemInformation.DragSize.Height)
                {
                    _isMouseDown = false;
                    var control = sender as Control;
                    _draggingControl = control;
                    _isExternalDrag = false;
                    control.DoDragDrop(control, DragDropEffects.Move);
                    _draggingControl = null;
                    _targetControl = null;
                    this.Invalidate();
                }
            }
        }

        private void Child_MouseUp(object sender, MouseEventArgs e)
        {
            _isMouseDown = false;
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            if (_draggingControl != null)
            {
                drgevent.Effect = DragDropEffects.Move;
                _isExternalDrag = false;
            }
            else if (drgevent.Data.GetDataPresent(typeof(PluginDragData)))
            {
                drgevent.Effect = DragDropEffects.Copy;
                _isExternalDrag = true;
            }
            else
            {
                drgevent.Effect = DragDropEffects.None;
            }
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);

            if (!_isExternalDrag && _draggingControl == null) return;

            Point clientPoint = this.PointToClient(new Point(drgevent.X, drgevent.Y));
            _targetControl = null;

            // Find which control we are hovering over
            foreach (Control child in this.Controls)
            {
                if (child == _draggingControl) continue;

                if (child.Bounds.Contains(clientPoint) || 
                    (clientPoint.Y >= child.Top && clientPoint.Y <= child.Bottom))
                {
                    _targetControl = child;
                    
                    // If we are in the bottom half of the control, insert after
                    _insertAfter = clientPoint.Y > (child.Top + (child.Height / 2));
                    break;
                }
            }

            // Force a repaint so we can draw the insertion line
            this.Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);

            int targetIndex = this.Controls.Count;

            if (_targetControl != null)
            {
                targetIndex = this.Controls.GetChildIndex(_targetControl);
                if (_insertAfter) targetIndex++;
            }

            if (_isExternalDrag && drgevent.Data.GetDataPresent(typeof(PluginDragData)))
            {
                var data = (PluginDragData)drgevent.Data.GetData(typeof(PluginDragData));
                if (ExternalItemDropped != null)
                {
                    ExternalItemDropped(this, new ItemDroppedEventArgs { Data = data, Index = targetIndex });
                }
            }
            else if (_draggingControl != null && _draggingControl != _targetControl)
            {
                int currentIndex = this.Controls.GetChildIndex(_draggingControl);
                if (currentIndex < targetIndex)
                {
                    targetIndex--;
                }
                this.Controls.SetChildIndex(_draggingControl, targetIndex);
                OnFlowOrderChanged();
            }

            _draggingControl = null;
            _targetControl = null;
            _isExternalDrag = false;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (this.Controls.Count == 0)
            {
                var rect = this.ClientRectangle;
                TextRenderer.DrawText(e.Graphics, "Drag plugins here to build your flow...", 
                    new Font("Segoe UI", 12F, FontStyle.Italic), 
                    rect, WbTheme.Subtext0, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Draw the insertion line
            if (_targetControl != null)
            {
                int yPos = _insertAfter ? _targetControl.Bottom + 1 : _targetControl.Top - 1;
                
                using (Pen pen = new Pen(WbTheme.Accent, 3))
                {
                    e.Graphics.DrawLine(pen, _targetControl.Left, yPos, _targetControl.Right, yPos);
                }
            }
            // If dragging over an empty space (or bottom), draw a line at the bottom
            else if (_isExternalDrag || _draggingControl != null)
            {
                int yPos = 10;
                if (this.Controls.Count > 0)
                {
                    yPos = this.Controls[this.Controls.Count - 1].Bottom + 5;
                }
                using (Pen pen = new Pen(WbTheme.Accent, 3))
                {
                    e.Graphics.DrawLine(pen, 10, yPos, this.Width - 20, yPos);
                }
            }
        }
    }
}
