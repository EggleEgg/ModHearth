using Microsoft.VisualBasic;
using ModHearth.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.ComponentModel.Design.ObjectSelectorEditor;

namespace ModHearth
{
    public partial class MainForm : Form
    {
        // Console allocation magic.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        // Manager reference.
        public ModHearthManager manager;

        // Public access to modlist panels (for draggable mod references mainly).
        public VerticalFlowPanel LeftModlistPanel => leftModlistPanel;
        public VerticalFlowPanel RightModlistPanel => rightModlistPanel;

        // Tracking unsaved changes and visually marking them.
        private bool changesMade;
        private bool changesMarked = false;

        // Tracking which modrefPanels are highlighted.
        private HashSet<ModRefPanel> highlightAffected = new HashSet<ModRefPanel>();

        // Selection tracking for shift/ctrl selection.
        private readonly HashSet<ModRefPanel> selectedPanels = new HashSet<ModRefPanel>();
        private ModRefPanel leftSelectionAnchor;
        private ModRefPanel rightSelectionAnchor;
        private ModRefPanel pendingSingleClickPanel;
        private bool pendingSingleClickActive;

        // ComboBox handling.
        private int lastIndex;
        private bool modifyingCombobox = false;

        // ToolTip.
        public ToolTip toolTip1;

        // Light mode tracking.
        private int lastStyle;

        // For when the form is restarting itself.
        public bool selfClosing;

        // The one and only instance of this form, for other classes to reference.
        public static MainForm instance;

        public MainForm()
        {
            // Set global instance.
            instance = this;

            // Make console function.
            AllocConsole();
            Console.ForegroundColor = ConsoleColor.White;

            // Basic initialization.
            InitializeComponent();
            manager = new ModHearthManager();

            // Set combobox for theme.
            lastStyle = manager.GetTheme();
            themeComboBox.SelectedIndex = lastStyle;

            // Get style and fix colors.
            FixStyle();

            // Set up tooltip manager and add some tooltips.
            SetupTooltipManager();
            AddTooltips();

            // Disable/enable change related buttons.
            SetChangesMade(false);

            // Set up combobox and modreference controls.
            SetupModlistBox();
            GenerateModrefControls();

            // Apply some post load fixes.
            this.Load += PostLoadFix;
            this.FormClosing += CloseConfirmation;

            // Fix resizing issues.
            this.Resize += ResizeFixes;

            selfClosing = false;
        }

        // Resize childen of panels.
        private void ResizeFixes(object sender, EventArgs e)
        {
            leftModlistPanel.FixChildrenStyle();
            rightModlistPanel.FixChildrenStyle();
        }

        // Check if they really want to close.
        private void CloseConfirmation(object sender, FormClosingEventArgs e)
        {
            // If this form is closing itself, do not interfere.
            if (selfClosing)
                return;

            // Ask the user to confirm closing the application.
            string message = "Are you sure you want to exit?";
            if (changesMade)
            {
                message = message + "There are unsaved changes. ";
            }

            if (LocationMessageBox.Show(message, "Exit", MessageBoxButtons.YesNo) == DialogResult.No)
                e.Cancel = true;
        }

        // Get style and fix colors.
        private void FixStyle()
        {
            Style style = manager.LoadStyle();

            // Force lightmode if true.
            if (manager.GetTheme() == 0)
            {
                style.formColor = Color.White;
                style.textColor = Color.Black;

                style.modRefPanelColor = Color.LightGray;
                style.modRefColor = Color.LightGray;
                style.modRefHighlightColor = Color.AliceBlue;
                style.modRefSelectedColor = Color.LightSteelBlue;
                style.modRefTextColor = Color.Black;
                style.modRefTextBadColor = Color.Red;
                style.modRefTextFilteredColor = Color.DarkGray;
            }

            this.BackColor = style.formColor;
            modTitleLabel.ForeColor = style.textColor;
            modDescriptionLabel.ForeColor = style.textColor;

            leftModlistPanel.BackColor = style.modRefPanelColor;
            rightModlistPanel.BackColor = style.modRefPanelColor;
            leftModlistPanel.FixChildrenStyle();
            rightModlistPanel.FixChildrenStyle();

            leftSearchBox.ForeColor = style.textColor;
            rightSearchBox.ForeColor = style.textColor;
            leftSearchBox.BackColor = style.modRefPanelColor;
            rightSearchBox.BackColor = style.modRefPanelColor;
            leftSearchBox.BorderStyle = BorderStyle.None;
            rightSearchBox.BorderStyle = BorderStyle.None;

            playGameButton.Enabled = false;
        }

