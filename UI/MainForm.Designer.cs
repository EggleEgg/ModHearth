using ModHearth.UI;

namespace ModHearth
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            rightModlistPanel = new VerticalFlowPanel();
            leftModlistPanel = new VerticalFlowPanel();
            modpackComboBox = new ComboBox();
            modlistColumnTableLayout = new TableLayoutPanel();
            rightSearchPanel = new Panel();
            rightSearchCloseButton = new Button();
            rightSearchBox = new TextBox();
            leftSearchPanel = new Panel();
            leftSearchCloseButton = new Button();
            leftSearchBox = new TextBox();
            outerTableLayout = new TableLayoutPanel();
            rightPanel = new Panel();
            themeComboBox = new ComboBox();
            redoConfigButton = new Button();
            exportButton = new Button();
            importButton = new Button();
            autoSortButton = new Button();
            newListButton = new Button();
            deleteListButton = new Button();
            renameListButton = new Button();
            reloadButton = new Button();
            playGameButton = new Button();
            undoChangesButton = new Button();
            saveButton = new Button();
            modInfoPanel = new TableLayoutPanel();
            modTitleLabel = new Label();
            modDescriptionLabel = new Label();
            modVersionLabel = new Label();
            modPictureBox = new PictureBox();
            modlistColumnTableLayout.SuspendLayout();
            rightSearchPanel.SuspendLayout();
            leftSearchPanel.SuspendLayout();
            outerTableLayout.SuspendLayout();
            rightPanel.SuspendLayout();
            modInfoPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)modPictureBox).BeginInit();
            SuspendLayout();
            // 
            // rightModlistPanel
            // 
            rightModlistPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            rightModlistPanel.AutoScroll = true;
            rightModlistPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            rightModlistPanel.BackColor = Color.Silver;
            rightModlistPanel.Location = new Point(264, 33);
            rightModlistPanel.Name = "rightModlistPanel";
            rightModlistPanel.Size = new Size(255, 762);
            rightModlistPanel.TabIndex = 4;
            // 
            // leftModlistPanel
            // 
            leftModlistPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            leftModlistPanel.AutoScroll = true;
            leftModlistPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            leftModlistPanel.BackColor = Color.Silver;
            leftModlistPanel.Location = new Point(3, 33);
            leftModlistPanel.Name = "leftModlistPanel";
            leftModlistPanel.Size = new Size(255, 762);
            leftModlistPanel.TabIndex = 3;
            // 
            // modpackComboBox
            // 
            modpackComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modpackComboBox.Font = new Font("Segoe UI", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            modpackComboBox.FormattingEnabled = true;
            modpackComboBox.Location = new Point(3, 49);
            modpackComboBox.Name = "modpackComboBox";
            modpackComboBox.Size = new Size(191, 28);
            modpackComboBox.TabIndex = 6;
            modpackComboBox.SelectedIndexChanged += modlistComboBox_SelectedIndexChanged;
            // 
            // modlistColumnTableLayout
            // 
            modlistColumnTableLayout.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            modlistColumnTableLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modlistColumnTableLayout.ColumnCount = 2;
            modlistColumnTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modlistColumnTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            modlistColumnTableLayout.Controls.Add(rightSearchPanel, 1, 0);
            modlistColumnTableLayout.Controls.Add(leftSearchPanel, 0, 0);
            modlistColumnTableLayout.Controls.Add(leftModlistPanel, 0, 1);
            modlistColumnTableLayout.Controls.Add(rightModlistPanel, 1, 1);
            modlistColumnTableLayout.Location = new Point(515, 3);
            modlistColumnTableLayout.Name = "modlistColumnTableLayout";
            modlistColumnTableLayout.RowCount = 2;
            modlistColumnTableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            modlistColumnTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            modlistColumnTableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            modlistColumnTableLayout.Size = new Size(522, 798);
            modlistColumnTableLayout.TabIndex = 7;
            // 
            // rightSearchPanel
            // 
            rightSearchPanel.Controls.Add(rightSearchCloseButton);
            rightSearchPanel.Controls.Add(rightSearchBox);
            rightSearchPanel.Location = new Point(264, 3);
            rightSearchPanel.Name = "rightSearchPanel";
            rightSearchPanel.Size = new Size(255, 24);
            rightSearchPanel.TabIndex = 17;
            // 
            // rightSearchCloseButton
            // 
            rightSearchCloseButton.Image = Resource1.XIcon;
            rightSearchCloseButton.Location = new Point(233, 0);
            rightSearchCloseButton.Name = "rightSearchCloseButton";
            rightSearchCloseButton.Size = new Size(24, 24);
            rightSearchCloseButton.TabIndex = 6;
            rightSearchCloseButton.UseVisualStyleBackColor = true;
            rightSearchCloseButton.Click += rightSearchCloseButton_Click;
            // 
            // rightSearchBox
            // 
            rightSearchBox.Font = new Font("Segoe UI", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            rightSearchBox.Location = new Point(0, 0);
            rightSearchBox.Name = "rightSearchBox";
            rightSearchBox.Size = new Size(230, 27);
            rightSearchBox.TabIndex = 5;
            rightSearchBox.TextChanged += rightSearchBox_TextChanged;
            // 
            // leftSearchPanel
            // 
            leftSearchPanel.Controls.Add(leftSearchCloseButton);
            leftSearchPanel.Controls.Add(leftSearchBox);
            leftSearchPanel.Location = new Point(3, 3);
            leftSearchPanel.Name = "leftSearchPanel";
            leftSearchPanel.Size = new Size(255, 24);
            leftSearchPanel.TabIndex = 16;
            // 
            // leftSearchCloseButton
            // 
            leftSearchCloseButton.BackgroundImageLayout = ImageLayout.Stretch;
            leftSearchCloseButton.Image = Resource1.XIcon;
            leftSearchCloseButton.Location = new Point(233, 0);
            leftSearchCloseButton.Name = "leftSearchCloseButton";
            leftSearchCloseButton.Size = new Size(24, 24);
            leftSearchCloseButton.TabIndex = 6;
            leftSearchCloseButton.UseVisualStyleBackColor = true;
            leftSearchCloseButton.Click += leftSearchCloseButton_Click;
            // 
            // leftSearchBox
            // 
            leftSearchBox.Font = new Font("Segoe UI", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
            leftSearchBox.Location = new Point(0, 0);
            leftSearchBox.Name = "leftSearchBox";
            leftSearchBox.Size = new Size(230, 27);
            leftSearchBox.TabIndex = 5;
            leftSearchBox.TextChanged += leftSearchBox_TextChanged;
            // 
            // outerTableLayout
            // 
            outerTableLayout.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            outerTableLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            outerTableLayout.ColumnCount = 3;
            outerTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 512F));
            outerTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            outerTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            outerTableLayout.Controls.Add(rightPanel, 2, 0);
            outerTableLayout.Controls.Add(modlistColumnTableLayout, 1, 0);
            outerTableLayout.Controls.Add(modInfoPanel, 0, 0);
            outerTableLayout.Location = new Point(12, 12);
            outerTableLayout.Name = "outerTableLayout";
            outerTableLayout.RowCount = 1;
            outerTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            outerTableLayout.Size = new Size(1240, 804);
            outerTableLayout.TabIndex = 8;
            // 
            // rightPanel
            // 
            rightPanel.Controls.Add(themeComboBox);
            rightPanel.Controls.Add(redoConfigButton);
            rightPanel.Controls.Add(exportButton);
            rightPanel.Controls.Add(importButton);
            rightPanel.Controls.Add(autoSortButton);
            rightPanel.Controls.Add(newListButton);
            rightPanel.Controls.Add(deleteListButton);
            rightPanel.Controls.Add(renameListButton);
            rightPanel.Controls.Add(reloadButton);
            rightPanel.Controls.Add(modpackComboBox);
            rightPanel.Controls.Add(playGameButton);
            rightPanel.Controls.Add(undoChangesButton);
            rightPanel.Controls.Add(saveButton);
            rightPanel.Location = new Point(1043, 3);
            rightPanel.Name = "rightPanel";
            rightPanel.Size = new Size(194, 798);
            rightPanel.TabIndex = 0;
            // 
            // themeComboBox
            // 
            themeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            themeComboBox.FormattingEnabled = true;
            themeComboBox.Items.AddRange(new object[] { "light theme", "dark theme" });
            themeComboBox.Location = new Point(70, 743);
            themeComboBox.Name = "themeComboBox";
            themeComboBox.Size = new Size(121, 23);
            themeComboBox.TabIndex = 17;
            themeComboBox.SelectedIndexChanged += themeComboBox_SelectedIndexChanged;
            // 
            // redoConfigButton
            // 
            redoConfigButton.Location = new Point(101, 772);
            redoConfigButton.Name = "redoConfigButton";
            redoConfigButton.Size = new Size(90, 23);
            redoConfigButton.TabIndex = 16;
            redoConfigButton.Text = "Redo Config";
            redoConfigButton.UseVisualStyleBackColor = true;
            redoConfigButton.Click += redoConfigButton_Click;
            // 
            // exportButton
            // 
            exportButton.Location = new Point(100, 112);
            exportButton.Name = "exportButton";
            exportButton.Size = new Size(94, 23);
            exportButton.TabIndex = 15;
            exportButton.Text = "Export";
            exportButton.UseVisualStyleBackColor = true;
            exportButton.Click += exportButton_Click;
            // 
            // importButton
            // 
            importButton.Location = new Point(3, 112);
            importButton.Name = "importButton";
            importButton.Size = new Size(89, 23);
            importButton.TabIndex = 14;
            importButton.Text = "Import";
            importButton.UseVisualStyleBackColor = true;
            importButton.Click += importButton_Click;
            // 
            // autoSortButton
            // 
            autoSortButton.Location = new Point(3, 141);
            autoSortButton.Name = "autoSortButton";
            autoSortButton.Size = new Size(191, 23);
            autoSortButton.TabIndex = 18;
            autoSortButton.Text = "Auto Sort";
            autoSortButton.UseVisualStyleBackColor = true;
            autoSortButton.Click += autoSortButton_Click;
            // 
            // newListButton
            // 
            newListButton.Location = new Point(3, 83);
            newListButton.Name = "newListButton";
            newListButton.Size = new Size(49, 23);
            newListButton.TabIndex = 13;
            newListButton.Text = "New";
            newListButton.UseVisualStyleBackColor = true;
            newListButton.Click += newPackButton_Click;
            // 
            // deleteListButton
            // 
            deleteListButton.Location = new Point(133, 83);
            deleteListButton.Name = "deleteListButton";
            deleteListButton.Size = new Size(61, 23);
            deleteListButton.TabIndex = 12;
            deleteListButton.Text = "Delete";
            deleteListButton.UseVisualStyleBackColor = true;
            deleteListButton.Click += deleteListButton_Click;
            // 
            // renameListButton
            // 
            renameListButton.Location = new Point(58, 83);
            renameListButton.Name = "renameListButton";
            renameListButton.Size = new Size(69, 23);
            renameListButton.TabIndex = 11;
            renameListButton.Text = "Rename";
            renameListButton.UseVisualStyleBackColor = true;
            renameListButton.Click += renameModpackButton_Click;
            // 
            // reloadButton
            // 
            reloadButton.Image = Resource1.reloadIcon;
            reloadButton.Location = new Point(150, 3);
            reloadButton.Name = "reloadButton";
            reloadButton.Size = new Size(43, 43);
            reloadButton.TabIndex = 10;
            reloadButton.UseVisualStyleBackColor = true;
            reloadButton.Click += restartButton_Click;
            // 
            // playGameButton
            // 
            playGameButton.Image = Resource1.playIcon;
            playGameButton.Location = new Point(101, 3);
            playGameButton.Name = "playGameButton";
            playGameButton.Size = new Size(43, 43);
            playGameButton.TabIndex = 9;
            playGameButton.UseVisualStyleBackColor = true;
            playGameButton.Click += playGameButton_Click;
            // 
            // undoChangesButton
            // 
            undoChangesButton.Image = Resource1.undoIcon;
            undoChangesButton.Location = new Point(52, 3);
            undoChangesButton.Name = "undoChangesButton";
            undoChangesButton.Size = new Size(43, 43);
            undoChangesButton.TabIndex = 8;
            undoChangesButton.UseVisualStyleBackColor = true;
            undoChangesButton.Click += undoChangesButton_Click;
            // 
            // saveButton
            // 
            saveButton.Image = Resource1.saveIcon;
            saveButton.Location = new Point(3, 3);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(43, 43);
            saveButton.TabIndex = 7;
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += saveButton_Click;
            // 
            // modInfoPanel
            // 
            modInfoPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            modInfoPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            modInfoPanel.ColumnCount = 1;
            modInfoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modInfoPanel.Controls.Add(modTitleLabel, 0, 0);
            modInfoPanel.Controls.Add(modPictureBox, 0, 1);
            modInfoPanel.Controls.Add(modDescriptionLabel, 0, 2);
            modInfoPanel.Controls.Add(modVersionLabel, 0, 3);
            modInfoPanel.Location = new Point(3, 3);
            modInfoPanel.Name = "modInfoPanel";
            modInfoPanel.RowCount = 4;
            modInfoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            modInfoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 636F));
            modInfoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            modInfoPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            modInfoPanel.Size = new Size(506, 798);
            modInfoPanel.TabIndex = 8;
            // 
            // modTitleLabel
            // 
            modTitleLabel.Dock = DockStyle.Fill;
            modTitleLabel.Font = new Font("Segoe UI", 24F, FontStyle.Regular, GraphicsUnit.Point);
            modTitleLabel.Location = new Point(3, 0);
            modTitleLabel.Name = "modTitleLabel";
            modTitleLabel.Size = new Size(500, 42);
            modTitleLabel.TabIndex = 0;
            // 
            // modDescriptionLabel
            // 
            modDescriptionLabel.Dock = DockStyle.Fill;
            modDescriptionLabel.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);
            modDescriptionLabel.Location = new Point(3, 678);
            modDescriptionLabel.Name = "modDescriptionLabel";
            modDescriptionLabel.Size = new Size(500, 102);
            modDescriptionLabel.TabIndex = 1;
            // 
            // modVersionLabel
            // 
            modVersionLabel.AutoSize = true;
            modVersionLabel.Dock = DockStyle.Fill;
            modVersionLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            modVersionLabel.Location = new Point(3, 780);
            modVersionLabel.Name = "modVersionLabel";
            modVersionLabel.Size = new Size(500, 18);
            modVersionLabel.TabIndex = 3;
            modVersionLabel.Text = "Build";
            // 
            // modPictureBox
            // 
            modPictureBox.Dock = DockStyle.Fill;
            modPictureBox.Location = new Point(3, 45);
            modPictureBox.Name = "modPictureBox";
            modPictureBox.Size = new Size(500, 630);
            modPictureBox.TabIndex = 2;
            modPictureBox.TabStop = false;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 828);
            Controls.Add(outerTableLayout);
            ForeColor = SystemColors.ControlText;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "MainForm";
            Text = "Mod Hearth";
            modlistColumnTableLayout.ResumeLayout(false);
            rightSearchPanel.ResumeLayout(false);
            rightSearchPanel.PerformLayout();
            leftSearchPanel.ResumeLayout(false);
            leftSearchPanel.PerformLayout();
            outerTableLayout.ResumeLayout(false);
            rightPanel.ResumeLayout(false);
            modInfoPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)modPictureBox).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private VerticalFlowPanel rightModlistPanel;
        private VerticalFlowPanel leftModlistPanel;
        private ComboBox modpackComboBox;
        private TableLayoutPanel modlistColumnTableLayout;
        private TableLayoutPanel outerTableLayout;
        private Panel rightPanel;
        private Button reloadButton;
        private Button playGameButton;
        private Button undoChangesButton;
        private Button saveButton;
        private TableLayoutPanel modInfoPanel;
        private Label modTitleLabel;
        private Label modDescriptionLabel;
        private Label modVersionLabel;
        private PictureBox modPictureBox;
        private Button newListButton;
        private Button deleteListButton;
        private Button renameListButton;
        private Button exportButton;
        private Button importButton;
        private Panel leftSearchPanel;
        private TextBox leftSearchBox;
        private Button leftSearchCloseButton;
        private Panel rightSearchPanel;
        private Button rightSearchCloseButton;
        private TextBox rightSearchBox;
        private Button redoConfigButton;
        private ComboBox themeComboBox;
        private Button autoSortButton;
    }
}
