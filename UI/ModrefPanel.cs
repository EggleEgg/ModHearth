using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace ModHearth.UI
{
    /// <summary>
    /// A panel representing a mod, to be dragged and dropped for GUI based modpack editing.
    /// Does not make actual changes, just notifies manager classes.
    /// </summary>
    public class ModRefPanel : Panel
    {
        private bool isDragging = false;
        private bool dragPending = false;
        private Point dragStart;
        private bool isSelected = false;
        private const int DragThreshold = 4;

        // Reference to the form, to notify of changes.
        public MainForm form;

        // Get the parent of this control as a VerticalFlowPanel (this is always the case).
        public VerticalFlowPanel vParent => Parent as VerticalFlowPanel;

        // Which ModReference this panel represents.
        public ModReference modref;

        // Quick extraction of DFHMod.
        public DFHMod dfmodref => modref.ToDFHMod();

        // The label showing the modrefs name
        private Label label;

        private ContextMenuStrip contextMenu;
        private ToolStripMenuItem deleteMenuItem;
        private ToolStripMenuItem openMenuItem;

        // Which ModrefPanel is currently being dragged. Only one is dragged at once, hence static.
        public static ModRefPanel draggee;

        // Keep track of the last position the mouse was when this is being dragged.
        private Point lastPosition;

        // Should this be highlighted up or down
        private bool highlightUp;
        private bool highlightDown;
        private bool jumpHighlighted;

        // Keep track of if this is a problem
        private bool problem;

        public ModRefPanel(ModReference modref, MainForm form)
        {
            // Basic references.
            this.form = form;
            this.modref = modref;

            // Set up the mod name label.
            label = new Label();
            label.Text = modref.name + " " + modref.displayedVersion;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.BackColor = Color.Transparent;
            label.Dock = DockStyle.Fill;
            Controls.Add(label);

            // Set up anchors.
            Margin = Style.modRefPadding;

            // Mouse function mapping.
            label.MouseDown += ModrefPanel_MouseDown;
            label.MouseMove += ModrefPanel_MouseMove;
            label.MouseUp += ModrefPanel_MouseUp;
            label.Click += ModrefPanel_Click;
            label.DoubleClick += ModrefPanel_DoubleClick;

            MouseDown += ModrefPanel_MouseDown;
            MouseMove += ModrefPanel_MouseMove;
            MouseUp += ModrefPanel_MouseUp;
            Click += ModrefPanel_Click;
            DoubleClick += ModrefPanel_DoubleClick;

            InitializeContextMenu();

            // Some style things.
            BorderStyle = Style.modRefBorder;
            Margin = Style.modRefPadding;

            Visible = true;
            highlightUp = false;
            highlightDown = false;
            problem = false;
            jumpHighlighted = false;
            isSelected = false;

            //BackgroundImage = Resource1.transparent_square;
        }

        // This is run once when this object is added to its parent.
        public void Initialize()
        {
            // Set width and height properly.
            Width = Parent.Width - Margin.Left - Margin.Right - SystemInformation.VerticalScrollBarWidth;
            label.Width = Width;
            Height = Style.modRefHeight;

            // Fix colors as well.
            label.Font = Style.modRefFont;
            label.ForeColor = Style.instance.modRefTextColor;
            UpdateSelectionVisual();
        }

        // On mouse down, set isDragging to true, this to be the draggee, and change cursor. Also change draggee color.
        private void ModrefPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                form.ModrefMouseDown(this, e);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            dragPending = true;
            dragStart = MousePosition;
            isDragging = false;
            form.ModrefMouseDown(this, e);
        }

        // While this is being dragged and gets moved, notify the form, and update the last recorded position.
        private void ModrefPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragPending && !isDragging)
            {
                Point mousePos = MousePosition;
                if (Math.Abs(mousePos.X - dragStart.X) >= DragThreshold || Math.Abs(mousePos.Y - dragStart.Y) >= DragThreshold)
                {
            isDragging = true;
            dragPending = false;
            draggee = this;
            Cursor.Current = new Cursor(Resource1.grab_cursor.GetHicon());
            draggee.BackColor = ControlPaint.Light(Style.instance.modRefColor);
            form.ModrefDragStart(this);
        }
            }

            if (isDragging)
            {
                Point mousePos = MousePosition;
                form.ModrefMouseMove(mousePos);
                lastPosition = mousePos;
            }
        }

        // When this panel is dropped, reset isDragging, reset the cursor, and notify the form. Also reset draggee color.
        private void ModrefPanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            if (isDragging)
            {
                isDragging = false;
                Cursor.Current = Cursors.Default;
                form.ModrefMouseUp(lastPosition, this);
                UpdateSelectionVisual();
            }
            else
            {
                dragPending = false;
                form.ModrefMouseUpNoDrag(this);
            }
        }

        // When this is clicked, show this mod info.
        private void ModrefPanel_Click(object sender, EventArgs e)
        {
            form.ChangeModInfoDisplay(modref);
        }

        // When this is double clicked, show info and do transfer.
        private void ModrefPanel_DoubleClick(object sender, EventArgs e)
        {
            form.ChangeModInfoDisplay(modref);
            form.ModRefDoubleClicked(this);
        }

        // This control paints the background image on top of background color, for highlighting.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Style based highlighting.
            if(highlightUp)
            {
                Rectangle topRect = new Rectangle(0, 0, this.Width, 2);
                e.Graphics.FillRectangle(new SolidBrush(Style.instance.modRefHighlightColor), topRect);
            }
            else if (highlightDown)
            {
                Rectangle bottomRect = new Rectangle(0, this.Height - 2, this.Width, 2);
                e.Graphics.FillRectangle(new SolidBrush(Style.instance.modRefHighlightColor), bottomRect);
            }
        }

        // Set the highlight status of this panel. Invalidate on change.
        public void SetHighlight(bool top, bool bottom)
        {
            // If not changed, do nothing.
            if (!(top != highlightUp || bottom != highlightDown))
                return;
            highlightUp = top; 
            highlightDown = bottom;
            Invalidate();
        }

        public bool IsSelected => isSelected;

        public void SetSelected(bool selected)
        {
            if (isSelected == selected)
                return;
            isSelected = selected;
            UpdateSelectionVisual();
        }

        public void SetJumpHighlight(bool active)
        {
            if (jumpHighlighted == active)
                return;
            jumpHighlighted = active;
            UpdateSelectionVisual();
        }

        private void UpdateSelectionVisual()
        {
            if (isDragging)
                return;
            Color baseColor = Style.instance.modRefColor;
            Color? overlay = null;
            if (jumpHighlighted)
            {
                overlay = Style.instance.modRefJumpHighlightColor
                    ?? Style.instance.modRefHighlightColor;
            }
            else if (isSelected)
            {
                overlay = Style.instance.modRefHighlightColor;
            }

            BackColor = overlay.HasValue
                ? BlendColor(baseColor, overlay.Value)
                : baseColor;
        }

        private Color BlendColor(Color baseColor, Color overlay)
        {
            if (overlay.A >= 255)
                return overlay;

            float a = overlay.A / 255f;
            int r = (int)(baseColor.R * (1 - a) + overlay.R * a);
            int g = (int)(baseColor.G * (1 - a) + overlay.G * a);
            int b = (int)(baseColor.B * (1 - a) + overlay.B * a);
            return Color.FromArgb(255, r, g, b);
        }

        private void InitializeContextMenu()
        {
            contextMenu = new ContextMenuStrip();
            deleteMenuItem = new ToolStripMenuItem("Delete from Mods folder");
            openMenuItem = new ToolStripMenuItem("Open mod location");

            deleteMenuItem.Click += (s, e) => form.DeleteModFromModsFolder(this);
            openMenuItem.Click += (s, e) => form.OpenModLocation(this);
            contextMenu.Items.AddRange(new ToolStripItem[] { deleteMenuItem, openMenuItem });
            contextMenu.Opening += (s, e) => form.ConfigureModContextMenu(this, deleteMenuItem);

            ContextMenuStrip = contextMenu;
            label.ContextMenuStrip = contextMenu;
        }

        // Set label color and tooltips.
        public void SetProblems(List<ModProblem> problems)
        {
            problem = true;
            label.ForeColor = Style.instance.modRefTextBadColor;

            string problemToolTipString = "Problems:";
            foreach (ModProblem problem in problems)
            {
                problemToolTipString += "\n" + problem.ToString();
            }

            form.toolTip1.SetToolTip(this, problemToolTipString);
            form.toolTip1.SetToolTip(label, problemToolTipString);
        }

        // Reset text color and tooltips.
        public void RemoveProblems()
        {
            problem = false;
            label.ForeColor = Style.instance.modRefTextColor;
            form.toolTip1.SetToolTip(this, null);
            form.toolTip1.SetToolTip(label, null);
        }

        // If a filter is applied and this is grayed out.
        public void SetFiltered(bool active)
        {
            if (active)
            {
                label.Font = Style.modRefFont;
                if (problem)
                    return;
                label.ForeColor = Style.instance.modRefTextColor;
            }
            else
            {
                label.Font = Style.modRefStrikeFont;
                if (problem)
                    return;
                label.ForeColor = Style.instance.modRefTextFilteredColor;
            }
        }
    }
}