        private void PostLoadFix(object sender, EventArgs e)
        {
            // Do one problem find and refresh.
            manager.FindModlistProblems();
            RefreshModlistPanels();

            // Select a random mod to be the shown one.
            long selectedModIndex = DateTime.Now.Ticks % manager.modPool.Count;
            ChangeModInfoDisplay(manager.GetRefFromDFHMod(manager.modPool.ToList()[(int)selectedModIndex]));
        }

        private void SetupTooltipManager()
        {
            // Create the ToolTip and associate with the Form container.
            toolTip1 = new ToolTip();

            // Set up the delays for the ToolTip.
            toolTip1.AutoPopDelay = 10000;
            toolTip1.InitialDelay = 0;
            toolTip1.ReshowDelay = 0;
            toolTip1.UseFading = false;

            // Force the ToolTip text to be displayed whether or not the form is active.
            toolTip1.ShowAlways = true;
        }

        private void AddTooltips()
        {
            toolTip1.SetToolTip(saveButton, "Save the current modlist");
            toolTip1.SetToolTip(undoChangesButton, "Undo changes to the current modlist");
            toolTip1.SetToolTip(playGameButton, "Run dwarf fortress");
            toolTip1.SetToolTip(autoSortButton, "Auto sort the current modlist and add missing dependencies");
        }

        private void SetupModlistBox()
        {
            modifyingCombobox = true;

            // Go through the modpacks, add them to combobox.
            foreach (DFHModpack m in manager.modpacks)
            {
                modpackComboBox.Items.Add(m.name);
            }

            // Set the index to the default modpack (manager selected default modpack when created).
            modpackComboBox.SelectedIndex = manager.selectedModlistIndex;
            lastIndex = manager.selectedModlistIndex;

            modifyingCombobox = false;
        }

        // Generate a full list of controls for each side.
        private void GenerateModrefControls()
        {
            // Make output pretty.
            Console.WriteLine();
            GenerateModrefControlSided(leftModlistPanel, new List<DFHMod>(manager.disabledMods), true);
            GenerateModrefControlSided(rightModlistPanel, new List<DFHMod>(manager.enabledMods), false);
        }


        // Generate draggable references, then initialize the panel.
        private void GenerateModrefControlSided(VerticalFlowPanel panel, List<DFHMod> members, bool left)
        {
            List<ModRefPanel> conts = new List<ModRefPanel>();
            foreach (DFHMod dfm in manager.modPool)
            {
                conts.Add(new ModRefPanel(manager.GetModRef(dfm.ToString()), this));
            }

            panel.Initialize(conts, members, !left);
        }

        private ModRefPanel GetSelectionAnchor(VerticalFlowPanel panel)
        {
            return panel == leftModlistPanel ? leftSelectionAnchor : rightSelectionAnchor;
        }

        private void SetSelectionAnchor(VerticalFlowPanel panel, ModRefPanel anchor)
        {
            if (panel == leftModlistPanel)
                leftSelectionAnchor = anchor;
            else
                rightSelectionAnchor = anchor;
        }

        private void ClearSelection(VerticalFlowPanel panel)
        {
            List<ModRefPanel> toClear = selectedPanels.Where(p => p.vParent == panel).ToList();
            foreach (ModRefPanel panelItem in toClear)
            {
                panelItem.SetSelected(false);
                selectedPanels.Remove(panelItem);
            }
        }

        private void ClearSelectionOtherPanel(VerticalFlowPanel activePanel)
        {
            VerticalFlowPanel other = activePanel == leftModlistPanel ? rightModlistPanel : leftModlistPanel;
            ClearSelection(other);
            SetSelectionAnchor(other, null);
        }

