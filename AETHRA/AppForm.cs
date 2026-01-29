using System;
using System.Windows.Forms;
using System.Media;
namespace AETHRA
{

    public class AppForm : Form
    {
        TextBox editor;
        Button runBtn, exportBtn;

        public AppForm()
        {
            Text = "ÆTHRA – Music Programming Language";
            Width = 900;
            Height = 550;

            editor = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 11),
                Text =
@"@Tempo(120)
@Track(Lead)
@Note(C5,1)
@Rest(0.5)
@Note(D5,1)

@Track(Bass)
@Volume(0.6)
@Note(C2,2)"
            };

            runBtn = new Button { Text = "▶", Height = 40, Dock = DockStyle.Bottom };
            exportBtn = new Button { Text = "💾", Height = 40, Dock = DockStyle.Bottom };

            runBtn.Click += (s, e) => Interpreter.PlayLive(editor.Text);
            exportBtn.Click += Export;

            Controls.Add(editor);
            Controls.Add(exportBtn);
            Controls.Add(runBtn);
        }

        void Export(object? s, EventArgs e)
        {
            SaveFileDialog sfd = new() { Filter = "WAV (*.wav)|*.wav" };
            if (sfd.ShowDialog() == DialogResult.OK)
                Interpreter.Run(editor.Text, sfd.FileName);
        }
    }
}