        private void AddSelection(ModRefPanel panel)
        {
            if (selectedPanels.Add(panel))
                panel.SetSelected(true);
        }

        private void RemoveSelection(ModRefPanel panel)
        {
            if (selectedPanels.Remove(panel))
                panel.SetSelected(false);
        }

        private void SelectSingle(ModRefPanel panel)
        {
            ClearSelection(panel.vParent);
            AddSelection(panel);
        }

        private int SelectionCount(VerticalFlowPanel panel)
        {
            return selectedPanels.Count(p => p.vParent == panel);
        }

        private bool IsSelected(ModRefPanel panel)
        {
            return selectedPanels.Contains(panel);
        }

        private void SelectRange(VerticalFlowPanel panel, ModRefPanel anchor, ModRefPanel target, bool addToExisting)
        {
            if (anchor == null || target == null)
            {
                if (!addToExisting && target != null)
                    SelectSingle(target);
                else if (target != null)
                    AddSelection(target);
                return;
            }

            List<ModRefPanel> ordered = panel.GetVisibleModrefs();
            int start = ordered.IndexOf(anchor);
            int end = ordered.IndexOf(target);
            if (start < 0 || end < 0)
            {
                if (!addToExisting)
                    SelectSingle(target);
                else
                    AddSelection(target);
                return;
            }

            if (!addToExisting)
                ClearSelection(panel);

            int min = Math.Min(start, end);
            int max = Math.Max(start, end);
            for (int i = min; i <= max; i++)
            {
                AddSelection(ordered[i]);
            }
        }

        private List<ModRefPanel> GetSelectedPanelsInOrder(VerticalFlowPanel panel)
        {
            List<ModRefPanel> ordered = panel.GetVisibleModrefs();
            return ordered.Where(p => selectedPanels.Contains(p)).ToList();
        }

        private void SelectModsInPanel(VerticalFlowPanel panel, IEnumerable<DFHMod> mods, bool clearFirst)
        {
            if (clearFirst)
                ClearSelection(panel);

            foreach (DFHMod mod in mods)
            {
                if (panel.modrefMap.TryGetValue(mod, out ModRefPanel panelItem))
                {
                    AddSelection(panelItem);
                }
            }
        }

        private void CancelPendingSingleClick()
        {
            pendingSingleClickActive = false;
            pendingSingleClickPanel = null;
        }

        public void ModrefMouseDown(ModRefPanel modrefPanel, MouseEventArgs e)
        {
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
            VerticalFlowPanel parent = modrefPanel.vParent;

            if (shift)
            {
                ClearSelectionOtherPanel(parent);
                ModRefPanel anchor = GetSelectionAnchor(parent) ?? modrefPanel;
                SelectRange(parent, anchor, modrefPanel, ctrl);
                SetSelectionAnchor(parent, anchor);
                CancelPendingSingleClick();
                return;
            }

            if (ctrl)
            {
                ClearSelectionOtherPanel(parent);
                if (IsSelected(modrefPanel))
                    RemoveSelection(modrefPanel);
                else
                    AddSelection(modrefPanel);
                SetSelectionAnchor(parent, modrefPanel);
                CancelPendingSingleClick();
                return;
            }

            ClearSelectionOtherPanel(parent);

            if (IsSelected(modrefPanel) && SelectionCount(parent) > 1)
            {
                pendingSingleClickActive = true;
                pendingSingleClickPanel = modrefPanel;
                SetSelectionAnchor(parent, modrefPanel);
                return;
            }

            SelectSingle(modrefPanel);
            SetSelectionAnchor(parent, modrefPanel);
            CancelPendingSingleClick();
        }

        public void ModrefDragStart(ModRefPanel modrefPanel)
        {
            CancelPendingSingleClick();
        }

        public void ModrefMouseUpNoDrag(ModRefPanel modrefPanel)
        {
            if (pendingSingleClickActive && pendingSingleClickPanel == modrefPanel)
            {
                SelectSingle(modrefPanel);
                SetSelectionAnchor(modrefPanel.vParent, modrefPanel);
            }
            CancelPendingSingleClick();
        }

        // When a modrefPanel is being dragged, it calls this. Just handles highlighting.
        public void ModrefMouseMove(Point position)
        {
            // Undo old highlights.
            UnsetSurroundingToHighlight();

            // See if mouse is in panel.
            bool overLeft = leftModlistPanel.GetIndexAtPosition(position, out int indexL);
            bool overRight = rightModlistPanel.GetIndexAtPosition(position, out int indexR);

            // Return if not in panel.
            if (!overLeft && !overRight)
            {
                return;
            }

            // Assume left, swap right if needed.
            int index = indexL;
            VerticalFlowPanel panel = leftModlistPanel;

            if (overRight)
            {
                index = indexR;
                panel = rightModlistPanel;
            }

            // Highlight accordingly.
            SetSurroundingToHighlight(index, panel);
        }

        // When a modrefPanel is dropped, it calls this.
        public void ModrefMouseUp(Point position, ModRefPanel modrefPanel)
        {
            // Undo old highlights.
            UnsetSurroundingToHighlight();

            // Show new info of dragged mod.
            ChangeModInfoDisplay(modrefPanel.modref);

            // See if mouse is in panel.
            bool overLeft = leftModlistPanel.GetIndexAtPosition(position, out int indexL);
            bool overRight = rightModlistPanel.GetIndexAtPosition(position, out int indexR);

            // Return if not in panel.
            if (!overLeft && !overRight)
            {
                return;
            }

            // Assume left, swap right if needed.
            int index = indexL;
            VerticalFlowPanel destinationPanel = leftModlistPanel;

            if (overRight)
            {
                index = indexR;
                destinationPanel = rightModlistPanel;
            }

            // If the source and destination wat the left panel, return, since the left panel is insorted.
            if (modrefPanel.vParent == leftModlistPanel && destinationPanel == LeftModlistPanel)
                return;

            // Changes have been made.
            SetAndMarkChanges(true);

            VerticalFlowPanel sourcePanel = modrefPanel.vParent;
            List<ModRefPanel> selectedPanelsInSource = GetSelectedPanelsInOrder(sourcePanel);
            if (selectedPanelsInSource.Count == 0 || !selectedPanelsInSource.Contains(modrefPanel))
            {
                selectedPanelsInSource = new List<ModRefPanel> { modrefPanel };
            }

            List<DFHMod> selectedMods = selectedPanelsInSource.Select(p => p.dfmodref).ToList();

            // Have the manager apply the changes to the actual enabled mods, then refresh panels to show.
            manager.MoveMods(selectedMods, index, sourcePanel == leftModlistPanel, destinationPanel == leftModlistPanel);

            if (sourcePanel != destinationPanel)
            {
                ClearSelection(sourcePanel);
                SelectModsInPanel(destinationPanel, selectedMods, true);
                DFHMod anchorMod = modrefPanel.dfmodref;
                if (anchorMod != null && destinationPanel.modrefMap.TryGetValue(anchorMod, out ModRefPanel anchorPanel))
                    SetSelectionAnchor(destinationPanel, anchorPanel);
            }

            RefreshModlistPanels();
        }

        // Given a modreference, set the image and description to show it's info.
        public void ChangeModInfoDisplay(ModReference modref)
        {
            modTitleLabel.Text = modref.name;
            modDescriptionLabel.Text = modref.description;

            string previewPath = Path.Combine(modref.path, "preview.png");
            if (File.Exists(previewPath))
            {
                // Use prewvie image if it exists
                using (FileStream stream = new FileStream(previewPath, FileMode.Open))
                {
                    Image originalImage = Image.FromStream(stream);
                    SetModPictureBoxImage(originalImage);
                }
            }
            else
            {
                // Use default image.
                SetModPictureBoxImage(Resource1.DFIcon);
            }

        }

        // Given a double clicked modref, do a transfer to the last index.
        public void ModRefDoubleClicked(ModRefPanel modrefPanel)
        {
            bool leftSource = modrefPanel.Parent == leftModlistPanel;

            // Changes have been made.
            SetAndMarkChanges(true);

            // Move mod appropriately and refresh.
            manager.MoveMod(modrefPanel.modref, manager.enabledMods.Count, leftSource, !leftSource);
            RefreshModlistPanels();
        }

        private void SetModPictureBoxImage(Image originalImage)
        {
            // Calculate scale factor and scale image by it.
            float scaleFactor = (float)modPictureBox.Width / originalImage.Width;
            int newWidth = (int)(originalImage.Width * scaleFactor);
            int newHeight = (int)(originalImage.Height * scaleFactor);
            Bitmap scaledImage = new Bitmap(originalImage, newWidth, newHeight);

            // Set box to show image.
            modPictureBox.Image = scaledImage;

            // Adjust height of picture box area.
            modInfoPanel.RowStyles[1].Height = modPictureBox.Image.Size.Height;
        }

        // If changes aren't already marked, adds a * to the combobox entry.
        private void MarkChanges(int index)
        {
            if (changesMarked)
                return;

            modifyingCombobox = true;

            modpackComboBox.Items[index] = modpackComboBox.Items[index].ToString() + "*";
            changesMarked = true;

            modifyingCombobox = false;
        }

        // If changes are marked, unmark them.
        private void UnmarkChanges(int index)
        {
            if (!changesMarked)
                return;
            modifyingCombobox = true;

            string currstr = modpackComboBox.Items[index].ToString();
            modpackComboBox.Items[index] = currstr.Substring(0, currstr.Length - 1);
            changesMarked = false;

            modifyingCombobox = false;
        }

        // Record that changes were or weren't made, and enable/disable appropriate buttons.
        private void SetChangesMade(bool changesMade)
        {
            // Set actual variable.
            this.changesMade = changesMade;

            // No undoing if there are no changes.
            undoChangesButton.Enabled = changesMade;

            // If there are changes then no renaming, importing, exporting, or new lists.
            renameListButton.Enabled = !changesMade;
            importButton.Enabled = !changesMade;
            exportButton.Enabled = !changesMade;
            newListButton.Enabled = !changesMade;
        }

        // Tell both modlistPanels to update which modreferencePanels are visible based on lists from manager.
        private void RefreshModlistPanels()
        {
            leftModlistPanel.UpdateVisibleOrder(new List<DFHMod>(manager.disabledMods));
            rightModlistPanel.UpdateVisibleOrder(manager.enabledMods);

            // Make the right panel show any problems that pop up.
            rightModlistPanel.ColorProblemMods(manager.modproblems);

            // Force refrash search boxes.
            leftSearchBox_TextChanged("", new EventArgs());
            rightSearchBox_TextChanged("", new EventArgs());
            //leftSearchBox.Text = leftSearchBox.Text + "";
            //rightSearchBox.Text = rightSearchBox.Text + "";
        }

        // Highlight the panel at and before index, if they exist.
        private void SetSurroundingToHighlight(int index, VerticalFlowPanel parentPanel)
        {
            if (index > 0)
            {
                ModRefPanel selected = parentPanel.GetVisibleControlAtIndex(index - 1) as ModRefPanel;
                selected.SetHighlight(false, true);
                highlightAffected.Add(selected);
            }
            if (index < parentPanel.memberMods.Count)
            {
                ModRefPanel selected = parentPanel.GetVisibleControlAtIndex(index) as ModRefPanel;
                selected.SetHighlight(true, false);
                highlightAffected.Add(selected);
            }
        }

        // Loop through all highlighted panels and unhighlight them, then clear hashset.
        private void UnsetSurroundingToHighlight()
        {
            foreach (ModRefPanel panel in highlightAffected)
            {
                panel.SetHighlight(false, false);
            }
            highlightAffected.Clear();
        }

        // Set changesMade, fix buttons, and mark/unmark changes.
        private void SetAndMarkChanges(bool changesMade)
        {
            SetChangesMade(changesMade);
            if (changesMade)
                MarkChanges(lastIndex);
            else
                UnmarkChanges(lastIndex);
        }

        // Tell manager to set the modlist to index, then refresh both panels.
        private void SetAndRefreshModpack(int index)
        {
            manager.SetSelectedModpack(index);
            RefreshModlistPanels();
        }

        // Save the list.
        private void saveButton_Click(object sender, EventArgs e)
        {
            SaveCurrentModpack();
        }

        private void undoChangesButton_Click(object sender, EventArgs e)
        {
            // Ask the user if they really want to undo their changes.
            DialogResult result = LocationMessageBox.Show("Are you sure you want to reset modlist changes?", "Undo changes", MessageBoxButtons.YesNo);
            if (result == DialogResult.Yes)
            {
                // Undo our changes and immediately refresh
                UndoListChanges();
            }
        }

        private void autoSortButton_Click(object sender, EventArgs e)
        {
            bool changed = manager.AutoSortEnabledMods();
            if (changed)
                SetAndMarkChanges(true);
            RefreshModlistPanels();
        }

        // Run the dwarf fortress executable.
        private void playGameButton_Click(object sender, EventArgs e)
        {
            manager.RunDwarfFortress();
        }

        // Restart the application, to look for new mods. #TODO: could me bade to rescan mods, but a full restart is easiest.
        private void restartButton_Click(object sender, EventArgs e)
        {
            // If changes were made, ask if the user wants to save them first. If no changes are made, ask if the user really wants to do this.
            if (changesMade)
            {
                DialogResult result = LocationMessageBox.Show($"You have unsaved changes to '{manager.SelectedModlist.name}', do you want to save before restarting the application?", "Unsaved Changes", MessageBoxButtons.YesNoCancel);

                if (result == DialogResult.Yes)
                {
                    // If yes, save changes before reloading.
                    SaveCurrentModpack();
                }
                else if (result == DialogResult.No)
                {
                    // If no, do nothing, since the application will reload.
                }
                else
                {
                    // If cancel, set index to last, then return, so as not to proceed with swap.
                    return;
                }
            }
            else
                if (LocationMessageBox.Show("Are you sure you want reload? Application will restart.", "Application Reload", MessageBoxButtons.YesNo) == DialogResult.No)
                return;

            // Mark that we are closing and restart the application.
            selfClosing = true;
            Application.Restart();
        }

        private void SaveCurrentModpack()
        {
            // Save changes to modpack.
            manager.SaveCurrentModpack();

            // Unmark changes and fix buttons.
            SetAndMarkChanges(false);
        }

        private void UndoListChanges()
        {
            Console.WriteLine("Undid changes.");

            // Set the modpack to lastIndex (loads modlist from modpack, undoing changes)
            SetAndRefreshModpack(lastIndex);

            // Unmark changes and fix buttons.
            SetAndMarkChanges(false);

        }

        private void modlistComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {

            // If we are programatically modifying combobox, do nothing.
            if (modifyingCombobox)
                return;

            Console.WriteLine($"Changed from index {lastIndex} to {modpackComboBox.SelectedIndex}");

            // If the modlist didn't change, do nothing.
            if (lastIndex == modpackComboBox.SelectedIndex)
                return;

            // If changes were made, prompt user to either save changes, discard changes, or cancel index change.
            if (changesMade)
            {
                DialogResult result = LocationMessageBox.Show($"You have unsaved changes to '{manager.SelectedModlist.name}', do you want to save before continuing?", "Unsaved Changes", MessageBoxButtons.YesNoCancel);
                if (result == DialogResult.Yes)
                {
                    // If yes, save changes before proceeding with swap.
                    SaveCurrentModpack();
                }
                else if (result == DialogResult.No)
                {
                    // If no, unset changes and fix buttons before proceeding (changing modlists discards changes inherently).
                    SetAndMarkChanges(false);
                }
                else
                {
                    // If cancel, set index to last, then return, so as not to proceed with swap.
                    modifyingCombobox = true;
                    modpackComboBox.SelectedIndex = lastIndex;
                    modifyingCombobox = false;
                    return;
                }
            }

            // Either no changes were made, or the user decided to proceed. Tell manager to swap to new index and update lastIndex.
            SetAndRefreshModpack(modpackComboBox.SelectedIndex);
            lastIndex = modpackComboBox.SelectedIndex;
        }

        // Creates a new modpack. This can only be pressed when no unsaved changes. 
        private void newPackButton_Click(object sender, EventArgs e)
        {
            // Ask the user for a name.
            string newName = Interaction.InputBox("Please enter a name for the new modpack", "New Modpack Name", "");
            if (string.IsNullOrEmpty(newName))
                return;

            // Create a new modpack.
            DFHModpack newPack = new DFHModpack(false, manager.GenerateVanillaModlist(), newName);

            // Register the modpack.
            RegisterNewModpack(newPack);
        }

        // Adds a new modpack to the list, and saves immediately. No changes are recorded since saving is immediate.
        private void RegisterNewModpack(DFHModpack newList)
        {
            modifyingCombobox = true;

            // Add the new modpack to the manager, and save it to the modpacks file
            manager.modpacks.Add(newList);
            manager.SaveAllModpacks();

            //add to combobox and select
            modpackComboBox.Items.Add(newList.name);
            modpackComboBox.SelectedIndex = modpackComboBox.Items.Count - 1;

            //set manager to it and refresh
            SetAndRefreshModpack(modpackComboBox.SelectedIndex);

            modifyingCombobox = false;
        }

        // Renames a modpack, and saves immediately. This can only be pressed when no unsaved changes.
        private void renameModpackButton_Click(object sender, EventArgs e)
        {
            string newName = Interaction.InputBox("Please enter a new name for the modpack", "New Modpack Name", manager.SelectedModlist.name);
            if (string.IsNullOrEmpty(newName))
                return;

            // Change names but do not set changesmade to true.
            modifyingCombobox = true;

            manager.SelectedModlist.name = newName;
            modpackComboBox.Items[modpackComboBox.SelectedIndex] = newName;

            // Save the current modpack to file.
            SaveCurrentModpack();

            modifyingCombobox = false;
        }

        // Deletes a modpack, and saves immediately. This can only be pressed when no unsaved changes. Fails if there is only one modpack left.
        private void deleteListButton_Click(object sender, EventArgs e)
        {
            DialogResult result = LocationMessageBox.Show($"Are you sure you want to delete {manager.SelectedModlist.name}? This is final.", "Delete modlist", MessageBoxButtons.YesNo);
            if (result == DialogResult.Yes)
            {
                // Undo our changes and fix buttons.
                SetAndMarkChanges(false);

                // Minimum one modpack FIXME: this is just because not having any modlists needs extra logic. Would be easy enough to generate a vanilla modpack if all are gone though.
                if (manager.modpacks.Count == 1)
                {
                    LocationMessageBox.Show("You cannot delete the last modlist.", "Failed", MessageBoxButtons.OK);
                    return;
                }

                modifyingCombobox = true;

                // Remove the modpack from both manager and combobox.
                modpackComboBox.Items.RemoveAt(modpackComboBox.SelectedIndex);
                manager.modpacks.Remove(manager.SelectedModlist);

                // Overwrite modpack file with missing modpack.
                manager.SaveAllModpacks();

                // Set the index to 0 and refresh.
                modpackComboBox.SelectedIndex = 0;
                SetAndRefreshModpack(0);

                modifyingCombobox = false;
            }
        }

        // Imports a JSON file and converts it to a modpack. This can only be pressed when no unsaved changes.
        private void importButton_Click(object sender, EventArgs e)
        {
            // Get the user to select a JSON file
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Text files (*.json)|*.json";
            openFileDialog.Title = "Select a Modpack JSON File";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    // Try opening and deserializing the file.
                    string filePath = openFileDialog.FileName;
                    string importedString = File.ReadAllText(filePath);
                    DFHModpack importedList = JsonSerializer.Deserialize<DFHModpack>(importedString);

                    // Check if another modpack by the same name exists. If so ask the user to overwrite. FIXME: it is unknown if dfhack allows multiple modpacks with the same name, but it is avoided anyways.
                    for (int i = 0; i < manager.modpacks.Count; i++)
                    {
                        DFHModpack otherModlist = manager.modpacks[i];
                        if (otherModlist.name == importedList.name)
                        {
                            // If the user said yes to overwrite, then do a more complex process, to allow the undo button to revert the imported changes.
                            DialogResult result = LocationMessageBox.Show($"A modpack with the name {otherModlist.name} is already present. Would you like to overwrite it?", "Modlist Already Present", MessageBoxButtons.YesNo);
                            if (result == DialogResult.Yes)
                            {
                                // Set the combobox to the matching modlist.
                                modifyingCombobox = true;
                                modpackComboBox.SelectedIndex = i;
                                lastIndex = i;
                                modifyingCombobox = false;

                                // Set the selected modpack to the matching one.
                                manager.SetSelectedModpack(i);

                                // Overwrite the enabled mods and refresh panels.
                                manager.SetActiveMods(importedList.modlist);
                                RefreshModlistPanels();

                                // Set changes made and mark changes.
                                SetChangesMade(true);
                                MarkChanges(i);

                            }
                            return;
                        }
                    }

                    // If a modpack with a matching name wasn't found, register this modpack as a new modpack.
                    RegisterNewModpack(importedList);
                }
                catch (Exception ex)
                {
                    LocationMessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK);
                }
            }
        }

        // Take the current modlist and export it to a JSON file. This can only be pressed when no unsaved changes.
        private void exportButton_Click(object sender, EventArgs e)
        {
            // Allow the user to choose a save location.
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Text files (*.json)|*.json";
            saveFileDialog.Title = "Save Modpack JSON File";
            saveFileDialog.DefaultExt = "json";
            saveFileDialog.AddExtension = true;

            // Try and save the file to that location.
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string filePath = saveFileDialog.FileName;
                    JsonSerializerOptions options = new JsonSerializerOptions
                    {
                        WriteIndented = true // Enable pretty formatting
                    };
                    string exportString = JsonSerializer.Serialize(manager.SelectedModlist, options);
                    File.WriteAllText(filePath, exportString);

                    LocationMessageBox.Show("File saved successfully.", "Success", MessageBoxButtons.OK);
                }
                catch (Exception ex)
                {
                    LocationMessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK);
                }
            }
        }

        // On search change, notify panel.
        private void leftSearchBox_TextChanged(object sender, EventArgs e)
        {
            leftModlistPanel.SearchFilter(leftSearchBox.Text.ToLower());
        }

        // On search change, notify panel.
        private void rightSearchBox_TextChanged(object sender, EventArgs e)
        {
            rightModlistPanel.SearchFilter(rightSearchBox.Text.ToLower());
        }

        // Remove the search filter.
        private void leftSearchCloseButton_Click(object sender, EventArgs e)
        {
            leftSearchBox.Text = string.Empty;
        }

        // Remove the search filter.
        private void rightSearchCloseButton_Click(object sender, EventArgs e)
        {
            rightSearchBox.Text = string.Empty;
        }

        // Delete the config file and restart the application. TODO: create actual config editing window
        private void redoConfigButton_Click(object sender, EventArgs e)
        {
            // Ask before proceeding.
            //DialogResult result = LocationMessageBox.Show("Are you sure you want to reset config file? Application will restart.", "Redo Config", MessageBoxButtons.YesNo);
            DialogResult result = LocationMessageBox.Show("Are you sure you want to reset config file? Application will restart.", "Redo Config", MessageBoxButtons.YesNo);
            if (result == DialogResult.No)
                return;

            manager.DestroyConfig();
            selfClosing = true;
            Application.Restart();
        }

        // If the theme index was changed, save the change to manager config file, and fix our style.
        private void themeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            Console.WriteLine($"Style change from {lastStyle} to {themeComboBox.SelectedIndex}");

            // Do nothing if style hasn't changed.
            if (themeComboBox.SelectedIndex == lastStyle)
                return;
            lastStyle = themeComboBox.SelectedIndex;
            manager.SetTheme(themeComboBox.SelectedIndex);
            FixStyle();
        }
    }
}
